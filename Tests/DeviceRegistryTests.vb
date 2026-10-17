Imports System.IO
Imports NUnit.Framework
Imports WhoIsPro.Models
Imports WhoIsPro.Storage

Namespace WhoIsPro.Tests
    ''' <summary>Registro dei dispositivi conosciuti: prima rilevazione tra una sessione e l'altra e dispositivi nuovi.</summary>
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
            Dim reg As New DeviceRegistry(testDir)
            Assert.That(reg.Observe(Host("192.168.1.40", "bc:ad:28:40:40:40", DateTime.Now)), Iz.True)
            Assert.That(reg.Observe(Host("192.168.1.40", "BC-AD-28-40-40-40", DateTime.Now)), Iz.False, "stesso MAC scritto in un altro modo")
            Assert.That(reg.Count, Iz.EqualTo(1))
        End Sub

        <Test>
        Public Sub FirstSeen_SurvivesAcrossSessions_AndIpChanges()
            Dim first = New DateTime(2026, 9, 1, 8, 30, 0)
            Dim reg As New DeviceRegistry(testDir)
            reg.Observe(Host("192.168.1.40", "BC:AD:28:40:40:40", first))
            reg.Save()

            ' un mese dopo, un'altra sessione: il DHCP gli ha dato un altro indirizzo
            Dim again As New DeviceRegistry(testDir)
            Dim h = Host("192.168.1.77", "BC:AD:28:40:40:40", DateTime.Now)
            Assert.That(again.Observe(h), Iz.False)
            Assert.That(h.FirstSeen, Iz.EqualTo(first), "la prima rilevazione viene dal registro")
            Assert.That(again.Find(h).LastIp, Iz.EqualTo("192.168.1.77"))
            Assert.That(again.Find(h).TimesSeen, Iz.EqualTo(2))
        End Sub

        <Test>
        Public Sub HostWithoutMac_IsKeyedByIp()
            Dim reg As New DeviceRegistry(testDir)
            Assert.That(DeviceRegistry.KeyOf(Host("8.8.8.8", "", DateTime.Now)), Iz.EqualTo("ip:8.8.8.8"))
            Assert.That(reg.Observe(Host("8.8.8.8", "", DateTime.Now)), Iz.True)
            Assert.That(reg.Observe(Host("8.8.4.4", "", DateTime.Now)), Iz.True)
            Assert.That(reg.Count, Iz.EqualTo(2))
        End Sub

        <Test>
        Public Sub Label_FollowsTheMac_InLaterScansAndSessions()
            Dim reg As New DeviceRegistry(testDir)
            Dim h = Host("192.168.1.40", "BC:AD:28:40:40:40", DateTime.Now)
            reg.Observe(h)
            reg.SetLabel(h, "  Telecamera ingresso ", "sopra il portone")
            Assert.That(h.CustomName, Iz.EqualTo("Telecamera ingresso"))

            ' un'altra sessione, un altro indirizzo: nome e note tornano dal registro
            Dim later = Host("192.168.1.77", "BC:AD:28:40:40:40", DateTime.Now)
            Dim again As New DeviceRegistry(testDir)
            again.Observe(later)
            Assert.That(later.CustomName, Iz.EqualTo("Telecamera ingresso"))
            Assert.That(later.Notes, Iz.EqualTo("sopra il portone"))

            ' una sessione con il nome, aperta su un PC che non lo conosce: il registro lo impara
            Dim other As New DeviceRegistry(Path.Combine(testDir, "altro-pc"))
            Dim fromFile = Host("192.168.1.50", "00:1C:B3:50:50:50", DateTime.Now)
            fromFile.CustomName = "Centrale allarme"
            other.Observe(fromFile)
            Assert.That(other.Find(fromFile).Name, Iz.EqualTo("Centrale allarme"))
        End Sub

        <Test>
        Public Sub Clear_ForgetsEverything_AndCorruptFileStartsEmpty()
            Dim reg As New DeviceRegistry(testDir)
            reg.Observe(Host("192.168.1.40", "BC:AD:28:40:40:40", DateTime.Now))
            reg.Save()
            reg.Clear()
            Assert.That(New DeviceRegistry(testDir).Count, Iz.EqualTo(0))

            File.WriteAllText(Path.Combine(testDir, "dispositivi.json"), "{ non è json")
            Dim broken As New DeviceRegistry(testDir)
            Assert.That(broken.Count, Iz.EqualTo(0))
            Assert.That(broken.Observe(Host("192.168.1.40", "BC:AD:28:40:40:40", DateTime.Now)), Iz.True)
        End Sub
    End Class
End Namespace
