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
