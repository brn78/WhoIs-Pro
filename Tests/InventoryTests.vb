Imports System.IO
Imports System.Threading
Imports NUnit.Framework
Imports WhoIsPro.Config
Imports WhoIsPro.Inventory
Imports WhoIsPro.Network.Scan
Imports WhoIsPro.UI

Namespace WhoIsPro.Tests

    ''' <summary>
    ''' Archivio della rete nel tempo, senza rete: rapporti sintetici in sequenza e gli eventi che devono produrre
    ''' (nuovo, cambiato per IP e porte, assente dopo due scansioni senza risposta, ricomparso), le schede curate
    ''' dall'utente e la regola «la prima scansione non genera avvisi» del servizio di sorveglianza.
    ''' </summary>
    Public Class InventoryTests
        Private testDir As String
        Private store As InventoryStore

        <SetUp>
        Public Sub Setup()
            testDir = Path.Combine(Path.GetTempPath(), "WhoIsProInventory_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(testDir)
            store = New InventoryStore(Path.Combine(testDir, "archivio-rete.db"))
        End Sub

        <TearDown>
        Public Sub TearDown()
            store.Dispose()
            Try
                Directory.Delete(testDir, True)
            Catch
            End Try
        End Sub

#Region "Rapporti sintetici"

        Private Shared Function Host(ip As String, mac As String, name As String, ParamArray ports As Integer()) As DiscoveredHost
            Dim h As New DiscoveredHost With {.Ip = ip, .Mac = mac, .HostName = name, .RespondsToPing = True, .LatencyMs = 2, .Ttl = 64, .DeviceType = "Dispositivo di prova", .Category = "Altro"}
            For Each p In ports
                h.Ports.Add(New OpenPort(p, "TCP", Nothing, Nothing))
            Next
            Return h
        End Function

        Private Shared Function Report(at As DateTime, ParamArray hosts As DiscoveredHost()) As ScanReport
            Return New ScanReport(at, TimeSpan.FromSeconds(3), "192.168.50.0/24", "192.168.50.5", "192.168.50.1", hosts)
        End Function

        ''' <summary>Rete di prova: questo PC, un NAS con SSH e web, una stampante.</summary>
        Private Shared Function ReportA(at As DateTime) As ScanReport
            Dim local = Host("192.168.50.5", "aa:bb:cc:00:00:05", "pc-prova")
            local.IsLocalMachine = True
            Return Report(at, local, Host("192.168.50.10", "aa:bb:cc:00:00:10", "nas-prova", 22, 80), Host("192.168.50.20", "aa:bb:cc:00:00:20", "stampante-prova", 9100))
        End Function

        ''' <summary>Seconda scansione: il NAS cambia IP e chiude la porta 80, la stampante tace, compare un telefono.</summary>
        Private Shared Function ReportB(at As DateTime) As ScanReport
            Dim local = Host("192.168.50.5", "aa:bb:cc:00:00:05", "pc-prova")
            local.IsLocalMachine = True
            Return Report(at, local, Host("192.168.50.11", "aa:bb:cc:00:00:10", "nas-prova", 22), Host("192.168.50.30", "aa:bb:cc:00:00:30", "telefono-prova"))
        End Function

#End Region

        <Test>
        Public Sub SameDeviceMerger_MergesSameMacButNotRepeatersOrDifferentSystems()
            ' telecamera con cavo e Wi-Fi sullo stesso MAC: una riga, l'IP che ha risposto a SADP è il principale
            Dim wired = Host("192.168.50.201", "bc:ad:28:00:00:c9", Nothing, 80, 554, 8000)
            wired.Sadp = New SadpDeviceInfo With {.Ip = "192.168.50.201", .Model = "DS-2CD2442FWD-IW"}
            Dim wifi = Host("192.168.50.202", "bc:ad:28:00:00:c9", Nothing, 80, 443)
            Dim other = Host("192.168.50.10", "aa:bb:cc:00:00:10", "nas-prova", 22)
            Dim r = SameDeviceMerger.MergeByMac({wifi, other, wired})
            Assert.That(r.Hosts.Count, [Is].EqualTo(2), "due dispositivi reali")
            Assert.That(r.Merged.Single().Ip, [Is].EqualTo("192.168.50.201"))
            Assert.That(r.Merged.Single().ExtraIps, [Is].EqualTo({"192.168.50.202"}))
            Assert.That(r.Merged.Single().Ports.Select(Function(p) p.Port), [Is].EqualTo({80, 443, 554, 8000}), "porte unite")

            ' TTL diversi (Windows dietro un ripetitore MAC-NAT con un telefono): non è lo stesso dispositivo
            Dim pc = Host("192.168.50.30", "11:22:33:44:55:66", Nothing) : pc.Ttl = 128
            Dim phone = Host("192.168.50.31", "11:22:33:44:55:66", Nothing) : phone.Ttl = 64
            Assert.That(SameDeviceMerger.MergeByMac({pc, phone}).Hosts.Count, [Is].EqualTo(2))

            ' nomi NetBIOS diversi: due macchine
            Dim a = Host("192.168.50.40", "22:22:22:22:22:22", Nothing) : a.NetBiosName = "PC-UNO"
            Dim b = Host("192.168.50.41", "22:22:22:22:22:22", Nothing) : b.NetBiosName = "PC-DUE"
            Assert.That(SameDeviceMerger.MergeByMac({a, b}).Hosts.Count, [Is].EqualTo(2))

            ' più di 4 indirizzi sullo stesso MAC: ripetitore o proxy ARP, niente unione
            Dim many = Enumerable.Range(50, 5).Select(Function(i) Host($"192.168.50.{i}", "33:33:33:33:33:33", Nothing)).ToArray()
            Assert.That(SameDeviceMerger.MergeByMac(many).Hosts.Count, [Is].EqualTo(5))

            ' PC con tre IPv4 sulla stessa scheda: un dispositivo, il gateway resta principale se c'è
            Dim three = {Host("192.168.50.62", "44:44:44:44:44:44", Nothing), Host("192.168.50.60", "44:44:44:44:44:44", Nothing), Host("192.168.50.61", "44:44:44:44:44:44", Nothing)}
            Dim m3 = SameDeviceMerger.MergeByMac(three)
            Assert.That(m3.Hosts.Single().Ip, [Is].EqualTo("192.168.50.60"))
            Assert.That(m3.Hosts.Single().ExtraIps, [Is].EqualTo({"192.168.50.61", "192.168.50.62"}))
        End Sub

        <Test>
        Public Sub RecordScan_SameDeviceWithTwoIps_AbsorbsOldSecondaryRowAndKeepsCount()
            ' prima (versioni precedenti): due righe, il secondo indirizzo salvato per IP, con un nome dato dall'utente
            Dim first = Report(New DateTime(2026, 10, 1, 9, 0, 0), Host("192.168.50.201", "bc:ad:28:00:00:c9", Nothing, 80), Host("192.168.50.202", "bc:ad:28:00:00:c9", Nothing, 80))
            store.Network.RecordScan(first)
            Assert.That(store.Network.ListHosts().Count, [Is].EqualTo(2))
            Dim secondary = store.Network.ListHosts().Single(Function(h) h.Ip = "192.168.50.202")
            store.Network.SetAlias(secondary.Id, "Telecamera ingresso")

            ' adesso: un solo dispositivo con due IPv4
            Dim merged = Host("192.168.50.201", "bc:ad:28:00:00:c9", Nothing, 80)
            merged.ExtraIps.Add("192.168.50.202")
            Dim diff = store.Network.RecordScan(Report(New DateTime(2026, 10, 1, 10, 0, 0), merged))
            Dim hosts = store.Network.ListHosts()
            Assert.That(hosts.Count, [Is].EqualTo(1), "la vecchia riga dell'indirizzo secondario è assorbita")
            Assert.That(hosts(0).ExtraIps, [Is].EqualTo({"192.168.50.202"}))
            Assert.That(hosts(0).Alias, [Is].EqualTo("Telecamera ingresso"), "il nome dato dall'utente passa al principale")
            Assert.That(diff.ChangedHosts, [Is].Empty, "nessun falso cambio di IP")
            Assert.That(diff.GoneHosts, [Is].Empty)

            ' la scansione dopo: niente «assente» per l'indirizzo secondario
            Dim third = store.Network.RecordScan(Report(New DateTime(2026, 10, 1, 11, 0, 0), merged))
            Assert.That(third.GoneHosts, [Is].Empty)
            Assert.That(third.ChangedHosts, [Is].Empty)
        End Sub

        <Test>
        Public Sub DhcpRange_NormalizedStoredAndCleared()
            Assert.That(NetworkInventory.NormalizeDhcpRange("192.168.1.100-199"), [Is].EqualTo("192.168.1.100-192.168.1.199"))
            Assert.That(NetworkInventory.NormalizeDhcpRange(" 192.168.1.199 - 192.168.1.100 "), [Is].EqualTo("192.168.1.100-192.168.1.199"), "in ordine anche se scritto al contrario")
            Assert.That(NetworkInventory.NormalizeDhcpRange("   "), [Is].Null)
            Assert.Throws(Of ArgumentException)(Sub() NetworkInventory.NormalizeDhcpRange("192.168.1.100"))
            Assert.Throws(Of ArgumentException)(Sub() NetworkInventory.NormalizeDhcpRange("192.168.1.100-300"))
            Assert.Throws(Of ArgumentException)(Sub() NetworkInventory.NormalizeDhcpRange("casa-ufficio"))

            store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 9, 0, 0)))
            Dim net = store.Network.ListNetworks().Single()
            Assert.That(net.DhcpRange, [Is].Null)
            store.Network.SetNetworkDhcpRange(net.Key, "192.168.50.100-150")
            Assert.That(store.Network.GetNetwork(net.Key).DhcpRange, [Is].EqualTo("192.168.50.100-192.168.50.150"))
            store.Network.SetNetworkDhcpRange(net.Key, "")
            Assert.That(store.Network.GetNetwork(net.Key).DhcpRange, [Is].Null, "campo vuoto = intervallo tolto")
        End Sub

        <Test>
        Public Sub BulkTrustAndDeleteHosts_ApplyToAllSelected()
            store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 9, 0, 0)))
            Dim all = store.Network.ListHosts()
            Assert.That(all.Count, [Is].EqualTo(3))
            Dim nas = all.Single(Function(h) h.HostName = "nas-prova")
            Dim printer = all.Single(Function(h) h.HostName = "stampante-prova")

            ' selezione multipla: la stessa fiducia a due dispositivi, il terzo non cambia
            store.Network.SetTrust({nas.Id, printer.Id}, HostTrust.Trusted)
            Assert.That(store.Network.Get(nas.Id).Trust, [Is].EqualTo(HostTrust.Trusted))
            Assert.That(store.Network.Get(printer.Id).Trust, [Is].EqualTo(HostTrust.Trusted))
            Assert.That(store.Network.ListHosts().Single(Function(h) h.HostName = "pc-prova").Trust, [Is].EqualTo(HostTrust.Unknown))

            ' «dimentica» su due insieme: restano uno e nessun evento orfano
            store.Network.DeleteHosts({nas.Id, printer.Id})
            Assert.That(store.Network.ListHosts().Select(Function(h) h.HostName), [Is].EqualTo({"pc-prova"}))
            Assert.That(store.Network.ListEvents(100).All(Function(e) Not e.HostId.HasValue OrElse e.HostId.Value <> nas.Id AndAlso e.HostId.Value <> printer.Id), [Is].True)
        End Sub

        <Test>
        Public Sub RecordScan_FirstScan_EverythingIsNewAndMarkedFirst()
            Dim diff = store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 9, 0, 0)))
            Assert.That(diff.IsFirst, [Is].True)
            Assert.That(diff.NewHosts.Count, [Is].EqualTo(3))
            Assert.That(diff.GoneHosts, [Is].Empty)
            Assert.That(diff.ChangedHosts, [Is].Empty)
            Assert.That(store.Network.ListEvents().Select(Function(e) e.Kind).Distinct(), [Is].EqualTo({"nuovo"}))
            Assert.That(store.Network.ListHosts().Count, [Is].EqualTo(3))
            Assert.That(store.Network.ListHosts().All(Function(h) h.IsOnline), [Is].True)
        End Sub

        <Test>
        Public Sub RecordScan_SecondScan_ProducesExactlyTheExpectedEvents()
            store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 9, 0, 0)))
            Dim diff = store.Network.RecordScan(ReportB(New DateTime(2026, 10, 1, 10, 0, 0)))

            Assert.That(diff.IsFirst, [Is].False)
            Assert.That(diff.NewHosts.Select(Function(h) h.Ip), [Is].EqualTo({"192.168.50.30"}), "il telefono è nuovo")
            Assert.That(diff.ChangedHosts.Count, [Is].EqualTo(1), "solo il NAS è cambiato")
            Assert.That(diff.ChangedHosts(0).Host.Ip, [Is].EqualTo("192.168.50.11"), "stesso MAC: il dispositivo è lo stesso con l'IP nuovo")
            Assert.That(diff.ChangedHosts(0).Change, Does.Contain("IP 192.168.50.10 → 192.168.50.11").And.Contain("porte chiuse 80"))
            Assert.That(diff.GoneHosts, [Is].Empty, "la stampante ha saltato una sola scansione: non è ancora assente")
            Assert.That(diff.ReturnedHosts, [Is].Empty)

            Dim kinds = store.Network.ListEvents().Where(Function(e) e.ScanId = diff.ScanId).Select(Function(e) e.Kind).OrderBy(Function(k) k).ToList()
            Assert.That(kinds, [Is].EqualTo({"cambiato", "nuovo"}))
            Assert.That(store.Network.ListHosts().Count, [Is].EqualTo(4), "la stampante resta in archivio")
            Dim printer = store.Network.Find("stampante-prova")
            Assert.That(printer.IsOnline, [Is].False)
            Assert.That(printer.MissCount, [Is].EqualTo(1))
        End Sub

        <Test>
        Public Sub RecordScan_AbsentAfterTwoMisses_ThenReturned()
            store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 9, 0, 0)))
            store.Network.RecordScan(ReportB(New DateTime(2026, 10, 1, 10, 0, 0)))
            Dim third = store.Network.RecordScan(ReportB(New DateTime(2026, 10, 1, 11, 0, 0)))
            Assert.That(third.GoneHosts.Select(Function(h) h.HostName), [Is].EqualTo({"stampante-prova"}), "alla seconda scansione senza risposta è assente")
            Assert.That(third.NewHosts, [Is].Empty)
            Assert.That(third.ChangedHosts, [Is].Empty)
            Assert.That(store.Network.ListEvents().First().Kind, [Is].EqualTo("assente"))

            ' la stampante torna: ricomparso, senza cambiamenti
            Dim fourth = store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 12, 0, 0)))
            Assert.That(fourth.ReturnedHosts.Select(Function(h) h.HostName), [Is].EqualTo({"stampante-prova"}))
            Assert.That(fourth.ChangedHosts.Count, [Is].EqualTo(1), "il NAS torna all'IP e alla porta di prima: cambiato")
            Assert.That(fourth.ChangedHosts(0).Change, Does.Contain("porte aperte 80"))
            Dim phone = store.Network.Find("telefono-prova")
            Assert.That(phone.MissCount, [Is].EqualTo(1), "il telefono manca da una scansione: non ancora assente")
            Assert.That(store.Network.LastScan().Summary, Does.Contain("ricompars"))
        End Sub

        <Test>
        Public Sub Hosts_AliasTrustNotesAndForget()
            store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 9, 0, 0)))
            Dim nas = store.Network.Find("nas-prova")
            store.Network.SetAlias(nas.Id, "Archivio ufficio")
            store.Network.SetTrust(nas.Id, HostTrust.Trusted)
            store.Network.SetNotes(nas.Id, "nello sgabuzzino")
            Dim again = store.Network.Get(nas.Id)
            Assert.That(again.DisplayName, [Is].EqualTo("Archivio ufficio"))
            Assert.That(again.Trust, [Is].EqualTo(HostTrust.Trusted))
            Assert.That(store.Network.Find("Archivio ufficio").Id, [Is].EqualTo(nas.Id))
            Assert.That(store.Network.ListHosts("sgabuzzino").Count, [Is].EqualTo(1))
            Assert.That(store.Network.ListHosts("aabbcc000010").Count, [Is].EqualTo(1), "il MAC si cerca anche senza i due punti")
            Assert.That(store.Network.Digest(), Does.Contain("3 dispositivi conosciuti"))

            store.Network.DeleteHost(nas.Id)
            Assert.That(store.Network.ListHosts().Count, [Is].EqualTo(2))
            Dim diff = store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 10, 0, 0)))
            Assert.That(diff.NewHosts.Select(Function(h) h.HostName), [Is].EqualTo({"nas-prova"}), "dimenticato e ricomparso: torna nuovo")
        End Sub

        <Test>
        Public Sub Events_AcknowledgeAndUnacknowledgedCount()
            store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 9, 0, 0)))
            Assert.That(store.Network.UnacknowledgedCount(), [Is].EqualTo(3))
            store.Network.Acknowledge(store.Network.ListEvents().First().Id)
            Assert.That(store.Network.UnacknowledgedCount(), [Is].EqualTo(2))
            Assert.That(store.Network.ListEvents(onlyUnacknowledged:=True).Count, [Is].EqualTo(2))
            store.Network.AcknowledgeAll()
            Assert.That(store.Network.UnacknowledgedCount(), [Is].EqualTo(0))
        End Sub

#Region "Una rete per sede"

        Private Shared Function Site(mac As String, cidr As String) As NetworkIdentity
            Return New NetworkIdentity With {.Key = "gw:" & mac, .Cidr = cidr, .GatewayIp = cidr.Replace(".0/24", ".1"), .GatewayMac = mac, .GatewayVendor = "Prova Networks Ltd."}
        End Function

        <Test>
        Public Sub Networks_TwoSites_DoNotMixAndNeverMarkEachOtherAbsent()
            Dim home = Site("aa:00:00:00:00:01", "192.168.50.0/24")
            Dim client = Site("bb:00:00:00:00:02", "192.168.50.0/24")
            store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 9, 0, 0)), home)
            ' dal cliente: stessa sottorete 192.168.50.0/24 ma un altro router, dispositivi diversi; due scansioni
            Dim atClient = Report(New DateTime(2026, 10, 2, 9, 0, 0), Host("192.168.50.10", "cc:dd:ee:00:00:10", "server-cliente", 445), Host("192.168.50.40", "cc:dd:ee:00:00:40", "nvr-cliente", 554))
            Dim first = store.Network.RecordScan(atClient, client)
            Dim second = store.Network.RecordScan(Report(New DateTime(2026, 10, 2, 10, 0, 0), Host("192.168.50.10", "cc:dd:ee:00:00:10", "server-cliente", 445), Host("192.168.50.40", "cc:dd:ee:00:00:40", "nvr-cliente", 554)), client)

            Assert.That(first.IsFirst, [Is].True, "la prima scansione di una rete nuova è un primo censimento, anche se l'archivio ha già un'altra rete")
            Assert.That(second.GoneHosts, [Is].Empty, "i dispositivi di casa non c'entrano con il cliente: nessun «assente»")
            Assert.That(store.Network.ListHosts(networkKey:=home.Key).Count, [Is].EqualTo(3))
            Assert.That(store.Network.ListHosts(networkKey:=client.Key).Count, [Is].EqualTo(2))
            Assert.That(store.Network.ListHosts(networkKey:=home.Key).All(Function(h) h.MissCount = 0), [Is].True)
            Assert.That(store.Network.ListHosts(networkKey:=home.Key).All(Function(h) h.IsOnline), [Is].True, "«presente» si giudica sull'ultima scansione della sua rete")
            Dim networks = store.Network.ListNetworks()
            Assert.That(networks.Count, [Is].EqualTo(2))
            Assert.That(networks.First().Key, [Is].EqualTo(client.Key), "la più recente per prima")
            Assert.That(networks.First().Label, [Is].EqualTo("192.168.50.0/24 · Prova"))
            Assert.That(networks.First().HostsCount, [Is].EqualTo(2))
            Assert.That(store.Network.UnacknowledgedCount(client.Key), [Is].EqualTo(2))
            Assert.That(store.Network.Digest(home.Key), Does.Contain("3 dispositivi conosciuti"))

            store.Network.SetNetworkAlias(client.Key, "Cliente Rossi")
            Assert.That(store.Network.GetNetwork(client.Key).DisplayName, [Is].EqualTo("Cliente Rossi"))
            store.Network.DeleteNetwork(client.Key)
            Assert.That(store.Network.ListNetworks().Select(Function(n) n.Key), [Is].EqualTo({home.Key}))
            Assert.That(store.Network.ListHosts().Count, [Is].EqualTo(3))
        End Sub

        <Test>
        Public Sub Networks_LegacyImportedDevice_IsAdoptedByTheRealNetwork()
            Dim imported = store.Network.ImportKnownDevices({New ImportedDevice With {.Mac = "AA:BB:CC:00:00:10", .Ip = "192.168.50.10", .Hostname = "nas-prova", .Name = "NAS ufficio", .FirstSeen = New DateTime(2026, 1, 5), .LastSeen = New DateTime(2026, 1, 6), .TimesSeen = 4}})
            Assert.That(imported, [Is].EqualTo(1))
            Assert.That(store.Network.ListNetworks().Single().Key, [Is].EqualTo("net:192.168.50.0/24"), "dal vecchio registro si conosce solo la sottorete")

            Dim home = Site("aa:00:00:00:00:01", "192.168.50.0/24")
            Dim diff = store.Network.RecordScan(ReportA(New DateTime(2026, 10, 1, 9, 0, 0)), home)
            Assert.That(diff.NewHosts.Select(Function(h) h.HostName), [Is].EquivalentTo({"pc-prova", "stampante-prova"}), "il NAS era già conosciuto: passa alla rete del router, non è nuovo")
            Dim nas = store.Network.Find("NAS ufficio")
            Assert.That(nas.NetworkKey, [Is].EqualTo(home.Key))
            Assert.That(nas.FirstSeen, [Is].EqualTo(New DateTime(2026, 1, 5)).Within(TimeSpan.FromSeconds(1)))
            Assert.That(nas.SeenCount, [Is].EqualTo(5))
            Assert.That(store.Network.ListNetworks().First(Function(n) n.Key = "net:192.168.50.0/24").HostsCount, [Is].EqualTo(0), "la rete «solo sottorete» si è svuotata")
        End Sub

        <Test>
        Public Sub Networks_DatabaseOfVersion260_IsMigratedWithoutLosingAnything()
            store.Dispose()
            Dim path = IO.Path.Combine(testDir, "archivio-260.db")
            ' lo schema della 2.6.0: un solo archivio, chiave unica sul solo dispositivo
            Using old As New Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}")
                old.Open()
                Using cmd = old.CreateCommand()
                    cmd.CommandText = "CREATE TABLE net_hosts (id INTEGER PRIMARY KEY AUTOINCREMENT, host_key TEXT NOT NULL UNIQUE, mac TEXT, ip TEXT NOT NULL, hostname TEXT, netbios TEXT, workgroup TEXT,
                        vendor TEXT, device_type TEXT, category TEXT, glyph TEXT, os_guess TEXT, is_gateway INTEGER NOT NULL DEFAULT 0, is_local INTEGER NOT NULL DEFAULT 0, first_seen TEXT NOT NULL,
                        last_seen TEXT NOT NULL, seen_count INTEGER NOT NULL DEFAULT 1, miss_count INTEGER NOT NULL DEFAULT 0, last_scan_id INTEGER, ports_json TEXT, latency_ms REAL, alias TEXT,
                        trust INTEGER NOT NULL DEFAULT 0, notes TEXT, icon TEXT, icon_color TEXT);
                        CREATE TABLE net_scans (id INTEGER PRIMARY KEY AUTOINCREMENT, started_at TEXT NOT NULL, duration_ms INTEGER NOT NULL, target TEXT NOT NULL, hosts_count INTEGER NOT NULL,
                        new_count INTEGER NOT NULL, gone_count INTEGER NOT NULL, changed_count INTEGER NOT NULL, summary TEXT NOT NULL);
                        CREATE TABLE net_events (id INTEGER PRIMARY KEY AUTOINCREMENT, scan_id INTEGER, host_id INTEGER, at TEXT NOT NULL, kind TEXT NOT NULL, title TEXT NOT NULL, details TEXT NOT NULL, acknowledged INTEGER NOT NULL DEFAULT 0);
                        INSERT INTO net_scans(started_at, duration_ms, target, hosts_count, new_count, gone_count, changed_count, summary) VALUES ('2026-10-30T10:00:00.0000000Z', 8000, '192.168.50.0/24', 1, 1, 0, 0, '1 nuovo');
                        INSERT INTO net_hosts(host_key, mac, ip, hostname, first_seen, last_seen, seen_count, last_scan_id, alias) VALUES ('aa:bb:cc:00:00:10', 'aa:bb:cc:00:00:10', '192.168.50.10', 'nas-prova', '2026-10-30T10:00:00.0000000Z', '2026-10-30T10:00:00.0000000Z', 1, 1, 'NAS ufficio');
                        INSERT INTO net_hosts(host_key, mac, ip, hostname, first_seen, last_seen, seen_count) VALUES ('dd:ee:ff:00:00:07', 'dd:ee:ff:00:00:07', '172.16.20.7', 'cam-cliente', '2026-10-22T15:59:00.0000000Z', '2026-10-22T15:59:00.0000000Z', 2);
                        INSERT INTO net_events(scan_id, host_id, at, kind, title, details) VALUES (1, 1, '2026-10-30T10:00:00.0000000Z', 'nuovo', 'Nuovo dispositivo: nas-prova', '192.168.50.10');"
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            store = New InventoryStore(path)
            Dim hosts = store.Network.ListHosts()
            Assert.That(hosts.Count, [Is].EqualTo(2))
            Dim nas = store.Network.Find("NAS ufficio")
            Assert.That(nas.NetworkKey, [Is].EqualTo("net:192.168.50.0/24"))
            Assert.That(nas.IsOnline, [Is].True, "la scansione della 2.6.0 resta l'ultima della sua rete")
            Assert.That(store.Network.Find("cam-cliente").NetworkKey, [Is].EqualTo("net:172.16.20.0/24"))
            Assert.That(store.Network.ListNetworks().Select(Function(n) n.Key), [Is].EquivalentTo({"net:192.168.50.0/24", "net:172.16.20.0/24"}))
            Assert.That(store.Network.ListEvents().Single().NetworkKey, [Is].EqualTo("net:192.168.50.0/24"))
            Assert.That(store.Network.ListScans().Single().NetworkKey, [Is].EqualTo("net:192.168.50.0/24"))

            ' la chiave unica è ora (rete, dispositivo): lo stesso MAC può stare in due sedi
            Dim client = Site("bb:00:00:00:00:02", "10.0.0.0/24")
            store.Network.RecordScan(Report(New DateTime(2026, 10, 31, 9, 0, 0), Host("10.0.0.10", "aa:bb:cc:00:00:10", "nas-gemello")), client)
            Assert.That(store.Network.ListHosts().Count, [Is].EqualTo(3))
            Assert.That(store.Network.Find("NAS ufficio").NetworkKey, [Is].EqualTo("net:192.168.50.0/24"), "l'adozione vale per la stessa sottorete, non per un altro cliente")
        End Sub

#End Region

#Region "Servizio di sorveglianza"

        ''' <summary>Motore finto: restituisce i rapporti in sequenza, senza toccare la rete.</summary>
        Private NotInheritable Class FakeScanner
            Implements INetworkScanner
            Private ReadOnly _reports As Queue(Of ScanReport)
            Public Sub New(ParamArray reports As ScanReport())
                _reports = New Queue(Of ScanReport)(reports)
            End Sub
            Public Function GetLocalNetworks() As IReadOnlyList(Of LocalNetwork) Implements INetworkScanner.GetLocalNetworks
                Return Array.Empty(Of LocalNetwork)()
            End Function
            Public Function IdentifyNetwork(target As String) As NetworkIdentity Implements INetworkScanner.IdentifyNetwork
                Return NetworkIdentity.ForSubnet("192.168.50.0/24")
            End Function
            Public Function ScanAsync(options As ScanOptions, progress As IProgress(Of ScanPhaseProgress), ct As CancellationToken) As Task(Of ScanReport) Implements INetworkScanner.ScanAsync
                progress?.Report(New ScanPhaseProgress("ping", 1, 1, "prova"))
                Return Task.FromResult(_reports.Dequeue())
            End Function
        End Class

        <Test>
        Public Async Function WatchService_FirstScanIsSilent_ThenNewDeviceRaisesAttention() As Task
            Dim settings As New AppSettings With {.NotifyNewDevices = True}
            Dim scanner As New FakeScanner(ReportA(New DateTime(2026, 10, 1, 9, 0, 0)), ReportB(New DateTime(2026, 10, 1, 10, 0, 0)))
            Using watch As New NetworkWatchService(settings, store, scanner, Nothing)
                Dim alerts As New List(Of NetworkAlert)()
                AddHandler watch.AlertRaised, Sub(s, a) alerts.Add(a)

                Dim first = Await watch.ScanAsync(automatic:=True)
                Assert.That(first.Outcome.Diff.IsFirst, [Is].True)
                Assert.That(alerts, [Is].Empty, "la prima scansione crea l'archivio senza avvisi")

                Dim second = Await watch.ScanAsync(automatic:=True)
                Assert.That(second.Outcome.Diff.NewHosts.Count, [Is].EqualTo(1))
                Assert.That(alerts.Count, [Is].EqualTo(1))
                Assert.That(alerts(0).Level, [Is].EqualTo("attention"))
                Assert.That(alerts(0).Title, Does.Contain("telefono-prova"))
                Assert.That(alerts(0).Detail, Does.Contain("192.168.50.30"))
                Assert.That(watch.IsScanning, [Is].False)
            End Using
        End Function

#End Region

    End Class

End Namespace
