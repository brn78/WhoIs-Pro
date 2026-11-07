Option Strict On

Imports System.IO
Imports System.Text.Json
Imports System.Threading
Imports System.Windows.Forms
Imports System.Net.Http
Imports WhoIsPro.Config
Imports WhoIsPro.Inventory
Imports WhoIsPro.Machine
Imports WhoIsPro.Models
Imports WhoIsPro.Network
Imports WhoIsPro.Network.Scan
Imports WhoIsPro.Storage
Imports WhoIsPro.Update
Imports WhoIsPro.Utils

Namespace UI

    ''' <summary>Errore da mostrare così com'è nella pagina (messaggio già scritto per chi usa il programma).</summary>
    Public NotInheritable Class UserMessageException
        Inherits Exception

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub
    End Class

    ''' <summary>
    ''' Comandi della pagina (ui\app.js): scansione della rete e delle porte, WHOIS, DNS, traceroute, connessioni attive,
    ''' sessione (nuova, apri, salva, esporta), database dei produttori e appunti. La sessione in corso (ScanSession) vive
    ''' qui; la pagina ne riceve una copia all'avvio e a ogni apertura, e gli elementi nuovi man mano che arrivano.
    ''' Le scansioni girano in background e mandano alla pagina eventi (network.progress, ports.progress, trace.hop…):
    ''' l'avanzamento viene accorpato ogni 120 ms, così anche 65535 porte non intasano la finestra. Tutto quello che tocca
    ''' la sessione passa dal thread della finestra (IProgress lo garantisce), quindi non servono lock.
    ''' </summary>
    Public NotInheritable Class AppBridge
        Implements IDisposable

        Private Const SessionFilter As String = "Sessione WhoIs Pro (*.whoispro)|*.whoispro|File JSON (*.json)|*.json"
        Private Shared ReadOnly WhoisServers As String() = {
            "whois.nic.it", "whois.verisign-grs.com", "whois.pir.org", "whois.arin.net", "whois.ripe.net",
            "whois.apnic.net", "whois.lacnic.net", "whois.afrinic.net", "whois.iana.org"
        }

        Private ReadOnly _owner As MainWindow
        Private ReadOnly _allowedFiles As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _flush As New System.Windows.Forms.Timer With {.Interval = 120}
        Private ReadOnly _updates As New UpdateService(MainWindow.LocalFolder)
        Private _installing As Boolean
        Private ReadOnly _settings As AppSettings = SettingsStore.Load(MainWindow.LocalFolder)
        Private ReadOnly _internetTimer As New System.Windows.Forms.Timer With {.Interval = 30000}
        Private Shared ReadOnly InternetProbe As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(5)}
        Private _online As Boolean?
        Private _internetMs As Integer?
        Private _online6 As Boolean?
        Private _checkingInternet As Boolean

        ''' <summary>Impostazioni del programma (per la finestra: icona di notifica e tema).</summary>
        Public ReadOnly Property Settings As AppSettings
            Get
                Return _settings
            End Get
        End Property

        Private _session As New ScanSession()
        Private _file As String = ""
        Private _dirty As Boolean
        Private _adapters As List(Of LocalAdapterInfo)

        ' sessione per rete: si salva da sola in Documenti\WhoIs Pro\Sessioni (un file per rete, con il nome della rete)
        ' due secondi e mezzo dopo l'ultima modifica; la rete della sessione sta nella sessione stessa (NetworkKey)
        Private ReadOnly _autoSave As New System.Windows.Forms.Timer With {.Interval = 2500}
        Private _autoSavedAt As DateTime?
        Private _autoSaveFailed As Boolean

        ' archivio della rete nel tempo (SQLite, Core\Inventory) e sorveglianza: scansioni periodiche e a richiesta, una alla
        ' volta, con il motore DiscoveryScanner. La scansione della pagina Rete passa dallo stesso servizio e aggiorna lo
        ' stesso archivio; il registro dei dispositivi (nome, note, icona per MAC) è una vista sullo stesso database.
        Private ReadOnly _store As New InventoryStore(Path.Combine(MainWindow.LocalFolder, InventoryStore.FileName))
        Private ReadOnly _watch As NetworkWatchService
        Private ReadOnly _devices As DeviceRegistry
        Private _sessionScan As Boolean
        Private _netOptions As ScanOptions
        Private _netTarget As String = ""
        Private _netTargets As HashSet(Of String)

        Private _portCts As CancellationTokenSource
        Private _traceCts As CancellationTokenSource
        Private _ouiBusy As Boolean

        ' avanzamento accumulato tra un invio e l'altro alla pagina
        Private _netProgress As ScanPhaseProgress
        Private _netWatch As Stopwatch
        Private ReadOnly _netFound As New Dictionary(Of String, HostInfo)(StringComparer.Ordinal)
        Private ReadOnly _netPorts As New List(Of PortInfo)()
        Private ReadOnly _netSeenIps As New HashSet(Of String)()
        Private ReadOnly _netObservedIps As New HashSet(Of String)(StringComparer.Ordinal)
        Private ReadOnly _netNew As New List(Of HostInfo)()
        Private _netFirstCensus As Boolean
        Private _portProgress As PortScanProgress
        Private _portWatch As Stopwatch
        Private ReadOnly _portFound As New List(Of PortInfo)()

        Public Sub New(owner As MainWindow)
            _owner = owner
            AddHandler _flush.Tick, AddressOf FlushProgress
            AddHandler _internetTimer.Tick, Sub() CheckInternet()
            _devices = New DeviceRegistry(_store)
            _watch = New NetworkWatchService(_settings, _store, New DiscoveryScanner(), SynchronizationContext.Current)
            AddHandler _autoSave.Tick, Sub() AutoSaveNow()
            AddHandler _watch.Progressed, AddressOf OnWatchProgress
            AddHandler _watch.Finished, AddressOf OnWatchFinished
            AddHandler _watch.NetworkChanged, AddressOf OnNetworkChanged
            AddHandler _watch.StateChanged, Sub() _owner.PostEvent("inventory.state", _watch.View())
            ' avvisi dell'archivio (dispositivo nuovo, sospetto ricomparso): schedina nella pagina e, a finestra nascosta, avviso di Windows
            AddHandler _watch.AlertRaised, Sub(s, a)
                                               _owner.PostEvent("alert", a)
                                               If a.Level = "attention" Then _owner.ShowNotice(a.Title, If(a.Detail, ""))
                                           End Sub
        End Sub

        Public Shared ReadOnly Property Version As String
            Get
                Dim v = GetType(AppBridge).Assembly.GetName().Version
                Return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}"
            End Get
        End Property

        ''' <summary>Testo dell'errore per la pagina.</summary>
        Public Shared Function Describe(ex As Exception) As String
            If TypeOf ex Is IOException Then Return $"Operazione sul file non riuscita: {ex.Message}"
            Return ex.Message
        End Function

        Public Async Function RunAsync(command As String, args As JsonElement) As Task(Of Object)
            Select Case command
                Case "start" : Return Start()
                Case "state" : Return State()
                Case "adapters"
                    _adapters = Nothing
                    Return Adapters()
                Case "scanNetwork" : Return StartNetworkScan(args)
                Case "rescanHost" : Return Await RescanHostAsync(args)
                Case "captureStart" : Return Await StartCaptureAsync(args)
                Case "captureStop"
                    _capture.RequestStop()
                    Return Nothing
                Case "captureResult" : Return _captureSummary
                Case "captureSave" : Return SaveCapture()
                Case "captureOpen"
                    If Not File.Exists(_capture.PcapFile) Then Throw New UserMessageException("Non c'è ancora nessuna cattura da aprire.")
                    Try
                        Process.Start(New ProcessStartInfo(_capture.PcapFile) With {.UseShellExecute = True})
                    Catch ex As Exception
                        Throw New UserMessageException("Nessun programma apre i file .pcapng: installa Wireshark (wireshark.org) oppure salva la cattura e aprila altrove.")
                    End Try
                    Return Nothing
                Case "perfStart"
                    RequireInternet("Test delle prestazioni")
                    Return StartPerf()
                Case "perfStop"
                    _perfCts?.Cancel()
                    Return Nothing
                Case "quickPing" : Return Await QuickPingAsync(ArgText(args, "target"), Clamp(ArgNumber(args, "count", 4), 1, 10))
                Case "quickLookup" : Return Await QuickLookupAsync(ArgText(args, "target"))
                Case "stopNetwork"
                    If _sessionScan Then _watch.CancelScan()
                    Return Nothing
                Case "inventory" : Return InventoryView(ArgText(args, "query"), ArgFlag(args, "onlyOnline"), ArgText(args, "network"))
                Case "inventoryHost" : Return InventoryHost(ArgLong(args, "id"))
                Case "inventoryBulk" : Return InventoryBulk(args)
                Case "inventorySave" : Return InventorySave(args)
                Case "inventoryForget"
                    _store.Network.DeleteHost(ArgLong(args, "id"))
                    Return InventoryView("", False, ArgText(args, "network"))
                Case "inventoryAck"
                    If ArgFlag(args, "all") Then _store.Network.AcknowledgeAll(NetworkFilter(ArgText(args, "network"))) Else _store.Network.Acknowledge(ArgLong(args, "id"))
                    Return New With {.unseen = _store.Network.UnacknowledgedCount(NetworkFilter("")), .state = State()}
                Case "inventoryNetwork" : Return RenameNetwork(ArgText(args, "key"), ArgText(args, "alias"))
                Case "inventoryNetworkDhcp" : Return SetNetworkDhcp(ArgText(args, "key"), ArgText(args, "range"))
                Case "inventoryForgetNetwork"
                    If _watch.IsScanning Then Throw New UserMessageException("C'è una scansione in corso: aspetta che finisca prima di dimenticare una rete.")
                    _store.Network.DeleteNetwork(ArgText(args, "key"))
                    Return InventoryView("", False, "")
                Case "switchNetworkSession" : Return SwitchNetworkSession()
                Case "openSessionsFolder"
                    Directory.CreateDirectory(SessionsFolder)
                    Process.Start(New ProcessStartInfo(SessionsFolder) With {.UseShellExecute = True})
                    Return Nothing
                Case "inventoryScan" : Return StartInventoryScan(ArgText(args, "target"))
                Case "inventoryStop"
                    If _watch.IsScanning AndAlso Not _sessionScan Then _watch.CancelScan()
                    Return Nothing
                Case "clearDevices"
                    If _watch.IsScanning Then Throw New UserMessageException("C'è una scansione in corso: aspetta che finisca prima di svuotare l'archivio.")
                    _devices.Clear()
                    Return SettingsView()
                Case "scanPorts" : Return StartPortScan(args)
                Case "stopPorts"
                    _portCts?.Cancel()
                    Return Nothing
                Case "whois"
                    RequireInternet("WHOIS")
                    Return Await WhoisAsync(ArgText(args, "target"), ArgText(args, "server"))
                Case "dns" : Return Await DnsAsync(ArgText(args, "target"))
                Case "traceroute" : Return StartTraceroute(args)
                Case "stopTrace"
                    _traceCts?.Cancel()
                    Return Nothing
                Case "netstat" : Return Await Task.Run(Function() NetstatInspector.GetActiveConnections())
                Case "killProcess" : Return KillProcess(ArgNumber(args, "pid", 0))
                Case "newSession" : Return NewSession()
                Case "openSession" : Return OpenSession()
                Case "saveSession" : Return SaveSession(ArgFlag(args, "saveAs"))
                Case "sessionInfo" : Return SessionInfo(ArgText(args, "title"), ArgText(args, "notes"))
                Case "clearList" : Return ClearList(ArgText(args, "list"))
                Case "removeHost" : Return RemoveHost(ArgText(args, "ip"))
                Case "hostLabel" : Return HostLabel(args)
                Case "iconsMeta" : Return Await Task.Run(Function() IconsMeta())
                Case "iconsSearch" : Return Await Task.Run(Function() IconsSearch(args))
                Case "iconPaths" : Return IconPaths(args)
                Case "export" : Return Export(ArgText(args, "format"))
                Case "updateOui"
                    RequireInternet("Aggiornamento del database dei produttori")
                    Return Await UpdateOuiAsync()
                Case "openUrl"
                    MainWindow.OpenExternal(ArgText(args, "url"))
                    Return Nothing
                Case "openFile" : Return OpenFile(ArgText(args, "path"))
                Case "checkUpdate"
                    RequireInternet("Controllo degli aggiornamenti")
                    Return Await _updates.CheckAsync(manual:=True, CancellationToken.None)
                Case "installUpdate"
                    RequireInternet("Scaricamento dell'aggiornamento")
                    Return Await InstallUpdateAsync()
                Case "settings" : Return SettingsView()
                Case "saveSettings" : Return SaveSettings(args)
                Case "saveProfile" : Return SaveProfile(args)
                Case "deleteProfile" : Return DeleteProfile(ArgText(args, "id"))
                Case "applyProfile" : Return Await ApplyProfileAsync(args)
                Case "adapterConfigs" : Return Await Task.Run(Function() NetworkConfig.ReadAll())
                Case "computer" : Return Await Task.Run(Function() ComputerInfo.Snapshot())
                Case "dashboard" : Return Await DashboardAsync()
                Case "checkInternet"
                    CheckInternet()
                    Return Nothing
                Case "launch"
                    Try
                        Launcher.Run(ArgText(args, "kind"), ArgText(args, "host"), ArgNumber(args, "port", 0))
                    Catch ex As ArgumentException
                        Throw New UserMessageException(ex.Message)
                    Catch ex As Exception
                        Throw New UserMessageException($"Apertura non riuscita: {ex.Message}")
                    End Try
                    Return Nothing
                Case "getClipboard" : Return If(Clipboard.ContainsText(), Clipboard.GetText(), "")
                Case "setClipboard"
                    Dim value = ArgText(args, "text")
                    If value <> "" Then Clipboard.SetText(value)
                    Return Nothing
                Case Else
                    Throw New UserMessageException($"Comando sconosciuto: {command}")
            End Select
        End Function

#Region "Stato"

        Private Function Start() As Object
            Dim unused = AutoCheckUpdateAsync()
            Dim unusedOui = AutoUpdateOuiAsync()
            CheckInternet()
            _internetTimer.Start()
            _watch.Start()
            ' il catalogo delle icone dei dispositivi si prepara subito, fuori dalla finestra: la scelta poi si apre già pronta
            Dim unusedIcons = Task.Run(Function() IconCatalog.Count)
            Dim reopened = OpenStartupSession()
            Return New With {.state = State(), .session = SessionData(), .reopened = reopened, .autoScan = AutoScanOffer()}
        End Function

#Region "Sessione per rete e salvataggio automatico"

        ''' <summary>Cartella delle sessioni salvate da sole: Documenti\WhoIs Pro\Sessioni (con una cartella dati propria, WHOISPRO_DATA_FOLDER, lì dentro).</summary>
        Public Shared ReadOnly Property SessionsFolder As String
            Get
                If Not String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WHOISPRO_DATA_FOLDER")) Then Return Path.Combine(MainWindow.LocalFolder, "Sessioni")
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WhoIs Pro", "Sessioni")
            End Get
        End Property

        ''' <summary>Lega la sessione a una rete: la chiave e il nome (quello dato dall'utente, altrimenti l'etichetta) restano nella sessione.</summary>
        Private Sub BindSessionNetwork(network As NetworkIdentity)
            If network Is Nothing Then Return
            _session.NetworkKey = network.Key
            _session.NetworkName = If(_store.Network.GetNetwork(network.Key)?.DisplayName, network.Label)
        End Sub

        ''' <summary>Il nome della rete della sessione per il file e per la barra ("" se la sessione non ha una rete).</summary>
        Private Function SessionNetworkName() As String
            If String.IsNullOrEmpty(_session.NetworkKey) Then Return ""
            Return If(_store.Network.GetNetwork(_session.NetworkKey)?.DisplayName, If(_session.NetworkName, ""))
        End Function

        ''' <summary>Per ogni rete l'ultimo file di sessione usato lì: così all'avvio, in quella rete, si riapre quello.</summary>
        Private Sub RememberNetworkSession()
            If String.IsNullOrEmpty(_session.NetworkKey) OrElse _file = "" Then Return
            Dim known As String = Nothing
            If _settings.NetworkSessions.TryGetValue(_session.NetworkKey, known) AndAlso String.Equals(known, _file, StringComparison.OrdinalIgnoreCase) Then Return
            _settings.NetworkSessions(_session.NetworkKey) = _file
            Try
                SettingsStore.Save(MainWindow.LocalFolder, _settings)
            Catch
                ' le impostazioni non si scrivono: al prossimo avvio si parte da una sessione nuova
            End Try
        End Sub

        ''' <summary>Nome di file per la sessione della sua rete: il nome della rete ripulito; se il file esiste già (un'altra sessione della stessa rete), con data e ora.</summary>
        Private Function DefaultSessionPath() As String
            Dim name = SessionNetworkName()
            If name = "" Then name = "Sessione"
            For Each c In Path.GetInvalidFileNameChars()
                name = name.Replace(c, "-"c)
            Next
            name = name.Replace("·", "-").Trim()
            If name.Length > 60 Then name = name.Substring(0, 60).Trim()
            Dim candidate = Path.Combine(SessionsFolder, name & ".whoispro")
            If File.Exists(candidate) Then candidate = Path.Combine(SessionsFolder, $"{name} {DateTime.Now:yyyy-MM-dd HHmm}.whoispro")
            Return candidate
        End Function

        ''' <summary>Qualcosa da conservare: dati, scansioni, oppure note e titolo scritti dall'utente.</summary>
        Private ReadOnly Property HasAnyData As Boolean
            Get
                Return _session.Hosts.Count + _session.Ports.Count + _session.WhoisEntries.Count + _session.DnsRecords.Count + _session.TracerouteHops.Count > 0 OrElse
                       (_session.Scans IsNot Nothing AndAlso _session.Scans.Count > 0) OrElse (_session.PerfTests IsNot Nothing AndAlso _session.PerfTests.Count > 0) OrElse
                       Not String.IsNullOrWhiteSpace(_session.Notes)
            End Get
        End Property

        ''' <summary>Il salvataggio automatico riparte da capo a ogni modifica: scrive 2,5 secondi dopo l'ultima.</summary>
        Private Sub ScheduleAutoSave()
            If Not _settings.AutoSaveSession OrElse _autoSaveFailed Then Return
            _autoSave.Stop()
            _autoSave.Start()
        End Sub

        ''' <summary>
        ''' Salva adesso, se il salvataggio automatico è attivo e c'è qualcosa da salvare: nel file della sessione, oppure nel
        ''' file della sua rete in Documenti\WhoIs Pro\Sessioni. Un errore ferma i tentativi fino al prossimo salvataggio a mano.
        ''' </summary>
        Public Sub AutoSaveNow()
            _autoSave.Stop()
            If Not _settings.AutoSaveSession OrElse Not _dirty OrElse _autoSaveFailed OrElse Not HasAnyData Then Return
            Try
                If String.IsNullOrEmpty(_session.NetworkKey) Then BindSessionNetwork(_watch.CurrentNetwork())
                If _file = "" Then
                    Directory.CreateDirectory(SessionsFolder)
                    _file = DefaultSessionPath()
                End If
                Dim res = AtomicFileManager.SaveSessionAtomic(_session, _file)
                If Not res.IsSuccess Then Throw New IOException(res.ErrorMessage)
                _dirty = False
                _autoSavedAt = DateTime.Now
                RememberSession(_file)
                RememberNetworkSession()
                _owner.PostEvent("session.saved", New With {.file = _file, .at = _autoSavedAt, .auto = True, .state = State()})
            Catch ex As Exception
                _autoSaveFailed = True
                _owner.PostEvent("session.autosaveFailed", New With {.file = _file, .error = Describe(ex), .state = State()})
            End Try
        End Sub

        ''' <summary>
        ''' All'avvio: con il salvataggio automatico si riapre la sessione della rete in cui si trova il PC (l'ultimo file usato
        ''' lì), altrimenti si parte con una sessione nuova legata a quella rete; senza, l'ultima sessione usata, come prima.
        ''' </summary>
        Private Function OpenStartupSession() As Object
            If Not _settings.AutoSaveSession Then Return ReopenLastSession()
            If _reopenTried Then Return Nothing
            _reopenTried = True
            Dim network = _watch.CurrentNetwork()
            BindSessionNetwork(network)
            Dim name = SessionNetworkName()
            Dim path As String = Nothing
            If network IsNot Nothing Then _settings.NetworkSessions.TryGetValue(network.Key, path)
            If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then
                Return New With {.network = name, .fresh = True, .known = network IsNot Nothing, .error = ""}
            End If
            Try
                Dim restored = False
                Dim loaded = AtomicFileManager.LoadSessionSafe(path, restored)
                If loaded Is Nothing Then Throw New InvalidDataException("il file non contiene una sessione di WhoIs Pro")
                _session = loaded
                _file = path
                _dirty = restored
                BindSessionNetwork(network)
                Return New With {.file = path, .network = name, .restoredFromBackup = restored, .error = ""}
            Catch ex As Exception
                Return New With {.file = path, .network = name, .restoredFromBackup = False, .error = ex.Message}
            End Try
        End Function

        ''' <summary>Il PC è passato a un'altra rete (altro cliente, casa): la pagina propone la sessione di quella rete.</summary>
        Private Sub OnNetworkChanged(sender As Object, network As NetworkIdentity)
            Dim name = If(_store.Network.GetNetwork(network.Key)?.DisplayName, network.Label)
            Dim path As String = Nothing
            _settings.NetworkSessions.TryGetValue(network.Key, path)
            _owner.PostEvent("network.changed", New With {
                .key = network.Key, .name = name, .sameSession = String.Equals(_session.NetworkKey, network.Key, StringComparison.Ordinal),
                .hasSession = Not String.IsNullOrEmpty(path) AndAlso File.Exists(path), .autoSave = _settings.AutoSaveSession, .state = State()
            })
        End Sub

        ''' <summary>«Passa alla sessione di questa rete»: salva quella attuale (se automatico), poi apre il file della rete o ne inizia una nuova.</summary>
        Private Function SwitchNetworkSession() As Object
            Dim network = _watch.CurrentNetwork(refresh:=True)
            If network Is Nothing Then Throw New UserMessageException("Nessuna rete locale riconosciuta adesso: collega il PC a una rete.")
            If _settings.AutoSaveSession Then
                AutoSaveNow()
            ElseIf HasUnsavedData Then
                Throw New UserMessageException("La sessione ha dati non salvati: salvala prima di passare a un'altra rete.")
            End If
            StopAll()
            Dim path As String = Nothing
            _settings.NetworkSessions.TryGetValue(network.Key, path)
            _session = New ScanSession()
            _file = ""
            _dirty = False
            _autoSaveFailed = False
            Dim reopened = False
            If Not String.IsNullOrEmpty(path) AndAlso File.Exists(path) Then
                Try
                    Dim restored = False
                    Dim loaded = AtomicFileManager.LoadSessionSafe(path, restored)
                    If loaded IsNot Nothing Then
                        _session = loaded
                        _file = path
                        _dirty = restored
                        reopened = True
                    End If
                Catch
                    ' file illeggibile: si parte da una sessione nuova per questa rete
                End Try
            End If
            BindSessionNetwork(network)
            RememberSession(_file)
            Return WrapSession(New With {.network = SessionNetworkName(), .reopened = reopened, .file = _file})
        End Function

#End Region

        Private _reopenTried As Boolean
        Private _autoScanOffered As Boolean

        ''' <summary>
        ''' Una volta sola, all'avvio: i secondi prima della scansione della rete automatica, se è attiva nelle impostazioni
        ''' (-1 = niente scansione). Se la pagina si ricarica non riparte.
        ''' </summary>
        Private Function AutoScanOffer() As Integer
            If _autoScanOffered Then Return -1
            _autoScanOffered = True
            Return If(_settings.AutoScan, Math.Clamp(_settings.AutoScanDelay, 0, 600), -1)
        End Function

        ''' <summary>
        ''' Una volta sola, all'avvio: riapre l'ultima sessione salvata o aperta, se il file c'è ancora; altrimenti resta la
        ''' sessione nuova. Il risultato va alla pagina per l'avviso (file riaperto, copia .bak o file sparito).
        ''' </summary>
        Private Function ReopenLastSession() As Object
            If _reopenTried Then Return Nothing
            _reopenTried = True
            Dim last = If(_settings.LastSession, "")
            If last = "" OrElse _file <> "" Then Return Nothing
            If Not File.Exists(last) Then
                RememberSession("")
                Return New With {.file = last, .missing = True, .error = ""}
            End If
            Try
                Dim restored = False
                Dim loaded = AtomicFileManager.LoadSessionSafe(last, restored)
                If loaded Is Nothing Then Throw New InvalidDataException("il file non contiene una sessione di WhoIs Pro")
                _session = loaded
                _file = last
                _dirty = restored
                Return New With {.file = last, .missing = False, .restoredFromBackup = restored, .error = ""}
            Catch ex As Exception
                Return New With {.file = last, .missing = False, .restoredFromBackup = False, .error = ex.Message}
            End Try
        End Function

        ''' <summary>L'ultima sessione da riaprire al prossimo avvio ("" = nessuna).</summary>
        Private Sub RememberSession(path As String)
            If String.Equals(If(_settings.LastSession, ""), path, StringComparison.OrdinalIgnoreCase) Then Return
            _settings.LastSession = path
            Try
                SettingsStore.Save(MainWindow.LocalFolder, _settings)
            Catch
                ' le impostazioni non si scrivono: al prossimo avvio si parte da una sessione nuova
            End Try
        End Sub

#Region "Internet"

        ''' <summary>
        ''' Prima di contattare un servizio esterno (GitHub, IEEE, WHOIS/RDAP, Cloudflare): se il controllo periodico dice
        ''' che Internet non c'è, il comando si ferma subito con un messaggio chiaro invece di aspettare un errore di rete.
        ''' Finché il primo controllo non ha risposto (avvio) si prova lo stesso.
        ''' </summary>
        Private Sub RequireInternet(what As String)
            If _online.HasValue AndAlso Not _online.Value Then
                CheckInternet()
                Throw New UserMessageException($"{what}: serve Internet, e adesso la connessione non c'è. Riprova quando in alto torna «Internet» in verde.")
            End If
        End Sub

        ''' <summary>Vero se una scheda collegata ha un indirizzo IPv6 utilizzabile su Internet (globale o temporaneo).</summary>
        Private Shared Function HasPublicIpv6() As Boolean
            Try
                Return Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().
                    Where(Function(n) n.OperationalStatus = Net.NetworkInformation.OperationalStatus.Up AndAlso n.NetworkInterfaceType <> Net.NetworkInformation.NetworkInterfaceType.Loopback).
                    Any(Function(n) Network.Ipv6.HasPublic(Network.Ipv6.AddressesOf(n.GetIPProperties())))
            Catch
                Return False
            End Try
        End Function

        ''' <summary>Connessione a Internet: una richiesta leggera alla pagina di prova di Windows (NCSI), ogni 30 secondi; l'esito va alla pagina solo se cambia.</summary>
        Private Async Sub CheckInternet()
            If _checkingInternet Then Return
            _checkingInternet = True
            Dim online As Boolean
            Dim elapsed As Integer? = Nothing
            Try
                If Not Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() Then
                    online = False
                Else
                    Dim watch = Stopwatch.StartNew()
                    Using request As New HttpRequestMessage(HttpMethod.Head, "http://www.msftconnecttest.com/connecttest.txt")
                        Using response = Await InternetProbe.SendAsync(request)
                            online = response.IsSuccessStatusCode
                        End Using
                    End Using
                    If online Then elapsed = CInt(watch.ElapsedMilliseconds)
                End If
            Catch
                online = False
            End Try
            ' tempo di risposta della stessa pagina di prova: la Panoramica lo mostra come «risposta di Internet»
            _internetMs = elapsed
            ' Internet anche in IPv6: la pagina di prova di Windows per IPv6 ha solo l'indirizzo IPv6. Si prova solo se una
            ' scheda ha un IPv6 pubblico: senza, la risposta è già «no» e la richiesta sarebbe inutile.
            Dim online6 = False
            If online AndAlso HasPublicIpv6() Then
                Try
                    Using request As New HttpRequestMessage(HttpMethod.Head, "http://ipv6.msftconnecttest.com/connecttest.txt")
                        Using response = Await InternetProbe.SendAsync(request)
                            online6 = response.IsSuccessStatusCode
                        End Using
                    End Using
                Catch
                    online6 = False
                End Try
            End If
            _online6 = online6
            _checkingInternet = False
            If Not _online.HasValue OrElse _online.Value <> online Then
                _online = online
                _owner.PostEvent("internet", New With {.online = online})
                ' controllo degli aggiornamenti rimandato perché mancava Internet: parte adesso
                If online AndAlso _updatePending Then
                    Dim unused = AutoCheckUpdateAsync(delay:=False)
                End If
                If online AndAlso _ouiPending Then
                    Dim unusedOui = AutoUpdateOuiAsync(delay:=False)
                End If
            End If
        End Sub

        ''' <summary>
        ''' Dati dal vivo della Panoramica, chiesti ogni due secondi: traffico delle schede, processore, memoria, disco e
        ''' risposta di Internet (l'ultimo controllo, ogni 30 secondi). Le letture girano fuori dal thread della finestra.
        ''' </summary>
        Private Async Function DashboardAsync() As Task(Of Object)
            Dim traffic = Await Task.Run(Function() TrafficMonitor.Read())
            Dim machine = Await Task.Run(Function() ComputerInfo.Summary())
            Return New With {
                .at = DateTime.Now,
                .traffic = traffic,
                .system = machine,
                .internet = New With {.online = _online, .latencyMs = _internetMs, .ipv6 = _online6}
            }
        End Function

#End Region

#Region "Impostazioni e profili di rete"

        Private Function SettingsView() As Object
            _settings.AutoStart = SettingsStore.AutoStartEnabled()
            Return New With {
                .autoStart = _settings.AutoStart,
                .trayIcon = _settings.TrayIcon,
                .closeToTray = _settings.CloseToTray,
                .startMinimized = _settings.StartMinimized,
                .theme = _settings.Theme,
                .autoScan = _settings.AutoScan,
                .autoScanDelay = _settings.AutoScanDelay,
                .autoInstallUpdates = _settings.AutoInstallUpdates,
                .autoUpdateOui = _settings.AutoUpdateOui,
                .autoSaveSession = _settings.AutoSaveSession,
                .sessionsFolder = SessionsFolder,
                .profiles = _settings.Profiles,
                .networkWatchEnabled = _settings.NetworkWatchEnabled,
                .networkScanIntervalMinutes = _settings.NetworkScanIntervalMinutes,
                .networkScanTarget = _settings.NetworkScanTarget,
                .networkScanTimeoutMs = _settings.NetworkScanTimeoutMs,
                .notifyNewDevices = _settings.NotifyNewDevices,
                .networks = _watch.Scanner.GetLocalNetworks(),
                .dataFolder = MainWindow.LocalFolder,
                .devices = _devices.Count,
                .devicesFile = _devices.FilePath,
                .exePath = Environment.ProcessPath
            }
        End Function

        Private Function SaveSettings(args As JsonElement) As Object
            Dim value As JsonElement
            If args.TryGetProperty("autoStart", value) AndAlso (value.ValueKind = JsonValueKind.True OrElse value.ValueKind = JsonValueKind.False) Then
                Dim enabled = value.ValueKind = JsonValueKind.True
                Try
                    SettingsStore.SetAutoStart(enabled)
                Catch ex As Exception
                    Throw New UserMessageException($"Avvio automatico non cambiato: {ex.Message}")
                End Try
                _settings.AutoStart = enabled
            End If
            If args.TryGetProperty("trayIcon", value) Then _settings.TrayIcon = value.ValueKind = JsonValueKind.True
            If args.TryGetProperty("closeToTray", value) Then _settings.CloseToTray = value.ValueKind = JsonValueKind.True
            If args.TryGetProperty("startMinimized", value) Then _settings.StartMinimized = value.ValueKind = JsonValueKind.True
            If args.TryGetProperty("autoScan", value) Then _settings.AutoScan = value.ValueKind = JsonValueKind.True
            If args.TryGetProperty("autoInstallUpdates", value) Then _settings.AutoInstallUpdates = value.ValueKind = JsonValueKind.True
            If args.TryGetProperty("autoUpdateOui", value) Then _settings.AutoUpdateOui = value.ValueKind = JsonValueKind.True
            Dim delay As Integer
            If args.TryGetProperty("autoScanDelay", value) AndAlso value.ValueKind = JsonValueKind.Number AndAlso value.TryGetInt32(delay) Then
                _settings.AutoScanDelay = Math.Clamp(delay, 0, 600)
            End If
            Dim theme = ArgText(args, "theme")
            If theme = "auto" OrElse theme = "light" OrElse theme = "dark" Then _settings.Theme = theme
            ' sessione salvata da sola, una per rete
            If args.TryGetProperty("autoSaveSession", value) Then
                _settings.AutoSaveSession = value.ValueKind = JsonValueKind.True
                _autoSaveFailed = False
                If _settings.AutoSaveSession AndAlso _dirty Then ScheduleAutoSave()
            End If
            ' archivio della rete: scansioni automatiche e avvisi
            If args.TryGetProperty("networkWatchEnabled", value) Then _settings.NetworkWatchEnabled = value.ValueKind = JsonValueKind.True
            If args.TryGetProperty("notifyNewDevices", value) Then _settings.NotifyNewDevices = value.ValueKind = JsonValueKind.True
            If args.TryGetProperty("networkScanIntervalMinutes", value) Then _settings.NetworkScanIntervalMinutes = Clamp(ArgNumber(args, "networkScanIntervalMinutes", 60), 5, 1440)
            If args.TryGetProperty("networkScanTimeoutMs", value) Then _settings.NetworkScanTimeoutMs = Clamp(ArgNumber(args, "networkScanTimeoutMs", 700), 100, 10000)
            If args.TryGetProperty("networkScanTarget", value) AndAlso value.ValueKind = JsonValueKind.String Then _settings.NetworkScanTarget = value.GetString().Trim()
            SettingsStore.Save(MainWindow.LocalFolder, _settings)
            _owner.ApplyTraySettings(_settings)
            _owner.ApplyTheme(_settings.Theme)
            _watch.ApplySettings()
            Return SettingsView()
        End Function

        Private Shared Function ReadProfile(args As JsonElement) As NetworkProfile
            Dim p As New NetworkProfile With {
                .Name = ArgText(args, "name").Trim(),
                .Adapter = ArgText(args, "adapter").Trim(),
                .Dhcp = ArgFlag(args, "dhcp"),
                .Ip = ArgText(args, "ip").Trim(),
                .Mask = ArgText(args, "mask").Trim(),
                .Gateway = ArgText(args, "gateway").Trim(),
                .Dns1 = ArgText(args, "dns1").Trim(),
                .Dns2 = ArgText(args, "dns2").Trim()
            }
            Dim id = ArgText(args, "id")
            If id <> "" Then p.Id = id
            Return p
        End Function

        Private Function SaveProfile(args As JsonElement) As Object
            Dim p = ReadProfile(args)
            If p.Name = "" Then Throw New UserMessageException("Dai un nome al profilo (per esempio il nome del sito).")
            Try
                NetworkConfig.Validate(p)
            Catch ex As ArgumentException
                Throw New UserMessageException(ex.Message)
            End Try
            Dim idx = _settings.Profiles.FindIndex(Function(x) x.Id = p.Id)
            If idx >= 0 Then _settings.Profiles(idx) = p Else _settings.Profiles.Add(p)
            SettingsStore.Save(MainWindow.LocalFolder, _settings)
            Return SettingsView()
        End Function

        Private Function DeleteProfile(id As String) As Object
            _settings.Profiles.RemoveAll(Function(x) x.Id = id)
            SettingsStore.Save(MainWindow.LocalFolder, _settings)
            Return SettingsView()
        End Function

        ''' <summary>Applica un profilo salvato (id) oppure i valori passati; Windows chiede il consenso UAC per netsh.</summary>
        Private Async Function ApplyProfileAsync(args As JsonElement) As Task(Of Object)
            Dim id = ArgText(args, "id")
            Dim p = If(id <> "", _settings.Profiles.FirstOrDefault(Function(x) x.Id = id), Nothing)
            If p Is Nothing Then p = ReadProfile(args)
            Dim configs = Await ApplyCoreAsync(p)
            Return New With {.state = State(), .adapters = configs, .profile = p}
        End Function

        ''' <summary>Scrive il profilo nella scheda con netsh (consenso UAC) e rilegge le schede; errori già in italiano.</summary>
        Private Async Function ApplyCoreAsync(p As NetworkProfile) As Task(Of List(Of AdapterConfig))
            Try
                NetworkConfig.Validate(p)
            Catch ex As ArgumentException
                Throw New UserMessageException(ex.Message)
            End Try
            Dim code As Integer
            Try
                code = Await Task.Run(Function() NetworkConfig.Apply(p, MainWindow.LocalFolder))
            Catch ex As OperationCanceledException
                Throw New UserMessageException(ex.Message)
            Catch ex As Exception
                Throw New UserMessageException($"Configurazione non applicata: {ex.Message}")
            End Try
            If code <> 0 Then Throw New UserMessageException($"netsh ha risposto con il codice {code}: controlla il nome della scheda e gli indirizzi.")
            _adapters = Nothing
            Await Task.Delay(1500)
            Return NetworkConfig.ReadAll()
        End Function

        ''' <summary>
        ''' Profilo scelto dal menu dell'icona nell'area di notifica (o «Torna a DHCP» di una scheda): lo applica, lo dice con
        ''' l'avviso di Windows e aggiorna la pagina, se è aperta.
        ''' </summary>
        Public Async Function ApplyFromTrayAsync(p As NetworkProfile) As Task
            Try
                Dim configs = Await ApplyCoreAsync(p)
                Dim a = configs.FirstOrDefault(Function(x) String.Equals(x.Name, p.Adapter, StringComparison.OrdinalIgnoreCase))
                Dim detail = If(a Is Nothing OrElse a.Ip = "", p.Adapter, $"{p.Adapter}: {If(a.Dhcp, "DHCP", "statico")} · {a.Ip}")
                _owner.ShowTrayMessage($"Profilo «{p.Name}» applicato", detail, ToolTipIcon.Info)
                _owner.PostEvent("profile.applied", New With {.profile = p, .adapters = configs, .state = State()})
            Catch ex As Exception
                _owner.ShowTrayMessage($"Profilo «{p.Name}» non applicato", Describe(ex), ToolTipIcon.Warning)
            End Try
        End Function

        ''' <summary>Profili salvati e configurazione attuale delle schede, per il menu dell'icona.</summary>
        Public Function TrayProfiles() As (Profiles As List(Of NetworkProfile), Adapters As List(Of AdapterConfig))
            Dim configs As List(Of AdapterConfig)
            Try
                configs = NetworkConfig.ReadAll()
            Catch
                configs = New List(Of AdapterConfig)()
            End Try
            Return (_settings.Profiles.ToList(), configs)
        End Function

#End Region

        Private _updatePending As Boolean

        ''' <summary>
        ''' Controllo delle nuove versioni a ogni avvio (anche il primo dopo l'installazione), qualche secondo dopo, solo con
        ''' Internet: se la connessione non c'è il controllo aspetta e parte appena torna (CheckInternet). Con una versione
        ''' nuova e l'aggiornamento automatico attivo la scarica e la installa: il setup chiude il programma e lo riapre.
        ''' </summary>
        Private Async Function AutoCheckUpdateAsync(Optional delay As Boolean = True) As Task
            Try
                If delay Then Await Task.Delay(TimeSpan.FromSeconds(6))
                If Not (_online.HasValue AndAlso _online.Value) Then
                    _updatePending = True
                    Return
                End If
                _updatePending = False
                Dim status = Await _updates.CheckAsync(manual:=False, CancellationToken.None)
                _owner.PostEvent("update.status", status)
                If CanAutoInstall(status) Then
                    _owner.PostEvent("update.auto", New With {.version = status.Version})
                    Await InstallUpdateAsync()
                End If
            Catch ex As Exception
                ' errore del servizio o dello scaricamento: resta l'avviso nella pagina, si riprova al prossimo avvio
            End Try
        End Function

        ''' <summary>
        ''' Installazione automatica solo se è attiva, la copia è quella del setup (con il setup pubblicato e verificabile) e
        ''' chiudere non fa perdere niente: nessun dato da salvare e nessuna scansione o test in corso. Altrimenti resta
        ''' l'avviso con «Scarica e installa».
        ''' </summary>
        Private Function CanAutoInstall(status As ReleaseStatus) As Boolean
            If Not (_settings.AutoInstallUpdates AndAlso status.Available AndAlso status.Installed AndAlso status.HasSetup) Then Return False
            If _installing OrElse HasUnsavedData Then Return False
            Return Not _watch.IsScanning AndAlso _portCts Is Nothing AndAlso _traceCts Is Nothing AndAlso Not _ouiBusy AndAlso
                   _perfCts Is Nothing AndAlso Not _captureActive
        End Function

        ''' <summary>«Scarica e installa»: alla fine il setup è partito e la finestra si chiude da sola.</summary>
        Private Async Function InstallUpdateAsync() As Task(Of Object)
            If _installing Then Throw New UserMessageException("Lo scaricamento è già in corso.")
            _installing = True
            Try
                Dim progress As New Progress(Of DownloadState)(Sub(s) _owner.PostEvent("update.progress", s))
                Dim setup = Await _updates.InstallAsync(progress, CancellationToken.None)
                _owner.BeginInvoke(Sub() _owner.CloseForUpdate())
                Return New With {.setup = setup}
            Catch ex As InvalidOperationException
                Throw New UserMessageException(ex.Message)
            Finally
                _installing = False
            End Try
        End Function

        Private Function State() As Object
            Return New With {
                .version = Version,
                .session = SessionMeta(),
                .oui = New With {.count = MacVendorLookup.TotalVendorsLoaded, .file = MacVendorLookup.SourceFile, .updating = _ouiBusy, .info = MacVendorLookup.DatabaseInfo()},
                .adapters = Adapters(),
                .presets = New With {
                    .ports = PortDictionary.GetPresets().Select(Function(p) New With {.key = p.Key, .name = p.Name, .ports = p.Text}).ToList(),
                    .whoisServers = WhoisServers
                },
                .running = New With {.network = _sessionScan, .inventory = _watch.IsScanning AndAlso Not _sessionScan, .ports = _portCts IsNot Nothing, .trace = _traceCts IsNot Nothing, .oui = _ouiBusy, .perf = _perfCts IsNot Nothing, .capture = _captureActive},
                .inventory = _watch.View(),
                .devices = _devices.Count,
                .update = _updates.Status(),
                .online = _online,
                .theme = _settings.Theme,
                .copyright = "© 2026 Bruno Leonardi. Tutti i diritti riservati."
            }
        End Function

        ''' <summary>La sessione senza gli elenchi: titolo, file, stato di salvataggio e conteggi.</summary>
        Private Function SessionMeta() As Object
            Return New With {
                .title = _session.SessionTitle,
                .notes = _session.Notes,
                .createdAt = _session.CreatedAt,
                .modifiedAt = _session.ModifiedAt,
                .checksum = _session.ChecksumSHA256,
                .file = _file,
                .fileName = If(_file = "", "", Path.GetFileName(_file)),
                .dirty = _dirty,
                .autoSave = _settings.AutoSaveSession,
                .autoSavedAt = _autoSavedAt,
                .autoSaveFailed = _autoSaveFailed,
                .networkKey = _session.NetworkKey,
                .networkName = SessionNetworkName(),
                .sessionsFolder = SessionsFolder,
                .counts = New With {
                    .hosts = _session.Hosts.Count,
                    .ports = _session.Ports.Count,
                    .whois = _session.WhoisEntries.Count,
                    .dns = _session.DnsRecords.Count,
                    .trace = _session.TracerouteHops.Count
                }
            }
        End Function

        ''' <summary>Gli elenchi della sessione, per la pagina.</summary>
        Private Function SessionData() As Object
            Return New With {
                .hosts = _session.Hosts,
                .ports = _session.Ports,
                .whois = _session.WhoisEntries,
                .dns = _session.DnsRecords,
                .trace = _session.TracerouteHops,
                .scans = If(_session.Scans, New List(Of ScanRecord)()),
                .perf = If(_session.PerfTests, New List(Of PerfResult)())
            }
        End Function

        ''' <summary>Risposta dei comandi che cambiano qualcosa: lo stato aggiornato e i dati del comando.</summary>
        Private Function Wrap(Optional extra As Object = Nothing) As Object
            Return New With {.state = State(), .result = extra}
        End Function

        ''' <summary>Risposta dei comandi che sostituiscono la sessione: stato ed elenchi insieme.</summary>
        Private Function WrapSession(Optional extra As Object = Nothing) As Object
            Return New With {.state = State(), .session = SessionData(), .result = extra}
        End Function

        ''' <summary>Schede di rete attive: prima quelle con il gateway (la rete vera), in fondo quelle con indirizzo automatico 169.254.</summary>
        Private Function Adapters() As Object
            If _adapters Is Nothing Then
                _adapters = NetworkUtils.GetLocalAdapters().
                    OrderBy(Function(a) If(a.IPAddress IsNot Nothing AndAlso a.IPAddress.ToString().StartsWith("169.254."), 2, If(a.Gateway Is Nothing, 1, 0))).
                    ThenBy(Function(a) a.Name).ToList()
            End If
            Return _adapters.Select(Function(a) New With {
                .name = a.Name,
                .description = a.Description,
                .ip = a.IPAddress?.ToString(),
                .mask = a.SubnetMask?.ToString(),
                .gateway = a.Gateway?.ToString(),
                .mac = a.MacAddress,
                .cidr = a.SubnetCidr,
                .ipv6 = a.Ipv6
            }).ToList()
        End Function

        Private Sub Touch()
            _dirty = True
            _session.ModifiedAt = DateTime.Now
            ScheduleAutoSave()
        End Sub

        ''' <summary>Vero se ci sono dati non salvati (la finestra lo chiede prima di chiudere).</summary>
        Public ReadOnly Property HasUnsavedData As Boolean
            Get
                Return _dirty AndAlso (_session.Hosts.Count + _session.Ports.Count + _session.WhoisEntries.Count + _session.DnsRecords.Count + _session.TracerouteHops.Count) > 0
            End Get
        End Property

#End Region

#Region "Scansione della rete"

        ''' <summary>
        ''' Scansione dalla pagina Rete con il motore DiscoveryScanner (ping e ARP insieme, nomi DNS e NetBIOS, porte TCP con
        ''' i banner, classificazione), tramite il servizio di sorveglianza: una scansione alla volta. Gli host e le porte
        ''' entrano nella sessione man mano; alla fine la scansione va nell'archivio della rete e le differenze (dispositivi
        ''' nuovi, ricomparsi, cambiati, assenti) tornano alla pagina insieme all'esito.
        ''' </summary>
        Private Function StartNetworkScan(args As JsonElement) As Object
            If _watch.IsScanning Then Throw New UserMessageException(If(_sessionScan, "Una scansione della rete è già in corso: fermala prima di avviarne un'altra.", "È in corso la scansione automatica dell'archivio: aspetta che finisca, oppure fermala dalla pagina Dispositivi."))
            Dim target = ArgText(args, "target").Trim()
            If target = "" Then Throw New UserMessageException("Indica la rete da scansionare, per esempio 192.168.1.0/24 oppure 192.168.1.1-50.")
            Dim targets = NetworkUtils.ParseTargets(target)
            If targets.Count = 0 Then Throw New UserMessageException($"Nessun indirizzo IP valido in «{target}». Si accettano una o più reti separate da virgola (es. 192.168.1.0/24, 192.168.2.0/24), intervalli (192.168.1.1-50) e indirizzi singoli.")
            If targets.Count > 4096 Then Throw New UserMessageException($"«{target}» ha {targets.Count:N0} indirizzi: il massimo è 4.096 indirizzi complessivi (1.024 per singola sottorete /22).")
            ' porte degli host trovati: il gruppo scelto nella pagina, altrimenti le porte comuni del motore
            Dim scanPorts = ArgFlag(args, "autoPorts")
            Dim portList As List(Of Integer) = Nothing
            If scanPorts Then
                portList = PortDictionary.ParsePorts(ArgText(args, "autoPortsList"))
                If portList.Count = 0 Then portList = Nothing
            End If
            Dim options As New ScanOptions With {
                .Target = target,
                .Parallelism = Clamp(ArgNumber(args, "threads", 128), 1, 512),
                .TimeoutMs = Clamp(ArgNumber(args, "timeout", 700), 100, 10000),
                .ResolveNames = ArgFlag(args, "resolveDns"),
                .ScanPorts = scanPorts,
                .Ports = portList,
                .GrabBanners = scanPorts
            }
            _sessionScan = True
            _netOptions = options
            _netTarget = target
            _netTargets = New HashSet(Of String)(targets.Select(Function(a) a.ToString()))
            _netWatch = Stopwatch.StartNew()
            _netFound.Clear()
            _netPorts.Clear()
            _netSeenIps.Clear()
            _netObservedIps.Clear()
            _netNew.Clear()
            _netFirstCensus = _devices.Count = 0
            Dim observer As New Progress(Of ScanPhaseProgress)(AddressOf OnSessionScanProgress)
            Dim unused = _watch.ScanAsync(automatic:=False, options:=options, observer:=observer)
            Return New With {.started = True, .total = targets.Count, .state = State()}
        End Function

        ''' <summary>Avanzamento della scansione della sessione: ogni host trovato o arricchito, con le sue porte, finisce nella sessione.</summary>
        Private Sub OnSessionScanProgress(p As ScanPhaseProgress)
            _netProgress = p
            If p.Host IsNot Nothing Then
                For Each port In p.Host.Ports
                    Dim pi = ToPortInfo(p.Host.Ip, port)
                    AddPort(pi, touchSession:=False)
                    _netPorts.RemoveAll(Function(x) x.Host = pi.Host AndAlso x.PortNumber = pi.PortNumber)
                    _netPorts.Add(pi)
                Next
                Dim info = ToHostInfo(p.Host)
                ' con le porte il tipo lo decide il motore (anche dai banner e dal NetBIOS); senza, aiutano quelle già nella sessione.
                ' Nella fase ping il MAC non c'è ancora: l'archivio si aggiorna nella fase ARP, a fine scansione e la prima volta che
                ' un host compare in un'altra fase, non a ogni fase (meno scritture SQLite a metà scansione)
                Dim shouldObserve = p.Phase = "arp" OrElse p.Phase = "fine" OrElse (p.Phase <> "ping" AndAlso Not _netObservedIps.Contains(info.IPAddress))
                If shouldObserve Then _netObservedIps.Add(info.IPAddress)
                If AddHost(info, classifyFromPorts:=Not _netOptions.ScanPorts, observe:=shouldObserve, network:=_watch.Network) Then _netNew.Add(info)
                _netFound(info.IPAddress) = info
                _netSeenIps.Add(info.IPAddress)
            End If
            If Not _flush.Enabled Then _flush.Start()
        End Sub

        ''' <summary>Avanzamento di una scansione automatica (archivio): solo lo stato per la barra superiore e la pagina Dispositivi.</summary>
        Private Sub OnWatchProgress(sender As Object, p As ScanPhaseProgress)
            If _sessionScan Then Return
            _owner.PostEvent("inventory.progress", New With {
                .phase = p.Phase, .label = ScanPhaseProgress.LabelOf(p.Phase), .done = p.Done, .total = p.Total,
                .percent = p.OverallPercent(If(_watch.Options, New ScanOptions())), .detail = p.Detail
            })
        End Sub

        ''' <summary>
        ''' Fine di una scansione: se era della sessione, host assenti segnati «Offline», IPv6 dalla tabella dei vicini, storico e
        ''' «network.done» alla pagina Rete; in ogni caso «inventory.done» alla pagina Dispositivi, con le differenze dell'archivio.
        ''' </summary>
        Private Async Sub OnWatchFinished(sender As Object, e As ScanFinishedEventArgs)
            Dim diff = e.Outcome?.Diff
            Dim diffView = If(diff Is Nothing, Nothing, New With {
                .isFirst = diff.IsFirst, .summary = diff.Summarize(), .newCount = diff.NewHosts.Count, .returnedCount = diff.ReturnedHosts.Count,
                .goneCount = diff.GoneHosts.Count, .changedCount = diff.ChangedHosts.Count
            })
            If _sessionScan Then
                _sessionScan = False
                _netWatch?.Stop()
                FlushProgress(Nothing, EventArgs.Empty)
                ' fermata a metà, il rapporto finale non c'è: contano gli host arrivati fin lì; a scansione completa vale il
                ' rapporto, dove più IPv4 con lo stesso MAC sono un solo dispositivo
                Dim complete = Not e.Cancelled AndAlso e.Error Is Nothing AndAlso e.HostsFound > 0
                Dim found = If(complete, e.HostsFound, Math.Max(e.HostsFound, _netSeenIps.Count))

                ' host della rete scansionata che questa volta non hanno risposto: restano nella sessione, segnati «Offline»
                Dim offline As New List(Of String)()
                If Not e.Cancelled AndAlso e.Error Is Nothing AndAlso _netTargets IsNot Nothing Then
                    For Each h In _session.Hosts
                        If _netTargets.Contains(h.IPAddress) AndAlso Not _netSeenIps.Contains(h.IPAddress) AndAlso h.Status <> "Offline" Then
                            h.Status = "Offline"
                            offline.Add(h.IPAddress)
                        End If
                    Next
                    If offline.Count > 0 Then Touch()
                End If

                ' IPv6 degli host trovati: un ping a tutti i nodi IPv6, poi la tabella dei vicini, per MAC
                Dim ipv6 = Await FillIpv6Async(wake:=True)

                ' i dispositivi mai visti: al primo censimento (archivio vuoto) non sono una notizia, tutti sarebbero «nuovi»
                Dim fresh = _netNew.ToList()
                Dim firstCensus = If(diff Is Nothing, _netFirstCensus, diff.IsFirst)
                Dim scan = AddScan("network", _netTarget, e.Seconds, found, e.Cancelled, e.Error)
                _owner.PostEvent("network.done", New With {
                    .found = found, .seconds = e.Seconds, .cancelled = e.Cancelled, .error = e.Error, .scan = scan,
                    .offline = offline, .newDevices = fresh, .firstCensus = firstCensus, .ipv6 = ipv6, .diff = diffView, .state = State()
                })
            End If
            _owner.PostEvent("inventory.done", New With {
                .automatic = e.Automatic, .found = e.HostsFound, .seconds = e.Seconds, .cancelled = e.Cancelled, .error = e.Error, .diff = diffView, .state = State()
            })
        End Sub

        ''' <summary>
        ''' Host della sessione da quello trovato dal motore (MAC in maiuscolo come nelle sessioni già salvate). Durante la
        ''' scansione produttore e tipo sono provvisori, calcolati da quello che c'è già; alla fine arriva la classificazione del motore.
        ''' </summary>
        Private Shared Function ToHostInfo(h As DiscoveredHost) As HostInfo
            Dim vendor = If(h.Vendor, MacVendorLookup.Lookup(h.Mac))
            Dim deviceType = h.DeviceType
            Dim category = h.Category
            If deviceType = "Dispositivo" AndAlso category = DeviceClassifier.CategoryOther Then
                Dim probe As New DiscoveredHost With {.Ip = h.Ip, .Mac = h.Mac, .Vendor = vendor, .HostName = h.HostName, .NetBiosName = h.NetBiosName, .Ttl = h.Ttl, .IsGateway = h.IsGateway, .IsLocalMachine = h.IsLocalMachine, .Sadp = h.Sadp}
                probe.Ports.AddRange(h.Ports)
                Dim c = DeviceClassifier.Classify(probe)
                deviceType = c.DeviceType
                category = c.Category
            End If
            Dim osGuess = h.OsGuess
            If osGuess Is Nothing AndAlso h.Sadp IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(h.Sadp.SoftwareVersion) Then
                osGuess = "Firmware " & h.Sadp.SoftwareVersion.Trim()
            End If
            Dim info As New HostInfo With {
                .IPAddress = h.Ip,
                .Hostname = If(h.HostName, ""),
                .NetBiosName = If(h.NetBiosName, ""),
                .Workgroup = If(h.NetBiosWorkgroup, ""),
                .MacAddress = If(h.Mac, "").ToUpperInvariant(),
                .MacVendor = If(vendor, If(h.Mac Is Nothing, "N/D (Remoto / Routed)", "Produttore non catalogato")),
                .DeviceType = deviceType,
                .DeviceCategory = category,
                .DeviceIcon = DeviceClassifier.IconFor(category),
                .Status = "Online",
                .RespondsToPing = h.RespondsToPing,
                .ResponseTimeMs = CLng(If(h.LatencyMs, 0)),
                .TTL = If(h.Ttl, 0),
                .OSGuess = If(osGuess, If(h.RespondsToPing, "Sconosciuto", "Host LAN (solo ARP)")),
                .IsGateway = h.IsGateway,
                .IsLocalMachine = h.IsLocalMachine,
                .OpenPortsCount = h.Ports.Count
            }
            info.OpenPorts.AddRange(h.Ports.Select(Function(p) p.Port))
            info.OtherIpv4.AddRange(h.ExtraIps)
            Return info
        End Function

        ''' <summary>Porta aperta trovata dal motore, come quelle della pagina Porte.</summary>
        Private Shared Function ToPortInfo(ip As String, port As OpenPort) As PortInfo
            Return New PortInfo With {
                .Host = ip, .PortNumber = port.Port, .Protocol = "TCP", .Status = "Open",
                .ServiceName = If(port.Service, PortDictionary.GetServiceName(port.Port)), .Banner = If(port.Banner, ""), .ResponseTimeMs = 0
            }
        End Function

        ''' <summary>
        ''' Indirizzi IPv6 degli host della sessione con un MAC, dalla tabella dei vicini di Windows (con wake, prima un ping
        ''' a tutti i nodi IPv6 così i dispositivi rispondono). Restituisce IPv4 → IPv6 degli host cambiati, per la pagina.
        ''' </summary>
        Private Async Function FillIpv6Async(wake As Boolean) As Task(Of Dictionary(Of String, List(Of String)))
            Dim changed As New Dictionary(Of String, List(Of String))()
            Try
                If wake Then Await Network.Ipv6.WakeNeighborsAsync()
                Dim neighbors = Await Task.Run(Function() Network.Ipv6.Neighbors())
                For Each h In _session.Hosts
                    If String.IsNullOrEmpty(h.MacAddress) Then Continue For
                    Dim list As List(Of String) = Nothing
                    If Not neighbors.TryGetValue(h.MacAddress.Replace("-", ":"), list) Then Continue For
                    If h.Ipv6 IsNot Nothing AndAlso h.Ipv6.SequenceEqual(list) Then Continue For
                    h.Ipv6 = list
                    changed(h.IPAddress) = list
                Next
                If changed.Count > 0 Then Touch()
            Catch
                ' senza IPv6 la scansione resta quella IPv4
            End Try
            Return changed
        End Function

        ''' <summary>
        ''' Riscansione di un solo indirizzo con il motore (ARP, ping, nomi, porte comuni con i banner, tipo), anche mentre
        ''' gira la scansione della rete: aggiorna l'host nella sessione e nell'archivio, oppure lo segna «Offline» se non risponde più.
        ''' </summary>
        Private Async Function RescanHostAsync(args As JsonElement) As Task(Of Object)
            Dim ip = ArgText(args, "ip").Trim()
            Dim address As Net.IPAddress = Nothing
            If Not Net.IPAddress.TryParse(ip, address) OrElse address.AddressFamily <> Net.Sockets.AddressFamily.InterNetwork Then
                Throw New UserMessageException($"«{ip}» non è un indirizzo IPv4.")
            End If
            Dim options As New ScanOptions With {
                .Target = ip, .Parallelism = 32,
                .TimeoutMs = Clamp(ArgNumber(args, "timeout", 700), 100, 10000),
                .ResolveNames = ArgFlag(args, "resolveDns"), .ScanPorts = True, .GrabBanners = True
            }
            Dim watch = Stopwatch.StartNew()
            Dim report As ScanReport
            Try
                report = Await _watch.Scanner.ScanAsync(options, Nothing, CancellationToken.None)
            Catch ex As Exception
                Throw New UserMessageException($"Riscansione non riuscita: {ex.Message}")
            End Try
            Dim discovered = report.Hosts.FirstOrDefault()
            Dim host As HostInfo = Nothing
            Dim isNew = False
            If discovered IsNot Nothing Then
                For Each port In discovered.Ports
                    AddPort(ToPortInfo(discovered.Ip, port))
                Next
                host = ToHostInfo(discovered)
                isNew = AddHost(host, classifyFromPorts:=False, network:=If(report.Network, _watch.CurrentNetwork()))
                Await FillIpv6Async(wake:=False)
            Else
                Dim known = _session.Hosts.FirstOrDefault(Function(h) h.IPAddress = ip)
                If known IsNot Nothing AndAlso known.Status <> "Offline" Then
                    known.Status = "Offline"
                    Touch()
                End If
            End If
            Dim scan = AddScan("host", ip, watch.Elapsed.TotalSeconds, If(host Is Nothing, 0, 1), False, Nothing)
            Return New With {
                .ip = ip, .online = host IsNot Nothing, .isNew = isNew,
                .host = If(host, _session.Hosts.FirstOrDefault(Function(h) h.IPAddress = ip)),
                .scan = scan, .state = State()
            }
        End Function

        ''' <summary>Scansione nello storico della sessione (le ultime 200), per «Ultime scansioni» della Panoramica.</summary>
        Private Function AddScan(kind As String, target As String, seconds As Double, found As Integer, cancelled As Boolean, failure As String) As ScanRecord
            Dim record As New ScanRecord With {
                .Kind = kind, .Target = target, .At = DateTime.Now, .Seconds = Math.Round(seconds, 1),
                .Found = found, .Cancelled = cancelled, .Error = If(failure, "")
            }
            If _session.Scans Is Nothing Then _session.Scans = New List(Of ScanRecord)()
            _session.Scans.Add(record)
            If _session.Scans.Count > 200 Then _session.Scans.RemoveRange(0, _session.Scans.Count - 200)
            Touch()
            Return record
        End Function

        ''' <summary>
        ''' Host nella sessione: aggiornato se c'è già lo stesso indirizzo, altrimenti aggiunto. Prende la prima
        ''' rilevazione, il nome, le note e l'icona dall'archivio della rete (anche da sessioni precedenti) e, se richiesto,
        ''' il tipo dalle porte già trovate. Vero se il dispositivo non era mai stato visto da questo PC.
        ''' </summary>
        Private Function AddHost(h As HostInfo, Optional classifyFromPorts As Boolean = True, Optional observe As Boolean = True, Optional network As NetworkIdentity = Nothing) As Boolean
            Dim idx = _session.Hosts.FindIndex(Function(x) x.IPAddress = h.IPAddress)
            If idx >= 0 Then
                h.FirstSeen = _session.Hosts(idx).FirstSeen
                h.Notes = _session.Hosts(idx).Notes
                h.CustomName = _session.Hosts(idx).CustomName
                h.Icon = _session.Hosts(idx).Icon
                h.IconColor = _session.Hosts(idx).IconColor
                If (h.Ipv6 Is Nothing OrElse h.Ipv6.Count = 0) AndAlso _session.Hosts(idx).Ipv6 IsNot Nothing Then h.Ipv6 = _session.Hosts(idx).Ipv6
                ' riscansione del solo indirizzo principale: gli altri IPv4 dello stesso dispositivo restano
                If (h.OtherIpv4 Is Nothing OrElse h.OtherIpv4.Count = 0) AndAlso _session.Hosts(idx).OtherIpv4 IsNot Nothing Then h.OtherIpv4 = _session.Hosts(idx).OtherIpv4
                _session.Hosts(idx) = h
            Else
                _session.Hosts.Add(h)
            End If
            MergeOtherIpv4(h)
            ApplyOpenPorts(h, classifyFromPorts)
            Dim isNew = observe AndAlso _devices.Observe(h, network)
            Touch()
            Return isNew
        End Function

        ''' <summary>
        ''' Dispositivo con più IPv4 (stesso MAC): le righe della sessione degli indirizzi secondari spariscono nel principale,
        ''' che prende nome, note e icona se non li ha. Le porte restano legate al loro indirizzo (pagina Porte).
        ''' </summary>
        Private Sub MergeOtherIpv4(h As HostInfo)
            If h.OtherIpv4 Is Nothing OrElse h.OtherIpv4.Count = 0 Then Return
            Dim mac = MacVendorLookup.Normalize(h.MacAddress)
            For Each ip In h.OtherIpv4
                Dim other = _session.Hosts.FirstOrDefault(Function(x) x IsNot h AndAlso x.IPAddress = ip)
                If other Is Nothing Then Continue For
                Dim otherMac = MacVendorLookup.Normalize(other.MacAddress)
                If otherMac IsNot Nothing AndAlso Not String.Equals(otherMac, mac, StringComparison.OrdinalIgnoreCase) Then Continue For
                If String.IsNullOrWhiteSpace(h.CustomName) Then h.CustomName = other.CustomName
                If String.IsNullOrWhiteSpace(h.Notes) Then h.Notes = other.Notes
                If String.IsNullOrWhiteSpace(h.Icon) Then
                    h.Icon = other.Icon
                    h.IconColor = other.IconColor
                End If
                If other.FirstSeen < h.FirstSeen Then h.FirstSeen = other.FirstSeen
                _session.Hosts.Remove(other)
                _netNew.RemoveAll(Function(x) x.IPAddress = ip)
                _netFound.Remove(ip)
            Next
        End Sub

        ''' <summary>Porte aperte della sessione sull'host: il conteggio e, se richiesto, il tipo di dispositivo, che con le porte si riconosce meglio.</summary>
        Private Sub ApplyOpenPorts(h As HostInfo, Optional classify As Boolean = True)
            Dim addresses = New HashSet(Of String)(If(h.OtherIpv4, New List(Of String)())) From {h.IPAddress}
            Dim mine = _session.Ports.Where(Function(p) addresses.Contains(p.Host)).GroupBy(Function(p) (p.PortNumber, p.Protocol)).Select(Function(g) g.First()).ToList()
            h.OpenPortsCount = mine.Count
            Dim tcpPorts = mine.Where(Function(p) p.Protocol = "TCP" AndAlso p.Status = "Open").ToList()
            If Not classify OrElse tcpPorts.Count = 0 Then Return
            Dim probe = DeviceRegistry.ToDiscovered(h, tcpPorts.Select(Function(p) New OpenPort(p.PortNumber, "TCP", p.ServiceName, p.Banner)))
            Dim c = DeviceClassifier.Classify(probe)
            h.DeviceType = c.DeviceType
            h.DeviceCategory = c.Category
            h.DeviceIcon = c.Icon
            Dim os = DeviceClassifier.GuessOs(probe)
            If os IsNot Nothing AndAlso (String.IsNullOrWhiteSpace(h.OSGuess) OrElse h.OSGuess = "Sconosciuto" OrElse os.StartsWith("Firmware ", StringComparison.Ordinal)) Then
                h.OSGuess = os
            End If
        End Sub

#End Region

#Region "Scansione delle porte"

        Private Function StartPortScan(args As JsonElement) As Object
            If _portCts IsNot Nothing Then Throw New UserMessageException("Una scansione delle porte è già in corso: fermala prima di avviarne un'altra.")
            Dim host = ArgText(args, "host").Trim()
            If host = "" Then Throw New UserMessageException("Indica l'host da scansionare (indirizzo IP o nome).")
            Dim ports = PortDictionary.ParsePorts(ArgText(args, "ports"))
            If ports.Count = 0 Then Throw New UserMessageException("Nessuna porta valida: scrivi porte singole (22, 80) o intervalli (8000-8100), da 1 a 65535.")
            Dim tcp = ArgFlag(args, "tcp")
            Dim udp = ArgFlag(args, "udp")
            If Not tcp AndAlso Not udp Then Throw New UserMessageException("Scegli almeno un protocollo: TCP o UDP.")

            Dim scanner As New PortScanner With {
                .Concurrency = Clamp(ArgNumber(args, "threads", 100), 1, 1000),
                .TimeoutMs = Clamp(ArgNumber(args, "timeout", 1200), 100, 10000),
                .GrabBanner = ArgFlag(args, "banner")
            }
            _portCts = New CancellationTokenSource()
            _portWatch = Stopwatch.StartNew()
            _portFound.Clear()
            Dim progress As New Progress(Of PortScanProgress)(Sub(p)
                                                                  _portProgress = p
                                                                  Dim found = p.DiscoveredPort
                                                                  If found IsNot Nothing AndAlso (found.Status = "Open" OrElse found.Status = "Open|Filtered") Then
                                                                      AddPort(found)
                                                                      _portFound.Add(found)
                                                                  End If
                                                                  If Not _flush.Enabled Then _flush.Start()
                                                              End Sub)
            Dim unused = RunPortScanAsync(scanner, host, ports, tcp, udp, progress, _portCts)
            Return New With {.started = True, .total = ports.Count * (If(tcp, 1, 0) + If(udp, 1, 0)), .state = State()}
        End Function

        Private Async Function RunPortScanAsync(scanner As PortScanner, host As String, ports As List(Of Integer), tcp As Boolean, udp As Boolean,
                                                progress As IProgress(Of PortScanProgress), cts As CancellationTokenSource) As Task
            Dim found = 0
            Dim cancelled = False
            Dim failure As String = Nothing
            Try
                Dim list = Await scanner.ScanPortsAsync(host, ports, tcp, udp, progress, cts.Token)
                found = list.Count
            Catch ex As OperationCanceledException
                cancelled = True
            Catch ex As Exception
                failure = ex.Message
            End Try
            If cts.IsCancellationRequested Then cancelled = True
            _portWatch.Stop()
            cts.Dispose()
            If ReferenceEquals(_portCts, cts) Then _portCts = Nothing
            FlushProgress(Nothing, EventArgs.Empty)
            Dim scan = AddScan("ports", host, _portWatch.Elapsed.TotalSeconds, found, cancelled, failure)
            _owner.PostEvent("ports.done", New With {
                .host = host, .found = found, .seconds = _portWatch.Elapsed.TotalSeconds, .cancelled = cancelled, .error = failure, .scan = scan, .state = State()
            })
        End Function

        Private Sub AddPort(p As PortInfo, Optional touchSession As Boolean = True)
            Dim idx = _session.Ports.FindIndex(Function(x) x.Host = p.Host AndAlso x.PortNumber = p.PortNumber AndAlso x.Protocol = p.Protocol)
            If idx >= 0 Then
                _session.Ports(idx) = p
            Else
                _session.Ports.Add(p)
            End If
            If touchSession Then Touch()
        End Sub

        ''' <summary>Manda alla pagina l'avanzamento accumulato (timer da 120 ms, o subito alla fine della scansione).</summary>
        Private Sub FlushProgress(sender As Object, e As EventArgs)
            If _netProgress IsNot Nothing Then
                Dim p = _netProgress
                _netProgress = Nothing
                Dim hosts = _netFound.Values.ToList()
                _netFound.Clear()
                Dim ports = _netPorts.ToList()
                _netPorts.Clear()
                _owner.PostEvent("network.progress", New With {
                    .phase = p.Phase, .phaseLabel = ScanPhaseProgress.LabelOf(p.Phase), .scanned = p.Done, .total = p.Total,
                    .percent = p.OverallPercent(If(_netOptions, New ScanOptions())), .currentIp = p.Detail,
                    .seconds = If(_netWatch Is Nothing, 0, _netWatch.Elapsed.TotalSeconds), .hosts = hosts, .ports = ports
                })
            End If
            If _portProgress IsNot Nothing Then
                Dim p = _portProgress
                _portProgress = Nothing
                Dim ports = _portFound.ToList()
                _portFound.Clear()
                _owner.PostEvent("ports.progress", New With {
                    .scanned = p.ScannedPorts, .total = p.TotalPorts, .percent = p.PercentComplete, .currentPort = p.CurrentPort,
                    .seconds = If(_portWatch Is Nothing, 0, _portWatch.Elapsed.TotalSeconds), .ports = ports
                })
            End If
            If _netProgress Is Nothing AndAlso _portProgress Is Nothing Then _flush.Stop()
        End Sub

#End Region

#Region "Archivio della rete"

        ''' <summary>La rete chiesta dalla pagina come filtro dell'archivio: "" = quella in cui si trova il PC, "*" = tutte (Nothing).</summary>
        Private Function NetworkFilter(networkArg As String) As String
            If networkArg = "*" Then Return Nothing
            If Not String.IsNullOrEmpty(networkArg) Then Return networkArg
            Return If(_watch.CurrentNetwork()?.Key, _store.Network.ListNetworks().FirstOrDefault()?.Key)
        End Function

        ''' <summary>
        ''' Tutto quello che serve alla pagina Dispositivi per una rete ("" = quella in cui si trova il PC, o la più recente;
        ''' "*" = tutte): elenco filtrato, cambiamenti, scansioni, digest, le reti conosciute e lo stato della sorveglianza.
        ''' </summary>
        Private Function InventoryView(query As String, onlyOnline As Boolean, networkArg As String) As Object
            Dim inv = _store.Network
            Dim networks = inv.ListNetworks()
            Dim current = _watch.CurrentNetwork()
            Dim filter = NetworkFilter(networkArg)
            Dim names = networks.ToDictionary(Function(n) n.Key, Function(n) n.DisplayName, StringComparer.Ordinal)
            Dim recentNew = inv.ListEvents(300, since:=DateTime.Now.AddHours(-24), networkKey:=filter).Where(Function(e) e.Kind = "nuovo" AndAlso e.HostId.HasValue).Select(Function(e) e.HostId.Value).ToHashSet()
            Dim hosts = inv.ListHosts(query, onlyOnline, filter).Select(Function(h) HostView(h, recentNew.Contains(h.Id), names)).ToList()
            Return New With {
                .network = If(filter, "*"),
                .current = _watch.NetworkView(current),
                .networks = networks.Select(Function(n) New With {
                    .key = n.Key, .label = n.Label, .alias = n.[Alias], .name = n.DisplayName, .hasGateway = n.HasGateway, .cidr = n.Cidr, .gatewayIp = n.GatewayIp, .dhcpRange = n.DhcpRange,
                    .hostsCount = n.HostsCount, .onlineCount = n.OnlineCount, .scansCount = n.ScansCount, .lastScanAt = n.LastScanAt, .lastSeen = n.LastSeen, .unseen = n.Unseen,
                    .isCurrent = current IsNot Nothing AndAlso n.Key = current.Key, .isSession = n.Key = _session.NetworkKey
                }).ToList(),
                .hosts = hosts,
                .total = If(filter Is Nothing, inv.Count, inv.CountIn(filter)),
                .events = inv.ListEvents(400, networkKey:=filter),
                .scans = inv.ListScans(100, filter),
                .digest = inv.Digest(filter),
                .watch = _watch.View()
            }
        End Function

        Private Shared Function HostView(h As NetHostRecord, isNew As Boolean, networkNames As IReadOnlyDictionary(Of String, String)) As Object
            Dim networkName As String = Nothing
            If networkNames IsNot Nothing AndAlso h.NetworkKey IsNot Nothing Then networkNames.TryGetValue(h.NetworkKey, networkName)
            Return New With {
                .id = h.Id, .hostKey = h.HostKey, .mac = If(h.Mac, "").ToUpperInvariant(), .ip = h.Ip, .extraIps = h.ExtraIps, .hostName = h.HostName, .netBios = h.NetBios, .workgroup = h.Workgroup,
                .vendor = h.Vendor, .deviceType = h.DeviceType, .category = h.Category, .osGuess = h.OsGuess, .isGateway = h.IsGateway, .isLocal = h.IsLocal,
                .firstSeen = h.FirstSeen, .lastSeen = h.LastSeen, .seenCount = h.SeenCount, .missCount = h.MissCount, .ports = h.Ports, .latencyMs = h.LatencyMs,
                .alias = h.[Alias], .trust = CInt(h.Trust), .notes = h.Notes, .icon = If(h.Icon, ""), .iconColor = If(h.IconColor, ""),
                .isOnline = h.IsOnline, .displayName = h.DisplayName, .isNew = isNew, .sensitivePorts = h.SensitivePorts,
                .network = h.NetworkKey, .networkName = networkName
            }
        End Function

        ''' <summary>Scheda di un dispositivo con la sua storia (ultimi 30 eventi).</summary>
        Private Function InventoryHost(id As Long) As Object
            Dim h = _store.Network.Get(id)
            If h Is Nothing Then Throw New UserMessageException("Il dispositivo non è più nell'archivio.")
            Dim isNew = _store.Network.ListEvents(50, since:=DateTime.Now.AddHours(-24), hostId:=id).Any(Function(e) e.Kind = "nuovo")
            Dim names = _store.Network.ListNetworks().ToDictionary(Function(n) n.Key, Function(n) n.DisplayName, StringComparer.Ordinal)
            Return New With {.host = HostView(h, isNew, names), .history = _store.Network.ListEvents(30, hostId:=id)}
        End Function

        ''' <summary>
        ''' Selezione multipla nell'elenco dei dispositivi: la stessa fiducia a tutti i selezionati, oppure «dimentica» per tutti.
        ''' Torna l'archivio della rete mostrata, aggiornato.
        ''' </summary>
        Private Function InventoryBulk(args As JsonElement) As Object
            Dim ids = ArgLongs(args, "ids")
            If ids.Count = 0 Then Throw New UserMessageException("Nessun dispositivo selezionato.")
            Select Case ArgText(args, "action")
                Case "trust"
                    _store.Network.SetTrust(ids, CType(Clamp(ArgNumber(args, "trust", 0), 0, 2), HostTrust))
                Case "forget"
                    _store.Network.DeleteHosts(ids)
                Case Else
                    Throw New UserMessageException("Operazione sconosciuta sui dispositivi selezionati.")
            End Select
            Return InventoryView("", False, ArgText(args, "network"))
        End Function

        ''' <summary>Nome dato dall'utente a una rete («Cliente Rossi»): nell'archivio e, se è la rete della sessione, anche nella sessione.</summary>
        Private Function RenameNetwork(key As String, [alias] As String) As Object
            If String.IsNullOrEmpty(key) Then Throw New UserMessageException("Rete non indicata.")
            _store.Network.SetNetworkAlias(key, [alias])
            If String.Equals(_session.NetworkKey, key, StringComparison.Ordinal) Then _session.NetworkName = SessionNetworkName()
            Return InventoryView("", False, key)
        End Function

        ''' <summary>Intervallo DHCP dichiarato dall'utente per una rete (vuoto = toglierlo): le pagine distinguono gli indirizzi assegnati dal router da quelli fissi.</summary>
        Private Function SetNetworkDhcp(key As String, range As String) As Object
            If String.IsNullOrEmpty(key) Then Throw New UserMessageException("Rete non indicata.")
            Try
                _store.Network.SetNetworkDhcpRange(key, range)
            Catch ex As ArgumentException
                Throw New UserMessageException(ex.Message)
            End Try
            Return InventoryView("", False, key)
        End Function

        ''' <summary>Alias, fiducia e note scritti nella scheda; l'alias e le note tornano anche sull'host della sessione, se c'è.</summary>
        Private Function InventorySave(args As JsonElement) As Object
            Dim id = ArgLong(args, "id")
            Dim inv = _store.Network
            Dim record = inv.Get(id)
            If record Is Nothing Then Throw New UserMessageException("Il dispositivo non è più nell'archivio.")
            Dim value As JsonElement
            If args.TryGetProperty("alias", value) Then inv.SetAlias(id, ArgText(args, "alias").Trim())
            If args.TryGetProperty("notes", value) Then inv.SetNotes(id, ArgText(args, "notes").Trim())
            If args.TryGetProperty("trust", value) Then inv.SetTrust(id, CType(Clamp(ArgNumber(args, "trust", 0), 0, 2), HostTrust))
            Dim updated = inv.Get(id)
            Dim inSession = _session.Hosts.FirstOrDefault(Function(x) String.Equals(MacVendorLookup.Normalize(x.MacAddress), updated.Mac, StringComparison.OrdinalIgnoreCase) OrElse (updated.Mac Is Nothing AndAlso x.IPAddress = updated.Ip))
            If inSession IsNot Nothing Then
                inSession.CustomName = If(updated.[Alias], "")
                inSession.Notes = If(updated.Notes, "")
                Touch()
            End If
            Return InventoryHost(id)
        End Function

        ''' <summary>«Scansiona adesso» dalla pagina Dispositivi: scansione dell'archivio, non della sessione.</summary>
        Private Function StartInventoryScan(target As String) As Object
            If _watch.IsScanning Then Throw New UserMessageException(If(_sessionScan, "C'è già una scansione della rete in corso dalla pagina Rete.", "La scansione è già in corso."))
            Dim options As New ScanOptions With {
                .Target = If(String.IsNullOrWhiteSpace(target), If(_settings.NetworkScanTarget, "").Trim(), target.Trim()),
                .TimeoutMs = Clamp(_settings.NetworkScanTimeoutMs, 100, 10000)
            }
            Dim unused = _watch.ScanAsync(automatic:=False, options:=options)
            Return New With {.started = True, .state = State()}
        End Function

#End Region

#Region "WHOIS, DNS e traceroute"

        Private Async Function WhoisAsync(target As String, server As String) As Task(Of Object)
            target = target.Trim()
            If target = "" Then Throw New UserMessageException("Indica un dominio o un indirizzo IP.")
            Dim result = Await New WhoisClient().QueryAsync(target, server.Trim())
            If result.IsSuccess Then
                _session.WhoisEntries.Add(result)
                Touch()
            End If
            Return New With {.entry = result, .state = State()}
        End Function

        Private _perfCts As CancellationTokenSource

#Region "Cattura dei pacchetti"

        Private ReadOnly _capture As New PacketCapture(MainWindow.LocalFolder)
        Private ReadOnly _captureTimer As New System.Windows.Forms.Timer With {.Interval = 500}
        Private _captureActive As Boolean
        Private _captureWatch As Stopwatch
        Private _captureAsked As DateTime
        Private _captureSummary As CaptureSummary
        Private _captureOptions As CaptureOptions

        ''' <summary>
        ''' Cattura con pktmon (pagina Pacchetti): Windows chiede il consenso una volta; poi un timer guarda la cartella della
        ''' cattura e manda l'avanzamento («capture.progress»), alla fine legge il .pcapng («capture.done»).
        ''' </summary>
        Private Async Function StartCaptureAsync(args As JsonElement) As Task(Of Object)
            If _captureActive Then Throw New UserMessageException("Una cattura è già in corso.")
            Dim o As New CaptureOptions With {
                .Seconds = Clamp(ArgNumber(args, "seconds", 30), 0, PacketCapture.MaxSeconds),
                .Ip = ArgText(args, "ip").Trim(),
                .Port = Clamp(ArgNumber(args, "port", 0), 0, 65535),
                .Protocol = ArgText(args, "protocol").Trim()
            }
            Try
                PacketCapture.Validate(o)
            Catch ex As ArgumentException
                Throw New UserMessageException(ex.Message)
            End Try
            If _captureTimer.Tag Is Nothing Then
                AddHandler _captureTimer.Tick, AddressOf CaptureTick
                _captureTimer.Tag = True
            End If
            _captureActive = True
            _captureWatch = Nothing
            _captureAsked = DateTime.Now
            _captureOptions = o
            Try
                ' la richiesta di consenso di Windows blocca chi la fa: fuori dal thread della finestra
                Await Task.Run(Sub() _capture.Start(o))
            Catch ex As OperationCanceledException
                _captureActive = False
                Throw New UserMessageException(ex.Message)
            Catch ex As Exception
                _captureActive = False
                Throw New UserMessageException($"Cattura non avviata: {ex.Message}")
            End Try
            _captureTimer.Start()
            Return New With {.started = True, .state = State()}
        End Function

        Private Async Sub CaptureTick(sender As Object, e As EventArgs)
            If Not _captureActive Then
                _captureTimer.Stop()
                Return
            End If
            Dim st = _capture.Status()
            If st.Started AndAlso _captureWatch Is Nothing Then _captureWatch = Stopwatch.StartNew()
            If Not st.Started AndAlso Not st.Done AndAlso (DateTime.Now - _captureAsked).TotalSeconds > 60 Then
                ' lo script non è mai partito (pktmon assente o bloccato): si smette di aspettare
                _captureTimer.Stop()
                _captureActive = False
                _owner.PostEvent("capture.done", New With {.error = "La cattura non è partita: pktmon non ha risposto entro un minuto.", .state = State()})
                Return
            End If
            If Not st.Done Then
                _owner.PostEvent("capture.progress", New With {
                    .started = st.Started, .seconds = If(_captureWatch Is Nothing, 0, _captureWatch.Elapsed.TotalSeconds), .bytes = st.Bytes,
                    .limit = _captureOptions.Seconds
                })
                Return
            End If
            _captureTimer.Stop()
            If st.Error <> "" Then
                _captureActive = False
                _owner.PostEvent("capture.done", New With {.error = $"pktmon: {st.Error}", .state = State()})
                Return
            End If
            _owner.PostEvent("capture.progress", New With {.started = True, .converting = True, .seconds = If(_captureWatch Is Nothing, 0, _captureWatch.Elapsed.TotalSeconds), .bytes = st.Bytes, .limit = _captureOptions.Seconds})
            Try
                Dim file = _capture.PcapFile
                _captureSummary = Await Task.Run(Function() PacketCapture.Read(file))
            Catch ex As Exception
                _captureActive = False
                _owner.PostEvent("capture.done", New With {.error = $"Cattura non letta: {ex.Message}", .state = State()})
                Return
            End Try
            _captureActive = False
            Dim o = _captureOptions
            Dim what = String.Join(" · ", {If(o.Ip <> "", o.Ip, ""), If(o.Port > 0, $"porta {o.Port}", ""), o.Protocol}.Where(Function(x) x <> ""))
            Dim scan = AddScan("capture", If(what = "", "tutta la rete", what), _captureSummary.Seconds, _captureSummary.Total, False, Nothing)
            _owner.PostEvent("capture.done", New With {.summary = _captureSummary, .scan = scan, .state = State()})
        End Sub

        ''' <summary>Copia la cattura (.pcapng) dove si vuole, per Wireshark o per mandarla a qualcuno.</summary>
        Private Function SaveCapture() As Object
            If Not File.Exists(_capture.PcapFile) Then Throw New UserMessageException("Non c'è ancora nessuna cattura da salvare.")
            Using save As New SaveFileDialog With {
                .Title = "Salva la cattura dei pacchetti",
                .Filter = "Cattura pcapng (Wireshark) (*.pcapng)|*.pcapng",
                .FileName = $"Cattura {DateTime.Now:yyyy-MM-dd HHmm}.pcapng",
                .InitialDirectory = If(_file = "", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), IO.Path.GetDirectoryName(_file))
            }
                If save.ShowDialog(_owner) <> DialogResult.OK Then Return Nothing
                File.Copy(_capture.PcapFile, save.FileName, overwrite:=True)
                _allowedFiles.Add(save.FileName)
                Return New With {.file = save.FileName}
            End Using
        End Function

#End Region

        ''' <summary>
        ''' Test delle prestazioni (pagina Prestazioni): gateway della scheda con la rete vera, poi Internet, DNS, download e
        ''' upload. L'avanzamento va alla pagina con «perf.progress», l'esito con «perf.done» e resta nella sessione.
        ''' </summary>
        Private Function StartPerf() As Object
            If _perfCts IsNot Nothing Then Throw New UserMessageException("Un test delle prestazioni è già in corso.")
            Dim nic = NetworkConfig.ReadAll().FirstOrDefault(Function(a) a.Up AndAlso a.Gateway <> "")
            Dim gateway As Net.IPAddress = Nothing
            If nic IsNot Nothing Then Net.IPAddress.TryParse(nic.Gateway, gateway)
            Dim linkMbps As Long = 0
            If nic IsNot Nothing Then
                Try
                    Dim ni = Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(Function(x) x.Name = nic.Name)
                    If ni IsNot Nothing AndAlso ni.Speed > 0 Then linkMbps = ni.Speed \ 1000000L
                Catch
                End Try
            End If
            _perfCts = New CancellationTokenSource()
            Dim progress As New Progress(Of PerfProgress)(Sub(p) _owner.PostEvent("perf.progress", p))
            Dim unused = RunPerfAsync(gateway, If(nic Is Nothing, "", nic.Name), linkMbps, progress, _perfCts)
            Return New With {.started = True, .adapter = If(nic Is Nothing, "", nic.Name), .gateway = If(gateway Is Nothing, "", gateway.ToString()), .state = State()}
        End Function

        Private Async Function RunPerfAsync(gateway As Net.IPAddress, adapter As String, linkMbps As Long, progress As IProgress(Of PerfProgress), cts As CancellationTokenSource) As Task
            Dim watch = Stopwatch.StartNew()
            Dim result As PerfResult
            Try
                result = Await New PerfTester().RunAsync(gateway, progress, cts.Token)
            Catch ex As OperationCanceledException
                result = New PerfResult With {.Cancelled = True}
            Catch ex As Exception
                result = New PerfResult With {.Error = ex.Message}
            End Try
            result.Adapter = adapter
            result.LinkMbps = linkMbps
            result.Seconds = Math.Round(watch.Elapsed.TotalSeconds, 1)
            cts.Dispose()
            If ReferenceEquals(_perfCts, cts) Then _perfCts = Nothing
            Dim scan As ScanRecord = Nothing
            If Not result.Cancelled AndAlso result.Error = "" Then
                If _session.PerfTests Is Nothing Then _session.PerfTests = New List(Of PerfResult)()
                _session.PerfTests.Add(result)
                If _session.PerfTests.Count > 50 Then _session.PerfTests.RemoveRange(0, _session.PerfTests.Count - 50)
                scan = AddScan("perf", If(result.Server = "", "Internet e rete locale", result.Server), result.Seconds, CInt(Math.Round(result.DownloadMbps)), False, Nothing)
            End If
            _owner.PostEvent("perf.done", New With {.result = result, .scan = scan, .state = State()})
        End Function

        ''' <summary>
        ''' Ping veloce dalla Panoramica: risolve il nome (prima IPv4, poi IPv6) e manda count richieste, una ogni 300 ms.
        ''' Niente nella sessione: è un controllo al volo.
        ''' </summary>
        Private Async Function QuickPingAsync(target As String, count As Integer) As Task(Of Object)
            target = target.Trim()
            If target = "" Then Throw New UserMessageException("Scrivi un indirizzo IP o un nome da pingare.")
            Dim address As Net.IPAddress = Nothing
            If Not Net.IPAddress.TryParse(target, address) Then
                Try
                    Dim all = Await Net.Dns.GetHostAddressesAsync(target)
                    address = If(all.FirstOrDefault(Function(a) a.AddressFamily = Net.Sockets.AddressFamily.InterNetwork), all.FirstOrDefault())
                Catch ex As Exception
                    Throw New UserMessageException($"Il nome «{target}» non si risolve: {ex.Message}")
                End Try
                If address Is Nothing Then Throw New UserMessageException($"Il nome «{target}» non ha indirizzi.")
            End If
            Dim replies As New List(Of Object)()
            Dim times As New List(Of Long)()
            Using pinger As New Net.NetworkInformation.Ping()
                For i = 1 To count
                    Dim status = "Error"
                    Dim ms As Long = 0
                    Dim ttl = 0
                    Try
                        Dim reply = Await pinger.SendPingAsync(address, 1500)
                        status = reply.Status.ToString()
                        If reply.Status = Net.NetworkInformation.IPStatus.Success Then
                            ms = reply.RoundtripTime
                            ttl = If(reply.Options Is Nothing, 0, reply.Options.Ttl)
                            times.Add(ms)
                        End If
                    Catch ex As Exception
                        status = "Error"
                    End Try
                    replies.Add(New With {.seq = i, .status = status, .ms = ms, .ttl = ttl})
                    If i < count Then Await Task.Delay(300)
                Next
            End Using
            Return New With {
                .target = target, .address = Network.Ipv6.Plain(address), .sent = count, .received = times.Count, .replies = replies,
                .min = If(times.Count > 0, times.Min(), 0L), .avg = If(times.Count > 0, CLng(Math.Round(times.Average())), 0L), .max = If(times.Count > 0, times.Max(), 0L)
            }
        End Function

        ''' <summary>
        ''' nslookup veloce dalla Panoramica: record A, AAAA, PTR (per un indirizzo), MX, NS e TXT, con il server DNS che ha
        ''' risposto. Niente nella sessione: per tenere i record c'è la pagina DNS.
        ''' </summary>
        Private Async Function QuickLookupAsync(target As String) As Task(Of Object)
            target = target.Trim()
            If target = "" Then Throw New UserMessageException("Scrivi un nome di dominio o un indirizzo IP.")
            Dim watch = Stopwatch.StartNew()
            Dim lookup = New DnsEngine().QueryAllRecordsAsync(target)
            Dim server = Task.Run(Function() DnsServerInfo(target))
            Dim records = Await lookup
            Dim info = Await server
            Return New With {.target = target, .records = records, .server = info.Name, .serverAddress = info.Address, .ms = watch.ElapsedMilliseconds}
        End Function

        ''' <summary>Il server DNS che risponde (le prime righe «Server:» e «Address:» di nslookup).</summary>
        Private Shared Function DnsServerInfo(target As String) As (Name As String, Address As String)
            Try
                Dim psi As New ProcessStartInfo With {
                    .FileName = "nslookup", .Arguments = $"""{target.Replace("""", "")}""",
                    .RedirectStandardOutput = True, .RedirectStandardError = True, .UseShellExecute = False, .CreateNoWindow = True
                }
                Using p = Process.Start(psi)
                    If p Is Nothing Then Return ("", "")
                    Dim output = p.StandardOutput.ReadToEnd()
                    p.WaitForExit(5000)
                    Dim name = ""
                    Dim address = ""
                    For Each line In output.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).Take(4)
                        Dim colon = line.IndexOf(":"c)
                        If colon < 0 Then Continue For
                        Dim key = line.Substring(0, colon).Trim()
                        Dim value = line.Substring(colon + 1).Trim()
                        If key.Equals("Server", StringComparison.OrdinalIgnoreCase) AndAlso name = "" Then name = value
                        If (key.StartsWith("Address", StringComparison.OrdinalIgnoreCase) OrElse key.StartsWith("Indirizz", StringComparison.OrdinalIgnoreCase)) AndAlso address = "" Then address = value
                    Next
                    Return (name, address)
                End Using
            Catch
                Return ("", "")
            End Try
        End Function

        Private Async Function DnsAsync(target As String) As Task(Of Object)
            target = target.Trim()
            If target = "" Then Throw New UserMessageException("Indica un dominio o un indirizzo IP.")
            Dim records = Await New DnsEngine().QueryAllRecordsAsync(target)
            Dim added As New List(Of DnsRecordInfo)()
            For Each r In records
                Dim known = _session.DnsRecords.Any(Function(x) x.Host = r.Host AndAlso x.RecordType = r.RecordType AndAlso x.Value = r.Value)
                If Not known Then
                    _session.DnsRecords.Add(r)
                    added.Add(r)
                End If
            Next
            If added.Count > 0 Then Touch()
            Return New With {.records = records, .added = added, .state = State()}
        End Function

        Private Function StartTraceroute(args As JsonElement) As Object
            If _traceCts IsNot Nothing Then Throw New UserMessageException("Un traceroute è già in corso: fermalo prima di avviarne un altro.")
            Dim target = ArgText(args, "target").Trim()
            If target = "" Then Throw New UserMessageException("Indica la destinazione del traceroute (indirizzo IP o nome).")
            Dim engine As New TracerouteEngine With {.MaxHops = Clamp(ArgNumber(args, "maxHops", 30), 1, 64)}
            ' un nuovo traceroute verso la stessa destinazione sostituisce il precedente
            Dim removed = _session.TracerouteHops.RemoveAll(Function(h) String.Equals(h.Target, target, StringComparison.OrdinalIgnoreCase))
            If removed > 0 Then Touch()
            _traceCts = New CancellationTokenSource()
            Dim progress As New Progress(Of TracerouteHop)(Sub(hop)
                                                               hop.Target = target
                                                               _session.TracerouteHops.Add(hop)
                                                               Touch()
                                                               _owner.PostEvent("trace.hop", hop)
                                                           End Sub)
            Dim unused = RunTracerouteAsync(engine, target, progress, _traceCts)
            Return New With {.started = True, .target = target, .state = State()}
        End Function

        Private Async Function RunTracerouteAsync(engine As TracerouteEngine, target As String, progress As IProgress(Of TracerouteHop), cts As CancellationTokenSource) As Task
            Dim watch = Stopwatch.StartNew()
            Dim hops = 0
            Dim cancelled = False
            Dim failure As String = Nothing
            Try
                Dim list = Await engine.TraceAsync(target, progress, cts.Token)
                hops = list.Count
            Catch ex As OperationCanceledException
                cancelled = True
            Catch ex As Exception
                failure = ex.Message
            End Try
            If cts.IsCancellationRequested Then cancelled = True
            cts.Dispose()
            If ReferenceEquals(_traceCts, cts) Then _traceCts = Nothing
            Dim scan = AddScan("trace", target, watch.Elapsed.TotalSeconds, hops, cancelled, failure)
            _owner.PostEvent("trace.done", New With {
                .target = target, .hops = hops, .cancelled = cancelled, .error = failure, .scan = scan,
                .unresolved = hops = 0 AndAlso Not cancelled AndAlso failure Is Nothing, .state = State()
            })
        End Function

#End Region

#Region "Connessioni attive"

        Private Function KillProcess(pid As Integer) As Object
            If pid <= 4 Then Throw New UserMessageException("I processi di sistema non si possono terminare da qui.")
            Dim name As String
            Try
                Using proc = Process.GetProcessById(pid)
                    name = proc.ProcessName
                    proc.Kill()
                End Using
            Catch ex As ArgumentException
                Throw New UserMessageException($"Il processo {pid} non c'è più.")
            Catch ex As Exception
                Throw New UserMessageException($"Processo non terminato: {ex.Message}")
            End Try
            Return New With {.pid = pid, .name = name}
        End Function

#End Region

#Region "Sessione"

        Private Function NewSession() As Object
            If _settings.AutoSaveSession Then AutoSaveNow()
            StopAll()
            _session = New ScanSession()
            _file = ""
            _dirty = False
            _autoSaveFailed = False
            BindSessionNetwork(_watch.CurrentNetwork())
            RememberSession("")
            Return WrapSession()
        End Function

        Private Function OpenSession() As Object
            Using open As New OpenFileDialog With {
                .Title = "Apri una sessione WhoIs Pro",
                .Filter = "Sessione WhoIs Pro (*.whoispro;*.json)|*.whoispro;*.json|Tutti i file (*.*)|*.*"
            }
                If open.ShowDialog(_owner) <> DialogResult.OK Then Return Nothing
                Dim restored = False
                Dim loaded As ScanSession
                Try
                    loaded = AtomicFileManager.LoadSessionSafe(open.FileName, restored)
                Catch ex As Exception
                    Throw New UserMessageException($"La sessione non si apre: {ex.Message}")
                End Try
                If loaded Is Nothing Then Throw New UserMessageException("Il file non contiene una sessione di WhoIs Pro.")
                If _settings.AutoSaveSession Then AutoSaveNow()
                StopAll()
                _session = loaded
                _file = open.FileName
                _dirty = restored
                _autoSaveFailed = False
                ' una sessione di prima della 2.7 non sa di che rete è: diventa di quella in cui si trova il PC
                If String.IsNullOrEmpty(_session.NetworkKey) Then BindSessionNetwork(_watch.CurrentNetwork())
                RememberSession(_file)
                RememberNetworkSession()
                Return WrapSession(New With {.restoredFromBackup = restored, .file = _file, .network = SessionNetworkName()})
            End Using
        End Function

        ''' <summary>Salvataggio atomico (file temporaneo, SHA-256, sostituzione con copia .bak); Nothing se annullato.</summary>
        Private Function SaveSession(saveAs As Boolean) As Object
            Dim path = _file
            If saveAs OrElse path = "" Then
                Using save As New SaveFileDialog With {
                    .Title = "Salva la sessione WhoIs Pro",
                    .Filter = SessionFilter,
                    .FileName = If(_file = "", $"Sessione {DateTime.Now:yyyy-MM-dd HHmm}.whoispro", IO.Path.GetFileName(_file)),
                    .InitialDirectory = If(_file = "", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), IO.Path.GetDirectoryName(_file))
                }
                    If save.ShowDialog(_owner) <> DialogResult.OK Then Return Nothing
                    path = save.FileName
                End Using
            End If
            If String.IsNullOrEmpty(_session.NetworkKey) Then BindSessionNetwork(_watch.CurrentNetwork())
            Dim res = AtomicFileManager.SaveSessionAtomic(_session, path)
            If Not res.IsSuccess Then Throw New UserMessageException(res.ErrorMessage)
            _file = path
            _dirty = False
            _autoSaveFailed = False
            _autoSavedAt = DateTime.Now
            _autoSave.Stop()
            RememberSession(path)
            RememberNetworkSession()
            Return Wrap(New With {.file = res.FilePath, .checksum = res.ChecksumSHA256, .backup = If(File.Exists(res.BackupPath), res.BackupPath, Nothing)})
        End Function

        ''' <summary>Salvataggio chiesto dalla finestra alla chiusura: falso se annullato o non riuscito.</summary>
        Public Function SaveBeforeClosing() As Boolean
            Try
                Return SaveSession(False) IsNot Nothing
            Catch ex As Exception
                MessageBox.Show(_owner, Describe(ex), "Sessione non salvata", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return False
            End Try
        End Function

        Private Function SessionInfo(title As String, notes As String) As Object
            title = title.Trim()
            If title = "" Then title = "Sessione WhoIs Pro"
            If title <> _session.SessionTitle OrElse notes <> _session.Notes Then
                _session.SessionTitle = title
                _session.Notes = notes
                Touch()
            End If
            Return Wrap()
        End Function

        Private Function ClearList(list As String) As Object
            Select Case list
                Case "hosts" : _session.Hosts.Clear()
                Case "ports" : _session.Ports.Clear()
                Case "whois" : _session.WhoisEntries.Clear()
                Case "dns" : _session.DnsRecords.Clear()
                Case "trace" : _session.TracerouteHops.Clear()
                Case "scans" : _session.Scans?.Clear()
                Case "perf" : _session.PerfTests?.Clear()
                Case Else : Throw New UserMessageException($"Elenco sconosciuto: {list}")
            End Select
            Touch()
            Return WrapSession()
        End Function

        Private Function RemoveHost(ip As String) As Object
            If _session.Hosts.RemoveAll(Function(h) h.IPAddress = ip) > 0 Then Touch()
            Return WrapSession()
        End Function

        ''' <summary>Nome e note di un dispositivo scelti dall'utente: nella sessione e nel registro dei dispositivi (per MAC).</summary>
        Private Function HostLabel(args As JsonElement) As Object
            Dim ip = ArgText(args, "ip")
            Dim h = _session.Hosts.FirstOrDefault(Function(x) x.IPAddress = ip)
            If h Is Nothing Then Throw New UserMessageException($"{ip} non è più nella sessione.")
            Dim name = ArgText(args, "name").Trim()
            If name.Length > 80 Then name = name.Substring(0, 80)
            Dim notes = ArgText(args, "notes")
            If notes.Length > 2000 Then notes = notes.Substring(0, 2000)
            ' icona: assente = invariata, "" = tolta (torna quella del tipo); il nome si salva nella forma canonica
            Dim icon As String = Nothing
            Dim color As String = Nothing
            Dim value As JsonElement
            If args.TryGetProperty("icon", value) Then
                Dim wanted = If(value.ValueKind = JsonValueKind.String, value.GetString(), "").Trim()
                icon = If(wanted = "", "", IconCatalog.CanonicalName(wanted))
                If wanted <> "" AndAlso icon = "" Then Throw New UserMessageException($"Icona «{wanted}» non trovata.")
                color = ArgText(args, "iconColor").Trim().ToLowerInvariant()
                If color <> "" AndAlso Not Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-f]{6}$") Then color = ""
            End If
            _devices.SetLabel(h, name, notes, icon, color, _watch.CurrentNetwork())
            Touch()
            Return New With {.host = h, .state = State()}
        End Function

#Region "Icone dei dispositivi (Material Design Icons)"

        ''' <summary>Versione, numero di icone e categorie (prima le raccolte pensate per la rete e gli impianti).</summary>
        Private Shared Function IconsMeta() As Object
            Return New With {
                .version = IconCatalog.Version, .count = IconCatalog.Count,
                .categories = IconCatalog.Categories().Select(Function(c) New With {.key = c.Key, .label = c.Label, .count = c.Count, .curated = c.Curated}).ToList()
            }
        End Function

        ''' <summary>Ricerca (parole italiane o inglesi), categoria e forma: { total, items: [{ name, path }] }.</summary>
        Private Shared Function IconsSearch(args As JsonElement) As Object
            Dim value As JsonElement
            Dim offset = If(args.TryGetProperty("offset", value) AndAlso value.ValueKind = JsonValueKind.Number, value.GetInt32(), 0)
            Dim limit = If(args.TryGetProperty("limit", value) AndAlso value.ValueKind = JsonValueKind.Number, value.GetInt32(), 200)
            Dim found = IconCatalog.Search(ArgText(args, "q"), ArgText(args, "cat"), ArgText(args, "variant"), offset, limit)
            Return New With {.total = found.Total, .items = found.Items.Select(Function(i) New With {.name = i.Name, .path = i.Path}).ToList()}
        End Function

        ''' <summary>I tracciati SVG delle icone chieste per nome (al massimo 300): { nome: tracciato }.</summary>
        Private Shared Function IconPaths(args As JsonElement) As Object
            Dim result As New Dictionary(Of String, String)(StringComparer.Ordinal)
            Dim value As JsonElement
            If args.TryGetProperty("names", value) AndAlso value.ValueKind = JsonValueKind.Array Then
                For Each n In value.EnumerateArray().Take(300)
                    If n.ValueKind <> JsonValueKind.String Then Continue For
                    Dim name = n.GetString()
                    Dim icon = IconCatalog.Find(name)
                    If icon IsNot Nothing AndAlso Not result.ContainsKey(name) Then result(name) = icon.Path
                Next
            End If
            Return result
        End Function

#End Region

        Private Function Export(format As String) As Object
            Dim filter As String
            Dim extension As String
            Select Case format
                Case "html"
                    filter = "Report HTML (*.html)|*.html"
                    extension = "html"
                Case "csv"
                    filter = "File CSV per Excel (*.csv)|*.csv"
                    extension = "csv"
                Case "txt"
                    filter = "File di testo (*.txt)|*.txt"
                    extension = "txt"
                Case Else
                    Throw New UserMessageException($"Formato sconosciuto: {format}")
            End Select
            Dim baseName = If(_file = "", "WhoIs Pro", IO.Path.GetFileNameWithoutExtension(_file))
            Using save As New SaveFileDialog With {
                .Title = "Esporta la sessione",
                .Filter = filter,
                .FileName = $"{baseName} {DateTime.Now:yyyy-MM-dd HHmm}.{extension}",
                .InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            }
                If save.ShowDialog(_owner) <> DialogResult.OK Then Return Nothing
                Select Case format
                    Case "html" : SessionExporter.ExportToHtml(_session, save.FileName)
                    Case "csv" : SessionExporter.ExportToCsv(_session, save.FileName)
                    Case "txt" : SessionExporter.ExportToTxt(_session, save.FileName)
                End Select
                _allowedFiles.Add(IO.Path.GetFullPath(save.FileName))
                Return New With {.file = save.FileName}
            End Using
        End Function

        Private Sub StopAll()
            If _sessionScan Then _watch.CancelScan()
            _perfCts?.Cancel()
            _portCts?.Cancel()
            _traceCts?.Cancel()
        End Sub

#End Region

#Region "Database dei produttori"

        Private _ouiPending As Boolean
        Private _ouiAutoTried As Boolean

        ''' <summary>
        ''' Aggiornamento automatico del database dei produttori, come quello del programma: all'avvio (una volta per
        ''' sessione del programma), qualche secondo dopo e solo con Internet (se manca, parte appena torna), quando il
        ''' database non viene dall'IEEE o ha più di un giorno: i registri si aggiornano ogni giorno e pesano qualche MB.
        ''' La pagina riceve «oui.auto» all'inizio e «oui.done» (o «oui.failed») alla fine.
        ''' </summary>
        Private Async Function AutoUpdateOuiAsync(Optional delay As Boolean = True) As Task
            If Not _settings.AutoUpdateOui OrElse (_ouiAutoTried AndAlso Not _ouiPending) Then Return
            _ouiAutoTried = True
            Dim info = MacVendorLookup.DatabaseInfo()
            If info.Source = "IEEE" AndAlso info.UpdatedAt.HasValue AndAlso (DateTime.Now - info.UpdatedAt.Value) < TimeSpan.FromDays(1) Then Return
            Try
                If delay Then Await Task.Delay(TimeSpan.FromSeconds(12))
                If Not (_online.HasValue AndAlso _online.Value) Then
                    _ouiPending = True
                    Return
                End If
                _ouiPending = False
                If _ouiBusy Then Return
                _owner.PostEvent("oui.auto", New With {.message = "Connessione ai registri dell'IEEE…"})
                Dim result = Await UpdateOuiAsync()
                _owner.PostEvent("oui.done", result)
            Catch ex As Exception
                _owner.PostEvent("oui.failed", New With {.error = ex.Message, .state = State()})
            End Try
        End Function

        ''' <summary>
        ''' Scarica i registri dell'IEEE (o, se non rispondono, il manuf di Wireshark), li salva in %LOCALAPPDATA%\WhoIsPro e
        ''' riclassifica gli host della sessione.
        ''' </summary>
        Private Async Function UpdateOuiAsync() As Task(Of Object)
            If _ouiBusy Then Throw New UserMessageException("L'aggiornamento del database è già in corso.")
            _ouiBusy = True
            Dim count As Integer
            Try
                Dim progress As New Progress(Of String)(Sub(message) _owner.PostEvent("oui.progress", New With {.message = message}))
                count = Await MacVendorLookup.UpdateDatabaseOnlineAsync(progress, CancellationToken.None)
            Catch ex As Exception
                Throw New UserMessageException($"Database non aggiornato: {ex.Message}. Resta in uso quello attuale.")
            Finally
                _ouiBusy = False
            End Try
            Dim changed = 0
            For Each h In _session.Hosts
                If String.IsNullOrEmpty(h.MacAddress) Then Continue For
                Dim vendor = MacVendorLookup.GetVendor(h.MacAddress)
                Dim before = h.MacVendor & "|" & h.DeviceType
                Dim c = DeviceClassifier.Classify(vendor, h.Hostname, Nothing, h.TTL, h.MacAddress)
                h.MacVendor = vendor
                h.DeviceType = c.DeviceType
                h.DeviceCategory = c.Category
                h.DeviceIcon = c.Icon
                ' con le porte aperte della sessione il tipo si riconosce meglio: non va perso
                ApplyOpenPorts(h)
                If before <> h.MacVendor & "|" & h.DeviceType Then changed += 1
            Next
            If changed > 0 Then Touch()
            Return WrapSession(New With {.count = count, .changedHosts = changed})
        End Function

#End Region

#Region "File e argomenti"

        ''' <summary>Apre un file prodotto dal programma (report esportato, sessione salvata).</summary>
        Private Function OpenFile(file As String) As Object
            If file = "" OrElse Not (_allowedFiles.Contains(IO.Path.GetFullPath(file)) OrElse String.Equals(file, _file, StringComparison.OrdinalIgnoreCase)) Then
                Throw New UserMessageException("File non disponibile.")
            End If
            If Not IO.File.Exists(file) Then Throw New UserMessageException($"Il file non c'è più: {file}")
            Process.Start(New ProcessStartInfo(file) With {.UseShellExecute = True})
            Return Nothing
        End Function

        Private Shared Function Clamp(value As Integer, min As Integer, max As Integer) As Integer
            Return Math.Max(min, Math.Min(max, value))
        End Function

        Private Shared Function ArgText(args As JsonElement, name As String) As String
            Dim value As JsonElement
            If args.ValueKind = JsonValueKind.Object AndAlso args.TryGetProperty(name, value) Then
                If value.ValueKind = JsonValueKind.String Then Return value.GetString()
                If value.ValueKind = JsonValueKind.Number Then Return value.GetRawText()
            End If
            Return ""
        End Function

        Private Shared Function ArgFlag(args As JsonElement, name As String) As Boolean
            Dim value As JsonElement
            Return args.ValueKind = JsonValueKind.Object AndAlso args.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.True
        End Function

        Private Shared Function ArgNumber(args As JsonElement, name As String, fallback As Integer) As Integer
            Dim value As JsonElement
            Dim n As Integer
            If args.ValueKind = JsonValueKind.Object AndAlso args.TryGetProperty(name, value) Then
                If value.ValueKind = JsonValueKind.Number AndAlso value.TryGetInt32(n) Then Return n
                If value.ValueKind = JsonValueKind.String AndAlso Integer.TryParse(value.GetString(), n) Then Return n
            End If
            Return fallback
        End Function

        ''' <summary>Elenco di numeri interi (es. gli id dei dispositivi selezionati); vuoto se il campo manca o non è un elenco.</summary>
        Private Shared Function ArgLongs(args As JsonElement, name As String) As List(Of Long)
            Dim list As New List(Of Long)()
            Dim value As JsonElement
            If args.ValueKind = JsonValueKind.Object AndAlso args.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.Array Then
                For Each item In value.EnumerateArray()
                    Dim n As Long
                    If item.ValueKind = JsonValueKind.Number AndAlso item.TryGetInt64(n) Then list.Add(n)
                Next
            End If
            Return list
        End Function

        Private Shared Function ArgLong(args As JsonElement, name As String) As Long
            Dim value As JsonElement
            Dim n As Long
            If args.ValueKind = JsonValueKind.Object AndAlso args.TryGetProperty(name, value) Then
                If value.ValueKind = JsonValueKind.Number AndAlso value.TryGetInt64(n) Then Return n
                If value.ValueKind = JsonValueKind.String AndAlso Long.TryParse(value.GetString(), n) Then Return n
            End If
            Return 0
        End Function

#End Region

        Public Sub Dispose() Implements IDisposable.Dispose
            StopAll()
            _autoSave.Stop()
            _autoSave.Dispose()
            _watch.Dispose()
            _flush.Stop()
            _flush.Dispose()
            _internetTimer.Stop()
            _internetTimer.Dispose()
            _updates.Dispose()
            _store.Dispose()
        End Sub

    End Class

End Namespace
