Option Strict On

Imports System.Collections.Concurrent
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Xml.Linq
Imports WhoIsPro.Utils

Namespace Network.Scan

    ''' <summary>Dati che un dispositivo Hikvision restituisce alla sonda SADP (risposta &lt;Hello&gt; o &lt;ProbeMatch&gt;).</summary>
    Public NotInheritable Class SadpDeviceInfo
        Public Property Ip As String = ""
        ''' <summary>MAC normalizzato "aa:bb:cc:dd:ee:ff", oppure Nothing se mancante.</summary>
        Public Property Mac As String
        ''' <summary>Codice modello (tag &lt;DeviceDescription&gt;, es. "DS-2CD2143G2-I" o "DS-7608NI-K2/8P").</summary>
        Public Property Model As String
        ''' <summary>Numero di serie completo (tag &lt;DeviceSN&gt;).</summary>
        Public Property SerialNumber As String
        ''' <summary>Versione del firmware (tag &lt;SoftwareVersion&gt;, es. "V5.7.3 build 220112").</summary>
        Public Property SoftwareVersion As String
        ''' <summary>Versione del DSP (tag &lt;DSPVersion&gt;).</summary>
        Public Property DspVersion As String
        ''' <summary>Codice numerico del tipo di dispositivo (tag &lt;DeviceType&gt;).</summary>
        Public Property DeviceType As Integer?
        ''' <summary>Porta web HTTP (tag &lt;HttpPort&gt;, predefinita 80).</summary>
        Public Property HttpPort As Integer = 80
        ''' <summary>Porta SDK binaria (tag &lt;CommandPort&gt;, predefinita 8000).</summary>
        Public Property CommandPort As Integer = 8000
        ''' <summary>Canali analogici (tag &lt;AnalogChannelNum&gt;).</summary>
        Public Property AnalogChannels As Integer
        ''' <summary>Canali IP / digitali (tag &lt;DigitalChannelNum&gt;).</summary>
        Public Property DigitalChannels As Integer
        ''' <summary>Stato di attivazione (tag &lt;Activated&gt;): False indica dispositivo vergine senza password impostata.</summary>
        Public Property Activated As Boolean?
        ''' <summary>Indirizzo assegnato in DHCP (tag &lt;DHCP&gt;).</summary>
        Public Property Dhcp As Boolean?
        ''' <summary>Data e ora di avvio del dispositivo (tag &lt;BootTime&gt;).</summary>
        Public Property BootTime As String
        ''' <summary>Informazioni OEM (tag &lt;OEMInfo&gt;).</summary>
        Public Property OemInfo As String

        ''' <summary>Numero complessivo di canali video dichiarati dal dispositivo.</summary>
        Public ReadOnly Property TotalChannels As Integer
            Get
                Return Math.Max(0, AnalogChannels) + Math.Max(0, DigitalChannels)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Sonda SADP (Search Active Devices Protocol) di Hikvision: un pacchetto XML &lt;Probe&gt; su UDP, in multicast
    ''' (239.255.255.250:37020) sulla rete locale oppure in unicast (porta 37020) verso indirizzi instradati o in VPN.
    ''' I dispositivi rispondono senza credenziali con modello esatto, firmware, seriale, porte e stato di attivazione.
    ''' Nessuna eccezione viene propagata, salvo l'annullamento richiesto dal chiamante.
    ''' </summary>
    Public Module SadpDiscovery

#Region "Costanti e pacchetto"

        Public Const MulticastAddressText As String = "239.255.255.250"
        Public Const MulticastPort As Integer = 37020
        ''' <summary>Secondo invio della sonda, nel caso il primo pacchetto UDP si perda (Wi-Fi, switch).</summary>
        Private Const ResendDelayMs As Integer = 220
        Private Const ReceiveBufferSize As Integer = 8192
        ''' <summary>Opzione di Windows che evita il 10054 (ICMP Port Unreachable) su un socket UDP in ascolto.</summary>
        Private Const SioUdpConnReset As Integer = -1744830452

        Private ReadOnly MulticastAddress As IPAddress = IPAddress.Parse(MulticastAddressText)
        Private ReadOnly MulticastEndpoint As New IPEndPoint(MulticastAddress, MulticastPort)
        ''' <summary>Molti firmware scrivono "V5.5.82build 190909": "build" va staccato, anche per leggere l'anno.</summary>
        Private ReadOnly BuildPattern As New Regex("(?<=\d)build", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant Or RegexOptions.Compiled)

        ''' <summary>Il pacchetto XML_Probe dell'interrogazione SADP (tipo "inquiry").</summary>
        Public Function BuildProbeXml(Optional uuid As String = Nothing) As String
            Dim id = If(String.IsNullOrWhiteSpace(uuid), Guid.NewGuid().ToString("D"), uuid.Trim())
            Return $"<?xml version=""1.0"" encoding=""UTF-8""?><Probe><Uuid>{id}</Uuid><Types>inquiry</Types></Probe>"
        End Function

#End Region

#Region "Interrogazione"

        ''' <summary>
        ''' Manda la sonda in multicast da ogni scheda locale indicata e raccoglie le risposte entro <paramref name="timeoutMs"/>
        ''' millisecondi. Le risposte sono distinte per MAC e indirizzo.
        ''' </summary>
        Public Async Function QueryAsync(localAddresses As IEnumerable(Of IPAddress), timeoutMs As Integer, ct As CancellationToken) As Task(Of IReadOnlyList(Of SadpDeviceInfo))
            Dim ips = Ipv4Only(localAddresses)
            If ips.Count = 0 Then Return Array.Empty(Of SadpDeviceInfo)()

            Dim results As New ConcurrentDictionary(Of String, SadpDeviceInfo)(StringComparer.OrdinalIgnoreCase)
            Dim probeBytes = Encoding.UTF8.GetBytes(BuildProbeXml())
            Using deadline = CancellationTokenSource.CreateLinkedTokenSource(ct)
                deadline.CancelAfter(Math.Clamp(timeoutMs, 300, 4000))
                Try
                    Await Task.WhenAll(ips.Select(Function(ip) ProbeInterfaceAsync(ip, probeBytes, results, deadline.Token, ct))).ConfigureAwait(False)
                Catch ex As OperationCanceledException When ct.IsCancellationRequested
                    Throw
                Catch
                    ' errori di socket o fine della finestra: valgono i dispositivi raccolti fin lì
                End Try
            End Using
            Return results.Values.ToList()
        End Function

        ''' <summary>
        ''' Manda la sonda in unicast (UDP 37020) a ogni indirizzo indicato: così modello, firmware, seriale e MAC dei
        ''' dispositivi Hikvision arrivano anche da sottoreti instradate o in VPN, dove il multicast non passa.
        ''' L'indirizzo di ogni risposta è quello da cui è arrivata, non quello che il dispositivo dichiara.
        ''' </summary>
        Public Async Function QueryUnicastAsync(targets As IEnumerable(Of IPAddress), timeoutMs As Integer, ct As CancellationToken) As Task(Of IReadOnlyList(Of SadpDeviceInfo))
            Dim ips = Ipv4Only(targets)
            If ips.Count = 0 Then Return Array.Empty(Of SadpDeviceInfo)()

            Dim results As New ConcurrentDictionary(Of String, SadpDeviceInfo)(StringComparer.OrdinalIgnoreCase)
            Dim probeBytes = Encoding.UTF8.GetBytes(BuildProbeXml())
            Dim sock As Socket = Nothing
            Using deadline = CancellationTokenSource.CreateLinkedTokenSource(ct)
                deadline.CancelAfter(Math.Clamp(timeoutMs, 400, 4000))
                Try
                    sock = NewUdpSocket()
                    sock.Bind(New IPEndPoint(IPAddress.Any, 0))
                    Dim send = Async Function() As Task
                                   For Each ip In ips
                                       If deadline.IsCancellationRequested Then Exit For
                                       Try
                                           Await sock.SendToAsync(probeBytes, SocketFlags.None, New IPEndPoint(ip, MulticastPort), deadline.Token).ConfigureAwait(False)
                                       Catch
                                           ' indirizzo non raggiungibile: si passa al prossimo
                                       End Try
                                   Next
                               End Function
                    Await send().ConfigureAwait(False)
                    ScheduleResend(send, deadline.Token)
                    Await ReceiveRepliesAsync(sock, results, deadline.Token, trustRemoteIp:=True).ConfigureAwait(False)
                Catch ex As OperationCanceledException When ct.IsCancellationRequested
                    Throw
                Catch
                    ' fine del tempo di ascolto
                Finally
                    sock?.Dispose()
                End Try
            End Using
            Return results.Values.ToList()
        End Function

        ''' <summary>Sonda multicast da una scheda locale: iscrizione al gruppo, invio (due volte) e ascolto fino alla scadenza.</summary>
        Private Async Function ProbeInterfaceAsync(localIp As IPAddress, probeBytes As Byte(), results As ConcurrentDictionary(Of String, SadpDeviceInfo),
                                                   deadline As CancellationToken, ct As CancellationToken) As Task
            Dim sock As Socket = Nothing
            Try
                sock = NewUdpSocket()
                ' sulla porta 37020 della scheda arrivano anche le risposte mandate in multicast; se è occupata va bene una porta libera
                Try
                    sock.Bind(New IPEndPoint(localIp, MulticastPort))
                Catch
                    sock.Bind(New IPEndPoint(localIp, 0))
                End Try
                Try
                    sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localIp.GetAddressBytes())
                    sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2)
                    sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, New MulticastOption(MulticastAddress, localIp))
                Catch
                    ' senza iscrizione al gruppo restano l'invio e le risposte unicast
                End Try

                Dim send = Async Function() As Task
                               Await sock.SendToAsync(probeBytes, SocketFlags.None, MulticastEndpoint, deadline).ConfigureAwait(False)
                           End Function
                Await send().ConfigureAwait(False)
                ScheduleResend(send, deadline)
                Await ReceiveRepliesAsync(sock, results, deadline, trustRemoteIp:=False).ConfigureAwait(False)
            Catch ex As OperationCanceledException When ct.IsCancellationRequested
                Throw
            Catch
                ' fine del tempo di ascolto o scheda non disponibile
            Finally
                sock?.Dispose()
            End Try
        End Function

        ''' <summary>Socket UDP con riuso dell'indirizzo e, su Windows, senza l'errore 10054 che fermerebbe l'ascolto delle altre risposte.</summary>
        Private Function NewUdpSocket() As Socket
            Dim sock As New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, True)
            Try
                If OperatingSystem.IsWindows() Then sock.IOControl(SioUdpConnReset, New Byte() {0, 0, 0, 0}, Nothing)
            Catch
            End Try
            Return sock
        End Function

        ''' <summary>Ripete l'invio dopo <see cref="ResendDelayMs"/> ms senza bloccare l'ascolto; errori e scadenza non contano.</summary>
        Private Sub ScheduleResend(send As Func(Of Task), deadline As CancellationToken)
            Task.Run(Async Function()
                         Try
                             Await Task.Delay(ResendDelayMs, deadline).ConfigureAwait(False)
                             Await send().ConfigureAwait(False)
                         Catch
                         End Try
                     End Function, CancellationToken.None)
        End Sub

        ''' <summary>
        ''' Legge le risposte sul socket finché la scadenza non arriva; ogni risposta valida finisce in <paramref name="results"/>
        ''' (chiave MAC e indirizzo, così un dispositivo con due indirizzi conta per entrambi).
        ''' </summary>
        Private Async Function ReceiveRepliesAsync(sock As Socket, results As ConcurrentDictionary(Of String, SadpDeviceInfo),
                                                   deadline As CancellationToken, trustRemoteIp As Boolean) As Task
            Dim buffer(ReceiveBufferSize - 1) As Byte
            Dim anyEndPoint As EndPoint = New IPEndPoint(IPAddress.Any, 0)
            While Not deadline.IsCancellationRequested
                Dim received = Await sock.ReceiveFromAsync(buffer, SocketFlags.None, anyEndPoint, deadline).ConfigureAwait(False)
                If received.ReceivedBytes <= 0 Then Continue While
                Dim remoteIp = TryCast(received.RemoteEndPoint, IPEndPoint)?.Address?.ToString()
                Dim info = ParseHello(Encoding.UTF8.GetString(buffer, 0, received.ReceivedBytes), remoteIp)
                If info Is Nothing Then Continue While
                If trustRemoteIp AndAlso Not String.IsNullOrEmpty(remoteIp) Then info.Ip = remoteIp
                results.TryAdd($"{info.Mac}|{info.Ip}", info)
            End While
        End Function

        Private Function Ipv4Only(addresses As IEnumerable(Of IPAddress)) As List(Of IPAddress)
            If addresses Is Nothing Then Return New List(Of IPAddress)()
            Return addresses.Where(Function(a) a IsNot Nothing AndAlso a.AddressFamily = AddressFamily.InterNetwork AndAlso Not IPAddress.IsLoopback(a)).Distinct().ToList()
        End Function

#End Region

#Region "Interpretazione"

        ''' <summary>
        ''' Interpreta una risposta XML &lt;Hello&gt; o &lt;ProbeMatch&gt; di un dispositivo Hikvision; Nothing se il testo è
        ''' malformato, non è una risposta SADP (per esempio l'eco della nostra sonda) o non ha né indirizzo né MAC validi.
        ''' </summary>
        Public Function ParseHello(xml As String, Optional fallbackIp As String = Nothing) As SadpDeviceInfo
            If String.IsNullOrWhiteSpace(xml) Then Return Nothing
            Dim trimmed = xml.TrimStart()
            If Not trimmed.StartsWith("<", StringComparison.Ordinal) Then Return Nothing
            Try
                Dim root = XDocument.Parse(trimmed).Root
                If root Is Nothing Then Return Nothing
                ' le telecamere rispondono con <ProbeMatch> ripetendo <Types>inquiry</Types>: il nome della radice basta a scartare l'eco
                Dim rootName = root.Name.LocalName
                If Not rootName.Equals("Hello", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not rootName.Equals("ProbeMatch", StringComparison.OrdinalIgnoreCase) Then Return Nothing

                Dim rawIp = FirstNonEmpty(ElementValue(root, "IPv4Address"), fallbackIp)
                Dim parsedIp As IPAddress = Nothing
                Dim cleanIp = If(rawIp IsNot Nothing AndAlso IPAddress.TryParse(rawIp, parsedIp) AndAlso parsedIp.AddressFamily = AddressFamily.InterNetwork AndAlso Not parsedIp.Equals(IPAddress.Any),
                                 parsedIp.ToString(), Nothing)
                Dim mac = MacVendorLookup.Normalize(ElementValue(root, "MAC"))
                If cleanIp Is Nothing AndAlso mac Is Nothing Then Return Nothing

                Return New SadpDeviceInfo With {
                    .Ip = If(cleanIp, ""),
                    .Mac = mac,
                    .Model = CleanText(ElementValue(root, "DeviceDescription")),
                    .SerialNumber = CleanText(ElementValue(root, "DeviceSN")),
                    .SoftwareVersion = NormalizeFirmware(ElementValue(root, "SoftwareVersion")),
                    .DspVersion = CleanText(ElementValue(root, "DSPVersion")),
                    .DeviceType = ParseInt(ElementValue(root, "DeviceType"), Integer.MinValue, Integer.MaxValue),
                    .HttpPort = If(ParseInt(ElementValue(root, "HttpPort"), 1, 65535), 80),
                    .CommandPort = If(ParseInt(ElementValue(root, "CommandPort"), 1, 65535), 8000),
                    .AnalogChannels = If(ParseInt(ElementValue(root, "AnalogChannelNum"), 0, Integer.MaxValue), 0),
                    .DigitalChannels = If(ParseInt(ElementValue(root, "DigitalChannelNum"), 0, Integer.MaxValue), 0),
                    .Activated = ParseBool(ElementValue(root, "Activated")),
                    .Dhcp = ParseBool(ElementValue(root, "DHCP")),
                    .BootTime = CleanText(ElementValue(root, "BootTime")),
                    .OemInfo = CleanText(ElementValue(root, "OEMInfo"))
                }
            Catch
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Banner per la porta SDK (CommandPort, di solito 8000) dai dati SADP:
        ''' "Hikvision SDK · DS-2CD2143G2-I · FW V5.7.3 build 220112 · SN …", più "NON ATTIVATO" se il dispositivo è vergine.
        ''' </summary>
        Public Function FormatBanner(info As SadpDeviceInfo) As String
            If info Is Nothing Then Return Nothing
            Dim parts As New List(Of String)(5) From {"Hikvision SDK"}
            If Not String.IsNullOrWhiteSpace(info.Model) Then parts.Add(info.Model)
            If Not String.IsNullOrWhiteSpace(info.SoftwareVersion) Then
                Dim fw = info.SoftwareVersion
                If Not fw.StartsWith("FW ", StringComparison.OrdinalIgnoreCase) AndAlso Not fw.StartsWith("Firmware ", StringComparison.OrdinalIgnoreCase) Then fw = "FW " & fw
                parts.Add(fw)
            End If
            If Not String.IsNullOrWhiteSpace(info.SerialNumber) Then
                Dim sn = info.SerialNumber
                ' il seriale spesso inizia con il modello: si mostra solo la parte che segue, per non ripeterlo
                If Not String.IsNullOrWhiteSpace(info.Model) AndAlso sn.StartsWith(info.Model, StringComparison.OrdinalIgnoreCase) AndAlso sn.Length > info.Model.Length + 4 Then
                    sn = sn.Substring(info.Model.Length).TrimStart("-"c, "_"c, " "c)
                End If
                parts.Add("SN " & sn)
            End If
            If info.Activated.HasValue AndAlso Not info.Activated.Value Then parts.Add("NON ATTIVATO")
            Return BannerGrabber.Clean(String.Join(" · ", parts), 160)
        End Function

        Private Function ElementValue(root As XElement, localName As String) As String
            Return root.Descendants().FirstOrDefault(Function(x) x.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value
        End Function

        Private Function FirstNonEmpty(a As String, b As String) As String
            If Not String.IsNullOrWhiteSpace(a) Then Return a.Trim()
            If Not String.IsNullOrWhiteSpace(b) Then Return b.Trim()
            Return Nothing
        End Function

        Private Function CleanText(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return Nothing
            Return BannerGrabber.Clean(value, 100)
        End Function

        Private Function NormalizeFirmware(value As String) As String
            Dim text = CleanText(value)
            Return If(text Is Nothing, Nothing, BuildPattern.Replace(text, " build"))
        End Function

        ''' <summary>Numero intero tra i limiti indicati, oppure Nothing.</summary>
        Private Function ParseInt(value As String, min As Integer, max As Integer) As Integer?
            Dim n As Integer
            If Integer.TryParse(value, n) AndAlso n >= min AndAlso n <= max Then Return n
            Return Nothing
        End Function

        Private Function ParseBool(value As String) As Boolean?
            If String.IsNullOrWhiteSpace(value) Then Return Nothing
            Dim v = value.Trim()
            If v.Equals("true", StringComparison.OrdinalIgnoreCase) OrElse v = "1" Then Return True
            If v.Equals("false", StringComparison.OrdinalIgnoreCase) OrElse v = "0" Then Return False
            Return Nothing
        End Function

#End Region

    End Module

End Namespace
