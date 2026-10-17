Imports System.IO
Imports NUnit.Framework
Imports WhoIsPro.Models
Imports WhoIsPro.Network
Imports WhoIsPro.Storage
Imports WhoIsPro.Utils

Namespace WhoIsPro.Tests
    ''' <summary>Prove dei motori in Core\: salvataggio atomico, produttori, classificazione, porte, DNS e connessioni.</summary>
    Public Class ScannerTests
        Private testDir As String

        <SetUp>
        Public Sub Setup()
            testDir = Path.Combine(Path.GetTempPath(), "WhoIsProTests_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(testDir)
        End Sub

        <TearDown>
        Public Sub TearDown()
            Try
                If Directory.Exists(testDir) Then
                    Directory.Delete(testDir, True)
                End If
            Catch
            End Try
        End Sub

        <Test>
        Public Sub AtomicSave_ShouldSucceed_AndVerifySha256Checksum()
            Dim session As New ScanSession With {
                .SessionTitle = "Test Session",
                .Notes = "Test session note"
            }
            session.Hosts.Add(New HostInfo With {
                .IPAddress = "192.168.1.10",
                .Hostname = "homeassistant.local",
                .MacAddress = "24:0A:C4:11:22:33",
                .MacVendor = "Espressif Inc.",
                .DeviceType = "Hub Domotico (Home Assistant)",
                .Status = "Online"
            })

            Dim targetPath = Path.Combine(testDir, "test.whoispro")
            Dim result = AtomicFileManager.SaveSessionAtomic(session, targetPath)

            Assert.That(result.IsSuccess, [Is].True, "Salvataggio atomico deve avere esito positivo")
            Assert.That(File.Exists(targetPath), [Is].True, "Il file di destinazione deve esistere")
            Assert.That(result.ChecksumSHA256, [Is].Not.Empty, "L'hash SHA-256 deve essere calcolato")

            Dim restoredFromBak As Boolean = False
            Dim loaded = AtomicFileManager.LoadSessionSafe(targetPath, restoredFromBak)

            Assert.That(restoredFromBak, [Is].False, "Non deve essere caricato da backup se il file è integro")
            Assert.That(loaded.Hosts.Count, [Is].EqualTo(1))
            Assert.That(loaded.Hosts(0).IPAddress, [Is].EqualTo("192.168.1.10"))
            Assert.That(loaded.Hosts(0).DeviceType, [Is].EqualTo("Hub Domotico (Home Assistant)"))
        End Sub

        <Test>
        Public Sub AtomicSave_WhenOverwriting_ShouldCreateBackupFile()
            Dim session As New ScanSession With {.SessionTitle = "Original"}
            Dim targetPath = Path.Combine(testDir, "overwrite_test.whoispro")

            Dim res1 = AtomicFileManager.SaveSessionAtomic(session, targetPath)
            Assert.That(res1.IsSuccess, [Is].True)

            session.SessionTitle = "Updated Version"
            Dim res2 = AtomicFileManager.SaveSessionAtomic(session, targetPath)
            Assert.That(res2.IsSuccess, [Is].True)

            Dim bakPath = targetPath & ".bak"
            Assert.That(File.Exists(bakPath), [Is].True, "Il file di backup .bak deve esistere dopo la sovrascrittura")

            Dim bakContent = File.ReadAllText(bakPath)
            Assert.That(bakContent.Contains("Original"), [Is].True, "Il file di backup deve contenere la versione precedente")
        End Sub

        <Test>
        Public Sub LoadSafe_WhenFileCorrupted_ShouldRecoverFromBackup()
            Dim session As New ScanSession With {.SessionTitle = "Original Good Session"}
            session.Hosts.Add(New HostInfo With {.IPAddress = "10.0.0.1"})

            Dim targetPath = Path.Combine(testDir, "corrupt_test.whoispro")
            AtomicFileManager.SaveSessionAtomic(session, targetPath)

            session.SessionTitle = "Updated Session"
            AtomicFileManager.SaveSessionAtomic(session, targetPath)

            ' Danneggiamo intenzionalmente il file principale (troncamento / caratteri invalidi)
            File.WriteAllText(targetPath, "{ corrupt json ... invalid content !!!")

            Dim restoredFromBak As Boolean = False
            Dim loaded = AtomicFileManager.LoadSessionSafe(targetPath, restoredFromBak)

            Assert.That(restoredFromBak, [Is].True, "Deve segnalare il ripristino automatico da backup")
            Assert.That(loaded, [Is].Not.Null)
            Assert.That(loaded.Hosts.Count, [Is].EqualTo(1))
            Assert.That(loaded.Hosts(0).IPAddress, [Is].EqualTo("10.0.0.1"))
        End Sub

        <Test>
        Public Sub Session_TracerouteHopsKeepTheirTarget()
            ' Un file della versione 1 non ha il campo Target: resta vuoto, i salti nuovi lo hanno
            Dim session As New ScanSession()
            session.TracerouteHops.Add(New TracerouteHop With {.Target = "esempio.it", .HopNumber = 1, .IPAddress = "192.168.1.1", .Status = "OK"})
            Dim targetPath = Path.Combine(testDir, "trace.whoispro")
            AtomicFileManager.SaveSessionAtomic(session, targetPath)
            Dim restored As Boolean
            Dim loaded = AtomicFileManager.LoadSessionSafe(targetPath, restored)
            Assert.That(loaded.TracerouteHops(0).Target, [Is].EqualTo("esempio.it"))

            File.WriteAllText(targetPath, "{""Hosts"":[],""Ports"":[],""WhoisEntries"":[],""DnsRecords"":[],""TracerouteHops"":[{""HopNumber"":1,""IPAddress"":""10.0.0.1""}]}")
            loaded = AtomicFileManager.LoadSessionSafe(targetPath, restored)
            Assert.That(loaded.TracerouteHops(0).Target, [Is].EqualTo(""))
        End Sub

        <Test>
        Public Sub MacVendorLookup_ShouldIdentifyKnownVendors()
            Assert.That(MacVendorLookup.GetVendor("24:0A:C4:AA:BB:CC"), Does.Contain("Espressif"))
            Assert.That(MacVendorLookup.GetVendor("B8:27:EB:11:22:33"), Does.Contain("Raspberry Pi"))
            Assert.That(MacVendorLookup.GetVendor("40:16:3B:44:55:66"), Does.Contain("Samsung"))
            Assert.That(MacVendorLookup.GetVendor("F0:18:98:77:88:99"), Does.Contain("Apple"))
            Assert.That(MacVendorLookup.GetVendor("34:31:C4:00:11:22"), Does.Contain("FRITZ!Box"))
        End Sub

        <Test>
        Public Sub MacVendorLookup_ShouldRecognizePrivateMacs()
            ' Bit «amministrato localmente»: indirizzo casuale per la privacy
            Assert.That(MacVendorLookup.IsRandomMac("7A:11:22:33:44:55"), [Is].True)
            Assert.That(MacVendorLookup.IsRandomMac("28:57:BE:11:22:33"), [Is].False)
            Assert.That(MacVendorLookup.GetVendor("7A:11:22:33:44:55"), [Is].EqualTo(MacVendorLookup.RandomMacLabel))
            ' Stesso bit, ma non sono telefoni
            Assert.That(MacVendorLookup.GetVendor("52:54:00:12:34:56"), Does.Contain("QEMU"))
            Assert.That(MacVendorLookup.GetVendor("02:42:AC:11:00:02"), Does.Contain("Docker"))
            Dim c = DeviceClassifier.Classify(MacVendorLookup.RandomMacLabel, "", Nothing, 64, "7A:11:22:33:44:55")
            Assert.That(c.Category, [Is].EqualTo("Smartphone / Tablet"))
        End Sub

        <Test>
        Public Sub MacVendorLookup_ShouldConvertIeeeRegistries()
            Dim sb As New System.Text.StringBuilder()
            Dim csv = "Registry,Assignment,Organization Name,Organization Address" & vbCrLf &
                      "MA-L,2857BE,""Hangzhou Hikvision Digital Technology Co.,Ltd."",""Hangzhou, CN""" & vbCrLf &
                      "MA-M,0055DA0,Shinko Technos co.,ltd.,Osaka JP" & vbCrLf
            Assert.That(MacVendorLookup.AppendIeeeCsv(csv, "MA-L", sb), [Is].EqualTo(1))
            Assert.That(MacVendorLookup.AppendIeeeCsv(csv, "MA-M", sb), [Is].EqualTo(1))
            Dim lines = sb.ToString().Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Assert.That(lines(0), [Is].EqualTo("28:57:BE" & vbTab & "Hangzhou" & vbTab & "Hangzhou Hikvision Digital Technology Co.,Ltd."))
            Assert.That(lines(1), Does.StartWith("00:55:DA:00/28" & vbTab))
        End Sub

        <Test>
        Public Sub DeviceClassifier_ShouldRecognizeCamerasAlarmsAndAutomation()
            Assert.That(DeviceClassifier.Classify("Hangzhou Hikvision Digital Technology Co.,Ltd.", "", Nothing, 64, "").DeviceType, Does.Contain("Hikvision"))
            Dim alarm = DeviceClassifier.Classify("INIM ELECTRONICS S.R.L.", "", Nothing, 64, "")
            Assert.That(alarm.Category, [Is].EqualTo("Centrale d'allarme"))
            Assert.That(alarm.DeviceType, Does.Contain("Inim"))
            ' A parole intere: Minimax non è una centrale Inim
            Assert.That(DeviceClassifier.Classify("Minimax GmbH & Co KG", "", Nothing, 64, "").Category, [Is].Not.EqualTo("Centrale d'allarme"))
            Assert.That(DeviceClassifier.Classify("Unknown", "plc-linea1", New Integer() {502}, 64, "").DeviceType, Does.Contain("Modbus"))
            Assert.That(DeviceClassifier.Classify("Kyocera Document Solutions Inc.", "", Nothing, 64, "").Category, [Is].EqualTo("Stampante"))
            ' Google (Nest, Chromecast) e i telefoni Android: dal produttore, non più «Linux / Unix» dal TTL
            Assert.That(DeviceClassifier.Classify("Google, Inc.", "", Nothing, 64, "").DeviceType, Does.Contain("Google"))
            Assert.That(DeviceClassifier.Classify("Huawei Device Co., Ltd.", "", Nothing, 64, "").Category, [Is].EqualTo("Smartphone / Tablet"))
        End Sub

        <Test>
        Public Sub DeviceClassifier_ShouldClassifyHomeAssistant()
            Dim c1 = DeviceClassifier.Classify("Espressif Inc.", "homeassistant", Nothing, 64, "")
            Assert.That(c1.Category, [Is].EqualTo("Domotica / IoT"))
            Assert.That(c1.DeviceType, Does.Contain("Home Assistant"))

            Dim c2 = DeviceClassifier.Classify("Unknown", "my-host", New Integer() {8123}, 64, "")
            Assert.That(c2.Category, [Is].EqualTo("Domotica / IoT"))
            Assert.That(c2.DeviceType, Does.Contain("Home Assistant"))
        End Sub

        <Test>
        Public Sub DeviceClassifier_ShouldClassifySamsungTv()
            Dim c1 = DeviceClassifier.Classify("Samsung Electronics (Smart TV)", "tv-living", Nothing, 64, "")
            Assert.That(c1.Category, [Is].EqualTo("Smart TV"))
            Assert.That(c1.DeviceType, Does.Contain("Samsung"))

            Dim c2 = DeviceClassifier.Classify("Samsung Electronics", "samsung-tizen", New Integer() {8001}, 64, "")
            Assert.That(c2.Category, [Is].EqualTo("Smart TV"))
            Assert.That(c2.DeviceType, Does.Contain("Tizen"))
        End Sub

        <Test>
        Public Sub DeviceClassifier_ShouldClassifySmartphones()
            Dim cApple = DeviceClassifier.Classify("Apple, Inc.", "iPhone-di-Marco", Nothing, 64, "")
            Assert.That(cApple.Category, [Is].EqualTo("Smartphone / Tablet"))
            Assert.That(cApple.DeviceType, Does.Contain("iPhone"))

            Dim cAndroid = DeviceClassifier.Classify("Xiaomi Communications", "redmi-note", Nothing, 64, "")
            Assert.That(cAndroid.Category, [Is].EqualTo("Smartphone / Tablet"))
            Assert.That(cAndroid.DeviceType, Does.Contain("Android"))
        End Sub

        <Test>
        Public Sub NetworkUtils_ParseCidr_ShouldGenerateValidIpRange()
            Dim ips = NetworkUtils.ParseTargets("192.168.1.0/30")
            ' /30 host count: 192.168.1.1 e 192.168.1.2
            Assert.That(ips.Count, [Is].EqualTo(2))
            Assert.That(ips(0).ToString(), [Is].EqualTo("192.168.1.1"))
            Assert.That(ips(1).ToString(), [Is].EqualTo("192.168.1.2"))
        End Sub

        <Test>
        Public Sub PortDictionary_ParsePorts_ShouldReadSinglesAndRanges()
            Dim ports = PortDictionary.ParsePorts("443, 22;80 8000-8003" & vbCrLf & "22 0 70000 abc 10-8")
            Assert.That(ports, [Is].EqualTo(New Integer() {22, 80, 443, 8000, 8001, 8002, 8003}))
            Assert.That(PortDictionary.ParsePorts(""), [Is].Empty)
            Assert.That(PortDictionary.ParsePorts("1-65535").Count, [Is].EqualTo(65535))
        End Sub

        <Test>
        Public Sub PortDictionary_Presets_ShouldHaveTextForTheForm()
            Dim presets = PortDictionary.GetPresets()
            Assert.That(presets.Select(Function(p) p.Key), Does.Contain("TOP20").And.Contain("IOT").And.Contain("ALL"))
            Dim top20 = presets.First(Function(p) p.Key = "TOP20")
            Assert.That(PortDictionary.ParsePorts(top20.Text), [Is].EqualTo(top20.Ports))
            Assert.That(presets.First(Function(p) p.Key = "ALL").Text, [Is].EqualTo("1-65535"))
        End Sub

        <Test>
        Public Sub SessionExporter_ShouldExportHtmlCsvAndTxt()
            Dim session As New ScanSession With {
                .SessionTitle = "Export Test Session",
                .ChecksumSHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
            }
            session.Hosts.Add(New HostInfo With {
                .IPAddress = "192.168.1.50",
                .Hostname = "Samsung-SmartTV",
                .MacAddress = "40:16:3B:11:22:33",
                .MacVendor = "Samsung Electronics (Smart TV)",
                .DeviceType = "Smart TV (Samsung)",
                .Status = "Online",
                .ResponseTimeMs = 5
            })

            Dim htmlFile = Path.Combine(testDir, "report.html")
            Dim csvFile = Path.Combine(testDir, "report.csv")
            Dim txtFile = Path.Combine(testDir, "report.txt")

            SessionExporter.ExportToHtml(session, htmlFile)
            SessionExporter.ExportToCsv(session, csvFile)
            SessionExporter.ExportToTxt(session, txtFile)

            Assert.That(File.Exists(htmlFile), [Is].True)
            Assert.That(File.Exists(csvFile), [Is].True)
            Assert.That(File.Exists(txtFile), [Is].True)

            Dim htmlContent = File.ReadAllText(htmlFile)
            Assert.That(htmlContent.Contains("WhoIs Pro - Report"), [Is].True)
            Assert.That(htmlContent.Contains("Samsung"), [Is].True)
        End Sub

        <Test>
        Public Sub NetstatInspector_ShouldRetrieveActiveConnections()
            Dim conns = NetstatInspector.GetActiveConnections()
            Assert.That(conns, [Is].Not.Null)
            Assert.That(conns.Count, [Is].GreaterThan(0), "Ci deve essere almeno una connessione o porta in ascolto")
            Assert.That(conns.Any(Function(c) c.ProcessId > 0), [Is].True, "Almeno una connessione deve avere un PID associato")
        End Sub

        <Test>
        Public Sub PortDictionary_ShouldReturnExpectedServiceNames()
            Assert.That(PortDictionary.GetServiceName(80), [Is].EqualTo("HTTP"))
            Assert.That(PortDictionary.GetServiceName(443), [Is].EqualTo("HTTPS"))
            Assert.That(PortDictionary.GetServiceName(8123), [Is].EqualTo("Home-Assistant"))
            Assert.That(PortDictionary.GetServiceName(22), [Is].EqualTo("SSH"))
            Assert.That(PortDictionary.GetServiceName(3389), [Is].EqualTo("RDP"))
            Assert.That(PortDictionary.GetServiceName(8001), [Is].EqualTo("Samsung-TV"))
        End Sub

        <Test>
        Public Async Function PortScanner_Loopback_ShouldExecuteAsync() As Task
            Dim scanner As New PortScanner With {.TimeoutMs = 500, .Concurrency = 5}
            Dim ports As New List(Of Integer) From {80, 135, 445}
            Dim cts As New System.Threading.CancellationTokenSource(3000)
            Dim results = Await scanner.ScanPortsAsync("127.0.0.1", ports, True, False, Nothing, cts.Token)
            Assert.That(results, [Is].Not.Null)
        End Function

        <Test>
        Public Sub MacVendorLookup_ShouldLoadOfficialDatabaseEntries()
            ' Se manuf.txt è presente nella cartella principale, devono essere caricati decine di migliaia di vendor
            Dim count = MacVendorLookup.TotalVendorsLoaded
            Assert.That(count, [Is].GreaterThan(20), "Il database deve contenere produttori")
        End Sub

        <Test>
        Public Sub MatchesHostFilter_SearchFiltering_ShouldMatchCorrectly()
            Dim host As New HostInfo With {
                .IPAddress = "192.168.1.42",
                .Hostname = "Samsung-SmartTV-LivingRoom",
                .MacAddress = "40:16:3B:AA:BB:CC",
                .MacVendor = "Samsung Electronics Co.,Ltd",
                .DeviceType = "Smart TV (Samsung Tizen)",
                .OSGuess = "Linux / Android",
                .ResponseTimeMs = 3
            }

            ' Ricerca vuota o spazi
            Assert.That(NetworkUtils.MatchesHostFilter(host, ""), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "   "), [Is].True)

            ' Ricerca per IP esatto o parziale
            Assert.That(NetworkUtils.MatchesHostFilter(host, "192.168.1.42"), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "1.42"), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "10.0.0.1"), [Is].False)

            ' Ricerca per Hostname case-insensitive
            Assert.That(NetworkUtils.MatchesHostFilter(host, "smarttv"), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "LIVINGROOM"), [Is].True)

            ' Ricerca per MAC con o senza due punti, maiuscolo/minuscolo
            Assert.That(NetworkUtils.MatchesHostFilter(host, "40:16:3b"), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "40163b"), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "aabbcc"), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "ff:ee:dd"), [Is].False)

            ' Ricerca per Vendor
            Assert.That(NetworkUtils.MatchesHostFilter(host, "Samsung"), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "Apple"), [Is].False)

            ' Ricerca per Tipologia dispositivo
            Assert.That(NetworkUtils.MatchesHostFilter(host, "Smart TV"), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "Tizen"), [Is].True)

            ' Ricerca Multi-token (logica AND)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "192.168 Samsung"), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "40163b Smart TV 42"), [Is].True)
            Assert.That(NetworkUtils.MatchesHostFilter(host, "192.168 Apple"), [Is].False, "Se uno dei token non corrisponde deve restituire False")
        End Sub
    End Class
End Namespace
