Imports System.IO
Imports System.Text.Json
Imports NUnit.Framework
Imports WhoIsPro.Machine
Imports WhoIsPro.Models
Imports WhoIsPro.Storage

Namespace WhoIsPro.Tests
    ''' <summary>Prove della Panoramica: storico delle scansioni nella sessione, traffico delle schede e riepilogo del sistema.</summary>
    Public Class DashboardTests
        Private Shared ReadOnly Camel As New JsonSerializerOptions With {.PropertyNamingPolicy = JsonNamingPolicy.CamelCase}
        Private testDir As String

        <SetUp>
        Public Sub Setup()
            testDir = Path.Combine(Path.GetTempPath(), "WhoIsProDash_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(testDir)
        End Sub

        <TearDown>
        Public Sub TearDown()
            Try
                If Directory.Exists(testDir) Then Directory.Delete(testDir, True)
            Catch
            End Try
        End Sub

        <Test>
        Public Sub Scans_RoundTripThroughAtomicSave()
            Dim session As New ScanSession()
            session.Scans.Add(New ScanRecord With {.Kind = "network", .Target = "192.168.1.0/24", .Seconds = 11.8, .Found = 15})
            session.Scans.Add(New ScanRecord With {.Kind = "ports", .Target = "192.168.1.40", .Seconds = 6.2, .Found = 2, .Cancelled = True, .Error = ""})
            Dim file = Path.Combine(testDir, "storico.whoispro")
            Assert.That(AtomicFileManager.SaveSessionAtomic(session, file).IsSuccess, Iz.True)

            Dim restored As Boolean
            Dim loaded = AtomicFileManager.LoadSessionSafe(file, restored)
            Assert.That(loaded.Scans.Count, Iz.EqualTo(2))
            Assert.That(loaded.Scans(0).Kind, Iz.EqualTo("network"))
            Assert.That(loaded.Scans(0).Found, Iz.EqualTo(15))
            Assert.That(loaded.Scans(1).Cancelled, Iz.True)
        End Sub

        <Test>
        Public Sub SessionWithoutScans_LoadsWithEmptyHistory()
            ' file salvato da una versione fino alla 2.3: niente proprietà Scans
            Dim file = Path.Combine(testDir, "vecchia.whoispro")
            IO.File.WriteAllText(file, "{""SessionTitle"":""Vecchia"",""Hosts"":[],""Ports"":[]}")
            Dim restored As Boolean
            Dim loaded = AtomicFileManager.LoadSessionSafe(file, restored)
            Assert.That(loaded.SessionTitle, Iz.EqualTo("Vecchia"))
            Assert.That(loaded.Scans, Iz.Not.Null)
            Assert.That(loaded.Scans, Iz.Empty)
        End Sub

        <Test>
        Public Sub ScanRecord_SerializesForThePage()
            Dim json = JsonSerializer.Serialize(New ScanRecord With {.Kind = "trace", .Target = "1.1.1.1", .Found = 8, .Error = "x"}, Camel)
            Using doc = JsonDocument.Parse(json)
                Dim root = doc.RootElement
                Assert.That(root.GetProperty("kind").GetString(), Iz.EqualTo("trace"))
                Assert.That(root.GetProperty("found").GetInt32(), Iz.EqualTo(8))
                Assert.That(root.GetProperty("error").GetString(), Iz.EqualTo("x"))
                Assert.That(root.TryGetProperty("at", Nothing), Iz.True)
            End Using
        End Sub

        <Test>
        Public Sub TrafficMonitor_ReadsAdaptersWithRates()
            TrafficMonitor.Read()
            Threading.Thread.Sleep(700)
            Dim rows = TrafficMonitor.Read()
            Dim json = JsonSerializer.Serialize(rows, Camel)
            Using doc = JsonDocument.Parse(json)
                For Each nic In doc.RootElement.EnumerateArray()
                    Assert.That(nic.GetProperty("id").GetString(), Iz.Not.Empty)
                    If nic.GetProperty("connected").GetBoolean() Then
                        Assert.That(nic.GetProperty("ip").GetString() <> "" OrElse nic.GetProperty("ipv6").GetArrayLength() > 0, "una scheda collegata ha un indirizzo")
                    End If
                    Assert.That(nic.GetProperty("rxBps").GetDouble(), Iz.GreaterThanOrEqualTo(0))
                    Assert.That(nic.GetProperty("txBps").GetDouble(), Iz.GreaterThanOrEqualTo(0))
                    Assert.That(nic.GetProperty("rxTotal").GetInt64(), Iz.GreaterThanOrEqualTo(0))
                    TestContext.Out.WriteLine($"{nic.GetProperty("name").GetString()} {nic.GetProperty("type").GetString()} {nic.GetProperty("ip").GetString()} rx {nic.GetProperty("rxBps").GetDouble()} B/s tx {nic.GetProperty("txBps").GetDouble()} B/s")
                Next
            End Using
        End Sub

        <Test>
        Public Sub ComputerSummary_HasCpuMemoryDiskAndOs()
            ComputerInfo.Summary()
            Dim json = JsonSerializer.Serialize(ComputerInfo.Summary(), Camel)
            Using doc = JsonDocument.Parse(json)
                Dim root = doc.RootElement
                Dim usage = root.GetProperty("cpu").GetProperty("usage").GetDouble()
                Assert.That(usage, Iz.InRange(0, 100))
                Assert.That(root.GetProperty("memory").GetProperty("total").GetUInt64(), Iz.GreaterThan(0UL))
                Assert.That(root.GetProperty("os").GetProperty("name").GetString(), Does.StartWith("Windows"))
                Assert.That(root.GetProperty("disk").ValueKind, Iz.EqualTo(JsonValueKind.Object))
                TestContext.Out.WriteLine(json)
            End Using
        End Sub
    End Class
End Namespace
