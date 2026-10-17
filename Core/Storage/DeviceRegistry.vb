Option Strict On

Imports System.IO
Imports System.Text.Json
Imports WhoIsPro.Models

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
        Public Property TimesSeen As Integer
        ''' <summary>Nome e note date dall'utente: tornano sull'host in ogni scansione e sessione successiva.</summary>
        Public Property Name As String = ""
        Public Property Notes As String = ""
        Public Property Icon As String = ""
        Public Property IconColor As String = ""
    End Class

    ''' <summary>
    ''' Registro dei dispositivi conosciuti (dispositivi.json in %LOCALAPPDATA%\WhoIsPro), indipendente dalle sessioni:
    ''' dice da quando un dispositivo è comparso la prima volta e riconosce quelli mai visti, così una scansione ripetuta
    ''' segnala chi si è collegato di nuovo. La chiave è il MAC (l'indirizzo IP solo per gli host senza MAC, fuori dalla
    ''' rete locale). Un telefono con il MAC privato ne usa uno per rete: in quella rete resta lo stesso dispositivo.
    ''' </summary>
    Public NotInheritable Class DeviceRegistry
        Private Shared ReadOnly JsonOpts As New JsonSerializerOptions With {.WriteIndented = True, .PropertyNameCaseInsensitive = True}
        Private ReadOnly _file As String
        Private ReadOnly _items As New Dictionary(Of String, KnownDevice)(StringComparer.OrdinalIgnoreCase)
        Private _dirty As Boolean

        Public Sub New(folder As String)
            _file = Path.Combine(folder, "dispositivi.json")
            Try
                If File.Exists(_file) Then
                    Dim list = JsonSerializer.Deserialize(Of List(Of KnownDevice))(File.ReadAllText(_file), JsonOpts)
                    For Each d In If(list, New List(Of KnownDevice)())
                        If Not String.IsNullOrEmpty(d.Key) Then _items(d.Key) = d
                    Next
                End If
            Catch
                ' registro illeggibile: si ricomincia, il file si riscrive al prossimo salvataggio
            End Try
        End Sub

        Public ReadOnly Property FilePath As String
            Get
                Return _file
            End Get
        End Property

        Public ReadOnly Property Count As Integer
            Get
                Return _items.Count
            End Get
        End Property

        ''' <summary>"mac:AA:BB:…" oppure "ip:10.0.0.5" se l'host non ha MAC.</summary>
        Public Shared Function KeyOf(h As HostInfo) As String
            If Not String.IsNullOrWhiteSpace(h.MacAddress) Then Return "mac:" & h.MacAddress.Trim().ToUpperInvariant().Replace("-", ":")
            Return "ip:" & h.IPAddress
        End Function

        Public Function Find(h As HostInfo) As KnownDevice
            Dim d As KnownDevice = Nothing
            Return If(_items.TryGetValue(KeyOf(h), d), d, Nothing)
        End Function

        ''' <summary>
        ''' L'host è stato visto adesso: aggiorna il registro e riporta sull'host la prima rilevazione più vecchia che si
        ''' conosce. Vero se il dispositivo non era mai stato visto da questo PC.
        ''' </summary>
        Public Function Observe(h As HostInfo) As Boolean
            Dim key = KeyOf(h)
            Dim d As KnownDevice = Nothing
            Dim isNew = Not _items.TryGetValue(key, d)
            If isNew Then
                d = New KnownDevice With {.Key = key, .Mac = h.MacAddress, .FirstSeen = h.FirstSeen}
                _items(key) = d
            ElseIf d.FirstSeen < h.FirstSeen Then
                h.FirstSeen = d.FirstSeen
            End If
            d.LastIp = h.IPAddress
            If Not String.IsNullOrEmpty(h.Hostname) Then d.Hostname = h.Hostname
            d.Vendor = h.MacVendor
            d.DeviceType = h.DeviceType
            d.LastSeen = h.LastSeen
            d.TimesSeen += 1
            SyncLabel(d, h)
            _dirty = True
            Return isNew
        End Function

        ''' <summary>
        ''' Nome e note: quelli del registro vanno sull'host; se il registro non li ha ma l'host sì (una sessione aperta da
        ''' un altro PC), il registro li impara.
        ''' </summary>
        Private Shared Sub SyncLabel(d As KnownDevice, h As HostInfo)
            If Not String.IsNullOrEmpty(d.Name) Then h.CustomName = d.Name Else d.Name = If(h.CustomName, "")
            If Not String.IsNullOrEmpty(d.Notes) Then h.Notes = d.Notes Else d.Notes = If(h.Notes, "")
            If Not String.IsNullOrEmpty(d.Icon) Then
                h.Icon = d.Icon
                h.IconColor = d.IconColor
            Else
                d.Icon = If(h.Icon, "")
                d.IconColor = If(h.IconColor, "")
            End If
        End Sub

        ''' <summary>Nome, note, icona e colore scelti dall'utente per il dispositivo (vuoti = tolti), sull'host e nel registro.</summary>
        Public Sub SetLabel(h As HostInfo, name As String, notes As String, Optional icon As String = Nothing, Optional iconColor As String = Nothing)
            h.CustomName = If(name, "").Trim()
            h.Notes = If(notes, "").Trim()
            If icon IsNot Nothing Then
                h.Icon = icon.Trim()
                h.IconColor = If(h.Icon = "", "", If(iconColor, "").Trim())
            End If
            Dim key = KeyOf(h)
            Dim d As KnownDevice = Nothing
            If Not _items.TryGetValue(key, d) Then
                d = New KnownDevice With {.Key = key, .Mac = h.MacAddress, .LastIp = h.IPAddress, .Hostname = h.Hostname,
                                          .Vendor = h.MacVendor, .DeviceType = h.DeviceType, .FirstSeen = h.FirstSeen, .LastSeen = h.LastSeen}
                _items(key) = d
            End If
            d.Name = h.CustomName
            d.Notes = h.Notes
            d.Icon = h.Icon
            d.IconColor = h.IconColor
            _dirty = True
            Save()
        End Sub

        Public Sub Clear()
            _items.Clear()
            _dirty = True
            Save()
        End Sub

        ''' <summary>Scrive il registro se è cambiato: file temporaneo e sostituzione, mai un file a metà.</summary>
        Public Sub Save()
            If Not _dirty Then Return
            Try
                Directory.CreateDirectory(Path.GetDirectoryName(_file))
                Dim temp = _file & ".tmp"
                File.WriteAllText(temp, JsonSerializer.Serialize(_items.Values.OrderBy(Function(d) d.FirstSeen).ToList(), JsonOpts))
                File.Move(temp, _file, overwrite:=True)
                _dirty = False
            Catch
                ' si riprova al prossimo salvataggio
            End Try
        End Sub
    End Class

End Namespace
