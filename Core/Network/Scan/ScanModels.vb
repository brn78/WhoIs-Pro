Option Strict On

Imports System.Net
Imports System.Text

Namespace Network.Scan

#Region "Opzioni e avanzamento"

    ''' <summary>Opzioni di una scansione della rete locale.</summary>
    Public Class ScanOptions
        ''' <summary>Rete da scansionare: "192.168.1" (prefisso /24), "192.168.1.0/24", "192.168.1.10-60", un solo IP; vuoto = rete principale.</summary>
        Public Property Target As String = ""
        ''' <summary>Risoluzione dei nomi (DNS inverso e NetBIOS).</summary>
        Public Property ResolveNames As Boolean = True
        ''' <summary>Scansione TCP delle porte sugli host trovati.</summary>
        Public Property ScanPorts As Boolean = True
        ''' <summary>Porte da provare; vuoto = elenco comune di PortDictionary.CommonPorts.</summary>
        Public Property Ports As IReadOnlyList(Of Integer)
        ''' <summary>Lettura dei banner (HTTP, SSH, FTP, SMTP…) sulle porte aperte.</summary>
        Public Property GrabBanners As Boolean = True
        ''' <summary>Timeout per singola prova (ping, connessione TCP, DNS) in millisecondi.</summary>
        Public Property TimeoutMs As Integer = 700
        ''' <summary>Quante prove in parallelo.</summary>
        Public Property Parallelism As Integer = 128
    End Class

    ''' <summary>Avanzamento: fase ("ping", "arp", "nomi", "porte", "banner", "fine"), fatti/totale, un dettaglio leggibile e l'host trovato o aggiornato.</summary>
    Public Class ScanPhaseProgress
        Public Sub New(phase As String, done As Integer, total As Integer, detail As String, Optional host As DiscoveredHost = Nothing)
            Me.Phase = phase
            Me.Done = done
            Me.Total = total
            Me.Detail = detail
            ' Progress(Of T) recapita su un altro thread mentre la scansione continua ad arricchire l'host (porte, banner, unione MAC)
            Me.Host = host?.Snapshot()
        End Sub

        Public ReadOnly Property Phase As String
        Public ReadOnly Property Done As Integer
        Public ReadOnly Property Total As Integer
        Public ReadOnly Property Detail As String
        ''' <summary>Host appena trovato o arricchito in questa fase (nomi, porte, classificazione); Nothing per i soli conteggi.</summary>
        Public ReadOnly Property Host As DiscoveredHost

        ''' <summary>Nome della fase per chi legge.</summary>
        Public Shared Function LabelOf(phase As String) As String
            Select Case phase
                Case "ping" : Return "Ping"
                Case "arp" : Return "Tabella ARP"
                Case "nomi" : Return "Nomi DNS e NetBIOS"
                Case "porte" : Return "Porte TCP"
                Case "banner" : Return "Banner dei servizi"
                Case "fine" : Return "Classificazione"
                Case Else : Return phase
            End Select
        End Function

        ''' <summary>Percentuale complessiva della scansione: le fasi pesano in modo diverso (il ping e le porte sono le più lunghe).</summary>
        Public Function OverallPercent(options As ScanOptions) As Integer
            Dim weights As New List(Of (Phase As String, Weight As Double)) From {("ping", 0.4), ("arp", 0.05)}
            If options.ResolveNames Then weights.Add(("nomi", 0.15))
            If options.ScanPorts Then weights.Add(("porte", 0.3))
            If options.ScanPorts AndAlso options.GrabBanners Then weights.Add(("banner", 0.1))
            Dim sum = weights.Sum(Function(w) w.Weight)
            Dim before = 0.0
            For Each w In weights
                If w.Phase = Phase Then
                    Dim frac = If(Total > 0, Math.Min(1.0, Done / Total), 1.0)
                    Return CInt(Math.Floor((before + w.Weight * frac) / sum * 100))
                End If
                before += w.Weight
            Next
            Return If(Phase = "fine", 100, 0)
        End Function
    End Class

#End Region

#Region "Risultati"

    ''' <summary>Porta TCP aperta con servizio riconosciuto e banner, se letto.</summary>
    Public Class OpenPort
        Public Sub New()
        End Sub

        Public Sub New(port As Integer, protocol As String, service As String, banner As String)
            Me.Port = port
            Me.Protocol = protocol
            Me.Service = service
            Me.Banner = banner
        End Sub

        Public Property Port As Integer
        Public Property Protocol As String = "TCP"
        Public Property Service As String
        Public Property Banner As String

        Public Overrides Function ToString() As String
            Return If(String.IsNullOrEmpty(Service), Port.ToString(), $"{Port}/{Service}")
        End Function
    End Class

    ''' <summary>Un host trovato nella rete: tutto quello che la scansione ha raccolto, normalizzato.</summary>
    Public Class DiscoveredHost
        Public Property Ip As String = ""
        ''' <summary>MAC normalizzato "aa:bb:cc:dd:ee:ff", oppure Nothing se non noto (host fuori dalla rete locale).</summary>
        Public Property Mac As String
        Public Property Vendor As String
        Public Property HostName As String
        Public Property NetBiosName As String
        Public Property NetBiosWorkgroup As String
        ''' <summary>Tipo riconosciuto (es. "Stampante di rete HP", "NAS Synology", "Host Hyper-V", "Router").</summary>
        Public Property DeviceType As String = "Dispositivo"
        ''' <summary>Categoria ampia (DeviceClassifier.Category*).</summary>
        Public Property Category As String = "Altro"
        Public Property RespondsToPing As Boolean
        Public Property LatencyMs As Double?
        Public Property Ttl As Integer?
        ''' <summary>Stima del sistema dal TTL e dai banner ("Windows", "Linux/Unix o dispositivo", "Apparato di rete"…).</summary>
        Public Property OsGuess As String
        Public Property IsGateway As Boolean
        Public Property IsLocalMachine As Boolean
        ''' <summary>Metadati SADP se il dispositivo ha risposto alla sonda multicast Hikvision; Nothing altrimenti.</summary>
        <System.Text.Json.Serialization.JsonIgnore>
        Public Property Sadp As SadpDeviceInfo
        Public ReadOnly Property Ports As New List(Of OpenPort)()
        ''' <summary>Altri indirizzi IPv4 dello stesso dispositivo (stesso MAC) trovati nella scansione; vuoto se ne ha uno solo.</summary>
        Public ReadOnly Property ExtraIps As New List(Of String)()
        Public Property SeenAt As DateTime = DateTime.Now

        ''' <summary>Nome migliore disponibile: NetBIOS, DNS, altrimenti IP.</summary>
        Public ReadOnly Property DisplayName As String
            Get
                If Not String.IsNullOrWhiteSpace(NetBiosName) Then Return NetBiosName
                If Not String.IsNullOrWhiteSpace(HostName) Then Return HostName
                Return Ip
            End Get
        End Property

        ''' <summary>Copia indipendente dell'host (con porte e altri IPv4), sicura da leggere su un altro thread mentre la scansione prosegue.</summary>
        Public Function Snapshot() As DiscoveredHost
            Dim copy As New DiscoveredHost With {
                .Ip = Ip, .Mac = Mac, .Vendor = Vendor, .HostName = HostName, .NetBiosName = NetBiosName, .NetBiosWorkgroup = NetBiosWorkgroup,
                .DeviceType = DeviceType, .Category = Category, .RespondsToPing = RespondsToPing, .LatencyMs = LatencyMs, .Ttl = Ttl,
                .OsGuess = OsGuess, .IsGateway = IsGateway, .IsLocalMachine = IsLocalMachine, .Sadp = Sadp, .SeenAt = SeenAt
            }
            For Each p In Ports.ToArray()
                If p IsNot Nothing Then copy.Ports.Add(New OpenPort(p.Port, p.Protocol, p.Service, p.Banner))
            Next
            copy.ExtraIps.AddRange(ExtraIps.ToArray())
            Return copy
        End Function
    End Class

    ''' <summary>
    ''' Identità di una rete, per tenere separati gli archivi delle sedi: la stessa 192.168.1.0/24 esiste in mille posti, il
    ''' MAC del router no. Chiave "gw:" più il MAC del gateway; senza un gateway raggiungibile (bersaglio instradato, vecchio
    ''' registro) "net:" più la sottorete.
    ''' </summary>
    Public Class NetworkIdentity
        Public Property Key As String = ""
        Public Property Cidr As String = ""
        Public Property GatewayIp As String
        Public Property GatewayMac As String
        Public Property GatewayVendor As String
        Public Property InterfaceName As String

        ''' <summary>Vero se la rete è riconosciuta dal router (chiave "gw:"), falso se solo dalla sottorete.</summary>
        Public ReadOnly Property HasGateway As Boolean
            Get
                Return Key.StartsWith("gw:", StringComparison.Ordinal)
            End Get
        End Property

        ''' <summary>«192.168.1.0/24 · Keenetic» oppure «192.168.1.0/24».</summary>
        Public ReadOnly Property Label As String
            Get
                Dim who = ShortVendor(GatewayVendor)
                Return If(who Is Nothing, Cidr, $"{Cidr} · {who}")
            End Get
        End Property

        ''' <summary>Rete riconosciuta solo dalla sottorete (chiave "net:").</summary>
        Public Shared Function ForSubnet(cidr As String, Optional interfaceName As String = Nothing, Optional gatewayIp As String = Nothing) As NetworkIdentity
            Dim text = If(String.IsNullOrWhiteSpace(cidr), "sconosciuta", cidr.Trim())
            Return New NetworkIdentity With {.Key = "net:" & text, .Cidr = text, .InterfaceName = interfaceName, .GatewayIp = gatewayIp}
        End Function

        ''' <summary>La /24 di un indirizzo IPv4 ("192.168.1.0/24"), come sottorete; per tutto il resto l'indirizzo com'è.</summary>
        Public Shared Function ForIp(ip As String) As NetworkIdentity
            Dim value As UInteger
            If Ipv4.TryParse(If(ip, "").Trim(), value) Then Return ForSubnet($"{Ipv4.Format(value And &HFFFFFF00UI)}/24")
            Return ForSubnet(ip)
        End Function

        Private Shared ReadOnly LegalForms As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "gmbh", "co", "ltd", "limited", "inc", "incorporated", "corp", "corporation", "llc", "spa", "s.p.a", "srl", "s.r.l", "sas", "sa", "s.a",
            "ag", "bv", "b.v", "nv", "n.v", "oy", "ab", "a/s", "plc", "kg", "pte", "pty", "technology", "technologies", "digital", "electronics",
            "electronic", "communications", "communication", "networks", "network", "international", "industrial", "industries", "systems",
            "system", "company", "computer", "corporate", "group", "holdings", "broadband", "&", "und", "and"
        }

        ''' <summary>Nome breve di un produttore: «Keenetic Limited» → «Keenetic», «Hangzhou Hikvision Digital Technology Co.,Ltd.» → «Hangzhou Hikvision».</summary>
        Public Shared Function ShortVendor(vendor As String) As String
            If String.IsNullOrWhiteSpace(vendor) Then Return Nothing
            Dim words As New List(Of String)()
            For Each raw In vendor.Split({" "c, ","c}, StringSplitOptions.RemoveEmptyEntries)
                Dim w = raw.Trim().TrimEnd("."c)
                If w.Length = 0 Then Continue For
                If LegalForms.Contains(w) Then Exit For
                words.Add(w)
                If words.Count = 2 Then Exit For
            Next
            Return If(words.Count = 0, vendor.Trim(), String.Join(" ", words))
        End Function
    End Class

    ''' <summary>Rete locale di una scheda attiva.</summary>
    Public Class LocalNetwork
        Public Property InterfaceName As String
        Public Property LocalIp As String
        Public Property PrefixLength As Integer
        Public Property Gateway As String
        Public Property Cidr As String
        Public Property IsPrimary As Boolean
    End Class

    ''' <summary>Esito completo di una scansione.</summary>
    Public Class ScanReport
        Public Sub New(startedAt As DateTime, duration As TimeSpan, target As String, localIp As String, gateway As String, hosts As IReadOnlyList(Of DiscoveredHost))
            Me.StartedAt = startedAt
            Me.Duration = duration
            Me.Target = target
            Me.LocalIp = localIp
            Me.Gateway = gateway
            Me.Hosts = hosts
        End Sub

        Public ReadOnly Property StartedAt As DateTime
        Public ReadOnly Property Duration As TimeSpan
        Public ReadOnly Property Target As String
        Public ReadOnly Property LocalIp As String
        Public ReadOnly Property Gateway As String
        Public ReadOnly Property Hosts As IReadOnlyList(Of DiscoveredHost)
        ''' <summary>Porte TCP provate su ogni host (vuoto = scansione senza porte); Nothing se non si sa (rapporti costruiti a mano).</summary>
        Public Property ProbedPorts As IReadOnlyCollection(Of Integer)
        ''' <summary>La rete scansionata (router o sottorete); Nothing nei rapporti costruiti a mano.</summary>
        Public Property Network As NetworkIdentity

        ''' <summary>Testo leggibile del resoconto.</summary>
        Public Function ToText() As String
            Dim sb As New StringBuilder()
            sb.AppendLine($"Scansione di {Target} del {StartedAt:dd/MM/yyyy HH:mm} ({Duration.TotalSeconds:N0} s): {Hosts.Count} host")
            For Each h In Hosts.OrderBy(Function(x) x.Ip, IpComparer.Instance)
                sb.Append($"- {h.Ip}")
                If h.ExtraIps.Count > 0 Then sb.Append($" (anche {String.Join(", ", h.ExtraIps)})")
                If h.DisplayName <> h.Ip Then sb.Append($" {h.DisplayName}")
                If h.Mac IsNot Nothing Then sb.Append($" [{h.Mac}{If(h.Vendor Is Nothing, "", " " & h.Vendor)}]")
                sb.Append($" · {h.DeviceType}")
                If h.IsGateway Then sb.Append(" · gateway")
                If h.IsLocalMachine Then sb.Append(" · questa macchina")
                If h.OsGuess IsNot Nothing Then sb.Append($" · {h.OsGuess}")
                If h.Ports.Count > 0 Then sb.Append(" · porte " & String.Join(",", h.Ports.Select(Function(p) p.ToString())))
                sb.AppendLine()
            Next
            Return sb.ToString().TrimEnd()
        End Function
    End Class

#End Region

#Region "Contratto del motore"

    ''' <summary>Motore di scansione della rete locale (DiscoveryScanner); sostituibile o simulabile nei test.</summary>
    Public Interface INetworkScanner
        ''' <summary>Reti locali delle schede attive; la principale è quella con il gateway predefinito.</summary>
        Function GetLocalNetworks() As IReadOnlyList(Of LocalNetwork)
        ''' <summary>La rete a cui appartiene una scansione del bersaglio (vuoto = rete principale): router o sottorete.</summary>
        Function IdentifyNetwork(target As String) As NetworkIdentity
        Function ScanAsync(options As ScanOptions, progress As IProgress(Of ScanPhaseProgress), ct As Threading.CancellationToken) As Task(Of ScanReport)
    End Interface

#End Region

#Region "Supporto"

    ''' <summary>Ordina indirizzi IPv4 in modo numerico.</summary>
    Public NotInheritable Class IpComparer
        Implements IComparer(Of String)

        Public Shared ReadOnly Instance As New IpComparer()

        Public Function Compare(x As String, y As String) As Integer Implements IComparer(Of String).Compare
            Dim ua As UInteger = 0
            Dim ub As UInteger = 0
            If Ipv4.TryParse(x, ua) AndAlso Ipv4.TryParse(y, ub) Then Return ua.CompareTo(ub)
            Dim a As IPAddress = Nothing
            Dim b As IPAddress = Nothing
            If Not IPAddress.TryParse(x, a) OrElse Not IPAddress.TryParse(y, b) Then Return String.CompareOrdinal(x, y)
            Dim ba = a.GetAddressBytes()
            Dim bb = b.GetAddressBytes()
            For i = 0 To Math.Min(ba.Length, bb.Length) - 1
                Dim c = ba(i).CompareTo(bb(i))
                If c <> 0 Then Return c
            Next
            Return ba.Length.CompareTo(bb.Length)
        End Function
    End Class

#End Region

End Namespace
