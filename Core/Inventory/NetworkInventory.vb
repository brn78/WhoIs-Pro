Option Strict On

Imports System.Text
Imports System.Text.Json
Imports Microsoft.Data.Sqlite
Imports WhoIsPro.Network.Scan
Imports WhoIsPro.Utils

Namespace Inventory

#Region "Record"

    ''' <summary>Livello di fiducia assegnato dall'utente a un dispositivo.</summary>
    Public Enum HostTrust
        Unknown = 0
        Trusted = 1
        Suspicious = 2
    End Enum

    ''' <summary>Dispositivo dell'archivio di rete.</summary>
    Public Class NetHostRecord
        Public Property Id As Long
        ''' <summary>La rete (sede) a cui appartiene: "gw:" più il MAC del router, oppure "net:" più la sottorete.</summary>
        Public Property NetworkKey As String
        Public Property HostKey As String
        Public Property Mac As String
        Public Property Ip As String
        Public Property HostName As String
        Public Property NetBios As String
        Public Property Workgroup As String
        Public Property Vendor As String
        Public Property DeviceType As String
        Public Property Category As String
        Public Property OsGuess As String
        Public Property IsGateway As Boolean
        Public Property IsLocal As Boolean
        Public Property FirstSeen As DateTime
        Public Property LastSeen As DateTime
        Public Property SeenCount As Integer
        ''' <summary>Scansioni consecutive senza risposta: «assente» scatta alla seconda (dispositivi Wi-Fi in risparmio energetico).</summary>
        Public Property MissCount As Integer
        Public Property LastScanId As Long?
        Public Property Ports As IReadOnlyList(Of OpenPort) = Array.Empty(Of OpenPort)()
        Public Property LatencyMs As Double?
        Public Property [Alias] As String
        Public Property Trust As HostTrust
        Public Property Notes As String
        ''' <summary>Icona scelta dall'utente (nome Material Design Icons) e il suo colore (#rrggbb); vuoti = quella del tipo.</summary>
        Public Property Icon As String
        Public Property IconColor As String
        ''' <summary>Vero se visto nell'ultima scansione registrata della sua rete.</summary>
        Public Property IsOnline As Boolean

        ''' <summary>Nome da mostrare: alias, poi NetBIOS, poi DNS, poi IP.</summary>
        Public ReadOnly Property DisplayName As String
            Get
                If Not String.IsNullOrWhiteSpace([Alias]) Then Return [Alias]
                If Not String.IsNullOrWhiteSpace(NetBios) Then Return NetBios
                If Not String.IsNullOrWhiteSpace(HostName) Then Return HostName
                Return Ip
            End Get
        End Property

        ''' <summary>Per avvisi e riepiloghi: «nas-ufficio (192.168.1.9)», oppure «192.168.1.9 · Synology» quando un nome non c'è.</summary>
        Public ReadOnly Property Label As String
            Get
                If Not String.Equals(DisplayName, Ip, StringComparison.Ordinal) Then Return $"{DisplayName} ({Ip})"
                Return If(String.IsNullOrEmpty(Vendor), Ip, $"{Ip} · {Vendor}")
            End Get
        End Property

        ''' <summary>Porte sensibili esposte (Telnet, FTP, SMB, RDP, VNC, WinRM…): da verificare.</summary>
        Public ReadOnly Property SensitivePorts As IReadOnlyList(Of String)
            Get
                Dim found As New List(Of String)()
                For Each p In Ports
                    Dim why As String = Nothing
                    If PortDictionary.SensitivePorts.TryGetValue(p.Port, why) Then found.Add($"{p.Port} · {why}")
                Next
                Return found
            End Get
        End Property

        Public ReadOnly Property PortsText As String
            Get
                Return If(Ports.Count = 0, "nessuna porta aperta", String.Join(", ", Ports.Select(Function(p) p.ToString())))
            End Get
        End Property
    End Class

    ''' <summary>Una rete (sede) dell'archivio, con i suoi numeri.</summary>
    Public Class NetNetworkRecord
        Public Property Key As String
        ''' <summary>«192.168.1.0/24 · Keenetic»: dalla scansione.</summary>
        Public Property Label As String
        Public Property Cidr As String
        Public Property GatewayIp As String
        Public Property GatewayMac As String
        ''' <summary>Nome dato dall'utente («Cliente Rossi», «Casa»).</summary>
        Public Property [Alias] As String
        Public Property FirstSeen As DateTime
        Public Property LastSeen As DateTime
        Public Property HostsCount As Integer
        Public Property OnlineCount As Integer
        Public Property ScansCount As Integer
        Public Property LastScanAt As DateTime?
        Public Property Unseen As Integer

        ''' <summary>Vero se riconosciuta dal router; falso se solo dalla sottorete (vecchio registro, bersaglio instradato).</summary>
        Public ReadOnly Property HasGateway As Boolean
            Get
                Return If(Key, "").StartsWith("gw:", StringComparison.Ordinal)
            End Get
        End Property

        Public ReadOnly Property DisplayName As String
            Get
                Return If(String.IsNullOrWhiteSpace([Alias]), Label, [Alias])
            End Get
        End Property
    End Class

    Public Class NetScanRecord
        Public Property Id As Long
        Public Property NetworkKey As String
        Public Property StartedAt As DateTime
        Public Property DurationMs As Long
        Public Property Target As String
        Public Property HostsCount As Integer
        Public Property NewCount As Integer
        Public Property GoneCount As Integer
        Public Property ChangedCount As Integer
        Public Property Summary As String
    End Class

    Public Class NetEventRecord
        Public Property Id As Long
        Public Property NetworkKey As String
        Public Property ScanId As Long?
        Public Property HostId As Long?
        Public Property At As DateTime
        ''' <summary>nuovo, ricomparso, cambiato, assente.</summary>
        Public Property Kind As String
        Public Property Title As String
        Public Property Details As String
        Public Property Acknowledged As Boolean
    End Class

    ''' <summary>Un dispositivo del vecchio registro (dispositivi.json), da importare nell'archivio.</summary>
    Public Class ImportedDevice
        Public Property Mac As String
        Public Property Ip As String
        Public Property Hostname As String
        Public Property Vendor As String
        Public Property DeviceType As String
        Public Property FirstSeen As DateTime = DateTime.Now
        Public Property LastSeen As DateTime = DateTime.Now
        Public Property TimesSeen As Integer
        Public Property Name As String
        Public Property Notes As String
        Public Property Icon As String
        Public Property IconColor As String
    End Class

    Public Class HostChange
        Public Sub New(host As NetHostRecord, change As String)
            Me.Host = host
            Me.Change = change
        End Sub
        Public ReadOnly Property Host As NetHostRecord
        Public ReadOnly Property Change As String
    End Class

    ''' <summary>Differenze tra l'ultima scansione di una rete e la precedente della stessa rete.</summary>
    Public Class ScanDiff
        Public Property ScanId As Long
        Public Property NetworkKey As String
        Public Property NetworkLabel As String
        ''' <summary>Prima scansione registrata per questa rete: tutto è nuovo, niente avvisi.</summary>
        Public Property IsFirst As Boolean
        Public Property NewHosts As New List(Of NetHostRecord)()
        Public Property ReturnedHosts As New List(Of NetHostRecord)()
        Public Property GoneHosts As New List(Of NetHostRecord)()
        Public Property ChangedHosts As New List(Of HostChange)()

        Public ReadOnly Property HasChanges As Boolean
            Get
                Return NewHosts.Count + ReturnedHosts.Count + GoneHosts.Count + ChangedHosts.Count > 0
            End Get
        End Property

        Public Function Summarize() As String
            Dim parts As New List(Of String)()
            If NewHosts.Count > 0 Then parts.Add($"{NewHosts.Count} nuov{If(NewHosts.Count = 1, "o", "i")}: {String.Join(", ", NewHosts.Take(6).Select(Function(h) h.Label))}")
            If ReturnedHosts.Count > 0 Then parts.Add($"{ReturnedHosts.Count} ricompars{If(ReturnedHosts.Count = 1, "o", "i")}: {String.Join(", ", ReturnedHosts.Take(6).Select(Function(h) h.DisplayName))}")
            If GoneHosts.Count > 0 Then parts.Add($"{GoneHosts.Count} assent{If(GoneHosts.Count = 1, "e", "i")}: {String.Join(", ", GoneHosts.Take(6).Select(Function(h) h.DisplayName))}")
            If ChangedHosts.Count > 0 Then parts.Add($"{ChangedHosts.Count} cambiat{If(ChangedHosts.Count = 1, "o", "i")}: {String.Join("; ", ChangedHosts.Take(6).Select(Function(c) $"{c.Host.DisplayName} {c.Change}"))}")
            Return If(parts.Count = 0, "nessun cambiamento rispetto alla scansione precedente", String.Join(". ", parts) & ".")
        End Function
    End Class

#End Region

    ''' <summary>
    ''' Archivio della rete nel tempo, una rete (sede) per volta: la rete è il router (chiave "gw:" più il suo MAC) o, in
    ''' mancanza, la sottorete ("net:…"). Ogni scansione aggiorna i dispositivi conosciuti della sua rete (chiave: MAC, o
    ''' "ip:" più l'indirizzo se il MAC non è noto), registra la scansione e produce eventi sui cambiamenti: nuovi, ricomparsi,
    ''' assenti, cambio di IP, nome o porte. I dispositivi di un'altra sede non c'entrano: non diventano mai «assenti».
    ''' L'utente può dare un nome alla rete e, a ogni dispositivo, alias, livello di fiducia, note e icona.
    ''' </summary>
    Public NotInheritable Class NetworkInventory

#Region "Campi"

        Private Const HostColumns As String = "id, host_key, mac, ip, hostname, netbios, workgroup, vendor, device_type, category, os_guess, is_gateway, is_local, first_seen, last_seen, seen_count, miss_count, last_scan_id, ports_json, latency_ms, alias, trust, notes, icon, icon_color, network_key"
        ''' <summary>Scansioni consecutive senza risposta prima di dichiarare un dispositivo assente.</summary>
        Public Const MissesBeforeGone As Integer = 2
        Private ReadOnly _store As InventoryStore
        Private ReadOnly _lock As New Object()

#End Region

#Region "Costruzione"

        Friend Sub New(store As InventoryStore)
            _store = store
        End Sub

#End Region

#Region "Reti"

        ''' <summary>La rete esiste in archivio (creata se manca), con etichetta e gateway aggiornati; il nome dato dall'utente non si tocca.</summary>
        Public Sub EnsureNetwork(network As NetworkIdentity, Optional seenAt As DateTime? = Nothing)
            SyncLock _lock
                EnsureNetworkCore(network, If(seenAt, DateTime.Now))
            End SyncLock
        End Sub

        Private Sub EnsureNetworkCore(network As NetworkIdentity, seenAt As DateTime)
            If network Is Nothing OrElse String.IsNullOrEmpty(network.Key) Then Return
            _store.Execute("INSERT INTO net_networks(key, label, cidr, gateway_ip, gateway_mac, first_seen, last_seen) VALUES ($k, $l, $c, $gi, $gm, $t, $t)
                            ON CONFLICT(key) DO UPDATE SET label = excluded.label, cidr = COALESCE(excluded.cidr, cidr), gateway_ip = COALESCE(excluded.gateway_ip, gateway_ip),
                                gateway_mac = COALESCE(excluded.gateway_mac, gateway_mac), last_seen = MAX(last_seen, excluded.last_seen)",
                           ("$k", network.Key), ("$l", network.Label), ("$c", Blank(network.Cidr)), ("$gi", Blank(network.GatewayIp)), ("$gm", Blank(network.GatewayMac)),
                           ("$t", InventoryStore.ToIso(seenAt)))
        End Sub

        ''' <summary>Le reti conosciute con i loro numeri, dalla più recente.</summary>
        Public Function ListNetworks() As IReadOnlyList(Of NetNetworkRecord)
            SyncLock _lock
                Dim list As New List(Of NetNetworkRecord)()
                Using cmd = _store.Command("SELECT n.key, n.label, n.cidr, n.gateway_ip, n.gateway_mac, n.alias, n.first_seen, n.last_seen,
                        (SELECT COUNT(*) FROM net_hosts h WHERE h.network_key = n.key),
                        (SELECT COUNT(*) FROM net_hosts h WHERE h.network_key = n.key AND h.last_scan_id IS NOT NULL AND h.last_scan_id = (SELECT MAX(s.id) FROM net_scans s WHERE s.network_key = n.key)),
                        (SELECT COUNT(*) FROM net_scans s WHERE s.network_key = n.key),
                        (SELECT s.started_at FROM net_scans s WHERE s.network_key = n.key ORDER BY s.id DESC LIMIT 1),
                        (SELECT COUNT(*) FROM net_events e WHERE e.network_key = n.key AND e.acknowledged = 0)
                    FROM net_networks n ORDER BY n.last_seen DESC")
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            list.Add(New NetNetworkRecord With {
                                .Key = r.GetString(0), .Label = r.GetString(1), .Cidr = Text(r, 2), .GatewayIp = Text(r, 3), .GatewayMac = Text(r, 4), .[Alias] = Text(r, 5),
                                .FirstSeen = InventoryStore.Parse(r.GetString(6)), .LastSeen = InventoryStore.Parse(r.GetString(7)),
                                .HostsCount = r.GetInt32(8), .OnlineCount = r.GetInt32(9), .ScansCount = r.GetInt32(10),
                                .LastScanAt = If(r.IsDBNull(11), CType(Nothing, DateTime?), InventoryStore.Parse(r.GetString(11))), .Unseen = r.GetInt32(12)
                            })
                        End While
                    End Using
                End Using
                Return list
            End SyncLock
        End Function

        Public Function GetNetwork(key As String) As NetNetworkRecord
            If String.IsNullOrEmpty(key) Then Return Nothing
            Return ListNetworks().FirstOrDefault(Function(n) n.Key = key)
        End Function

        ''' <summary>Nome dato dall'utente alla rete («Cliente Rossi»); vuoto = torna l'etichetta della scansione.</summary>
        Public Sub SetNetworkAlias(key As String, [alias] As String)
            _store.Execute("UPDATE net_networks SET alias = $a WHERE key = $k", ("$a", Blank([alias])), ("$k", key))
        End Sub

        ''' <summary>«Dimentica la rete»: i suoi dispositivi, la loro storia e le sue scansioni spariscono.</summary>
        Public Sub DeleteNetwork(key As String)
            SyncLock _lock
                Using tx = _store.BeginTransaction()
                    _store.Execute("DELETE FROM net_events WHERE network_key = $k OR host_id IN (SELECT id FROM net_hosts WHERE network_key = $k)", ("$k", key))
                    _store.Execute("DELETE FROM net_hosts WHERE network_key = $k", ("$k", key))
                    _store.Execute("DELETE FROM net_scans WHERE network_key = $k", ("$k", key))
                    _store.Execute("DELETE FROM net_networks WHERE key = $k", ("$k", key))
                    tx.Commit()
                End Using
            End SyncLock
        End Sub

        ''' <summary>Rete di un rapporto senza identità (prove, rapporti costruiti a mano): la /24 del bersaglio o del primo host.</summary>
        Private Shared Function PseudoNetworkFor(report As ScanReport) As NetworkIdentity
            If report.Network IsNot Nothing Then Return report.Network
            Dim target = If(report.Target, "").Trim()
            Dim seed = If(target.Contains("/"c), target.Split("/"c)(0), If(report.Hosts.FirstOrDefault()?.Ip, target))
            Return NetworkIdentity.ForIp(seed)
        End Function

#End Region

#Region "Registrazione di una scansione"

        ''' <summary>
        ''' Aggiorna l'archivio della rete con una scansione e restituisce le differenze rispetto alla scansione precedente
        ''' della stessa rete, in una sola transazione. <paramref name="network"/> prevale sulla rete del rapporto.
        ''' </summary>
        Public Function RecordScan(report As ScanReport, Optional network As NetworkIdentity = Nothing) As ScanDiff
            If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
            Dim net = If(network, PseudoNetworkFor(report))
            SyncLock _lock
                Using tx = _store.BeginTransaction()
                    EnsureNetworkCore(net, report.StartedAt)
                    Dim previousScanId = LastScanId(net.Key)
                    Dim knownBefore = LoadIds("SELECT id FROM net_hosts WHERE network_key = $n", ("$n", net.Key))
                    _store.Execute("INSERT INTO net_scans(started_at, duration_ms, target, hosts_count, new_count, gone_count, changed_count, summary, network_key) VALUES ($t, $d, $g, $h, 0, 0, 0, '', $n)",
                                   ("$t", InventoryStore.ToIso(report.StartedAt)), ("$d", CLng(report.Duration.TotalMilliseconds)), ("$g", report.Target), ("$h", report.Hosts.Count), ("$n", net.Key))
                    Dim scanId = _store.LastInsertId()
                    Dim diff As New ScanDiff With {.ScanId = scanId, .IsFirst = Not previousScanId.HasValue, .NetworkKey = net.Key, .NetworkLabel = net.Label}
                    Dim presentIds As New HashSet(Of Long)()
                    Dim keysThisScan As New HashSet(Of String)(StringComparer.Ordinal)
                    Dim nowIso = InventoryStore.ToIso(report.StartedAt)
                    ' porte provate in questa scansione (Nothing = non si sa: si confronta tutto); quelle non provate restano com'erano
                    Dim probed = If(report.ProbedPorts Is Nothing, Nothing, report.ProbedPorts.ToHashSet())

                    For Each h In report.Hosts
                        ' lo stesso MAC su due indirizzi nella stessa scansione (router con più IP, alias): il secondo si tiene per IP
                        Dim key = KeyFor(h.Mac, h.Ip)
                        Dim byMac = Not key.StartsWith("ip:", StringComparison.Ordinal)
                        If Not keysThisScan.Add(key) Then
                            key = "ip:" & h.Ip
                            byMac = False
                            keysThisScan.Add(key)
                        End If
                        Dim existing = Locate(net, key, h.Ip, byMac)
                        If existing IsNot Nothing AndAlso presentIds.Contains(existing.Id) Then existing = Nothing
                        Dim portsJson = JsonSerializer.Serialize(h.Ports)
                        Dim vendor = If(h.Vendor = MacVendorLookup.PrivateAddressLabel, Nothing, h.Vendor)
                        ' visto dalla pagina (Observe) ma mai in una scansione registrata: per l'archivio è nuovo adesso
                        Dim neverRecorded = existing IsNot Nothing AndAlso existing.SeenCount = 0

                        If existing Is Nothing Then
                            _store.Execute("INSERT INTO net_hosts(network_key, host_key, mac, ip, hostname, netbios, workgroup, vendor, device_type, category, os_guess, is_gateway, is_local,
                                                                  first_seen, last_seen, seen_count, miss_count, last_scan_id, ports_json, latency_ms, trust)
                                            VALUES ($n, $k, $m, $ip, $hn, $nb, $wg, $v, $dt, $cat, $os, $gw, $loc, $t, $t, 1, 0, $s, $pj, $lat, 0)",
                                           ("$n", net.Key), ("$k", key), ("$m", MacVendorLookup.Normalize(h.Mac)), ("$ip", h.Ip), ("$hn", h.HostName), ("$nb", h.NetBiosName), ("$wg", h.NetBiosWorkgroup),
                                           ("$v", vendor), ("$dt", h.DeviceType), ("$cat", h.Category), ("$os", h.OsGuess), ("$gw", If(h.IsGateway, 1, 0)), ("$loc", If(h.IsLocalMachine, 1, 0)),
                                           ("$t", nowIso), ("$s", scanId), ("$pj", portsJson), ("$lat", h.LatencyMs))
                            Dim id = _store.LastInsertId()
                            presentIds.Add(id)
                            Dim record = [Get](id)
                            diff.NewHosts.Add(record)
                            AddEvent(net.Key, scanId, id, report.StartedAt, "nuovo", $"Nuovo dispositivo: {record.DisplayName}", NewDeviceDetails(h, vendor))
                            Continue For
                        End If

                        presentIds.Add(existing.Id)
                        Dim changes As New List(Of String)()
                        If Not String.Equals(existing.Ip, h.Ip, StringComparison.Ordinal) Then changes.Add($"IP {existing.Ip} → {h.Ip}")
                        Dim newName = If(h.NetBiosName, h.HostName)
                        Dim oldName = If(existing.NetBios, existing.HostName)
                        If newName IsNot Nothing AndAlso oldName IsNot Nothing AndAlso Not String.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase) Then changes.Add($"nome {oldName} → {newName}")
                        Dim oldPorts = existing.Ports.Select(Function(p) p.Port).ToHashSet()
                        Dim effective = h.Ports.ToList()
                        If probed IsNot Nothing Then effective.AddRange(existing.Ports.Where(Function(p) Not probed.Contains(p.Port) AndAlso Not HasPort(effective, p.Port)))
                        portsJson = JsonSerializer.Serialize(effective)
                        Dim newPorts = effective.Select(Function(p) p.Port).ToHashSet()
                        If effective.Count + oldPorts.Count > 0 AndAlso Not oldPorts.SetEquals(newPorts) Then
                            Dim added = newPorts.Except(oldPorts).OrderBy(Function(p) p).ToList()
                            Dim removed = oldPorts.Except(newPorts).OrderBy(Function(p) p).ToList()
                            If added.Count > 0 Then changes.Add("porte aperte " & String.Join(",", added))
                            If removed.Count > 0 Then changes.Add("porte chiuse " & String.Join(",", removed))
                        End If
                        Dim wasAbsent = existing.MissCount >= MissesBeforeGone

                        _store.Execute("UPDATE net_hosts SET network_key = $n, host_key = $k, mac = COALESCE($m, mac), ip = $ip, hostname = COALESCE($hn, hostname), netbios = COALESCE($nb, netbios),
                                            workgroup = COALESCE($wg, workgroup), vendor = COALESCE($v, vendor), device_type = $dt, category = $cat, os_guess = COALESCE($os, os_guess),
                                            is_gateway = $gw, is_local = $loc, last_seen = $t, seen_count = seen_count + 1, miss_count = 0, last_scan_id = $s, ports_json = $pj, latency_ms = $lat
                                        WHERE id = $id",
                                       ("$n", net.Key), ("$k", key), ("$m", MacVendorLookup.Normalize(h.Mac)), ("$ip", h.Ip), ("$hn", h.HostName), ("$nb", h.NetBiosName), ("$wg", h.NetBiosWorkgroup), ("$v", vendor),
                                       ("$dt", h.DeviceType), ("$cat", h.Category), ("$os", h.OsGuess), ("$gw", If(h.IsGateway, 1, 0)), ("$loc", If(h.IsLocalMachine, 1, 0)),
                                       ("$t", nowIso), ("$s", scanId), ("$pj", portsJson), ("$lat", h.LatencyMs), ("$id", existing.Id))

                        Dim updated = [Get](existing.Id)
                        If neverRecorded Then
                            diff.NewHosts.Add(updated)
                            AddEvent(net.Key, scanId, updated.Id, report.StartedAt, "nuovo", $"Nuovo dispositivo: {updated.DisplayName}", NewDeviceDetails(h, vendor))
                            Continue For
                        End If
                        If wasAbsent Then
                            diff.ReturnedHosts.Add(updated)
                            AddEvent(net.Key, scanId, updated.Id, report.StartedAt, "ricomparso", $"Ricomparso: {updated.DisplayName}", $"{h.Ip} · assente nelle scansioni precedenti")
                        End If
                        If changes.Count > 0 Then
                            Dim text = String.Join(", ", changes)
                            diff.ChangedHosts.Add(New HostChange(updated, text))
                            AddEvent(net.Key, scanId, updated.Id, report.StartedAt, "cambiato", $"Cambiato: {updated.DisplayName}", text)
                        End If
                    Next

                    ' chi manca, in questa rete: un conteggio in più; «assente» alla seconda scansione consecutiva senza risposta (mai per questa macchina)
                    For Each id In knownBefore.Where(Function(x) Not presentIds.Contains(x))
                        Dim host = [Get](id)
                        If host Is Nothing OrElse host.IsLocal OrElse host.NetworkKey <> net.Key Then Continue For
                        Dim misses = host.MissCount + 1
                        _store.Execute("UPDATE net_hosts SET miss_count = $n WHERE id = $id", ("$n", misses), ("$id", id))
                        If misses = MissesBeforeGone Then
                            diff.GoneHosts.Add(host)
                            AddEvent(net.Key, scanId, id, report.StartedAt, "assente", $"Assente: {host.DisplayName}", $"{host.Ip} · non risponde da {misses} scansioni")
                        End If
                    Next

                    _store.Execute("UPDATE net_scans SET new_count = $n, gone_count = $g, changed_count = $c, summary = $s WHERE id = $id",
                                   ("$n", diff.NewHosts.Count), ("$g", diff.GoneHosts.Count), ("$c", diff.ChangedHosts.Count + diff.ReturnedHosts.Count), ("$s", diff.Summarize()), ("$id", scanId))
                    tx.Commit()
                    Return diff
                End Using
            End SyncLock
        End Function

        Private Shared Function NewDeviceDetails(h As DiscoveredHost, vendor As String) As String
            Return $"{h.Ip}{If(h.Mac Is Nothing, "", " · " & h.Mac)}{If(vendor Is Nothing, "", " · " & vendor)} · {h.DeviceType}" &
                   If(h.Ports.Count > 0, " · porte " & String.Join(",", h.Ports.Select(Function(p) p.Port)), "")
        End Function

        Private Sub AddEvent(networkKey As String, scanId As Long, hostId As Long, at As DateTime, kind As String, title As String, details As String)
            _store.Execute("INSERT INTO net_events(network_key, scan_id, host_id, at, kind, title, details) VALUES ($n, $s, $h, $t, $k, $ti, $d)",
                           ("$n", networkKey), ("$s", scanId), ("$h", hostId), ("$t", InventoryStore.ToIso(at)), ("$k", kind), ("$ti", title), ("$d", details))
        End Sub

        Private Shared Function HasPort(ports As List(Of OpenPort), number As Integer) As Boolean
            Return ports.Any(Function(p) p.Port = number)
        End Function

        ''' <summary>
        ''' Il dispositivo nella rete: per chiave, poi per «ip:…» se il MAC è arrivato solo adesso, infine adottato da una rete
        ''' riconosciuta solo dalla sottorete (vecchio registro, scansione senza il MAC del router). Una riga «ip:…» mai
        ''' confermata per lo stesso indirizzo (fase ping, senza MAC) si toglie: era lo stesso dispositivo.
        ''' </summary>
        Private Function Locate(net As NetworkIdentity, key As String, ip As String, byMac As Boolean) As NetHostRecord
            Dim existing = FindByKey(net.Key, key)
            If existing Is Nothing AndAlso byMac Then existing = FindByKey(net.Key, "ip:" & ip)
            If existing Is Nothing Then existing = Adopt(net, key, ip, byMac)
            If existing IsNot Nothing AndAlso byMac Then
                _store.Execute("DELETE FROM net_hosts WHERE network_key = $n AND host_key = $k AND seen_count = 0 AND id <> $id", ("$n", net.Key), ("$k", "ip:" & ip), ("$id", existing.Id))
            End If
            Return existing
        End Function

        ''' <summary>
        ''' Dispositivo registrato per la stessa sottorete ma senza router riconosciuto («net:…»: vecchio registro, versioni
        ''' precedenti, scansione con il router muto) che adesso compare nella rete con il router riconosciuto: passa a quella
        ''' rete con la sua storia, nome e note, invece di diventare un doppione. Un'altra sottorete è un'altra sede: non si tocca.
        ''' </summary>
        Private Function Adopt(net As NetworkIdentity, key As String, ip As String, byMac As Boolean) As NetHostRecord
            If Not net.HasGateway Then Return Nothing
            For Each source In {"net:" & net.Cidr, NetworkIdentity.ForIp(ip).Key}.Distinct()
                Dim candidate = FindByKey(source, key)
                If candidate Is Nothing AndAlso byMac Then candidate = FindByKey(source, "ip:" & ip)
                If candidate Is Nothing Then Continue For
                _store.Execute("UPDATE net_hosts SET network_key = $n WHERE id = $id", ("$n", net.Key), ("$id", candidate.Id))
                _store.Execute("UPDATE net_events SET network_key = $n WHERE host_id = $id", ("$n", net.Key), ("$id", candidate.Id))
                Return [Get](candidate.Id)
            Next
            Return Nothing
        End Function

#End Region

#Region "Osservazione di un singolo host"

        ''' <summary>
        ''' Un host visto adesso fuori da una scansione completa (pagina Rete, riscansione, sessione riaperta): entra
        ''' nell'archivio della sua rete se manca, con seen_count 0 finché una scansione registrata non lo conferma («nuovo»
        ''' allora), e si aggiorna se c'è già, senza toccare i conteggi né creare eventi. Senza rete indicata vale la sottorete
        ''' /24 dell'indirizzo. Restituisce il record e se è stato creato adesso.
        ''' </summary>
        Public Function Observe(h As DiscoveredHost, seenAt As DateTime, Optional network As NetworkIdentity = Nothing) As (Record As NetHostRecord, IsNew As Boolean)
            If h Is Nothing Then Throw New ArgumentNullException(NameOf(h))
            Dim net = If(network, NetworkIdentity.ForIp(h.Ip))
            SyncLock _lock
                EnsureNetworkCore(net, seenAt)
                Dim key = KeyFor(h.Mac, h.Ip)
                Dim byMac = Not key.StartsWith("ip:", StringComparison.Ordinal)
                Dim existing = Locate(net, key, h.Ip, byMac)
                Dim vendor = If(h.Vendor = MacVendorLookup.PrivateAddressLabel, Nothing, h.Vendor)
                ' le porte si sommano a quelle note: solo una scansione registrata (RecordScan) decide quali si sono chiuse
                Dim ports = h.Ports.ToList()
                If existing IsNot Nothing Then ports.AddRange(existing.Ports.Where(Function(p) Not HasPort(ports, p.Port)))
                Dim portsJson = If(ports.Count > 0, JsonSerializer.Serialize(ports), Nothing)
                Dim nowIso = InventoryStore.ToIso(seenAt)
                If existing Is Nothing Then
                    _store.Execute("INSERT INTO net_hosts(network_key, host_key, mac, ip, hostname, netbios, workgroup, vendor, device_type, category, os_guess, is_gateway, is_local,
                                                          first_seen, last_seen, seen_count, miss_count, last_scan_id, ports_json, latency_ms, trust)
                                    VALUES ($n, $k, $m, $ip, $hn, $nb, $wg, $v, $dt, $cat, $os, $gw, $loc, $t, $t, 0, 0, NULL, $pj, $lat, 0)",
                                   ("$n", net.Key), ("$k", key), ("$m", MacVendorLookup.Normalize(h.Mac)), ("$ip", h.Ip), ("$hn", h.HostName), ("$nb", h.NetBiosName), ("$wg", h.NetBiosWorkgroup),
                                   ("$v", vendor), ("$dt", h.DeviceType), ("$cat", h.Category), ("$os", h.OsGuess), ("$gw", If(h.IsGateway, 1, 0)), ("$loc", If(h.IsLocalMachine, 1, 0)),
                                   ("$t", nowIso), ("$pj", portsJson), ("$lat", h.LatencyMs))
                    Return ([Get](_store.LastInsertId()), True)
                End If
                _store.Execute("UPDATE net_hosts SET network_key = $n, host_key = $k, mac = COALESCE($m, mac), ip = $ip, hostname = COALESCE($hn, hostname), netbios = COALESCE($nb, netbios),
                                    workgroup = COALESCE($wg, workgroup), vendor = COALESCE($v, vendor), device_type = $dt, category = $cat, os_guess = COALESCE($os, os_guess),
                                    is_gateway = $gw, is_local = $loc, last_seen = $t, ports_json = COALESCE($pj, ports_json), latency_ms = COALESCE($lat, latency_ms)
                                WHERE id = $id",
                               ("$n", net.Key), ("$k", key), ("$m", MacVendorLookup.Normalize(h.Mac)), ("$ip", h.Ip), ("$hn", h.HostName), ("$nb", h.NetBiosName), ("$wg", h.NetBiosWorkgroup), ("$v", vendor),
                               ("$dt", h.DeviceType), ("$cat", h.Category), ("$os", h.OsGuess), ("$gw", If(h.IsGateway, 1, 0)), ("$loc", If(h.IsLocalMachine, 1, 0)),
                               ("$t", nowIso), ("$pj", portsJson), ("$lat", h.LatencyMs), ("$id", existing.Id))
                Return ([Get](existing.Id), False)
            End SyncLock
        End Function

        ''' <summary>Dispositivo per rete e chiave («aa:bb:cc:dd:ee:ff» oppure «ip:…»), Nothing se non c'è.</summary>
        Public Function GetByKey(networkKey As String, key As String) As NetHostRecord
            Return FindByKey(networkKey, key)
        End Function

        ''' <summary>Dispositivo per chiave in qualunque rete, il visto più di recente; Nothing se non c'è.</summary>
        Public Function FindAnywhere(key As String) As NetHostRecord
            Using cmd = _store.Command($"SELECT {HostColumns} FROM net_hosts WHERE host_key = $k ORDER BY last_seen DESC LIMIT 1", ("$k", key))
                Using r = cmd.ExecuteReader()
                    Return If(r.Read(), Read(r, LastScanIds()), Nothing)
                End Using
            End Using
        End Function

        ''' <summary>Numero di dispositivi in archivio, in tutte le reti.</summary>
        Public ReadOnly Property Count As Integer
            Get
                Return Convert.ToInt32(If(_store.Scalar("SELECT COUNT(*) FROM net_hosts"), 0))
            End Get
        End Property

        ''' <summary>Numero di dispositivi di una rete.</summary>
        Public Function CountIn(networkKey As String) As Integer
            Return Convert.ToInt32(If(_store.Scalar("SELECT COUNT(*) FROM net_hosts WHERE network_key = $n", ("$n", networkKey)), 0))
        End Function

        ''' <summary>Svuota tutto: reti, dispositivi, storia e scansioni. Alla prossima scansione tutti tornano «nuovi» senza avvisi.</summary>
        Public Sub ClearAll()
            SyncLock _lock
                _store.Execute("DELETE FROM net_events; DELETE FROM net_hosts; DELETE FROM net_scans; DELETE FROM net_networks;")
            End SyncLock
        End Sub

        ''' <summary>
        ''' Importa i dispositivi del vecchio registro (dispositivi.json delle versioni 2.4–2.5): nome, note, icona, prima e
        ''' ultima rilevazione. Ognuno va nella rete della sua sottorete /24; appena ricompare in una rete con il router
        ''' riconosciuto passa a quella. Quelli già in archivio non si toccano. Restituisce quanti ne sono entrati.
        ''' </summary>
        Public Function ImportKnownDevices(items As IEnumerable(Of ImportedDevice)) As Integer
            If items Is Nothing Then Return 0
            Dim imported = 0
            SyncLock _lock
                Using tx = _store.BeginTransaction()
                    For Each d In items
                        Dim key = If(MacVendorLookup.Normalize(d.Mac), If(String.IsNullOrEmpty(d.Ip), Nothing, "ip:" & d.Ip))
                        If key Is Nothing Then Continue For
                        Dim net = NetworkIdentity.ForIp(d.Ip)
                        If FindByKey(net.Key, key) IsNot Nothing Then Continue For
                        EnsureNetworkCore(net, d.LastSeen)
                        _store.Execute("INSERT INTO net_hosts(network_key, host_key, mac, ip, hostname, vendor, device_type, category, first_seen, last_seen, seen_count, miss_count, trust, alias, notes, icon, icon_color)
                                        VALUES ($n, $k, $m, $ip, $hn, $v, $dt, $cat, $f, $l, $c, 0, 0, $a, $no, $ic, $col)",
                                       ("$n", net.Key), ("$k", key), ("$m", MacVendorLookup.Normalize(d.Mac)), ("$ip", If(d.Ip, "")), ("$hn", Blank(d.Hostname)), ("$v", Blank(d.Vendor)),
                                       ("$dt", If(Blank(d.DeviceType), "Dispositivo")), ("$cat", DeviceClassifier.CategoryOther),
                                       ("$f", InventoryStore.ToIso(d.FirstSeen)), ("$l", InventoryStore.ToIso(d.LastSeen)), ("$c", Math.Max(0, d.TimesSeen)),
                                       ("$a", Blank(d.Name)), ("$no", Blank(d.Notes)), ("$ic", Blank(d.Icon)), ("$col", Blank(d.IconColor)))
                        imported += 1
                    Next
                    tx.Commit()
                End Using
            End SyncLock
            Return imported
        End Function

        Private Shared Function Blank(value As String) As String
            Return If(String.IsNullOrWhiteSpace(value), Nothing, value.Trim())
        End Function

#End Region

#Region "Dispositivi"

        ''' <summary>
        ''' Dispositivi conosciuti di una rete (Nothing = tutte), in ordine numerico di IP; filtro per parole su IP, MAC, nomi,
        ''' produttore, tipo, alias e note.
        ''' </summary>
        Public Function ListHosts(Optional query As String = Nothing, Optional onlyOnline As Boolean = False, Optional networkKey As String = Nothing) As IReadOnlyList(Of NetHostRecord)
            SyncLock _lock
                Dim lastScans = LastScanIds()
                Dim list As New List(Of NetHostRecord)()
                Dim sql = $"SELECT {HostColumns} FROM net_hosts" & If(networkKey Is Nothing, "", " WHERE network_key = $n")
                Using cmd = If(networkKey Is Nothing, _store.Command(sql), _store.Command(sql, ("$n", networkKey)))
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            Dim host = Read(r, lastScans)
                            If onlyOnline AndAlso Not host.IsOnline Then Continue While
                            If Not String.IsNullOrWhiteSpace(query) AndAlso Not MatchesQuery(host, query) Then Continue While
                            list.Add(host)
                        End While
                    End Using
                End Using
                Return list.OrderBy(Function(h) h.Ip, IpComparer.Instance).ToList()
            End SyncLock
        End Function

        Public Function [Get](id As Long) As NetHostRecord
            Using cmd = _store.Command($"SELECT {HostColumns} FROM net_hosts WHERE id = $id", ("$id", id))
                Using r = cmd.ExecuteReader()
                    Return If(r.Read(), Read(r, LastScanIds()), Nothing)
                End Using
            End Using
        End Function

        ''' <summary>Trova per IP, MAC, alias, nome DNS o NetBIOS, in tutte le reti.</summary>
        Public Function Find(text As String) As NetHostRecord
            text = If(text, "").Trim()
            Dim mac = MacVendorLookup.Normalize(text)
            Return ListHosts().FirstOrDefault(Function(h) h.Ip.Equals(text, StringComparison.OrdinalIgnoreCase) OrElse
                                                          (mac IsNot Nothing AndAlso h.Mac = mac) OrElse
                                                          String.Equals(h.[Alias], text, StringComparison.OrdinalIgnoreCase) OrElse
                                                          String.Equals(h.HostName, text, StringComparison.OrdinalIgnoreCase) OrElse
                                                          String.Equals(h.NetBios, text, StringComparison.OrdinalIgnoreCase))
        End Function

        Public Sub SetAlias(id As Long, [alias] As String)
            _store.Execute("UPDATE net_hosts SET alias = $a WHERE id = $id", ("$a", Blank([alias])), ("$id", id))
        End Sub

        Public Sub SetTrust(id As Long, trust As HostTrust)
            _store.Execute("UPDATE net_hosts SET trust = $t WHERE id = $id", ("$t", CInt(trust)), ("$id", id))
        End Sub

        Public Sub SetNotes(id As Long, notes As String)
            _store.Execute("UPDATE net_hosts SET notes = $n WHERE id = $id", ("$n", Blank(notes)), ("$id", id))
        End Sub

        ''' <summary>Icona scelta dall'utente e il suo colore; vuoti = torna quella del tipo.</summary>
        Public Sub SetIcon(id As Long, icon As String, color As String)
            Dim ic = Blank(icon)
            _store.Execute("UPDATE net_hosts SET icon = $i, icon_color = $c WHERE id = $id", ("$i", ic), ("$c", If(ic Is Nothing, Nothing, Blank(color))), ("$id", id))
        End Sub

        ''' <summary>«Dimentica»: il dispositivo e la sua storia spariscono; se ricompare torna «nuovo».</summary>
        Public Sub DeleteHost(id As Long)
            SyncLock _lock
                _store.Execute("DELETE FROM net_events WHERE host_id = $id", ("$id", id))
                _store.Execute("DELETE FROM net_hosts WHERE id = $id", ("$id", id))
            End SyncLock
        End Sub

#End Region

#Region "Scansioni ed eventi"

        Public Function ListScans(Optional limit As Integer = 50, Optional networkKey As String = Nothing) As IReadOnlyList(Of NetScanRecord)
            Dim list As New List(Of NetScanRecord)()
            Dim sql = "SELECT id, started_at, duration_ms, target, hosts_count, new_count, gone_count, changed_count, summary, network_key FROM net_scans" &
                      If(networkKey Is Nothing, "", " WHERE network_key = $n") & " ORDER BY id DESC LIMIT $lim"
            Using cmd = If(networkKey Is Nothing, _store.Command(sql, ("$lim", limit)), _store.Command(sql, ("$lim", limit), ("$n", networkKey)))
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        list.Add(New NetScanRecord With {
                            .Id = r.GetInt64(0), .StartedAt = InventoryStore.Parse(r.GetString(1)), .DurationMs = r.GetInt64(2), .Target = r.GetString(3),
                            .HostsCount = r.GetInt32(4), .NewCount = r.GetInt32(5), .GoneCount = r.GetInt32(6), .ChangedCount = r.GetInt32(7), .Summary = r.GetString(8),
                            .NetworkKey = Text(r, 9)
                        })
                    End While
                End Using
            End Using
            Return list
        End Function

        ''' <summary>L'ultima scansione di una rete (Nothing = di qualunque rete).</summary>
        Public Function LastScan(Optional networkKey As String = Nothing) As NetScanRecord
            Return ListScans(1, networkKey).FirstOrDefault()
        End Function

        Public Function ListEvents(Optional limit As Integer = 100, Optional onlyUnacknowledged As Boolean = False, Optional since As DateTime? = Nothing,
                                   Optional hostId As Long? = Nothing, Optional networkKey As String = Nothing) As IReadOnlyList(Of NetEventRecord)
            Dim list As New List(Of NetEventRecord)()
            Dim sql = "SELECT id, scan_id, host_id, at, kind, title, details, acknowledged, network_key FROM net_events WHERE 1 = 1" &
                      If(onlyUnacknowledged, " AND acknowledged = 0", "") &
                      If(since.HasValue, " AND at >= $since", "") &
                      If(hostId.HasValue, " AND host_id = $host", "") &
                      If(networkKey Is Nothing, "", " AND network_key = $net") &
                      " ORDER BY id DESC LIMIT $n"
            Dim parameters As New List(Of (Name As String, Value As Object)) From {("$n", CObj(limit))}
            If since.HasValue Then parameters.Add(("$since", InventoryStore.ToIso(since.Value)))
            If hostId.HasValue Then parameters.Add(("$host", hostId.Value))
            If networkKey IsNot Nothing Then parameters.Add(("$net", networkKey))
            Using cmd = _store.Command(sql, parameters.ToArray())
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        list.Add(New NetEventRecord With {
                            .Id = r.GetInt64(0), .ScanId = If(r.IsDBNull(1), CType(Nothing, Long?), r.GetInt64(1)), .HostId = If(r.IsDBNull(2), CType(Nothing, Long?), r.GetInt64(2)),
                            .At = InventoryStore.Parse(r.GetString(3)), .Kind = r.GetString(4), .Title = r.GetString(5), .Details = r.GetString(6), .Acknowledged = r.GetInt32(7) = 1,
                            .NetworkKey = Text(r, 8)
                        })
                    End While
                End Using
            End Using
            Return list
        End Function

        ''' <summary>Segna come visti i cambiamenti di una rete (Nothing = di tutte).</summary>
        Public Sub AcknowledgeAll(Optional networkKey As String = Nothing)
            If networkKey Is Nothing Then
                _store.Execute("UPDATE net_events SET acknowledged = 1 WHERE acknowledged = 0")
            Else
                _store.Execute("UPDATE net_events SET acknowledged = 1 WHERE acknowledged = 0 AND network_key = $n", ("$n", networkKey))
            End If
        End Sub

        Public Sub Acknowledge(eventId As Long)
            _store.Execute("UPDATE net_events SET acknowledged = 1 WHERE id = $id", ("$id", eventId))
        End Sub

        Public Function UnacknowledgedCount(Optional networkKey As String = Nothing) As Integer
            If networkKey Is Nothing Then Return Convert.ToInt32(If(_store.Scalar("SELECT COUNT(*) FROM net_events WHERE acknowledged = 0"), 0))
            Return Convert.ToInt32(If(_store.Scalar("SELECT COUNT(*) FROM net_events WHERE acknowledged = 0 AND network_key = $n", ("$n", networkKey)), 0))
        End Function

#End Region

#Region "Riepiloghi"

        ''' <summary>Una riga per la rete (Nothing = tutte): quanti dispositivi, quanti presenti nell'ultima scansione, quanti nuovi nelle 24 ore, quanti da classificare, quanti sospetti.</summary>
        Public Function Digest(Optional networkKey As String = Nothing) As String
            Dim hosts = ListHosts(networkKey:=networkKey)
            If hosts.Count = 0 Then Return If(networkKey Is Nothing, "Archivio vuoto: nessuna scansione registrata.", "Nessun dispositivo in archivio per questa rete: avvia una scansione.")
            Dim last = LastScan(networkKey)
            Dim newToday = ListEvents(500, since:=DateTime.Now.AddHours(-24), networkKey:=networkKey).Where(Function(e) e.Kind = "nuovo").Count()
            Dim unknown = hosts.Where(Function(h) h.Trust = HostTrust.Unknown AndAlso Not h.IsLocal).Count()
            Dim suspicious = hosts.Where(Function(h) h.Trust = HostTrust.Suspicious).Count()
            Dim online = hosts.Where(Function(h) h.IsOnline).Count()
            Dim sb As New StringBuilder()
            sb.Append($"{hosts.Count} dispositivi conosciuti ({online} presenti nell'ultima scansione")
            If last IsNot Nothing Then sb.Append($" del {last.StartedAt:dd/MM HH:mm}")
            sb.Append(")"c)
            If newToday > 0 Then sb.Append($", {newToday} nuovi nelle ultime 24 ore")
            If unknown > 0 Then sb.Append($", {unknown} da classificare")
            If suspicious > 0 Then sb.Append($", {suspicious} segnati come sospetti")
            sb.Append("."c)
            Return sb.ToString()
        End Function

#End Region

#Region "Supporto"

        Private Function LastScanId(networkKey As String) As Long?
            Dim value = _store.Scalar("SELECT MAX(id) FROM net_scans WHERE network_key = $n", ("$n", networkKey))
            Return If(TypeOf value Is Long, CType(CLng(value), Long?), Nothing)
        End Function

        ''' <summary>L'ultima scansione di ogni rete: serve a dire chi è «presente».</summary>
        Private Function LastScanIds() As Dictionary(Of String, Long)
            Dim map As New Dictionary(Of String, Long)(StringComparer.Ordinal)
            Using cmd = _store.Command("SELECT network_key, MAX(id) FROM net_scans WHERE network_key IS NOT NULL GROUP BY network_key")
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        map(r.GetString(0)) = r.GetInt64(1)
                    End While
                End Using
            End Using
            Return map
        End Function

        Private Function LoadIds(sql As String, ParamArray parameters As (Name As String, Value As Object)()) As HashSet(Of Long)
            Dim ids As New HashSet(Of Long)()
            Using cmd = _store.Command(sql, parameters)
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        ids.Add(r.GetInt64(0))
                    End While
                End Using
            End Using
            Return ids
        End Function

        Private Function FindByKey(networkKey As String, key As String) As NetHostRecord
            Using cmd = _store.Command($"SELECT {HostColumns} FROM net_hosts WHERE network_key = $n AND host_key = $k", ("$n", networkKey), ("$k", key))
                Using r = cmd.ExecuteReader()
                    Return If(r.Read(), Read(r, LastScanIds()), Nothing)
                End Using
            End Using
        End Function

        Private Shared Function Read(r As SqliteDataReader, lastScans As Dictionary(Of String, Long)) As NetHostRecord
            Dim lastScan As Long? = If(r.IsDBNull(17), CType(Nothing, Long?), r.GetInt64(17))
            Dim ports As IReadOnlyList(Of OpenPort) = Array.Empty(Of OpenPort)()
            If Not r.IsDBNull(18) Then
                Try
                    ports = If(JsonSerializer.Deserialize(Of List(Of OpenPort))(r.GetString(18)), New List(Of OpenPort)())
                Catch
                    ' JSON non leggibile
                End Try
            End If
            Dim networkKey = If(Text(r, 25), "")
            Dim networkLast As Long
            Return New NetHostRecord With {
                .Id = r.GetInt64(0), .HostKey = r.GetString(1), .Mac = Text(r, 2), .Ip = r.GetString(3), .HostName = Text(r, 4), .NetBios = Text(r, 5), .Workgroup = Text(r, 6),
                .Vendor = Text(r, 7), .DeviceType = If(Text(r, 8), "Dispositivo"), .Category = If(Text(r, 9), DeviceClassifier.CategoryOther), .OsGuess = Text(r, 10),
                .IsGateway = r.GetInt32(11) = 1, .IsLocal = r.GetInt32(12) = 1, .FirstSeen = InventoryStore.Parse(r.GetString(13)), .LastSeen = InventoryStore.Parse(r.GetString(14)),
                .SeenCount = r.GetInt32(15), .MissCount = r.GetInt32(16), .LastScanId = lastScan, .Ports = ports,
                .LatencyMs = If(r.IsDBNull(19), CType(Nothing, Double?), r.GetDouble(19)), .[Alias] = Text(r, 20), .Trust = CType(r.GetInt32(21), HostTrust), .Notes = Text(r, 22),
                .Icon = Text(r, 23), .IconColor = Text(r, 24), .NetworkKey = networkKey,
                .IsOnline = lastScan.HasValue AndAlso lastScans.TryGetValue(networkKey, networkLast) AndAlso networkLast = lastScan.Value
            }
        End Function

        Private Shared Function Text(r As SqliteDataReader, index As Integer) As String
            Return If(r.IsDBNull(index), Nothing, r.GetString(index))
        End Function

        Private Shared Function MatchesQuery(h As NetHostRecord, query As String) As Boolean
            Dim words = query.Split(" "c, StringSplitOptions.RemoveEmptyEntries Or StringSplitOptions.TrimEntries)
            Dim hay = String.Join(" ", {h.Ip, h.Mac, h.HostName, h.NetBios, h.Workgroup, h.Vendor, h.DeviceType, h.Category, h.[Alias], h.Notes, h.OsGuess}.Where(Function(x) x IsNot Nothing))
            Dim compact = hay.Replace(":", "").Replace("-", "")
            Return words.All(Function(w) hay.Contains(w, StringComparison.OrdinalIgnoreCase) OrElse compact.Contains(w.Replace(":", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase))
        End Function

        Private Shared Function KeyFor(mac As String, ip As String) As String
            Return If(MacVendorLookup.Normalize(mac), "ip:" & ip)
        End Function

#End Region

    End Class

End Namespace
