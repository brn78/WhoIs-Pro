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
        ''' <summary>Vero se visto nell'ultima scansione registrata.</summary>
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

    Public Class NetScanRecord
        Public Property Id As Long
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
        Public Property ScanId As Long?
        Public Property HostId As Long?
        Public Property At As DateTime
        ''' <summary>nuovo, ricomparso, cambiato, assente.</summary>
        Public Property Kind As String
        Public Property Title As String
        Public Property Details As String
        Public Property Acknowledged As Boolean
    End Class

    Public Class HostChange
        Public Sub New(host As NetHostRecord, change As String)
            Me.Host = host
            Me.Change = change
        End Sub
        Public ReadOnly Property Host As NetHostRecord
        Public ReadOnly Property Change As String
    End Class

    ''' <summary>Differenze tra l'ultima scansione e la precedente.</summary>
    Public Class ScanDiff
        Public Property ScanId As Long
        ''' <summary>Prima scansione in assoluto: l'archivio nasce, tutto è «nuovo» ma non deve partire nessun avviso.</summary>
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
            If NewHosts.Count > 0 Then parts.Add($"{NewHosts.Count} nuov{If(NewHosts.Count = 1, "o", "i")}: {String.Join(", ", NewHosts.Take(6).Select(Function(h) $"{h.DisplayName} ({h.Ip}{If(h.Vendor Is Nothing, "", ", " & h.Vendor)})"))}")
            If ReturnedHosts.Count > 0 Then parts.Add($"{ReturnedHosts.Count} ricompars{If(ReturnedHosts.Count = 1, "o", "i")}: {String.Join(", ", ReturnedHosts.Take(6).Select(Function(h) h.DisplayName))}")
            If GoneHosts.Count > 0 Then parts.Add($"{GoneHosts.Count} assent{If(GoneHosts.Count = 1, "e", "i")}: {String.Join(", ", GoneHosts.Take(6).Select(Function(h) h.DisplayName))}")
            If ChangedHosts.Count > 0 Then parts.Add($"{ChangedHosts.Count} cambiat{If(ChangedHosts.Count = 1, "o", "i")}: {String.Join("; ", ChangedHosts.Take(6).Select(Function(c) $"{c.Host.DisplayName} {c.Change}"))}")
            Return If(parts.Count = 0, "nessun cambiamento rispetto alla scansione precedente", String.Join(". ", parts) & ".")
        End Function
    End Class

#End Region

    ''' <summary>
    ''' Archivio della rete nel tempo: ogni scansione aggiorna i dispositivi conosciuti (chiave: MAC, o "ip:" più
    ''' l'indirizzo se il MAC non è noto), registra la scansione e produce eventi sui cambiamenti: nuovi, ricomparsi,
    ''' assenti, cambio di IP, nome o porte. L'utente può dare un alias, un livello di fiducia e note a ogni dispositivo.
    ''' </summary>
    Public NotInheritable Class NetworkInventory

#Region "Campi"

        Private Const HostColumns As String = "id, host_key, mac, ip, hostname, netbios, workgroup, vendor, device_type, category, os_guess, is_gateway, is_local, first_seen, last_seen, seen_count, miss_count, last_scan_id, ports_json, latency_ms, alias, trust, notes"
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

#Region "Registrazione di una scansione"

        ''' <summary>Aggiorna l'archivio con una scansione e restituisce le differenze rispetto alla precedente, in una sola transazione.</summary>
        Public Function RecordScan(report As ScanReport) As ScanDiff
            If report Is Nothing Then Throw New ArgumentNullException(NameOf(report))
            SyncLock _lock
                Dim previousScanId = LastScanId()
                Dim knownBefore = LoadIds("SELECT id FROM net_hosts")
                Using tx = _store.BeginTransaction()
                    _store.Execute("INSERT INTO net_scans(started_at, duration_ms, target, hosts_count, new_count, gone_count, changed_count, summary) VALUES ($t, $d, $g, $h, 0, 0, 0, '')",
                                   ("$t", InventoryStore.ToIso(report.StartedAt)), ("$d", CLng(report.Duration.TotalMilliseconds)), ("$g", report.Target), ("$h", report.Hosts.Count))
                    Dim scanId = _store.LastInsertId()
                    Dim diff As New ScanDiff With {.ScanId = scanId, .IsFirst = Not previousScanId.HasValue}
                    Dim presentIds As New HashSet(Of Long)()
                    Dim keysThisScan As New HashSet(Of String)(StringComparer.Ordinal)
                    Dim nowIso = InventoryStore.ToIso(report.StartedAt)

                    For Each h In report.Hosts
                        ' lo stesso MAC su due indirizzi nella stessa scansione (router con più IP, alias): il secondo si tiene per IP
                        Dim key = KeyFor(h.Mac, h.Ip)
                        Dim byMac = Not key.StartsWith("ip:", StringComparison.Ordinal)
                        If Not keysThisScan.Add(key) Then
                            key = "ip:" & h.Ip
                            byMac = False
                            keysThisScan.Add(key)
                        End If
                        Dim existing = FindByKey(key)
                        ' il MAC è arrivato solo adesso: il dispositivo era registrato per IP
                        If existing Is Nothing AndAlso byMac Then existing = FindByKey("ip:" & h.Ip)
                        If existing IsNot Nothing AndAlso presentIds.Contains(existing.Id) Then existing = Nothing
                        Dim portsJson = JsonSerializer.Serialize(h.Ports)
                        Dim vendor = If(h.Vendor = MacVendorLookup.PrivateAddressLabel, Nothing, h.Vendor)

                        If existing Is Nothing Then
                            _store.Execute("INSERT INTO net_hosts(host_key, mac, ip, hostname, netbios, workgroup, vendor, device_type, category, os_guess, is_gateway, is_local,
                                                                  first_seen, last_seen, seen_count, miss_count, last_scan_id, ports_json, latency_ms, trust)
                                            VALUES ($k, $m, $ip, $hn, $nb, $wg, $v, $dt, $cat, $os, $gw, $loc, $t, $t, 1, 0, $s, $pj, $lat, 0)",
                                           ("$k", key), ("$m", MacVendorLookup.Normalize(h.Mac)), ("$ip", h.Ip), ("$hn", h.HostName), ("$nb", h.NetBiosName), ("$wg", h.NetBiosWorkgroup),
                                           ("$v", vendor), ("$dt", h.DeviceType), ("$cat", h.Category), ("$os", h.OsGuess), ("$gw", If(h.IsGateway, 1, 0)), ("$loc", If(h.IsLocalMachine, 1, 0)),
                                           ("$t", nowIso), ("$s", scanId), ("$pj", portsJson), ("$lat", h.LatencyMs))
                            Dim id = _store.LastInsertId()
                            presentIds.Add(id)
                            Dim record = [Get](id)
                            diff.NewHosts.Add(record)
                            AddEvent(scanId, id, report.StartedAt, "nuovo", $"Nuovo dispositivo: {record.DisplayName}",
                                     $"{h.Ip}{If(h.Mac Is Nothing, "", " · " & h.Mac)}{If(vendor Is Nothing, "", " · " & vendor)} · {h.DeviceType}" &
                                     If(h.Ports.Count > 0, " · porte " & String.Join(",", h.Ports.Select(Function(p) p.Port)), ""))
                            Continue For
                        End If

                        presentIds.Add(existing.Id)
                        Dim changes As New List(Of String)()
                        If Not String.Equals(existing.Ip, h.Ip, StringComparison.Ordinal) Then changes.Add($"IP {existing.Ip} → {h.Ip}")
                        Dim newName = If(h.NetBiosName, h.HostName)
                        Dim oldName = If(existing.NetBios, existing.HostName)
                        If newName IsNot Nothing AndAlso oldName IsNot Nothing AndAlso Not String.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase) Then changes.Add($"nome {oldName} → {newName}")
                        Dim oldPorts = existing.Ports.Select(Function(p) p.Port).ToHashSet()
                        Dim newPorts = h.Ports.Select(Function(p) p.Port).ToHashSet()
                        If h.Ports.Count + oldPorts.Count > 0 AndAlso Not oldPorts.SetEquals(newPorts) Then
                            Dim added = newPorts.Except(oldPorts).OrderBy(Function(p) p).ToList()
                            Dim removed = oldPorts.Except(newPorts).OrderBy(Function(p) p).ToList()
                            If added.Count > 0 Then changes.Add("porte aperte " & String.Join(",", added))
                            If removed.Count > 0 Then changes.Add("porte chiuse " & String.Join(",", removed))
                        End If
                        Dim wasAbsent = existing.MissCount >= MissesBeforeGone

                        _store.Execute("UPDATE net_hosts SET host_key = $k, mac = COALESCE($m, mac), ip = $ip, hostname = COALESCE($hn, hostname), netbios = COALESCE($nb, netbios),
                                            workgroup = COALESCE($wg, workgroup), vendor = COALESCE($v, vendor), device_type = $dt, category = $cat, os_guess = COALESCE($os, os_guess),
                                            is_gateway = $gw, is_local = $loc, last_seen = $t, seen_count = seen_count + 1, miss_count = 0, last_scan_id = $s, ports_json = $pj, latency_ms = $lat
                                        WHERE id = $id",
                                       ("$k", key), ("$m", MacVendorLookup.Normalize(h.Mac)), ("$ip", h.Ip), ("$hn", h.HostName), ("$nb", h.NetBiosName), ("$wg", h.NetBiosWorkgroup), ("$v", vendor),
                                       ("$dt", h.DeviceType), ("$cat", h.Category), ("$os", h.OsGuess), ("$gw", If(h.IsGateway, 1, 0)), ("$loc", If(h.IsLocalMachine, 1, 0)),
                                       ("$t", nowIso), ("$s", scanId), ("$pj", portsJson), ("$lat", h.LatencyMs), ("$id", existing.Id))

                        Dim updated = [Get](existing.Id)
                        If wasAbsent Then
                            diff.ReturnedHosts.Add(updated)
                            AddEvent(scanId, updated.Id, report.StartedAt, "ricomparso", $"Ricomparso: {updated.DisplayName}", $"{h.Ip} · assente nelle scansioni precedenti")
                        End If
                        If changes.Count > 0 Then
                            Dim text = String.Join(", ", changes)
                            diff.ChangedHosts.Add(New HostChange(updated, text))
                            AddEvent(scanId, updated.Id, report.StartedAt, "cambiato", $"Cambiato: {updated.DisplayName}", text)
                        End If
                    Next

                    ' chi manca: un conteggio in più; «assente» alla seconda scansione consecutiva senza risposta (mai per questa macchina)
                    For Each id In knownBefore.Where(Function(x) Not presentIds.Contains(x))
                        Dim host = [Get](id)
                        If host Is Nothing OrElse host.IsLocal Then Continue For
                        Dim misses = host.MissCount + 1
                        _store.Execute("UPDATE net_hosts SET miss_count = $n WHERE id = $id", ("$n", misses), ("$id", id))
                        If misses = MissesBeforeGone Then
                            diff.GoneHosts.Add(host)
                            AddEvent(scanId, id, report.StartedAt, "assente", $"Assente: {host.DisplayName}", $"{host.Ip} · non risponde da {misses} scansioni")
                        End If
                    Next

                    _store.Execute("UPDATE net_scans SET new_count = $n, gone_count = $g, changed_count = $c, summary = $s WHERE id = $id",
                                   ("$n", diff.NewHosts.Count), ("$g", diff.GoneHosts.Count), ("$c", diff.ChangedHosts.Count + diff.ReturnedHosts.Count), ("$s", diff.Summarize()), ("$id", scanId))
                    tx.Commit()
                    Return diff
                End Using
            End SyncLock
        End Function

        Private Sub AddEvent(scanId As Long, hostId As Long, at As DateTime, kind As String, title As String, details As String)
            _store.Execute("INSERT INTO net_events(scan_id, host_id, at, kind, title, details) VALUES ($s, $h, $t, $k, $ti, $d)",
                           ("$s", scanId), ("$h", hostId), ("$t", InventoryStore.ToIso(at)), ("$k", kind), ("$ti", title), ("$d", details))
        End Sub

#End Region

#Region "Dispositivi"

        ''' <summary>Dispositivi conosciuti, in ordine numerico di IP; filtro per parole su IP, MAC, nomi, produttore, tipo, alias e note.</summary>
        Public Function ListHosts(Optional query As String = Nothing, Optional onlyOnline As Boolean = False) As IReadOnlyList(Of NetHostRecord)
            SyncLock _lock
                Dim lastScan = LastScanId()
                Dim list As New List(Of NetHostRecord)()
                Using cmd = _store.Command($"SELECT {HostColumns} FROM net_hosts")
                    Using r = cmd.ExecuteReader()
                        While r.Read()
                            Dim host = Read(r, lastScan)
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
                    Return If(r.Read(), Read(r, LastScanId()), Nothing)
                End Using
            End Using
        End Function

        ''' <summary>Trova per IP, MAC, alias, nome DNS o NetBIOS.</summary>
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
            _store.Execute("UPDATE net_hosts SET alias = $a WHERE id = $id", ("$a", If(String.IsNullOrWhiteSpace([alias]), Nothing, [alias].Trim())), ("$id", id))
        End Sub

        Public Sub SetTrust(id As Long, trust As HostTrust)
            _store.Execute("UPDATE net_hosts SET trust = $t WHERE id = $id", ("$t", CInt(trust)), ("$id", id))
        End Sub

        Public Sub SetNotes(id As Long, notes As String)
            _store.Execute("UPDATE net_hosts SET notes = $n WHERE id = $id", ("$n", If(String.IsNullOrWhiteSpace(notes), Nothing, notes.Trim())), ("$id", id))
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

        Public Function ListScans(Optional limit As Integer = 50) As IReadOnlyList(Of NetScanRecord)
            Dim list As New List(Of NetScanRecord)()
            Using cmd = _store.Command("SELECT id, started_at, duration_ms, target, hosts_count, new_count, gone_count, changed_count, summary FROM net_scans ORDER BY id DESC LIMIT $n", ("$n", limit))
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        list.Add(New NetScanRecord With {
                            .Id = r.GetInt64(0), .StartedAt = InventoryStore.Parse(r.GetString(1)), .DurationMs = r.GetInt64(2), .Target = r.GetString(3),
                            .HostsCount = r.GetInt32(4), .NewCount = r.GetInt32(5), .GoneCount = r.GetInt32(6), .ChangedCount = r.GetInt32(7), .Summary = r.GetString(8)
                        })
                    End While
                End Using
            End Using
            Return list
        End Function

        Public Function LastScan() As NetScanRecord
            Return ListScans(1).FirstOrDefault()
        End Function

        Public Function ListEvents(Optional limit As Integer = 100, Optional onlyUnacknowledged As Boolean = False, Optional since As DateTime? = Nothing, Optional hostId As Long? = Nothing) As IReadOnlyList(Of NetEventRecord)
            Dim list As New List(Of NetEventRecord)()
            Dim sql = "SELECT id, scan_id, host_id, at, kind, title, details, acknowledged FROM net_events WHERE 1 = 1" &
                      If(onlyUnacknowledged, " AND acknowledged = 0", "") &
                      If(since.HasValue, " AND at >= $since", "") &
                      If(hostId.HasValue, " AND host_id = $host", "") &
                      " ORDER BY id DESC LIMIT $n"
            Dim parameters As New List(Of (Name As String, Value As Object)) From {("$n", CObj(limit))}
            If since.HasValue Then parameters.Add(("$since", InventoryStore.ToIso(since.Value)))
            If hostId.HasValue Then parameters.Add(("$host", hostId.Value))
            Using cmd = _store.Command(sql, parameters.ToArray())
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        list.Add(New NetEventRecord With {
                            .Id = r.GetInt64(0), .ScanId = If(r.IsDBNull(1), CType(Nothing, Long?), r.GetInt64(1)), .HostId = If(r.IsDBNull(2), CType(Nothing, Long?), r.GetInt64(2)),
                            .At = InventoryStore.Parse(r.GetString(3)), .Kind = r.GetString(4), .Title = r.GetString(5), .Details = r.GetString(6), .Acknowledged = r.GetInt32(7) = 1
                        })
                    End While
                End Using
            End Using
            Return list
        End Function

        Public Sub AcknowledgeAll()
            _store.Execute("UPDATE net_events SET acknowledged = 1 WHERE acknowledged = 0")
        End Sub

        Public Sub Acknowledge(eventId As Long)
            _store.Execute("UPDATE net_events SET acknowledged = 1 WHERE id = $id", ("$id", eventId))
        End Sub

        Public Function UnacknowledgedCount() As Integer
            Return Convert.ToInt32(If(_store.Scalar("SELECT COUNT(*) FROM net_events WHERE acknowledged = 0"), 0))
        End Function

#End Region

#Region "Riepiloghi"

        ''' <summary>Una riga: quanti dispositivi, quanti presenti nell'ultima scansione, quanti nuovi nelle 24 ore, quanti da classificare, quanti sospetti.</summary>
        Public Function Digest() As String
            Dim hosts = ListHosts()
            If hosts.Count = 0 Then Return "Archivio vuoto: nessuna scansione registrata."
            Dim last = LastScan()
            Dim newToday = ListEvents(200, since:=DateTime.Now.AddHours(-24)).Where(Function(e) e.Kind = "nuovo").Count()
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

        Private Function LastScanId() As Long?
            Dim value = _store.Scalar("SELECT MAX(id) FROM net_scans")
            Return If(TypeOf value Is Long, CType(CLng(value), Long?), Nothing)
        End Function

        Private Function LoadIds(sql As String) As HashSet(Of Long)
            Dim ids As New HashSet(Of Long)()
            Using cmd = _store.Command(sql)
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        ids.Add(r.GetInt64(0))
                    End While
                End Using
            End Using
            Return ids
        End Function

        Private Function FindByKey(key As String) As NetHostRecord
            Using cmd = _store.Command($"SELECT {HostColumns} FROM net_hosts WHERE host_key = $k", ("$k", key))
                Using r = cmd.ExecuteReader()
                    Return If(r.Read(), Read(r, LastScanId()), Nothing)
                End Using
            End Using
        End Function

        Private Shared Function Read(r As SqliteDataReader, lastScanId As Long?) As NetHostRecord
            Dim lastScan As Long? = If(r.IsDBNull(17), CType(Nothing, Long?), r.GetInt64(17))
            Dim ports As IReadOnlyList(Of OpenPort) = Array.Empty(Of OpenPort)()
            If Not r.IsDBNull(18) Then
                Try
                    ports = If(JsonSerializer.Deserialize(Of List(Of OpenPort))(r.GetString(18)), New List(Of OpenPort)())
                Catch
                    ' JSON non leggibile
                End Try
            End If
            Return New NetHostRecord With {
                .Id = r.GetInt64(0), .HostKey = r.GetString(1), .Mac = Text(r, 2), .Ip = r.GetString(3), .HostName = Text(r, 4), .NetBios = Text(r, 5), .Workgroup = Text(r, 6),
                .Vendor = Text(r, 7), .DeviceType = If(Text(r, 8), "Dispositivo"), .Category = If(Text(r, 9), DeviceClassifier.CategoryOther), .OsGuess = Text(r, 10),
                .IsGateway = r.GetInt32(11) = 1, .IsLocal = r.GetInt32(12) = 1, .FirstSeen = InventoryStore.Parse(r.GetString(13)), .LastSeen = InventoryStore.Parse(r.GetString(14)),
                .SeenCount = r.GetInt32(15), .MissCount = r.GetInt32(16), .LastScanId = lastScan, .Ports = ports,
                .LatencyMs = If(r.IsDBNull(19), CType(Nothing, Double?), r.GetDouble(19)), .[Alias] = Text(r, 20), .Trust = CType(r.GetInt32(21), HostTrust), .Notes = Text(r, 22),
                .IsOnline = lastScanId.HasValue AndAlso lastScan.HasValue AndAlso lastScan.Value = lastScanId.Value
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
