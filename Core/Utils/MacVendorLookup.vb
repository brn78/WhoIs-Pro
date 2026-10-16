Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading

Namespace Utils
    Public Module MacVendorLookup
        Private ReadOnly VendorDb As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly LockObj As New Object()
        Private Const OfficialManufUrl As String = "https://www.wireshark.org/download/automated/data/manuf"

        Public ReadOnly Property TotalVendorsLoaded As Integer
            Get
                SyncLock LockObj
                    Return VendorDb.Count
                End SyncLock
            End Get
        End Property

        ''' <summary>File manuf.txt in uso, oppure "" se ci sono solo i produttori incorporati.</summary>
        Public ReadOnly Property SourceFile As String
            Get
                SyncLock LockObj
                    Return _sourceFile
                End SyncLock
            End Get
        End Property

        Private _sourceFile As String = ""

        Sub New()
            LoadInitialDatabase()
        End Sub

        Public Sub LoadInitialDatabase()
            SyncLock LockObj
                VendorDb.Clear()
                _sourceFile = ""
                LoadBuiltinFallbacks()

                ' Il file aggiornato online (AppData) vince su quello distribuito con il programma
                Dim candidatePaths = New String() {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhoIsPro", "manuf.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "manuf.txt"),
                    "manuf.txt"
                }

                For Each p In candidatePaths
                    If File.Exists(p) Then
                        Try
                            ParseManufFile(p)
                            _sourceFile = Path.GetFullPath(p)
                            Exit For
                        Catch
                        End Try
                    End If
                Next
            End SyncLock
        End Sub

        Public Async Function UpdateDatabaseOnlineAsync(progress As IProgress(Of String), token As CancellationToken) As Task(Of Integer)
            If progress IsNot Nothing Then progress.Report("Connessione al server ufficiale Wireshark OUI...")

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromSeconds(25)
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WhoIsPro/1.0")

                Dim response = Await client.GetAsync(OfficialManufUrl, HttpCompletionOption.ResponseHeadersRead, token)
                response.EnsureSuccessStatusCode()

                If progress IsNot Nothing Then progress.Report("Download database OUI (circa 3 MB)...")
                Dim content = Await response.Content.ReadAsStringAsync(token)

                ' Salvataggio locale su AppData e directory corrente
                Dim appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhoIsPro")
                If Not Directory.Exists(appDataDir) Then Directory.CreateDirectory(appDataDir)
                Dim appDataFile = Path.Combine(appDataDir, "manuf.txt")
                Dim baseDirFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "manuf.txt")

                File.WriteAllText(appDataFile, content, Encoding.UTF8)
                Try
                    File.WriteAllText(baseDirFile, content, Encoding.UTF8)
                Catch
                End Try

                If progress IsNot Nothing Then progress.Report("Elaborazione e indicizzazione prefissi MAC...")
                Dim loadedCount As Integer
                SyncLock LockObj
                    VendorDb.Clear()
                    LoadBuiltinFallbacks()
                    loadedCount = ParseManufContent(content)
                    _sourceFile = appDataFile
                End SyncLock

                If progress IsNot Nothing Then progress.Report($"Database aggiornato con successo! {loadedCount:N0} produttori caricati.")
                Return loadedCount
            End Using
        End Function

        Private Sub ParseManufFile(filePath As String)
            Using reader As New StreamReader(filePath, Encoding.UTF8)
                Dim line As String
                While True
                    line = reader.ReadLine()
                    If line Is Nothing Then Exit While
                    ParseManufLine(line)
                End While
            End Using
        End Sub

        Private Function ParseManufContent(content As String) As Integer
            Dim lines = content.Split(New Char() {ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries)
            For Each line In lines
                ParseManufLine(line)
            Next
            Return VendorDb.Count
        End Function

        Private Sub ParseManufLine(line As String)
            If String.IsNullOrWhiteSpace(line) OrElse line.StartsWith("#"c) Then Return

            Dim tokens = line.Split(New Char() {ControlChars.Tab, " "c}, StringSplitOptions.RemoveEmptyEntries)
            If tokens.Length < 2 Then Return

            Dim rawPrefix = tokens(0).Trim()
            Dim shortName = tokens(1).Trim()
            Dim fullName As String = ""

            If tokens.Length >= 3 Then
                Dim firstToken = tokens(0)
                Dim secondToken = tokens(1)
                Dim idxFirst = line.IndexOf(firstToken)
                Dim idxSecond = line.IndexOf(secondToken, idxFirst + firstToken.Length)
                Dim idxThird = idxSecond + secondToken.Length
                If idxThird < line.Length Then
                    fullName = line.Substring(idxThird).Trim()
                End If
            End If

            Dim vendorName = If(Not String.IsNullOrEmpty(fullName), fullName, shortName)

            Dim cleanHex = Regex.Replace(rawPrefix.Split("/"c)(0), "[^0-9A-Fa-f]", "").ToUpperInvariant()
            If rawPrefix.Contains("/28") AndAlso cleanHex.Length >= 7 Then
                AddKey(cleanHex.Substring(0, 7), vendorName)
            ElseIf rawPrefix.Contains("/36") AndAlso cleanHex.Length >= 9 Then
                AddKey(cleanHex.Substring(0, 9), vendorName)
            ElseIf cleanHex.Length >= 6 Then
                AddKey(cleanHex.Substring(0, 6), vendorName)
            End If
        End Sub

        Private Sub AddKey(key As String, name As String)
            If Not VendorDb.ContainsKey(key) Then
                VendorDb.Add(key, name)
            End If
        End Sub

        Public Function GetVendor(mac As String) As String
            If String.IsNullOrWhiteSpace(mac) Then Return "Sconosciuto"
            Dim cleanMac As String = Regex.Replace(mac.Trim(), "[^0-9A-Fa-f]", "").ToUpperInvariant()

            SyncLock LockObj
                ' 1. Prova prefisso a 36-bit (9 caratteri hex)
                If cleanMac.Length >= 9 Then
                    Dim p36 = cleanMac.Substring(0, 9)
                    If VendorDb.ContainsKey(p36) Then Return VendorDb(p36)
                End If

                ' 2. Prova prefisso a 28-bit (7 caratteri hex)
                If cleanMac.Length >= 7 Then
                    Dim p28 = cleanMac.Substring(0, 7)
                    If VendorDb.ContainsKey(p28) Then Return VendorDb(p28)
                End If

                ' 3. Prova prefisso standard a 24-bit OUI (6 caratteri hex)
                If cleanMac.Length >= 6 Then
                    Dim p24 = cleanMac.Substring(0, 6)
                    If VendorDb.ContainsKey(p24) Then Return VendorDb(p24)
                End If
            End SyncLock

            Return "Produttore non catalogato"
        End Function

        Public Function FormatMac(macBytes As Byte()) As String
            If macBytes Is Nothing OrElse macBytes.Length = 0 Then Return ""
            Return String.Join(":", macBytes.Select(Function(b) b.ToString("X2")))
        End Function

        Private Sub LoadBuiltinFallbacks()
            ' Fallback immediati per vendor critici
            AddKey("000C29", "VMware, Inc.")
            AddKey("005056", "VMware, Inc.")
            AddKey("00155D", "Microsoft Corp. (Hyper-V)")
            AddKey("B827EB", "Raspberry Pi Foundation")
            AddKey("DCA632", "Raspberry Pi Trading Ltd")
            AddKey("240AC4", "Espressif Inc. (ESP32/ESP8266)")
            AddKey("30AEA4", "Espressif Inc. (ESP32/ESP8266)")
            AddKey("A4CF12", "Espressif Inc. (ESP32/ESP8266)")
            AddKey("ECFABC", "Tuya Smart Inc. (IoT)")
            AddKey("E8DB84", "Shelly / Allterco Robotics")
            AddKey("600194", "Sonoff / ITEAD Intelligent Systems")
            AddKey("001788", "Philips Hue (Signify Netherlands)")
            AddKey("BCDDCD", "Samsung Electronics Co.,Ltd")
            AddKey("40163B", "Samsung Electronics (Smart TV)")
            AddKey("50EC50", "LG Electronics (Smart TV/WebOS)")
            AddKey("F01898", "Apple, Inc. (iPhone/iPad/Mac)")
            AddKey("FC65DE", "Amazon Technologies (Echo / Alexa)")
            AddKey("001A11", "Google LLC (Chromecast)")
            AddKey("3431C4", "AVM Audiovisuelles (FRITZ!Box)")
            AddKey("7483C2", "Ubiquiti Networks (UniFi)")
            AddKey("CC50E3", "TP-Link Corporation")
            AddKey("001132", "Synology Inc. (DiskStation NAS)")
            AddKey("00089B", "QNAP Systems, Inc. (NAS)")
        End Sub
    End Module
End Namespace
