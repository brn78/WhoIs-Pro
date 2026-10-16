Option Strict On

Imports System.IO
Imports System.Text.Json
Imports System.Threading
Imports System.Windows.Forms
Imports System.Net.Http
Imports WhoIsPro.Config
Imports WhoIsPro.Machine
Imports WhoIsPro.Models
Imports WhoIsPro.Network
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

        Private _netCts As CancellationTokenSource
        Private _portCts As CancellationTokenSource
        Private _traceCts As CancellationTokenSource
        Private _ouiBusy As Boolean

        ' avanzamento accumulato tra un invio e l'altro alla pagina
        Private _netProgress As ScanProgress
        Private _netWatch As Stopwatch
        Private ReadOnly _netFound As New List(Of HostInfo)()
        Private _netSeen As Integer
        Private ReadOnly _netSeenIps As New HashSet(Of String)()
        Private ReadOnly _netNew As New List(Of HostInfo)()
        Private _netFirstCensus As Boolean
        Private ReadOnly _devices As New DeviceRegistry(MainWindow.LocalFolder)
        Private _portProgress As PortScanProgress
        Private _portWatch As Stopwatch
        Private ReadOnly _portFound As New List(Of PortInfo)()

        Public Sub New(owner As MainWindow)
            _owner = owner
            AddHandler _flush.Tick, AddressOf FlushProgress
            AddHandler _internetTimer.Tick, Sub() CheckInternet()
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
                Case "quickPing" : Return Await QuickPingAsync(ArgText(args, "target"), Clamp(ArgNumber(args, "count", 4), 1, 10))
                Case "quickLookup" : Return Await QuickLookupAsync(ArgText(args, "target"))
                Case "stopNetwork"
                    _netCts?.Cancel()
                    _autoCts?.Cancel()
                    Return Nothing
                Case "stopAutoPorts"
                    _autoCts?.Cancel()
                    CheckAutoPortsDone()
                    Return Nothing
                Case "clearDevices"
                    _devices.Clear()
                    Return SettingsView()
                Case "scanPorts" : Return StartPortScan(args)
                Case "stopPorts"
                    _portCts?.Cancel()
                    Return Nothing
                Case "whois" : Return Await WhoisAsync(ArgText(args, "target"), ArgText(args, "server"))
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
                Case "export" : Return Export(ArgText(args, "format"))
                Case "updateOui" : Return Await UpdateOuiAsync()
                Case "openUrl"
                    MainWindow.OpenExternal(ArgText(args, "url"))
                    Return Nothing
                Case "openFile" : Return OpenFile(ArgText(args, "path"))
                Case "checkUpdate" : Return Await _updates.CheckAsync(manual:=True, CancellationToken.None)
                Case "installUpdate" : Return Await InstallUpdateAsync()
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
            CheckInternet()
            _internetTimer.Start()
            Dim reopened = ReopenLastSession()
            Return New With {.state = State(), .session = SessionData(), .reopened = reopened}
        End Function

        Private _reopenTried As Boolean

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
            ' Internet anche in IPv6: la pagina di prova di Windows per IPv6 ha solo l'indirizzo IPv6
            Dim online6 = False
            If online Then
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
                .profiles = _settings.Profiles,
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
            Dim theme = ArgText(args, "theme")
            If theme = "auto" OrElse theme = "light" OrElse theme = "dark" Then _settings.Theme = theme
            SettingsStore.Save(MainWindow.LocalFolder, _settings)
            _owner.ApplyTraySettings(_settings)
            _owner.ApplyTheme(_settings.Theme)
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

        ''' <summary>Controllo automatico delle nuove versioni (una volta al giorno), qualche secondo dopo l'avvio.</summary>
        Private Async Function AutoCheckUpdateAsync() As Task
            If Not _updates.IsDue Then Return
            Try
                Await Task.Delay(TimeSpan.FromSeconds(6))
                Dim status = Await _updates.CheckAsync(manual:=False, CancellationToken.None)
                _owner.PostEvent("update.status", status)
            Catch ex As Exception
                ' senza Internet non succede nulla: si riprova al prossimo avvio
            End Try
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
                .running = New With {.network = _netCts IsNot Nothing, .ports = _portCts IsNot Nothing, .trace = _traceCts IsNot Nothing, .oui = _ouiBusy, .autoPorts = _autoCts IsNot Nothing},
                .autoPorts = New With {.done = _autoDone, .total = _autoSeen.Count, .running = _autoRunning, .open = _autoOpen},
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
                .scans = If(_session.Scans, New List(Of ScanRecord)())
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
        End Sub

        ''' <summary>Vero se ci sono dati non salvati (la finestra lo chiede prima di chiudere).</summary>
        Public ReadOnly Property HasUnsavedData As Boolean
            Get
                Return _dirty AndAlso (_session.Hosts.Count + _session.Ports.Count + _session.WhoisEntries.Count + _session.DnsRecords.Count + _session.TracerouteHops.Count) > 0
            End Get
        End Property

#End Region

#Region "Scansione della rete"

        Private Function StartNetworkScan(args As JsonElement) As Object
            If _netCts IsNot Nothing Then Throw New UserMessageException("Una scansione della rete è già in corso: fermala prima di avviarne un'altra.")
            Dim target = ArgText(args, "target").Trim()
            If target = "" Then Throw New UserMessageException("Indica la rete da scansionare, per esempio 192.168.1.0/24 oppure 192.168.1.1-50.")
            Dim targets = NetworkUtils.ParseTargets(target)
            If targets.Count = 0 Then Throw New UserMessageException($"Nessun indirizzo IP valido in «{target}». Si accettano reti (192.168.1.0/24, al massimo /16), intervalli (192.168.1.1-50) e indirizzi singoli.")

            Dim scanner As New NetworkScanner With {
                .Concurrency = Clamp(ArgNumber(args, "threads", 80), 1, 500),
                .TimeoutMs = Clamp(ArgNumber(args, "timeout", 900), 100, 10000),
                .ResolveHostnames = ArgFlag(args, "resolveDns")
            }
            ' porte degli host trovati: man mano che rispondono, a gruppi di cinque
            Dim autoPorts = ArgFlag(args, "autoPorts")
            If autoPorts Then
                Dim list = PortDictionary.ParsePorts(ArgText(args, "autoPortsList"))
                If list.Count = 0 Then Throw New UserMessageException("Scegli le porte da controllare sugli host trovati.")
                BeginAutoPorts(list)
            End If

            _netCts = New CancellationTokenSource()
            _netWatch = Stopwatch.StartNew()
            _netFound.Clear()
            _netSeen = 0
            _netSeenIps.Clear()
            _netNew.Clear()
            _netFirstCensus = _devices.Count = 0
            Dim progress As New Progress(Of ScanProgress)(Sub(p)
                                                              _netProgress = p
                                                              Dim h = p.DiscoveredHost
                                                              If h IsNot Nothing Then
                                                                  If AddHost(h) Then _netNew.Add(h)
                                                                  _netFound.Add(h)
                                                                  _netSeen += 1
                                                                  _netSeenIps.Add(h.IPAddress)
                                                                  If autoPorts Then EnqueueAutoPorts(h.IPAddress)
                                                              End If
                                                              If Not _flush.Enabled Then _flush.Start()
                                                          End Sub)
            Dim unused = RunNetworkScanAsync(scanner, target, targets, progress, _netCts)
            Return New With {.started = True, .total = targets.Count, .state = State()}
        End Function

        Private Async Function RunNetworkScanAsync(scanner As NetworkScanner, target As String, targets As List(Of Net.IPAddress),
                                                   progress As IProgress(Of ScanProgress), cts As CancellationTokenSource) As Task
            Dim found = 0
            Dim cancelled = False
            Dim failure As String = Nothing
            Try
                Dim list = Await scanner.ScanRangeAsync(targets, progress, cts.Token)
                found = list.Count
            Catch ex As OperationCanceledException
                cancelled = True
            Catch ex As Exception
                failure = ex.Message
            End Try
            If cts.IsCancellationRequested Then cancelled = True
            _netWatch.Stop()
            cts.Dispose()
            If ReferenceEquals(_netCts, cts) Then _netCts = Nothing
            FlushProgress(Nothing, EventArgs.Empty)
            ' fermata a metà, l'elenco finale non c'è: contano gli host arrivati fin lì
            found = Math.Max(found, _netSeen)

            ' host della rete scansionata che questa volta non hanno risposto: restano nella sessione, segnati «Offline»
            Dim offline As New List(Of String)()
            If Not cancelled AndAlso failure Is Nothing Then
                Dim scanned As New HashSet(Of String)(targets.Select(Function(a) a.ToString()))
                For Each h In _session.Hosts
                    If scanned.Contains(h.IPAddress) AndAlso Not _netSeenIps.Contains(h.IPAddress) AndAlso h.Status <> "Offline" Then
                        h.Status = "Offline"
                        offline.Add(h.IPAddress)
                    End If
                Next
                If offline.Count > 0 Then Touch()
            End If

            ' IPv6 degli host trovati: un ping a tutti i nodi IPv6, poi la tabella dei vicini, per MAC
            Dim ipv6 = Await FillIpv6Async(wake:=True)

            _devices.Save()
            Dim fresh = _netNew.ToList()
            Dim firstCensus = _netFirstCensus
            ' il primo censimento (registro vuoto) non è una notizia: tutti sarebbero «nuovi»
            If fresh.Count > 0 AndAlso Not firstCensus Then NotifyNewDevices(fresh)

            Dim scan = AddScan("network", target, _netWatch.Elapsed.TotalSeconds, found, cancelled, failure)
            _owner.PostEvent("network.done", New With {
                .found = found, .seconds = _netWatch.Elapsed.TotalSeconds, .cancelled = cancelled, .error = failure, .scan = scan,
                .offline = offline, .newDevices = fresh, .firstCensus = firstCensus, .ipv6 = ipv6, .state = State()
            })
            CheckAutoPortsDone()
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

        ''' <summary>Avviso di Windows per i dispositivi mai visti prima (se la finestra è nascosta); nella pagina lo dice un toast.</summary>
        Private Sub NotifyNewDevices(list As List(Of HostInfo))
            Dim title = If(list.Count = 1, "Dispositivo nuovo nella rete", $"{list.Count} dispositivi nuovi nella rete")
            Dim lines = list.Take(3).Select(Function(h) $"{h.IPAddress} · {If(String.IsNullOrEmpty(h.Hostname), h.DeviceType, h.Hostname)}").ToList()
            If list.Count > 3 Then lines.Add($"e altri {list.Count - 3}")
            _owner.ShowNotice(title, String.Join(vbLf, lines))
        End Sub

        ''' <summary>
        ''' Riscansione di un solo indirizzo (ARP, ping, nome DNS, produttore e tipo), anche mentre gira la scansione della
        ''' rete: aggiorna l'host nella sessione, oppure lo segna «Offline» se non risponde più.
        ''' </summary>
        Private Async Function RescanHostAsync(args As JsonElement) As Task(Of Object)
            Dim ip = ArgText(args, "ip").Trim()
            Dim address As Net.IPAddress = Nothing
            If Not Net.IPAddress.TryParse(ip, address) OrElse address.AddressFamily <> Net.Sockets.AddressFamily.InterNetwork Then
                Throw New UserMessageException($"«{ip}» non è un indirizzo IPv4.")
            End If
            Dim scanner As New NetworkScanner With {
                .Concurrency = 1,
                .TimeoutMs = Clamp(ArgNumber(args, "timeout", 900), 100, 10000),
                .ResolveHostnames = ArgFlag(args, "resolveDns")
            }
            Dim watch = Stopwatch.StartNew()
            Dim found = Await scanner.ScanRangeAsync(New List(Of Net.IPAddress) From {address}, Nothing, CancellationToken.None)
            Dim host = found.FirstOrDefault()
            Dim isNew = False
            If host IsNot Nothing Then
                isNew = AddHost(host)
                Await FillIpv6Async(wake:=False)
                _devices.Save()
                If isNew Then NotifyNewDevices(New List(Of HostInfo) From {host})
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
        ''' rilevazione dal registro dei dispositivi (anche da sessioni precedenti) e il tipo dalle porte già trovate.
        ''' Vero se il dispositivo non era mai stato visto da questo PC.
        ''' </summary>
        Private Function AddHost(h As HostInfo) As Boolean
            Dim idx = _session.Hosts.FindIndex(Function(x) x.IPAddress = h.IPAddress)
            If idx >= 0 Then
                h.FirstSeen = _session.Hosts(idx).FirstSeen
                h.Notes = _session.Hosts(idx).Notes
                If (h.Ipv6 Is Nothing OrElse h.Ipv6.Count = 0) AndAlso _session.Hosts(idx).Ipv6 IsNot Nothing Then h.Ipv6 = _session.Hosts(idx).Ipv6
                _session.Hosts(idx) = h
            Else
                _session.Hosts.Add(h)
            End If
            ApplyOpenPorts(h)
            Dim isNew = _devices.Observe(h)
            Touch()
            Return isNew
        End Function

        ''' <summary>Porte aperte della sessione sull'host: il conteggio e il tipo di dispositivo, che con le porte si riconosce meglio.</summary>
        Private Sub ApplyOpenPorts(h As HostInfo)
            Dim mine = _session.Ports.Where(Function(p) p.Host = h.IPAddress).ToList()
            h.OpenPortsCount = mine.Count
            Dim tcp = mine.Where(Function(p) p.Protocol = "TCP" AndAlso p.Status = "Open").Select(Function(p) p.PortNumber).Distinct().ToList()
            If tcp.Count = 0 Then Return
            Dim c = DeviceClassifier.Classify(h.MacVendor, h.Hostname, tcp, h.TTL, h.MacAddress)
            h.DeviceType = c.DeviceType
            h.DeviceCategory = c.Category
            h.DeviceIcon = c.Icon
        End Sub

#End Region

#Region "Porte degli host trovati (durante la scansione della rete)"

        Private Const AutoParallelHosts As Integer = 5
        Private _autoCts As CancellationTokenSource
        Private ReadOnly _autoQueue As New Queue(Of String)()
        Private ReadOnly _autoSeen As New HashSet(Of String)()
        Private ReadOnly _autoFound As New List(Of PortInfo)()
        Private _autoList As List(Of Integer)
        Private _autoRunning As Integer
        Private _autoDone As Integer
        Private _autoOpen As Integer
        Private _autoWatch As Stopwatch

        ''' <summary>Prepara la coda; se ce n'è già una in corso continua quella, con le porte nuove.</summary>
        Private Sub BeginAutoPorts(list As List(Of Integer))
            _autoList = list
            If _autoCts IsNot Nothing Then Return
            _autoCts = New CancellationTokenSource()
            _autoQueue.Clear()
            _autoSeen.Clear()
            _autoFound.Clear()
            _autoRunning = 0
            _autoDone = 0
            _autoOpen = 0
            _autoWatch = Stopwatch.StartNew()
        End Sub

        Private Sub EnqueueAutoPorts(ip As String)
            If _autoCts Is Nothing OrElse Not _autoSeen.Add(ip) Then Return
            _autoQueue.Enqueue(ip)
            PumpAutoPorts()
        End Sub

        ''' <summary>Fa partire gli host in coda finché ne girano cinque insieme.</summary>
        Private Sub PumpAutoPorts()
            While _autoCts IsNot Nothing AndAlso Not _autoCts.IsCancellationRequested AndAlso _autoRunning < AutoParallelHosts AndAlso _autoQueue.Count > 0
                Dim ip = _autoQueue.Dequeue()
                _autoRunning += 1
                Dim unused = ScanAutoHostAsync(ip, _autoCts)
            End While
        End Sub

        Private Async Function ScanAutoHostAsync(ip As String, cts As CancellationTokenSource) As Task
            Dim scanner As New PortScanner With {.Concurrency = 64, .TimeoutMs = 800, .GrabBanner = True}
            Dim found = 0
            Dim progress As New Progress(Of PortScanProgress)(Sub(p)
                                                                  Dim port = p.DiscoveredPort
                                                                  If port IsNot Nothing AndAlso port.Status = "Open" Then
                                                                      AddPort(port)
                                                                      _autoFound.Add(port)
                                                                      found += 1
                                                                      If Not _flush.Enabled Then _flush.Start()
                                                                  End If
                                                              End Sub)
            Try
                Await scanner.ScanPortsAsync(ip, _autoList, True, False, progress, cts.Token)
            Catch ex As OperationCanceledException
            Catch ex As Exception
                ' un host che sparisce a metà non ferma gli altri
            End Try
            ' gli ultimi avanzamenti arrivano dopo la fine del metodo: un attimo, così sono contati
            Await Task.Delay(100)
            _autoRunning -= 1
            _autoDone += 1
            _autoOpen += found
            Dim host = _session.Hosts.FirstOrDefault(Function(h) h.IPAddress = ip)
            If host IsNot Nothing Then
                ApplyOpenPorts(host)
                _devices.Observe(host)
                _owner.PostEvent("autoports.host", New With {
                    .ip = ip, .found = found, .host = host, .done = _autoDone, .total = _autoSeen.Count, .running = _autoRunning
                })
            End If
            PumpAutoPorts()
            CheckAutoPortsDone()
        End Function

        ''' <summary>Coda finita (scansione della rete conclusa e nessun host in corso, o fermata): esito e storico.</summary>
        Private Sub CheckAutoPortsDone()
            If _autoCts Is Nothing OrElse _netCts IsNot Nothing OrElse _autoRunning > 0 Then Return
            If _autoQueue.Count > 0 AndAlso Not _autoCts.IsCancellationRequested Then Return
            Dim cancelled = _autoCts.IsCancellationRequested
            _autoCts.Dispose()
            _autoCts = Nothing
            _autoQueue.Clear()
            _autoWatch.Stop()
            _devices.Save()
            FlushProgress(Nothing, EventArgs.Empty)
            Dim scan As ScanRecord = Nothing
            If _autoDone > 0 Then scan = AddScan("autoports", $"{_autoDone} host della rete", _autoWatch.Elapsed.TotalSeconds, _autoOpen, cancelled, Nothing)
            _owner.PostEvent("autoports.done", New With {
                .hosts = _autoDone, .found = _autoOpen, .seconds = _autoWatch.Elapsed.TotalSeconds, .cancelled = cancelled, .scan = scan, .state = State()
            })
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

        Private Sub AddPort(p As PortInfo)
            Dim idx = _session.Ports.FindIndex(Function(x) x.Host = p.Host AndAlso x.PortNumber = p.PortNumber AndAlso x.Protocol = p.Protocol)
            If idx >= 0 Then
                _session.Ports(idx) = p
            Else
                _session.Ports.Add(p)
            End If
            Touch()
        End Sub

        ''' <summary>Manda alla pagina l'avanzamento accumulato (timer da 120 ms, o subito alla fine della scansione).</summary>
        Private Sub FlushProgress(sender As Object, e As EventArgs)
            If _netProgress IsNot Nothing Then
                Dim p = _netProgress
                _netProgress = Nothing
                Dim hosts = _netFound.ToList()
                _netFound.Clear()
                _owner.PostEvent("network.progress", New With {
                    .scanned = p.ScannedCount, .total = p.TotalCount, .percent = p.PercentComplete, .currentIp = p.CurrentIp,
                    .seconds = If(_netWatch Is Nothing, 0, _netWatch.Elapsed.TotalSeconds), .hosts = hosts
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
            If _autoFound.Count > 0 Then
                Dim found = _autoFound.ToList()
                _autoFound.Clear()
                _owner.PostEvent("autoports.progress", New With {.ports = found, .done = _autoDone, .total = _autoSeen.Count, .running = _autoRunning})
            End If
            If _netProgress Is Nothing AndAlso _portProgress Is Nothing AndAlso _autoFound.Count = 0 Then _flush.Stop()
        End Sub

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
            StopAll()
            _session = New ScanSession()
            _file = ""
            _dirty = False
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
                StopAll()
                _session = loaded
                _file = open.FileName
                _dirty = restored
                RememberSession(_file)
                Return WrapSession(New With {.restoredFromBackup = restored, .file = _file})
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
            Dim res = AtomicFileManager.SaveSessionAtomic(_session, path)
            If Not res.IsSuccess Then Throw New UserMessageException(res.ErrorMessage)
            _file = path
            _dirty = False
            RememberSession(path)
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
                Case Else : Throw New UserMessageException($"Elenco sconosciuto: {list}")
            End Select
            Touch()
            Return WrapSession()
        End Function

        Private Function RemoveHost(ip As String) As Object
            If _session.Hosts.RemoveAll(Function(h) h.IPAddress = ip) > 0 Then Touch()
            Return WrapSession()
        End Function

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
            _netCts?.Cancel()
            _autoCts?.Cancel()
            _portCts?.Cancel()
            _traceCts?.Cancel()
        End Sub

#End Region

#Region "Database dei produttori"

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

#End Region

        Public Sub Dispose() Implements IDisposable.Dispose
            StopAll()
            _flush.Stop()
            _flush.Dispose()
            _internetTimer.Stop()
            _internetTimer.Dispose()
            _updates.Dispose()
        End Sub

    End Class

End Namespace
