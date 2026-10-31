Option Strict On

Imports System.Collections.Concurrent
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Text.RegularExpressions
Imports System.Threading
Imports WhoIsPro.Utils

Namespace Network.Scan

    ''' <summary>
    ''' Motore di scansione della rete locale, senza privilegi amministrativi: ping ICMP in parallelo, MAC dalla cache ARP
    ''' (tabella dei vicini e SendARP con la scheda LAN come sorgente), nomi da DNS inverso e NetBIOS, porte TCP con
    ''' SIO_TCP_INITIAL_RTO (porte chiuse istantanee), banner dei servizi, stima del sistema e classificazione.
    ''' Nessuna singola macchina può far fallire la scansione. Una /24 con le porte comuni in una quindicina di secondi.
    ''' </summary>
    Public NotInheritable Class DiscoveryScanner
        Implements INetworkScanner

#Region "Costanti"

        ''' <summary>Rete più ampia scansionabile: una /22, cioè 1024 indirizzi.</summary>
        Private Const MinPrefixLength As Integer = 22
        Private Const MaxAddresses As Integer = 1024
        ''' <summary>Ricerche DNS inverse contemporanee: Windows le esegue bloccando un thread ciascuna.</summary>
        Private Const MaxConcurrentDns As Integer = 16

        ''' <summary>Indizi nel nome o nella descrizione delle schede virtuali o di tunnel, che non possono essere principali.</summary>
        Private Shared ReadOnly VirtualAdapterHints As String() = {
            "vethernet", "hyper-v virtual", "vmware", "virtualbox", "tailscale", "wsl", "docker", "zerotier", "wireguard", "wintun",
            "tap-windows", "openvpn", "vpn", "anyconnect", "fortinet", "globalprotect", "pangp", "npcap", "loopback", "bluetooth",
            "virtual", "teredo", "isatap", "pseudo"
        }
        Private Shared ReadOnly PingPayload(31) As Byte
        Private Shared ReadOnly ThreeOctets As New Regex("^\d{1,3}\.\d{1,3}\.\d{1,3}\.?\*?$", RegexOptions.CultureInvariant Or RegexOptions.Compiled)

#End Region

#Region "Reti locali"

        ''' <summary>Scheda locale con quanto serve alla scansione (indice, MAC, appartenenza alla sottorete).</summary>
        Private NotInheritable Class LocalInterface
            Public Property Name As String
            Public Property Index As Integer
            Public Property Address As IPAddress
            Public Property PrefixLength As Integer
            Public Property Gateway As IPAddress
            Public Property IsVirtual As Boolean
            Public Property Mac As Byte()
            Public Property IsPrimary As Boolean

            Public ReadOnly Property Value As UInteger
                Get
                    Return Ipv4.ToUInt32(Address)
                End Get
            End Property
            Public ReadOnly Property NetworkAddress As UInteger
                Get
                    Return Value And Ipv4.Mask(PrefixLength)
                End Get
            End Property
            Public ReadOnly Property BroadcastAddress As UInteger
                Get
                    Return NetworkAddress Or Not Ipv4.Mask(PrefixLength)
                End Get
            End Property
            Public ReadOnly Property Cidr As String
                Get
                    Return $"{Ipv4.Format(NetworkAddress)}/{PrefixLength}"
                End Get
            End Property
            Public Function Contains(address As UInteger) As Boolean
                Return (address And Ipv4.Mask(PrefixLength)) = NetworkAddress
            End Function
            Public Function ToLocalNetwork() As LocalNetwork
                Return New LocalNetwork With {.InterfaceName = Name, .LocalIp = Address.ToString(), .PrefixLength = PrefixLength,
                                              .Gateway = Gateway?.ToString(), .Cidr = Cidr, .IsPrimary = IsPrimary}
            End Function
        End Class

        ''' <summary>Reti delle schede IPv4 attive; la principale (prima dell'elenco) è quella con il gateway predefinito.</summary>
        Public Function GetLocalNetworks() As IReadOnlyList(Of LocalNetwork) Implements INetworkScanner.GetLocalNetworks
            Return DiscoverInterfaces().Select(Function(i) i.ToLocalNetwork()).ToList()
        End Function

        Private Shared Function DiscoverInterfaces() As List(Of LocalInterface)
            Dim found As New List(Of LocalInterface)()
            Dim nics As NetworkInterface()
            Try
                nics = NetworkInterface.GetAllNetworkInterfaces()
            Catch
                Return found
            End Try
            For Each nic In nics
                Try
                    If nic.OperationalStatus <> OperationalStatus.Up OrElse nic.NetworkInterfaceType = NetworkInterfaceType.Loopback OrElse
                       nic.NetworkInterfaceType = NetworkInterfaceType.Tunnel Then Continue For
                    Dim props = nic.GetIPProperties()
                    Dim gateway = props.GatewayAddresses.Select(Function(g) g.Address).
                        FirstOrDefault(Function(a) a.AddressFamily = AddressFamily.InterNetwork AndAlso Not a.Equals(IPAddress.Any))
                    Dim index = -1
                    Try
                        index = If(props.GetIPv4Properties()?.Index, -1)
                    Catch
                        ' scheda senza proprietà IPv4: l'indice serve solo a riconoscere la rotta predefinita
                    End Try
                    Dim isVirtual = IsVirtualAdapter(nic, gateway)
                    Dim mac = nic.GetPhysicalAddress().GetAddressBytes()
                    For Each unicast In props.UnicastAddresses
                        Dim ip = unicast.Address
                        If ip.AddressFamily <> AddressFamily.InterNetwork OrElse IPAddress.IsLoopback(ip) OrElse Ipv4.IsApipa(Ipv4.ToUInt32(ip)) Then Continue For
                        Dim prefix = If(unicast.PrefixLength > 0 AndAlso unicast.PrefixLength <= 32, unicast.PrefixLength, Ipv4.PrefixFromMask(unicast.IPv4Mask))
                        found.Add(New LocalInterface With {.Name = nic.Name, .Index = index, .Address = ip, .PrefixLength = prefix, .Gateway = gateway, .IsVirtual = isVirtual, .Mac = mac})
                    Next
                Catch
                    ' una scheda illeggibile non impedisce di leggere le altre
                End Try
            Next

            ' Principale: non virtuale e con gateway, preferendo la scheda della rotta predefinita; se nessuna ha
            ' gateway, la prima non virtuale. Le schede virtuali restano nell'elenco ma non sono mai principali.
            Dim routeIndex = IpHelper.BestInterfaceIndex(IPAddress.Parse("8.8.8.8"))
            Dim primary = If(found.FirstOrDefault(Function(i) Not i.IsVirtual AndAlso i.Gateway IsNot Nothing AndAlso routeIndex.HasValue AndAlso i.Index = routeIndex.Value),
                             If(found.FirstOrDefault(Function(i) Not i.IsVirtual AndAlso i.Gateway IsNot Nothing),
                                found.FirstOrDefault(Function(i) Not i.IsVirtual)))
            If primary IsNot Nothing Then primary.IsPrimary = True
            Return found.OrderByDescending(Function(i) i.IsPrimary).ThenBy(Function(i) i.IsVirtual).ThenBy(Function(i) i.Name, StringComparer.OrdinalIgnoreCase).ToList()
        End Function

        ''' <summary>
        ''' Scheda virtuale o di tunnel. Eccezioni: la scheda di una VM Hyper-V ("Hyper-V Network Adapter") è la sua vera
        ''' scheda, e su un host Hyper-V la LAN passa da "vEthernet" sul vSwitch esterno, riconoscibile dal gateway.
        ''' </summary>
        Private Shared Function IsVirtualAdapter(nic As NetworkInterface, gateway As IPAddress) As Boolean
            Dim text = $"{nic.Name} {nic.Description}".ToLowerInvariant()
            If text.Contains("hyper-v network adapter", StringComparison.Ordinal) Then Return False
            Dim hyperVSwitch = text.Contains("vethernet", StringComparison.Ordinal) OrElse text.Contains("hyper-v virtual", StringComparison.Ordinal)
            If hyperVSwitch AndAlso gateway IsNot Nothing AndAlso Not text.Contains("default switch", StringComparison.Ordinal) AndAlso Not text.Contains("wsl", StringComparison.Ordinal) Then Return False
            Return VirtualAdapterHints.Any(Function(hint) text.Contains(hint, StringComparison.Ordinal))
        End Function

#End Region

#Region "Scansione"

        ''' <summary>
        ''' Scansione completa. Tutto il lavoro gira nel pool di thread: il thread chiamante (anche quello della finestra)
        ''' non si blocca mai. Bersaglio non valido: ArgumentException; annullamento: OperationCanceledException.
        ''' </summary>
        Public Function ScanAsync(options As ScanOptions, progress As IProgress(Of ScanPhaseProgress), ct As CancellationToken) As Task(Of ScanReport) Implements INetworkScanner.ScanAsync
            If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
            Return Task.Run(Function() ScanCoreAsync(options, progress, ct), ct)
        End Function

        Private Shared Async Function ScanCoreAsync(options As ScanOptions, progress As IProgress(Of ScanPhaseProgress), ct As CancellationToken) As Task(Of ScanReport)
            Dim startedAt = DateTime.Now
            Dim clock = Stopwatch.StartNew()
            Dim timeout = Math.Clamp(options.TimeoutMs, 100, 10000)
            Dim parallelism = Math.Clamp(options.Parallelism, 1, 512)

            Dim interfaces = DiscoverInterfaces()
            Dim plan = ResolveTarget(options.Target, interfaces)

            ' Il database dei produttori si carica in parallelo al ping, così non pesa sulla scansione
            Dim vendorWarmup = Task.Run(Function() MacVendorLookup.TotalVendorsLoaded, CancellationToken.None)

            Dim hosts As New ConcurrentDictionary(Of UInteger, DiscoveredHost)()
            Await PingPhaseAsync(plan, hosts, timeout, parallelism, progress, ct).ConfigureAwait(False)
            Dim arpOnly = Await ArpPhaseAsync(plan, hosts, timeout, parallelism, progress, ct).ConfigureAwait(False)
            MarkLocalMachineAndGateway(hosts, interfaces)

            Dim found = hosts.Values.OrderBy(Function(h) h.Ip, IpComparer.Instance).ToList()
            If options.ResolveNames Then Await NamesPhaseAsync(found, timeout, parallelism, progress, ct).ConfigureAwait(False)
            ' le porte provate finiscono nel rapporto: l'archivio confronta solo quelle (un gruppo diverso non «chiude» le altre)
            Dim probed As IReadOnlyList(Of Integer) = Array.Empty(Of Integer)()
            If options.ScanPorts Then
                probed = NormalizePorts(options.Ports)
                Await PortsPhaseAsync(found, probed, timeout, parallelism, progress, ct).ConfigureAwait(False)
                If options.GrabBanners Then Await BannerPhaseAsync(found, timeout, parallelism, progress, ct).ConfigureAwait(False)
            End If

            DropVanishedArpHosts(hosts, arpOnly)
            Await vendorWarmup.ConfigureAwait(False)

            Dim result = hosts.Values.OrderBy(Function(h) h.Ip, IpComparer.Instance).ToList()
            Dim final As New PhaseReporter(progress, "fine", result.Count)
            For Each host In result
                Describe(host)
                final.Step(Nothing, host)
            Next
            final.Complete($"{result.Count} host classificati")
            Return New ScanReport(startedAt, clock.Elapsed, plan.Label, If(plan.Interface?.Address.ToString(), ""), plan.Interface?.Gateway?.ToString(), result) With {.ProbedPorts = probed}
        End Function

        ''' <summary>Porte richieste valide, uniche e ordinate; l'elenco comune se mancano.</summary>
        Private Shared Function NormalizePorts(ports As IReadOnlyList(Of Integer)) As IReadOnlyList(Of Integer)
            Dim valid = If(ports, Array.Empty(Of Integer)()).Where(Function(p) p >= 1 AndAlso p <= 65535).Distinct().OrderBy(Function(p) p).ToArray()
            Return If(valid.Length > 0, valid, PortDictionary.CommonPorts)
        End Function

#End Region

#Region "Fase ping"

        Private Shared Async Function PingPhaseAsync(plan As ScanPlan, hosts As ConcurrentDictionary(Of UInteger, DiscoveredHost), timeout As Integer,
                                                     parallelism As Integer, progress As IProgress(Of ScanPhaseProgress), ct As CancellationToken) As Task
            Dim phase As New PhaseReporter(progress, "ping", plan.Addresses.Length)
            phase.Start($"Ping di {plan.Addresses.Length} indirizzi ({plan.Label})")
            Await RunThrottledAsync(plan.Addresses, parallelism,
                Async Function(address, token)
                    ' secondo tentativo per chi tace: il primo può perdersi mentre Windows risolve l'ARP o il Wi-Fi si sveglia
                    Dim reply = Await PingAsync(address, timeout, token).ConfigureAwait(False)
                    If reply Is Nothing Then reply = Await PingAsync(address, timeout, token).ConfigureAwait(False)
                    If reply Is Nothing Then
                        phase.Step()
                        Return
                    End If
                    Dim host As New DiscoveredHost With {.Ip = Ipv4.Format(address), .RespondsToPing = True, .LatencyMs = reply.RoundtripTime, .Ttl = reply.Options?.Ttl}
                    hosts(address) = host
                    phase.Step($"{host.Ip} risponde ({reply.RoundtripTime} ms)", host)
                End Function, ct).ConfigureAwait(False)
            phase.Complete($"{hosts.Count} host rispondono al ping")
        End Function

        Private Shared Async Function PingAsync(address As UInteger, timeout As Integer, ct As CancellationToken) As Task(Of PingReply)
            Try
                Using ping As New Ping()
                    Dim reply = Await ping.SendPingAsync(Ipv4.ToAddress(address), TimeSpan.FromMilliseconds(timeout), PingPayload, Nothing, ct).ConfigureAwait(False)
                    Return If(reply.Status = IPStatus.Success AndAlso Ipv4.ToUInt32(reply.Address) = address, reply, Nothing)
                End Using
            Catch ex As OperationCanceledException When ct.IsCancellationRequested
                Throw
            Catch
                Return Nothing
            End Try
        End Function

#End Region

#Region "Fase ARP"

        ''' <summary>
        ''' MAC degli host che rispondono (tabella dei vicini, poi SendARP per chi manca) e host che non rispondono al
        ''' ping ma hanno una voce ARP valida: il ping stesso fa partire la risoluzione ARP, a cui rispondono anche i
        ''' dispositivi con l'ICMP bloccato. Restituisce gli indirizzi trovati solo così.
        ''' </summary>
        Private Shared Async Function ArpPhaseAsync(plan As ScanPlan, hosts As ConcurrentDictionary(Of UInteger, DiscoveredHost), timeout As Integer,
                                                    parallelism As Integer, progress As IProgress(Of ScanPhaseProgress), ct As CancellationToken) As Task(Of HashSet(Of UInteger))
            Dim arpOnly As New HashSet(Of UInteger)()
            Dim onLink = plan.Addresses.Where(AddressOf plan.IsOnLink).ToHashSet()
            If onLink.Count = 0 Then
                Dim skipped As New PhaseReporter(progress, "arp", 1)
                skipped.Complete("Rete non direttamente collegata: MAC non disponibili")
                Return arpOnly
            End If

            Dim neighbors As Dictionary(Of UInteger, IpHelper.Neighbor) = If(IpHelper.ReadNeighbors(), New List(Of IpHelper.Neighbor)()).
                Where(Function(n) onLink.Contains(n.Address) AndAlso n.HasUsableMac AndAlso n.IsPresent).
                GroupBy(Function(n) n.Address).
                ToDictionary(Function(g) g.Key, Function(g) g.OrderByDescending(Function(n) n.State).First())

            Dim localAddresses = plan.Interfaces.Select(Function(i) i.Value).ToHashSet()
            Dim missing As New List(Of (Address As UInteger, Source As UInteger, Host As DiscoveredHost))()
            For Each pair In hosts
                Dim neighbor As IpHelper.Neighbor = Nothing
                If neighbors.TryGetValue(pair.Key, neighbor) Then
                    pair.Value.Mac = MacVendorLookup.Normalize(MacVendorLookup.FormatMac(neighbor.Mac))
                ElseIf onLink.Contains(pair.Key) AndAlso Not localAddresses.Contains(pair.Key) Then
                    missing.Add((pair.Key, plan.SourceFor(pair.Key), pair.Value))
                End If
            Next

            Dim phase As New PhaseReporter(progress, "arp", missing.Count + 1)
            phase.Start("Lettura della tabella ARP")
            phase.Step()
            Await RunThrottledAsync(missing, Math.Min(parallelism, 16),
                Async Function(item, token)
                    Dim mac = Await SendArpAsync(item.Address, item.Source, timeout * 2, token).ConfigureAwait(False)
                    If mac IsNot Nothing Then item.Host.Mac = mac
                    phase.Step(If(mac Is Nothing, Nothing, $"{item.Host.Ip} → {mac}"))
                End Function, ct).ConfigureAwait(False)

            For Each pair In neighbors
                If hosts.ContainsKey(pair.Key) Then Continue For
                Dim host As New DiscoveredHost With {.Ip = Ipv4.Format(pair.Key), .Mac = MacVendorLookup.Normalize(MacVendorLookup.FormatMac(pair.Value.Mac)), .RespondsToPing = False}
                hosts(pair.Key) = host
                arpOnly.Add(pair.Key)
                phase.Step($"{host.Ip} presente in ARP", host)
            Next
            phase.Complete(If(arpOnly.Count > 0, $"{arpOnly.Count} host trovati solo tramite ARP (non rispondono al ping)", "MAC raccolti dalla tabella ARP"))
            Return arpOnly
        End Function

        ''' <summary>
        ''' SendARP blocca il thread finché l'ARP non risponde: gira nel pool, con un limite di tempo. L'indirizzo sorgente
        ''' fissa la scheda: senza, Windows seguirebbe la rotta migliore, che può essere una VPN (es. rotta di subnet Tailscale).
        ''' </summary>
        Private Shared Async Function SendArpAsync(address As UInteger, source As UInteger, timeoutMs As Integer, ct As CancellationToken) As Task(Of String)
            Dim lookup = Task.Run(Function() IpHelper.SendArp(address, source), CancellationToken.None)
            Dim finished = Await Task.WhenAny(lookup, Task.Delay(timeoutMs, ct)).ConfigureAwait(False)
            Return If(finished Is lookup, Await lookup.ConfigureAwait(False), Nothing)
        End Function

        ''' <summary>
        ''' A fine scansione si rilegge la tabella ARP: un host visto solo lì, senza porte né risposta NetBIOS, la cui voce è
        ''' diventata irraggiungibile era una voce vecchia di un dispositivo che non c'è più.
        ''' </summary>
        Private Shared Sub DropVanishedArpHosts(hosts As ConcurrentDictionary(Of UInteger, DiscoveredHost), arpOnly As HashSet(Of UInteger))
            If arpOnly.Count = 0 Then Return
            Dim table = IpHelper.ReadNeighbors()
            If table Is Nothing Then Return
            Dim present = table.Where(Function(n) n.HasUsableMac AndAlso n.IsPresent).Select(Function(n) n.Address).ToHashSet()
            For Each address In arpOnly
                Dim host As DiscoveredHost = Nothing
                If Not hosts.TryGetValue(address, host) Then Continue For
                Dim evidence = host.Ports.Count > 0 OrElse host.NetBiosName IsNot Nothing OrElse host.IsGateway OrElse host.IsLocalMachine
                If Not evidence AndAlso Not present.Contains(address) Then hosts.TryRemove(address, host)
            Next
        End Sub

        ''' <summary>Gateway delle schede locali e indirizzi di questa macchina (con il MAC della scheda e il nome del PC).</summary>
        Private Shared Sub MarkLocalMachineAndGateway(hosts As ConcurrentDictionary(Of UInteger, DiscoveredHost), interfaces As List(Of LocalInterface))
            Dim gateways = interfaces.Where(Function(i) i.Gateway IsNot Nothing).Select(Function(i) Ipv4.ToUInt32(i.Gateway)).ToHashSet()
            For Each pair In hosts
                pair.Value.IsGateway = gateways.Contains(pair.Key)
                Dim local = interfaces.FirstOrDefault(Function(i) i.Value = pair.Key)
                If local Is Nothing Then Continue For
                pair.Value.IsLocalMachine = True
                If MacVendorLookup.IsUsableUnicast(local.Mac) Then pair.Value.Mac = MacVendorLookup.Normalize(MacVendorLookup.FormatMac(local.Mac))
                If pair.Value.NetBiosName Is Nothing Then pair.Value.NetBiosName = Environment.MachineName
            Next
        End Sub

#End Region

#Region "Fase nomi"

        Private Shared Async Function NamesPhaseAsync(hosts As List(Of DiscoveredHost), timeout As Integer, parallelism As Integer,
                                                      progress As IProgress(Of ScanPhaseProgress), ct As CancellationToken) As Task
            Dim phase As New PhaseReporter(progress, "nomi", hosts.Count)
            phase.Start($"Nomi DNS e NetBIOS di {hosts.Count} host")
            Dim nameTimeout = Math.Max(timeout, 1000)
            ' non si chiude mai: i posti si liberano anche dopo la fine della fase, quando terminano le ricerche lente
            Dim dnsGate As New SemaphoreSlim(MaxConcurrentDns, MaxConcurrentDns)

            Await RunThrottledAsync(hosts, Math.Min(parallelism, 64),
                Async Function(host, token)
                    Dim address = IPAddress.Parse(host.Ip)
                    Dim dns = ReverseDnsAsync(address, nameTimeout, dnsGate, token)
                    Dim netbios = NetBiosResolver.QueryAsync(address, nameTimeout, token)
                    Await Task.WhenAll(dns, netbios).ConfigureAwait(False)
                    If dns.Result IsNot Nothing Then host.HostName = dns.Result
                    Dim info = netbios.Result
                    If info IsNot Nothing Then
                        If info.Name IsNot Nothing Then host.NetBiosName = info.Name
                        host.NetBiosWorkgroup = info.Workgroup
                        If host.Mac Is Nothing Then host.Mac = info.Mac
                    End If
                    phase.Step(If(host.DisplayName <> host.Ip, $"{host.Ip} → {host.DisplayName}", Nothing), host)
                End Function, ct).ConfigureAwait(False)
            phase.Complete($"{hosts.Where(Function(h) h.DisplayName <> h.Ip).Count()} host con nome su {hosts.Count}")
        End Function

        ''' <summary>DNS inverso con timeout (Task.WhenAny): il nome restituito, senza punto finale, o Nothing.</summary>
        Private Shared Async Function ReverseDnsAsync(address As IPAddress, timeoutMs As Integer, gate As SemaphoreSlim, ct As CancellationToken) As Task(Of String)
            If Not Await gate.WaitAsync(timeoutMs, ct).ConfigureAwait(False) Then Return Nothing
            Dim lookup As Task(Of IPHostEntry)
            Try
                lookup = Dns.GetHostEntryAsync(address)
            Catch
                gate.Release()
                Return Nothing
            End Try
            ' il posto si libera quando la ricerca finisce davvero, non allo scadere del nostro tempo
            Dim unused = lookup.ContinueWith(Sub(t)
                                                 Dim ignored = t.Exception
                                                 gate.Release()
                                             End Sub, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
            Dim finished = Await Task.WhenAny(lookup, Task.Delay(timeoutMs, ct)).ConfigureAwait(False)
            If finished IsNot lookup OrElse Not lookup.IsCompletedSuccessfully Then Return Nothing
            Dim name = lookup.Result.HostName.Trim().TrimEnd("."c)
            Dim parsed As IPAddress = Nothing
            Return If(name.Length = 0 OrElse name = address.ToString() OrElse IPAddress.TryParse(name, parsed), Nothing, name)
        End Function

#End Region

#Region "Fase porte"

        Private Shared Async Function PortsPhaseAsync(hosts As List(Of DiscoveredHost), ports As IReadOnlyList(Of Integer), timeout As Integer, parallelism As Integer,
                                                      progress As IProgress(Of ScanPhaseProgress), ct As CancellationToken) As Task
            ' prove ordinate porta per porta: le connessioni contemporanee si distribuiscono su tutti gli host
            Dim addresses = hosts.ToDictionary(Function(h) h, Function(h) IPAddress.Parse(h.Ip))
            Dim probes As New List(Of (Host As DiscoveredHost, Port As Integer))(hosts.Count * ports.Count)
            For Each port In ports
                For Each host In hosts
                    probes.Add((host, port))
                Next
            Next
            Dim phase As New PhaseReporter(progress, "porte", probes.Count)
            phase.Start($"{ports.Count} porte TCP su {hosts.Count} host")
            Dim open As New ConcurrentDictionary(Of DiscoveredHost, ConcurrentBag(Of Integer))()

            Await RunThrottledAsync(probes, parallelism,
                Async Function(probe, token)
                    Using client = Await TcpConnector.ConnectAsync(addresses(probe.Host), probe.Port, timeout, token).ConfigureAwait(False)
                        If client Is Nothing Then
                            phase.Step()
                            Return
                        End If
                    End Using
                    open.GetOrAdd(probe.Host, Function(k) New ConcurrentBag(Of Integer)()).Add(probe.Port)
                    Dim service = PortDictionary.ServiceNameOrNull(probe.Port)
                    phase.Step($"{probe.Host.Ip}: porta {probe.Port} aperta{If(service Is Nothing, "", $" ({service})")}")
                End Function, ct).ConfigureAwait(False)

            For Each pair In open
                For Each port In pair.Value.Distinct().OrderBy(Function(p) p)
                    pair.Key.Ports.Add(New OpenPort(port, "TCP", PortDictionary.ServiceNameOrNull(port), Nothing))
                Next
                phase.Update(pair.Key)
            Next
            phase.Complete($"{open.Values.Sum(Function(p) p.Count)} porte aperte su {open.Count} host")
        End Function

#End Region

#Region "Fase banner"

        Private Shared Async Function BannerPhaseAsync(hosts As List(Of DiscoveredHost), timeout As Integer, parallelism As Integer,
                                                       progress As IProgress(Of ScanPhaseProgress), ct As CancellationToken) As Task
            Dim jobs = hosts.SelectMany(Function(h) h.Ports.Where(Function(p) BannerGrabber.Supports(p.Port)).Select(Function(p) (Host:=h, Port:=p.Port))).ToList()
            Dim phase As New PhaseReporter(progress, "banner", jobs.Count)
            phase.Start($"Banner di {jobs.Count} servizi")
            If jobs.Count = 0 Then
                phase.Complete("Nessun servizio da interrogare")
                Return
            End If
            ' tempi stretti: tutta la lettura (connessione compresa) entro poco più di due timeout
            Dim budget = Math.Clamp(timeout * 2, 800, 2500)
            Dim banners As New ConcurrentDictionary(Of (Host As DiscoveredHost, Port As Integer), String)()

            Await RunThrottledAsync(jobs, Math.Min(parallelism, 64),
                Async Function(job, token)
                    Dim banner = Await BannerGrabber.GrabAsync(IPAddress.Parse(job.Host.Ip), job.Port, budget, token).ConfigureAwait(False)
                    If Not String.IsNullOrWhiteSpace(banner) Then banners(job) = banner
                    phase.Step(If(banner Is Nothing, Nothing, $"{job.Host.Ip}:{job.Port} {banner}"))
                End Function, ct).ConfigureAwait(False)

            Dim touched As New HashSet(Of DiscoveredHost)()
            For Each pair In banners
                Dim index = pair.Key.Host.Ports.FindIndex(Function(p) p.Port = pair.Key.Port)
                If index >= 0 Then
                    pair.Key.Host.Ports(index).Banner = pair.Value
                    touched.Add(pair.Key.Host)
                End If
            Next
            For Each host In touched
                phase.Update(host)
            Next
            phase.Complete($"{banners.Count} banner letti")
        End Function

#End Region

#Region "Classificazione"

        ''' <summary>Produttore dal MAC, stima del sistema, tipo e categoria del dispositivo.</summary>
        Private Shared Sub Describe(host As DiscoveredHost)
            Try
                host.Mac = If(MacVendorLookup.Normalize(host.Mac), host.Mac)
                host.Vendor = MacVendorLookup.Lookup(host.Mac)
                Dim c = DeviceClassifier.Classify(host)
                host.DeviceType = c.DeviceType
                host.Category = c.Category
                ' dopo la classificazione: la stima del sistema tiene conto della categoria (un modulo IoT con TTL 128 non è Windows)
                host.OsGuess = If(host.IsLocalMachine, "Windows", DeviceClassifier.GuessOs(host))
            Catch
                ' un dispositivo dai dati strani resta "Dispositivo" senza fermare il resoconto
            End Try
        End Sub

#End Region

#Region "Bersaglio"

        ''' <summary>Indirizzi da provare, etichetta leggibile e scheda locale da cui si raggiunge la rete.</summary>
        Private NotInheritable Class ScanPlan
            Public Property Label As String
            Public Property Addresses As UInteger()
            Public Property [Interface] As LocalInterface
            Public Property Interfaces As List(Of LocalInterface)

            ''' <summary>Indirizzo nella sottorete di una scheda locale: raggiungibile via ARP senza passare dal router.</summary>
            Public Function IsOnLink(address As UInteger) As Boolean
                Return Interfaces.Any(Function(i) i.Contains(address))
            End Function

            ''' <summary>Indirizzo locale della scheda sulla stessa sottorete (non virtuale se possibile); 0 se nessuna.</summary>
            Public Function SourceFor(address As UInteger) As UInteger
                Dim i = If(Interfaces.FirstOrDefault(Function(x) Not x.IsVirtual AndAlso x.Contains(address)), Interfaces.FirstOrDefault(Function(x) x.Contains(address)))
                Return If(i Is Nothing, 0UI, i.Value)
            End Function
        End Class

        ''' <summary>
        ''' Interpreta il bersaglio: vuoto = rete principale (al massimo una /22 attorno a questo PC), "192.168.1" = /24,
        ''' "192.168.1.0/24" = CIDR fino a /22, "192.168.1.10-60" o "192.168.1.10-192.168.1.60" = intervallo, oppure un solo IP.
        ''' </summary>
        Private Shared Function ResolveTarget(target As String, interfaces As List(Of LocalInterface)) As ScanPlan
            Dim text = If(target, "").Trim()
            Dim first As UInteger, last As UInteger
            Dim label As String

            If text.Length = 0 Then
                Dim primary = interfaces.FirstOrDefault(Function(i) i.IsPrimary)
                If primary Is Nothing Then Throw New InvalidOperationException("Nessuna rete locale attiva: collega il PC a una rete oppure indica la rete da scansionare.")
                Dim prefix = Math.Max(primary.PrefixLength, MinPrefixLength)
                Dim network = primary.Value And Ipv4.Mask(prefix)
                HostRange(network, prefix, first, last)
                label = $"{Ipv4.Format(network)}/{prefix}"
            ElseIf ThreeOctets.IsMatch(text) Then
                Dim network As UInteger
                If Not Ipv4.TryParse(text.TrimEnd("."c, "*"c).TrimEnd("."c) & ".0", network) Then Throw InvalidTarget(text)
                HostRange(network, 24, first, last)
                label = $"{Ipv4.Format(network)}/24"
            ElseIf text.Contains("/"c) Then
                Dim parts = text.Split("/"c, 2, StringSplitOptions.TrimEntries)
                Dim address As UInteger, prefix As Integer
                If Not Ipv4.TryParse(parts(0), address) OrElse Not Integer.TryParse(parts(1), prefix) OrElse prefix < 0 OrElse prefix > 32 Then Throw InvalidTarget(text)
                If prefix < MinPrefixLength Then Throw New ArgumentException($"La rete {text} è troppo ampia: il massimo è una /{MinPrefixLength} ({MaxAddresses} indirizzi).", NameOf(target))
                Dim network = address And Ipv4.Mask(prefix)
                HostRange(network, prefix, first, last)
                label = $"{Ipv4.Format(network)}/{prefix}"
            ElseIf text.Contains("-"c) Then
                Dim parts = text.Split("-"c, 2, StringSplitOptions.TrimEntries)
                If Not Ipv4.TryParse(parts(0), first) Then Throw InvalidTarget(text)
                Dim endAddress As UInteger, lastOctet As Integer
                If Ipv4.TryParse(parts(1), endAddress) Then
                    last = endAddress
                ElseIf Integer.TryParse(parts(1), lastOctet) AndAlso lastOctet >= 0 AndAlso lastOctet <= 255 Then
                    last = (first And &HFFFFFF00UI) Or CUInt(lastOctet)
                Else
                    Throw InvalidTarget(text)
                End If
                If last < first Then
                    Dim t = first : first = last : last = t
                End If
                label = If((first And &HFFFFFF00UI) = (last And &HFFFFFF00UI), $"{Ipv4.Format(first)}-{last And &HFFUI}", $"{Ipv4.Format(first)}-{Ipv4.Format(last)}")
            Else
                Dim one As UInteger
                If Not Ipv4.TryParse(text, one) Then Throw InvalidTarget(text)
                If (one And &HFFUI) = 0 Then
                    ' "192.168.1.0" indica la rete, non l'indirizzo di rete: si intende la /24
                    HostRange(one, 24, first, last)
                    label = $"{Ipv4.Format(one)}/24"
                Else
                    first = one : last = one
                    label = Ipv4.Format(one)
                End If
            End If

            If CULng(last) - first + 1UL > CULng(MaxAddresses) Then Throw New ArgumentException($"L'intervallo {text} è troppo ampio: al massimo {MaxAddresses} indirizzi.", NameOf(target))

            ' indirizzi di rete e di broadcast delle sottoreti locali non sono host: si saltano
            Dim excluded = interfaces.Where(Function(i) i.PrefixLength <= 30).SelectMany(Function(i) {i.NetworkAddress, i.BroadcastAddress}).ToHashSet()
            Dim addresses = Enumerable.Range(0, CInt(last - first + 1UI)).Select(Function(offset) first + CUInt(offset)).
                Where(Function(a) Not excluded.Contains(a) OrElse first = last).ToArray()
            Dim iface = If(interfaces.FirstOrDefault(Function(i) i.Contains(first)), If(interfaces.FirstOrDefault(Function(i) i.IsPrimary), interfaces.FirstOrDefault()))
            Return New ScanPlan With {.Label = label, .Addresses = addresses, .Interface = iface, .Interfaces = interfaces}
        End Function

        ''' <summary>Primo e ultimo host della rete (senza indirizzo di rete e broadcast fino alla /30).</summary>
        Private Shared Sub HostRange(network As UInteger, prefix As Integer, ByRef first As UInteger, ByRef last As UInteger)
            Dim broadcast = network Or Not Ipv4.Mask(prefix)
            Select Case prefix
                Case 32
                    first = network : last = network
                Case 31
                    first = network : last = broadcast
                Case Else
                    first = network + 1UI : last = broadcast - 1UI
            End Select
        End Sub

        Private Shared Function InvalidTarget(text As String) As ArgumentException
            Return New ArgumentException($"Rete non valida: «{text}». Esempi validi: 192.168.1 (tutta la /24), 192.168.1.0/24, 192.168.1.10-60, 192.168.1.20.", "target")
        End Function

#End Region

#Region "Esecuzione in parallelo e avanzamento"

        ''' <summary>
        ''' Esegue <paramref name="body"/> su ogni elemento con al massimo <paramref name="parallelism"/> prove insieme
        ''' (SemaphoreSlim). Gli errori delle singole prove si ignorano; l'annullamento attende le prove già partite e poi si propaga.
        ''' </summary>
        Private Shared Async Function RunThrottledAsync(Of T)(items As IEnumerable(Of T), parallelism As Integer, body As Func(Of T, CancellationToken, Task), ct As CancellationToken) As Task
            Dim gate As New SemaphoreSlim(parallelism, parallelism)
            Dim running As New List(Of Task)()
            Dim failure As Exception = Nothing
            Try
                For Each item In items
                    Await gate.WaitAsync(ct).ConfigureAwait(False)
                    running.Add(RunOneAsync(item, body, gate, ct))
                Next
            Catch ex As Exception
                failure = ex ' di solito l'annullamento: si aspettano comunque le prove già partite
            End Try
            Try
                Await Task.WhenAll(running).ConfigureAwait(False)
            Catch
                ' le singole prove non propagano errori: qui si attende soltanto
            End Try
            gate.Dispose()
            If failure IsNot Nothing Then Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
            ct.ThrowIfCancellationRequested()
        End Function

        Private Shared Async Function RunOneAsync(Of T)(item As T, body As Func(Of T, CancellationToken, Task), gate As SemaphoreSlim, ct As CancellationToken) As Task
            Try
                Await body(item, ct).ConfigureAwait(False)
            Catch
                ' una macchina che non risponde o risponde male non deve mai far fallire la scansione
            Finally
                gate.Release()
            End Try
        End Function

        ''' <summary>Avanzamento di una fase, con i resoconti diradati (al massimo uno ogni 60 ms, più le scoperte e gli host aggiornati).</summary>
        Private NotInheritable Class PhaseReporter
            Private Const IntervalMs As Long = 60
            Private ReadOnly _progress As IProgress(Of ScanPhaseProgress)
            Private ReadOnly _phase As String
            Private ReadOnly _total As Integer
            Private _done As Integer
            Private _lastReport As Long

            Public Sub New(progress As IProgress(Of ScanPhaseProgress), phase As String, total As Integer)
                _progress = progress
                _phase = phase
                _total = total
            End Sub

            Public Sub Start(detail As String)
                _progress?.Report(New ScanPhaseProgress(_phase, 0, _total, detail))
            End Sub

            ''' <summary>Una prova finita; con un dettaglio (una scoperta) o un host il resoconto parte sempre.</summary>
            Public Sub [Step](Optional detail As String = Nothing, Optional host As DiscoveredHost = Nothing)
                Dim done = Interlocked.Increment(_done)
                If _progress Is Nothing Then Return
                Dim now = Environment.TickCount64
                Dim last = Interlocked.Read(_lastReport)
                Dim due = detail IsNot Nothing OrElse host IsNot Nothing OrElse done >= _total OrElse now - last >= IntervalMs
                If Not due Then Return
                Interlocked.Exchange(_lastReport, now)
                _progress.Report(New ScanPhaseProgress(_phase, Math.Min(done, _total), _total, detail, host))
            End Sub

            ''' <summary>Host arricchito a fine fase (porte, banner), senza avanzare il conteggio.</summary>
            Public Sub Update(host As DiscoveredHost)
                _progress?.Report(New ScanPhaseProgress(_phase, Math.Min(_done, _total), _total, Nothing, host))
            End Sub

            Public Sub Complete(detail As String)
                _progress?.Report(New ScanPhaseProgress(_phase, _total, _total, detail))
            End Sub
        End Class

#End Region

    End Class

End Namespace
