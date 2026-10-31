Imports System.IO
Imports NUnit.Framework
Imports WhoIsPro.Models
Imports WhoIsPro.Storage

Namespace WhoIsPro.Tests
    ''' <summary>Registro dei dispositivi sull'archivio della rete: prima rilevazione tra una sessione e l'altra, dispositivi nuovi, etichette.</summary>
    Public Class DeviceRegistryTests
        Private testDir As String

        <SetUp>
        Public Sub Setup()
            testDir = Path.Combine(Path.GetTempPath(), "WhoIsProDevices_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(testDir)
        End Sub

        <TearDown>
        Public Sub TearDown()
            Try
                If Directory.Exists(testDir) Then Directory.Delete(testDir, True)
            Catch
            End Try
        End Sub

        Private Shared Function Host(ip As String, mac As String, seen As DateTime) As HostInfo
            Return New HostInfo With {.IPAddress = ip, .MacAddress = mac, .FirstSeen = seen, .LastSeen = seen, .Hostname = "cam-ingresso", .MacVendor = "Hikvision"}
        End Function

        <Test>
        Public Sub Observe_FirstTimeIsNew_ThenKnown()
            Using reg As New DeviceRegistry(testDir)
                Assert.That(reg.Observe(Host("192.168.1.40", "bc:ad:28:40:40:40", DateTime.Now)), Iz.True)
                Assert.That(reg.Observe(Host("192.168.1.40", "BC-AD-28-40-40-40", DateTime.Now)), Iz.False, "stesso MAC scritto in un altro modo")
                Assert.That(reg.Count, Iz.EqualTo(1))
            End Using
        End Sub

        <Test>
        Public Sub FirstSeen_SurvivesAcrossSessions_AndIpChanges()
            Dim first = New DateTime(2026, 9, 1, 8, 30, 0)
            Using reg As New DeviceRegistry(testDir)
                reg.Observe(Host("192.168.1.40", "BC:AD:28:40:40:40", first))
            End Using

            ' un mese dopo, un'altra sessione: il DHCP gli ha dato un altro indirizzo
            Using again As New DeviceRegistry(testDir)
                Dim h = Host("192.168.1.77", "BC:AD:28:40:40:40", DateTime.Now)
                Assert.That(again.Observe(h), Iz.False)
                Assert.That(h.FirstSeen, Iz.EqualTo(first).Within(TimeSpan.FromSeconds(1)), "la prima rilevazione viene dall'archivio")
                Assert.That(again.Find(h).LastIp, Iz.EqualTo("192.168.1.77"))
                Assert.That(again.Count, Iz.EqualTo(1), "stesso dispositivo, non un doppione")
            End Using
        End Sub

        <Test>
        Public Sub HostWithoutMac_IsKeyedByIp()
            Using reg As New DeviceRegistry(testDir)
                Assert.That(DeviceRegistry.KeyOf(Host("8.8.8.8", "", DateTime.Now)), Iz.EqualTo("ip:8.8.8.8"))
                Assert.That(reg.Observe(Host("8.8.8.8", "", DateTime.Now)), Iz.True)
                Assert.That(reg.Observe(Host("8.8.4.4", "", DateTime.Now)), Iz.True)
                Assert.That(reg.Count, Iz.EqualTo(2))
            End Using
        End Sub

        <Test>
        Public Sub Label_FollowsTheMac_InLaterScansAndSessions()
            Using reg As New DeviceRegistry(testDir)
                Dim h = Host("192.168.1.40", "BC:AD:28:40:40:40", DateTime.Now)
                reg.Observe(h)
                reg.SetLabel(h, "  Telecamera ingresso ", "sopra il portone", "cctv", "#dc2626")
                Assert.That(h.CustomName, Iz.EqualTo("Telecamera ingresso"))
                Assert.That(h.Icon, Iz.EqualTo("cctv"))
            End Using

            ' un'altra sessione, un altro indirizzo: nome, note e icona tornano dall'archivio
            Using again As New DeviceRegistry(testDir)
                Dim later = Host("192.168.1.77", "BC:AD:28:40:40:40", DateTime.Now)
                again.Observe(later)
                Assert.That(later.CustomName, Iz.EqualTo("Telecamera ingresso"))
                Assert.That(later.Notes, Iz.EqualTo("sopra il portone"))
                Assert.That(later.Icon, Iz.EqualTo("cctv"))
                Assert.That(later.IconColor, Iz.EqualTo("#dc2626"))
            End Using

            ' una sessione con il nome, aperta su un PC che non lo conosce: l'archivio lo impara
            Using other As New DeviceRegistry(Path.Combine(testDir, "altro-pc"))
                Dim fromFile = Host("192.168.1.50", "00:1C:B3:50:50:50", DateTime.Now)
                fromFile.CustomName = "Centrale allarme"
                other.Observe(fromFile)
                Assert.That(other.Find(fromFile).Name, Iz.EqualTo("Centrale allarme"))
            End Using
        End Sub

        <Test>
        Public Sub Clear_ForgetsEverything_AndLegacyFileIsImportedOnce()
            Using reg As New DeviceRegistry(testDir)
                reg.Observe(Host("192.168.1.40", "BC:AD:28:40:40:40", DateTime.Now))
                reg.Clear()
                Assert.That(reg.Count, Iz.EqualTo(0))
            End Using

            ' il vecchio dispositivi.json della 2.5: entra nell'archivio con nome e icona, poi viene rinominato
            Dim legacy = Path.Combine(testDir, "legacy")
            Directory.CreateDirectory(legacy)
            File.WriteAllText(Path.Combine(legacy, "dispositivi.json"),
                "[{""Key"":""mac:AA:BB:CC:DD:EE:01"",""Mac"":""AA:BB:CC:DD:EE:01"",""LastIp"":""192.168.1.9"",""Hostname"":""nas"",""Vendor"":""Synology"",""DeviceType"":""NAS"",""FirstSeen"":""2026-01-02T10:00:00"",""LastSeen"":""2026-01-03T10:00:00"",""TimesSeen"":4,""Name"":""NAS ufficio"",""Notes"":"""",""Icon"":""nas"",""IconColor"":""#2563eb""}]")
            Using imported As New DeviceRegistry(legacy)
                Assert.That(imported.Count, Iz.EqualTo(1))
                Dim h = Host("192.168.1.9", "AA:BB:CC:DD:EE:01", DateTime.Now)
                Assert.That(imported.Observe(h), Iz.False, "importato dal vecchio registro: non è nuovo")
                Assert.That(h.CustomName, Iz.EqualTo("NAS ufficio"))
                Assert.That(h.Icon, Iz.EqualTo("nas"))
                Assert.That(h.FirstSeen, Iz.EqualTo(New DateTime(2026, 1, 2, 10, 0, 0)).Within(TimeSpan.FromSeconds(1)))
            End Using
            Assert.That(File.Exists(Path.Combine(legacy, "dispositivi.json")), Iz.False)
            Assert.That(File.Exists(Path.Combine(legacy, "dispositivi.json.importato")), Iz.True)

            ' un file illeggibile non rompe niente: archivio vuoto, tutto nuovo
            Dim broken = Path.Combine(testDir, "rotto")
            Directory.CreateDirectory(broken)
            File.WriteAllText(Path.Combine(broken, "dispositivi.json"), "{ non è json")
            Using b As New DeviceRegistry(broken)
                Assert.That(b.Count, Iz.EqualTo(0))
                Assert.That(b.Observe(Host("192.168.1.40", "BC:AD:28:40:40:40", DateTime.Now)), Iz.True)
            End Using
        End Sub
    End Class
End Namespace
