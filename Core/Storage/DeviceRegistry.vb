Option Strict On

Imports System.IO
Imports System.Text.Json
Imports WhoIsPro.Inventory
Imports WhoIsPro.Models
Imports WhoIsPro.Network.Scan
Imports WhoIsPro.Utils

Namespace Storage

    ''' <summary>Un dispositivo visto almeno una volta da questo PC: quando la prima volta, quando l'ultima, con che indirizzo.</summary>
    Public Class KnownDevice
        Public Property Key As String = ""
        Public Property Mac As String = ""
        Public Property LastIp As String = ""
        Public Property Hostname As String = ""
        Public Property Vendor As String = ""
        Public Property DeviceType As String = ""
        Public Property FirstSeen As DateTime = DateTime.Now
        Public Property LastSeen As DateTime = DateTime.Now
        ''' <summary>Scansioni registrate nell'archivio in cui il dispositivo ha risposto.</summary>
        Public Property TimesSeen As Integer
        ''' <summary>Nome e note date dall'utente: tornano sull'host in ogni scansione e sessione successiva.</summary>
        Public Property Name As String = ""
        Public Property Notes As String = ""
        Public Property Icon As String = ""
        Public Property IconColor As String = ""
    End Class

    ''' <summary>
    ''' Registro dei dispositivi conosciuti: la stessa tabella dell'archivio della rete (Core\Inventory, archivio-rete.db
    ''' in %LOCALAPPDATA%\WhoIsPro), vista dalla sessione. Dice da quando un dispositivo è comparso la prima volta,
    ''' riconosce quelli mai visti e riporta sugli host nome, note e icona scelti dall'utente. La chiave è il MAC
    ''' (l'indirizzo IP solo per gli host senza MAC, fuori dalla rete locale). Il vecchio dispositivi.json delle
    ''' versioni 2.4–2.5 viene importato al primo avvio e rinominato.
    ''' </summary>
    Public NotInheritable Class DeviceRegistry
        Implements IDisposable

        Private Shared ReadOnly JsonOpts As New JsonSerializerOptions With {.WriteIndented = True, .PropertyNameCaseInsensitive = True}
        Private ReadOnly _store As InventoryStore
        Private ReadOnly _ownsStore As Boolean

        ''' <summary>Registro con un archivio proprio nella cartella (prove e uso autonomo).</summary>
        Public Sub New(folder As String)
            Me.New(New InventoryStore(Path.Combine(folder, InventoryStore.FileName)), ownsStore:=True)
        End Sub

        ''' <summary>Registro sull'archivio del programma.</summary>
        Public Sub New(store As InventoryStore, Optional ownsStore As Boolean = False)
            _store = store
            _ownsStore = ownsStore
            ImportLegacyFile()
        End Sub

        Private ReadOnly Property Inventory As NetworkInventory
            Get
                Return _store.Network
            End Get
        End Property

        ''' <summary>Il database dell'archivio.</summary>
        Public ReadOnly Property FilePath As String
            Get
                Return _store.DatabasePath
            End Get
        End Property

        Public ReadOnly Property Count As Integer
            Get
                Return Inventory.Count
            End Get
        End Property

#Region "Vecchio registro"

        ''' <summary>dispositivi.json accanto al database: importato una volta sola, poi rinominato in .importato.</summary>
        Private Sub ImportLegacyFile()
            Try
                Dim file = Path.Combine(Path.GetDirectoryName(_store.DatabasePath), "dispositivi.json")
                If Not IO.File.Exists(file) Then Return
                Dim imported = 0
                Try
                    Dim list = JsonSerializer.Deserialize(Of List(Of KnownDevice))(IO.File.ReadAllText(file), JsonOpts)
                    If list IsNot Nothing Then
                        imported = Inventory.ImportKnownDevices(list.Where(Function(d) Not String.IsNullOrEmpty(d.Key)).Select(Function(d) New ImportedDevice With {
                            .Mac = d.Mac, .Ip = d.LastIp, .Hostname = d.Hostname, .Vendor = d.Vendor, .DeviceType = d.DeviceType,
                            .FirstSeen = d.FirstSeen, .LastSeen = d.LastSeen, .TimesSeen = d.TimesSeen,
                            .Name = d.Name, .Notes = d.Notes, .Icon = d.Icon, .IconColor = d.IconColor
                        }))
                    End If
                Catch
                    ' registro illeggibile: non c'è niente da importare
                End Try
                IO.File.Move(file, file & ".importato", overwrite:=True)
            Catch
                ' si riprova al prossimo avvio
            End Try
        End Sub

#End Region

#Region "Chiave e ricerca"

        ''' <summary>"mac:AA:BB:…" oppure "ip:10.0.0.5" se l'host non ha MAC.</summary>
        Public Shared Function KeyOf(h As HostInfo) As String
            If Not String.IsNullOrWhiteSpace(h.MacAddress) Then Return "mac:" & h.MacAddress.Trim().ToUpperInvariant().Replace("-", ":")
            Return "ip:" & h.IPAddress
        End Function

        ''' <summary>La chiave dell'archivio: MAC normalizzato, oppure «ip:…».</summary>
        Private Shared Function StoreKey(h As HostInfo) As String
            Return If(MacVendorLookup.Normalize(h.MacAddress), "ip:" & h.IPAddress)
        End Function

        ''' <summary>Il dispositivo in archivio: prima nella rete indicata, poi in qualunque rete (il visto più di recente).</summary>
        Public Function Find(h As HostInfo, Optional networkKey As String = Nothing) As KnownDevice
            Dim r = FindRecord(h, networkKey)
            Return If(r Is Nothing, Nothing, ToKnown(r))
        End Function

        Private Function FindRecord(h As HostInfo, networkKey As String) As NetHostRecord
            Dim key = StoreKey(h)
            Dim hasMac = Not String.IsNullOrWhiteSpace(h.MacAddress)
            Dim r As NetHostRecord = Nothing
            If Not String.IsNullOrEmpty(networkKey) Then
                r = Inventory.GetByKey(networkKey, key)
                If r Is Nothing AndAlso hasMac Then r = Inventory.GetByKey(networkKey, "ip:" & h.IPAddress)
            End If
            If r Is Nothing Then r = Inventory.FindAnywhere(key)
            If r Is Nothing AndAlso hasMac Then r = Inventory.FindAnywhere("ip:" & h.IPAddress)
            Return r
        End Function

        Private Shared Function ToKnown(r As NetHostRecord) As KnownDevice
            Return New KnownDevice With {
                .Key = If(r.Mac Is Nothing, "ip:" & r.Ip, "mac:" & r.Mac.ToUpperInvariant()), .Mac = If(r.Mac, "").ToUpperInvariant(), .LastIp = r.Ip,
                .Hostname = If(r.NetBios, If(r.HostName, "")), .Vendor = If(r.Vendor, ""), .DeviceType = If(r.DeviceType, ""),
                .FirstSeen = r.FirstSeen, .LastSeen = r.LastSeen, .TimesSeen = r.SeenCount,
                .Name = If(r.Alias, ""), .Notes = If(r.Notes, ""), .Icon = If(r.Icon, ""), .IconColor = If(r.IconColor, "")
            }
        End Function

#End Region

#Region "Osservazione ed etichette"

        ''' <summary>
        ''' L'host è stato visto adesso nella rete indicata (senza, la sottorete /24 dell'indirizzo): aggiorna l'archivio e
        ''' riporta sull'host la prima rilevazione più vecchia che si conosce, il nome, le note e l'icona. Vero se il
        ''' dispositivo non era mai stato visto da questo PC in quella rete.
        ''' </summary>
        Public Function Observe(h As HostInfo, Optional network As NetworkIdentity = Nothing) As Boolean
            Dim result = Inventory.Observe(ToDiscovered(h), h.LastSeen, network)
            Dim r = result.Record
            If Not result.IsNew AndAlso r.FirstSeen < h.FirstSeen Then h.FirstSeen = r.FirstSeen
            SyncLabel(r, h)
            Return result.IsNew
        End Function

        ''' <summary>
        ''' Nome, note e icona: quelli dell'archivio vanno sull'host; se l'archivio non li ha ma l'host sì (una sessione aperta
        ''' da un altro PC), l'archivio li impara.
        ''' </summary>
        Private Sub SyncLabel(r As NetHostRecord, h As HostInfo)
            If Not String.IsNullOrEmpty(r.Alias) Then
                h.CustomName = r.Alias
            ElseIf Not String.IsNullOrEmpty(h.CustomName) Then
                Inventory.SetAlias(r.Id, h.CustomName)
            End If
            If Not String.IsNullOrEmpty(r.Notes) Then
                h.Notes = r.Notes
            ElseIf Not String.IsNullOrEmpty(h.Notes) Then
                Inventory.SetNotes(r.Id, h.Notes)
            End If
            If Not String.IsNullOrEmpty(r.Icon) Then
                h.Icon = r.Icon
                h.IconColor = If(r.IconColor, "")
            ElseIf Not String.IsNullOrEmpty(h.Icon) Then
                Inventory.SetIcon(r.Id, h.Icon, h.IconColor)
            End If
        End Sub

        ''' <summary>
        ''' Nome, note, icona e colore scelti dall'utente per il dispositivo (vuoti = tolti), sull'host e nell'archivio: nel
        ''' record della rete indicata, altrimenti in quello che c'è (una sessione di un altro cliente aperta altrove), altrimenti
        ''' ne nasce uno nella rete indicata.
        ''' </summary>
        Public Sub SetLabel(h As HostInfo, name As String, notes As String, Optional icon As String = Nothing, Optional iconColor As String = Nothing, Optional network As NetworkIdentity = Nothing)
            h.CustomName = If(name, "").Trim()
            h.Notes = If(notes, "").Trim()
            If icon IsNot Nothing Then
                h.Icon = icon.Trim()
                h.IconColor = If(h.Icon = "", "", If(iconColor, "").Trim())
            End If
            Dim r = If(FindRecord(h, network?.Key), Inventory.Observe(ToDiscovered(h), h.LastSeen, network).Record)
            Inventory.SetAlias(r.Id, h.CustomName)
            Inventory.SetNotes(r.Id, h.Notes)
            If icon IsNot Nothing Then Inventory.SetIcon(r.Id, h.Icon, h.IconColor)
        End Sub

        ''' <summary>Svuota l'archivio: alla prossima scansione tutti i dispositivi tornano «nuovi» (senza avvisi).</summary>
        Public Sub Clear()
            Inventory.ClearAll()
        End Sub

        ''' <summary>L'archivio scrive subito: resta per compatibilità con chi chiamava il registro su file.</summary>
        Public Sub Save()
        End Sub

        ''' <summary>Host della sessione come lo vede il motore, per l'archivio o la riclassificazione.</summary>
        Friend Shared Function ToDiscovered(h As HostInfo, Optional openPorts As IEnumerable(Of OpenPort) = Nothing) As DiscoveredHost
            Dim d As New DiscoveredHost With {
                .Ip = h.IPAddress, .Mac = MacVendorLookup.Normalize(h.MacAddress), .Vendor = MacVendorLookup.CleanVendor(h.MacVendor),
                .HostName = If(String.IsNullOrWhiteSpace(h.Hostname), Nothing, h.Hostname), .NetBiosName = If(String.IsNullOrWhiteSpace(h.NetBiosName), Nothing, h.NetBiosName),
                .NetBiosWorkgroup = If(String.IsNullOrWhiteSpace(h.Workgroup), Nothing, h.Workgroup),
                .DeviceType = If(h.DeviceType, "Dispositivo"), .Category = If(h.DeviceCategory, DeviceClassifier.CategoryOther),
                .OsGuess = If(String.IsNullOrEmpty(h.OSGuess) OrElse h.OSGuess = "Sconosciuto", Nothing, h.OSGuess),
                .RespondsToPing = h.RespondsToPing, .LatencyMs = If(h.ResponseTimeMs > 0, h.ResponseTimeMs, CType(Nothing, Double?)),
                .Ttl = If(h.TTL > 0, h.TTL, CType(Nothing, Integer?)), .IsGateway = h.IsGateway, .IsLocalMachine = h.IsLocalMachine
            }
            If openPorts IsNot Nothing Then
                d.Ports.AddRange(openPorts)
            Else
                For Each p In If(h.OpenPorts, New List(Of Integer)())
                    d.Ports.Add(New OpenPort(p, "TCP", PortDictionary.ServiceNameOrNull(p), Nothing))
                Next
            End If
            If h.OtherIpv4 IsNot Nothing Then d.ExtraIps.AddRange(h.OtherIpv4)
            Return d
        End Function

#End Region

        Public Sub Dispose() Implements IDisposable.Dispose
            If _ownsStore Then _store.Dispose()
        End Sub

    End Class

End Namespace
