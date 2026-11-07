Option Strict On

Imports System.Threading
Imports System.Windows.Forms
Imports WhoIsPro.Config
Imports WhoIsPro.Inventory
Imports WhoIsPro.Network.Scan

Namespace UI

#Region "Esiti ed eventi"

    ''' <summary>Una scansione conclusa: rapporto, differenze rispetto alla precedente e se è partita da sola.</summary>
    Public NotInheritable Class ScanOutcome
        Public Sub New(report As ScanReport, diff As ScanDiff, automatic As Boolean)
            Me.Report = report
            Me.Diff = diff
            Me.Automatic = automatic
        End Sub
        Public ReadOnly Property Report As ScanReport
        Public ReadOnly Property Diff As ScanDiff
        Public ReadOnly Property Automatic As Boolean
    End Class

    ''' <summary>Fine di una scansione, comunque sia andata: esito (se registrato), annullamento o errore.</summary>
    Public NotInheritable Class ScanFinishedEventArgs
        Inherits EventArgs
        Public Property Outcome As ScanOutcome
        Public Property Automatic As Boolean
        Public Property Cancelled As Boolean
        Public Property [Error] As String
        Public Property Seconds As Double
        Public Property HostsFound As Integer
        ''' <summary>La rete scansionata (router o sottorete).</summary>
        Public Property Network As NetworkIdentity
    End Class

    ''' <summary>Avviso per l'utente (schedina in basso a destra): livello "info" (16 s) o "attention" (45 s).</summary>
    Public NotInheritable Class NetworkAlert
        Public Property Level As String = "info"
        Public Property Title As String
        Public Property Detail As String
        Public Property HostId As Long?
        Public Property At As DateTime = DateTime.Now
    End Class

#End Region

    ''' <summary>
    ''' Sorveglianza della rete: scansioni periodiche (intervallo e rete dalle impostazioni) e a richiesta, sempre una
    ''' alla volta, con il motore DiscoveryScanner; ogni scansione finita aggiorna l'archivio (NetworkInventory) e
    ''' produce gli avvisi sui cambiamenti. Gli eventi arrivano sul thread della finestra (SynchronizationContext).
    ''' </summary>
    Public NotInheritable Class NetworkWatchService
        Implements IDisposable

#Region "Campi"

        ''' <summary>Finestra anti-ripetizione: lo stesso dispositivo non genera un secondo avviso entro questo tempo.</summary>
        Private Shared ReadOnly AlertRepeatWindow As TimeSpan = TimeSpan.FromMinutes(30)

        Private ReadOnly _settings As AppSettings
        Private ReadOnly _store As InventoryStore
        Private ReadOnly _scanner As INetworkScanner
        Private ReadOnly _sync As SynchronizationContext
        Private ReadOnly _timer As New System.Windows.Forms.Timer With {.Interval = 60000}
        Private ReadOnly _kickoff As New System.Windows.Forms.Timer With {.Interval = 20000}
        Private ReadOnly _lastAlert As New Dictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)
        Private _scanCts As CancellationTokenSource
        Private _started As Boolean
        ' la rete in cui si trova il PC adesso: costa una lettura delle schede e della tabella ARP, si tiene per mezzo minuto
        Private _current As NetworkIdentity
        Private _currentAt As DateTime
        Private _currentChecked As Boolean
        Private _lastKnownKey As String

#End Region

#Region "Costruzione"

        Public Sub New(settings As AppSettings, store As InventoryStore, scanner As INetworkScanner, sync As SynchronizationContext)
            _settings = settings
            _store = store
            _scanner = scanner
            _sync = sync
            AddHandler _timer.Tick, Async Sub()
                                        Await CheckNetworkChangeAsync()
                                        If IsDue Then Await ScanAsync(automatic:=True)
                                    End Sub
            AddHandler _kickoff.Tick, Async Sub()
                                          _kickoff.Stop()
                                          Await CheckNetworkChangeAsync()
                                          If IsDue Then Await ScanAsync(automatic:=True)
                                          _timer.Start()
                                      End Sub
        End Sub

#End Region

#Region "Stato"

        Public ReadOnly Property Inventory As NetworkInventory
            Get
                Return _store.Network
            End Get
        End Property

        Public ReadOnly Property Scanner As INetworkScanner
            Get
                Return _scanner
            End Get
        End Property

        Public Property IsScanning As Boolean
        ''' <summary>La scansione in corso è partita da sola (archivio) e non dalla pagina Rete (sessione).</summary>
        Public Property IsAutomatic As Boolean
        ''' <summary>Ultimo avanzamento ricevuto; Nothing a riposo.</summary>
        Public Property Progress As ScanPhaseProgress
        Public Property Options As ScanOptions
        Public Property StartedAt As DateTime?
        ''' <summary>La rete della scansione in corso (o dell'ultima): decisa all'avvio della scansione, serve a chi osserva gli host man mano.</summary>
        Public Property Network As NetworkIdentity

        ''' <summary>
        ''' La rete in cui si trova il PC adesso (scheda principale: router o sottorete); Nothing senza una rete locale.
        ''' Letta al massimo ogni mezzo minuto, salvo <paramref name="refresh"/>.
        ''' </summary>
        Public Function CurrentNetwork(Optional refresh As Boolean = False) As NetworkIdentity
            If Not refresh AndAlso _currentChecked AndAlso DateTime.Now - _currentAt < TimeSpan.FromSeconds(30) Then Return _current
            Try
                _current = _scanner.IdentifyNetwork("")
            Catch
                _current = Nothing
            End Try
            _currentAt = DateTime.Now
            _currentChecked = True
            Return _current
        End Function

        ''' <summary>Ogni minuto: se il PC è passato a un'altra rete (altro cliente, casa) lo dice a chi ascolta.</summary>
        Private Async Function CheckNetworkChangeAsync() As Task
            Dim now = Await Task.Run(Function() CurrentNetwork(refresh:=True))
            Dim key = now?.Key
            If String.Equals(key, _lastKnownKey, StringComparison.Ordinal) Then Return
            Dim hadOne = _lastKnownKey IsNot Nothing
            _lastKnownKey = key
            If now IsNot Nothing Then Inventory.EnsureNetwork(now)
            If hadOne AndAlso now IsNot Nothing Then RaiseEvent NetworkChanged(Me, now)
            RaiseEvent StateChanged(Me, EventArgs.Empty)
        End Function

        ''' <summary>Fase in corso, leggibile («Porte TCP · 1.240 / 17.000»); Nothing a riposo.</summary>
        Public ReadOnly Property Status As String
            Get
                If Not IsScanning Then Return Nothing
                Dim p = Progress
                If p Is Nothing Then Return "Preparazione"
                Return If(p.Total > 0, $"{ScanPhaseProgress.LabelOf(p.Phase)} · {p.Done:N0} / {p.Total:N0}", ScanPhaseProgress.LabelOf(p.Phase))
            End Get
        End Property

        Public ReadOnly Property Percent As Integer
            Get
                If Not IsScanning OrElse Progress Is Nothing Then Return 0
                Return Progress.OverallPercent(If(Options, New ScanOptions()))
            End Get
        End Property

        ''' <summary>Ultima scansione registrata della rete in cui si trova il PC adesso (di qualunque rete, se non se ne riconosce una).</summary>
        Public ReadOnly Property LastScanAt As DateTime?
            Get
                Return Inventory.LastScan(CurrentNetwork()?.Key)?.StartedAt
            End Get
        End Property

        Public ReadOnly Property Interval As TimeSpan
            Get
                Return TimeSpan.FromMinutes(Math.Clamp(_settings.NetworkScanIntervalMinutes, 5, 24 * 60))
            End Get
        End Property

        ''' <summary>Prossima scansione automatica prevista; Nothing se la sorveglianza è spenta.</summary>
        Public ReadOnly Property NextScanAt As DateTime?
            Get
                If Not _settings.NetworkWatchEnabled Then Return Nothing
                Dim last = LastScanAt
                Return If(last.HasValue, last.Value + Interval, DateTime.Now)
            End Get
        End Property

        ''' <summary>Vero se la sorveglianza è accesa, niente è in corso e l'ultima scansione è più vecchia dell'intervallo.</summary>
        Public ReadOnly Property IsDue As Boolean
            Get
                If Not _settings.NetworkWatchEnabled OrElse IsScanning Then Return False
                Dim last = LastScanAt
                Return Not last.HasValue OrElse DateTime.Now - last.Value > Interval
            End Get
        End Property

        ''' <summary>Stato per la pagina (barra superiore, pagina Dispositivi, Impostazioni), con la rete in cui si trova il PC.</summary>
        Public Function View() As Object
            Dim current = CurrentNetwork()
            Dim last = Inventory.LastScan(current?.Key)?.StartedAt
            Dim nextScan As DateTime? = If(_settings.NetworkWatchEnabled, If(last.HasValue, last.Value + Interval, DateTime.Now), CType(Nothing, DateTime?))
            Return New With {
                .scanning = IsScanning, .automatic = IsAutomatic, .status = Status, .percent = Percent,
                .phase = Progress?.Phase, .target = Options?.Target,
                .lastScanAt = last, .nextScanAt = nextScan, .enabled = _settings.NetworkWatchEnabled,
                .intervalMinutes = _settings.NetworkScanIntervalMinutes, .unseen = Inventory.UnacknowledgedCount(current?.Key),
                .network = NetworkView(current)
            }
        End Function

        ''' <summary>Una rete per la pagina: chiave, etichetta, nome dato dall'utente e se è riconosciuta dal router.</summary>
        Public Function NetworkView(network As NetworkIdentity) As Object
            If network Is Nothing Then Return Nothing
            Dim known = Inventory.GetNetwork(network.Key)
            Return New With {.key = network.Key, .label = network.Label, .alias = known?.[Alias], .name = If(known?.DisplayName, network.Label), .hasGateway = network.HasGateway, .cidr = network.Cidr}
        End Function

        Public Event StateChanged As EventHandler
        Public Event Progressed As EventHandler(Of ScanPhaseProgress)
        Public Event Finished As EventHandler(Of ScanFinishedEventArgs)
        Public Event AlertRaised As EventHandler(Of NetworkAlert)
        ''' <summary>Il PC è passato a un'altra rete (dopo la prima rilevazione).</summary>
        Public Event NetworkChanged As EventHandler(Of NetworkIdentity)

#End Region

#Region "Pianificazione"

        ''' <summary>Prima verifica poco dopo l'avvio, poi ogni minuto si guarda se è ora di scansionare.</summary>
        Public Sub Start()
            If _started Then Return
            _started = True
            _kickoff.Start()
        End Sub

        ''' <summary>Le impostazioni si rileggono a ogni giro del timer: niente da riavviare.</summary>
        Public Sub ApplySettings()
            RaiseEvent StateChanged(Me, EventArgs.Empty)
        End Sub

#End Region

#Region "Scansione"

        ''' <summary>
        ''' Scansiona e registra nell'archivio. <paramref name="observer"/> riceve ogni avanzamento (la pagina Rete ci
        ''' aggiunge gli host alla sessione). Restituisce Nothing se una scansione è già in corso.
        ''' </summary>
        Public Async Function ScanAsync(automatic As Boolean, Optional options As ScanOptions = Nothing, Optional observer As IProgress(Of ScanPhaseProgress) = Nothing) As Task(Of ScanFinishedEventArgs)
            If IsScanning Then Return Nothing
            IsScanning = True
            IsAutomatic = automatic
            Progress = Nothing
            StartedAt = DateTime.Now
            If options Is Nothing Then
                options = New ScanOptions With {.Target = If(_settings.NetworkScanTarget, "").Trim(), .TimeoutMs = Math.Clamp(_settings.NetworkScanTimeoutMs, 100, 10000)}
            End If
            Options = options
            Network = Nothing
            RaiseEvent StateChanged(Me, EventArgs.Empty)
            _scanCts = New CancellationTokenSource()
            Dim cts = _scanCts
            Dim clock = Stopwatch.StartNew()
            Dim result As New ScanFinishedEventArgs With {.Automatic = automatic}
            Try
                ' la rete della scansione si decide prima di partire: chi osserva gli host man mano (pagina Rete) la usa subito
                Dim target = options.Target
                Network = Await Task.Run(Function() _scanner.IdentifyNetwork(target))
                result.Network = Network
                Inventory.EnsureNetwork(Network, StartedAt)
                Dim progress As New Progress(Of ScanPhaseProgress)(Sub(p)
                                                                      Me.Progress = p
                                                                      observer?.Report(p)
                                                                      RaiseEvent Progressed(Me, p)
                                                                  End Sub)
                Dim report = Await _scanner.ScanAsync(options, progress, cts.Token)
                result.HostsFound = report.Hosts.Count
                ' se il router ha risposto solo durante la scansione, vale la rete del rapporto: gli host osservati prima la raggiungono
                Dim recorded = If(report.Network IsNot Nothing AndAlso report.Network.HasGateway, report.Network, Me.Network)
                result.Network = recorded
                Dim diff = Inventory.RecordScan(report, recorded)
                result.Outcome = New ScanOutcome(report, diff, automatic)
            Catch ex As OperationCanceledException
                result.Cancelled = True
            Catch ex As Exception
                result.Error = ex.Message
            Finally
                clock.Stop()
                result.Seconds = clock.Elapsed.TotalSeconds
                If cts.IsCancellationRequested Then result.Cancelled = True
                cts.Dispose()
                If ReferenceEquals(_scanCts, cts) Then _scanCts = Nothing
                IsScanning = False
                Progress = Nothing
            End Try
            If result.Outcome IsNot Nothing Then RaiseAlerts(result.Outcome.Diff)
            RaiseEvent Finished(Me, result)
            RaiseEvent StateChanged(Me, EventArgs.Empty)
            Return result
        End Function

        Public Sub CancelScan()
            _scanCts?.Cancel()
        End Sub

#End Region

#Region "Avvisi"

        ''' <summary>
        ''' Dopo ogni scansione tranne la prima: dispositivo nuovo → «attenzione» (uno solo aggregato se sono più di tre);
        ''' dispositivo sospetto che ricompare → «attenzione»; cambiamenti e assenze restano solo nello storico.
        ''' </summary>
        Private Sub RaiseAlerts(diff As ScanDiff)
            If diff Is Nothing OrElse diff.IsFirst OrElse Not _settings.NotifyNewDevices Then Return
            Dim fresh = diff.NewHosts.Where(Function(h) Not h.IsLocal AndAlso AllowAlert(h.HostKey)).ToList()
            If fresh.Count > 3 Then
                Raise(New NetworkAlert With {
                    .Level = "attention", .Title = $"{fresh.Count} dispositivi nuovi nella rete",
                    .Detail = String.Join(", ", fresh.Take(8).Select(Function(h) h.Label)) & If(fresh.Count > 8, "…", "")
                })
            Else
                For Each h In fresh
                    Raise(New NetworkAlert With {
                        .Level = "attention", .Title = $"Nuovo dispositivo: {h.DisplayName}", .HostId = h.Id,
                        .Detail = String.Join(" · ", {h.Ip, If(h.Mac, "").ToUpperInvariant(), h.Vendor, h.DeviceType, If(h.Ports.Count > 0, "porte " & String.Join(",", h.Ports.Select(Function(p) p.Port)), Nothing)}.Where(Function(x) Not String.IsNullOrEmpty(x)))
                    })
                Next
            End If
            For Each h In diff.ReturnedHosts.Where(Function(x) x.Trust = HostTrust.Suspicious AndAlso AllowAlert(x.HostKey))
                Raise(New NetworkAlert With {.Level = "attention", .Title = $"Dispositivo sospetto di nuovo presente: {h.DisplayName}", .HostId = h.Id, .Detail = $"{h.Ip}{If(h.Vendor Is Nothing, "", " · " & h.Vendor)} · {h.DeviceType}"})
            Next
        End Sub

        Private Function AllowAlert(key As String) As Boolean
            Dim last As DateTime
            If _lastAlert.TryGetValue(key, last) AndAlso DateTime.Now - last < AlertRepeatWindow Then Return False
            _lastAlert(key) = DateTime.Now
            Return True
        End Function

        Private Sub Raise(alert As NetworkAlert)
            RaiseEvent AlertRaised(Me, alert)
        End Sub

#End Region

        Public Sub Dispose() Implements IDisposable.Dispose
            _timer.Stop()
            _kickoff.Stop()
            _timer.Dispose()
            _kickoff.Dispose()
            CancelScan()
        End Sub

    End Class

End Namespace
