Option Strict On

Imports WhoIsPro.Utils

Namespace Network.Scan

    ''' <summary>
    ''' Più indirizzi IPv4 con lo stesso MAC nella stessa scansione (PC con più IP sulla scheda, router con alias, telecamera
    ''' con cavo e Wi-Fi sullo stesso MAC) sono un solo dispositivo: una riga con l'indirizzo principale e gli altri in
    ''' <see cref="DiscoveredHost.ExtraIps"/>, così il conteggio dice quanti dispositivi ci sono davvero.
    ''' Non si uniscono gruppi sospetti: più di <see cref="MaxAddressesPerDevice"/> indirizzi (ripetitore Wi-Fi in modalità
    ''' MAC-NAT o proxy ARP: tanti dispositivi dietro un solo MAC), nomi NetBIOS diversi, TTL diversi (sistemi diversi) o
    ''' categorie diverse.
    ''' </summary>
    Public Module SameDeviceMerger

        ''' <summary>Oltre questo numero di indirizzi sullo stesso MAC si pensa a un ripetitore o a un proxy ARP: niente unione.</summary>
        Public Const MaxAddressesPerDevice As Integer = 4

        ''' <summary>Dati minimi di un indirizzo per decidere se il gruppo è un solo dispositivo.</summary>
        Public Structure Candidate
            Public Ip As String
            Public NetBios As String
            Public Category As String
            Public Ttl As Integer?
        End Structure

        ''' <summary>Vero se gli indirizzi (stesso MAC) sono con buona probabilità lo stesso dispositivo.</summary>
        Public Function IsSameDevice(group As IReadOnlyCollection(Of Candidate)) As Boolean
            If group Is Nothing OrElse group.Count < 2 OrElse group.Count > MaxAddressesPerDevice Then Return False
            Dim names = group.Select(Function(c) If(c.NetBios, "").Trim()).Where(Function(n) n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            If names > 1 Then Return False
            Dim ttls = group.Where(Function(c) c.Ttl.HasValue AndAlso c.Ttl.Value > 0).Select(Function(c) c.Ttl.Value).Distinct().Count()
            If ttls > 1 Then Return False
            Dim categories = group.Select(Function(c) If(c.Category, "")).
                Where(Function(c) c.Length > 0 AndAlso Not String.Equals(c, DeviceClassifier.CategoryOther, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Count()
            Return categories <= 1
        End Function

        ''' <summary>
        ''' Unisce gli host con lo stesso MAC: resta il principale (gateway, poi quello che ha risposto a SADP con il suo IP, poi
        ''' questa macchina, poi chi risponde al ping, poi chi ha più porte, poi l'IP più basso) con gli altri indirizzi, le
        ''' porte e i nomi che gli mancavano. Restituisce l'elenco aggiornato e i principali toccati.
        ''' </summary>
        Public Function MergeByMac(hosts As IReadOnlyList(Of DiscoveredHost)) As (Hosts As List(Of DiscoveredHost), Merged As List(Of DiscoveredHost))
            Dim merged As New List(Of DiscoveredHost)()
            If hosts Is Nothing Then Return (New List(Of DiscoveredHost)(), merged)
            Dim removed As New HashSet(Of DiscoveredHost)()
            Dim groups = hosts.Where(Function(h) MacVendorLookup.Normalize(h.Mac) IsNot Nothing).
                GroupBy(Function(h) MacVendorLookup.Normalize(h.Mac), StringComparer.OrdinalIgnoreCase).
                Where(Function(g) g.Count() > 1)
            For Each g In groups
                Dim members = g.ToList()
                If Not IsSameDevice(members.Select(Function(h) New Candidate With {.Ip = h.Ip, .NetBios = h.NetBiosName, .Category = h.Category, .Ttl = h.Ttl}).ToList()) Then Continue For
                Dim ordered = members.OrderByDescending(Function(h) h.IsGateway).
                    ThenByDescending(Function(h) h.Sadp IsNot Nothing AndAlso String.Equals(h.Sadp.Ip, h.Ip, StringComparison.Ordinal)).
                    ThenByDescending(Function(h) h.IsLocalMachine).
                    ThenByDescending(Function(h) h.RespondsToPing).
                    ThenByDescending(Function(h) h.Ports.Count).
                    ThenBy(Function(h) h.Ip, IpComparer.Instance).ToList()
                Dim primary = ordered(0)
                For Each other In ordered.Skip(1)
                    Absorb(primary, other)
                    removed.Add(other)
                Next
                primary.ExtraIps.Sort(IpComparer.Instance)
                merged.Add(primary)
            Next
            Return (hosts.Where(Function(h) Not removed.Contains(h)).ToList(), merged)
        End Function

        Private Sub Absorb(primary As DiscoveredHost, other As DiscoveredHost)
            If Not primary.ExtraIps.Contains(other.Ip) AndAlso other.Ip <> primary.Ip Then primary.ExtraIps.Add(other.Ip)
            For Each ip In other.ExtraIps
                If ip <> primary.Ip AndAlso Not primary.ExtraIps.Contains(ip) Then primary.ExtraIps.Add(ip)
            Next
            For Each p In other.Ports
                Dim mine = primary.Ports.FirstOrDefault(Function(x) x.Port = p.Port)
                If mine Is Nothing Then
                    primary.Ports.Add(New OpenPort(p.Port, p.Protocol, p.Service, p.Banner))
                ElseIf String.IsNullOrWhiteSpace(mine.Banner) AndAlso Not String.IsNullOrWhiteSpace(p.Banner) Then
                    mine.Banner = p.Banner
                End If
            Next
            primary.Ports.Sort(Function(a, b) a.Port.CompareTo(b.Port))
            If String.IsNullOrWhiteSpace(primary.HostName) Then primary.HostName = other.HostName
            If String.IsNullOrWhiteSpace(primary.NetBiosName) Then
                primary.NetBiosName = other.NetBiosName
                primary.NetBiosWorkgroup = other.NetBiosWorkgroup
            End If
            If primary.Sadp Is Nothing Then primary.Sadp = other.Sadp
            If Not primary.Ttl.HasValue Then primary.Ttl = other.Ttl
            primary.RespondsToPing = primary.RespondsToPing OrElse other.RespondsToPing
            If other.LatencyMs.HasValue AndAlso (Not primary.LatencyMs.HasValue OrElse other.LatencyMs.Value < primary.LatencyMs.Value) Then primary.LatencyMs = other.LatencyMs
            primary.IsGateway = primary.IsGateway OrElse other.IsGateway
            primary.IsLocalMachine = primary.IsLocalMachine OrElse other.IsLocalMachine
        End Sub

    End Module

End Namespace
