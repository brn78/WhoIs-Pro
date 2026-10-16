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

        ''' <summary>
        ''' I tre registri ufficiali dell'IEEE: blocchi grandi MA-L (24 bit), medi MA-M (28 bit) e piccoli MA-S (36 bit).
        ''' L'IEEE li aggiorna ogni giorno, Wireshark una volta alla settimana: si prova prima l'IEEE.
        ''' </summary>
        Private ReadOnly IeeeRegistries As (Url As String, Registry As String)() = {
            ("https://standards-oui.ieee.org/oui/oui.csv", "MA-L"),
            ("https://standards-oui.ieee.org/oui28/mam.csv", "MA-M"),
            ("https://standards-oui.ieee.org/oui36/oui36.csv", "MA-S")}

        ''' <summary>
        ''' Il produttore di un MAC «privato»: telefoni, tablet e PC recenti usano un indirizzo casuale diverso per ogni rete
        ''' Wi-Fi (bit «amministrato localmente» del primo byte), quindi il produttore non si può sapere.
        ''' </summary>
        Public Const RandomMacLabel As String = "MAC privato (casuale)"

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

        ''' <summary>
        ''' Aggiorna il database: i tre registri ufficiali dell'IEEE (MA-L, MA-M, MA-S), scritti nel formato «manuf»; se
        ''' l'IEEE non risponde, il file manuf di Wireshark. Restituisce quanti prefissi sono caricati.
        ''' </summary>
        Public Async Function UpdateDatabaseOnlineAsync(progress As IProgress(Of String), token As CancellationToken) As Task(Of Integer)
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromSeconds(60)
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WhoIsPro/1.0")

                Dim content As String = Nothing
                Dim source = "IEEE"
                Try
                    content = Await DownloadIeeeAsync(client, progress, token)
                Catch ex As Exception When Not token.IsCancellationRequested AndAlso
                                           (TypeOf ex Is HttpRequestException OrElse TypeOf ex Is TaskCanceledException OrElse TypeOf ex Is InvalidDataException)
                    content = Nothing
                End Try
                If content Is Nothing Then
                    ' L'IEEE a volte rifiuta i download automatici: resta il file di Wireshark
                    source = "Wireshark"
                    If progress IsNot Nothing Then progress.Report("L'IEEE non risponde: download del database di Wireshark (circa 3 MB)...")
                    Dim response = Await client.GetAsync(OfficialManufUrl, HttpCompletionOption.ResponseHeadersRead, token)
                    response.EnsureSuccessStatusCode()
                    content = Await response.Content.ReadAsStringAsync(token)
                End If

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

                If progress IsNot Nothing Then progress.Report($"Database aggiornato da {source}: {loadedCount:N0} prefissi caricati.")
                Return loadedCount
            End Using
        End Function

        ''' <summary>
        ''' Scarica i tre registri dell'IEEE e li scrive nel formato manuf di Wireshark (prefisso, nome breve, nome completo,
        ''' separati da tab; «/28» e «/36» per i blocchi MA-M e MA-S): il file salvato si legge come prima.
        ''' </summary>
        Private Async Function DownloadIeeeAsync(client As HttpClient, progress As IProgress(Of String), token As CancellationToken) As Task(Of String)
            Dim sb As New StringBuilder()
            sb.AppendLine($"# Generato da WhoIs Pro il {DateTime.Now:yyyy-MM-dd HH:mm} dai registri pubblici dell'IEEE (MA-L, MA-M, MA-S),")
            sb.AppendLine("# nel formato manuf di Wireshark: prefisso, nome breve e nome completo separati da tab.")
            For Each reg In IeeeRegistries
                If progress IsNot Nothing Then progress.Report($"Download del registro {reg.Registry} dall'IEEE...")
                Dim response = Await client.GetAsync(reg.Url, HttpCompletionOption.ResponseHeadersRead, token)
                response.EnsureSuccessStatusCode()
                Dim csv = Await response.Content.ReadAsStringAsync(token)
                ' Un registro vuoto o non riconosciuto (una pagina d'errore al posto del CSV) vale come IEEE non raggiungibile
                If AppendIeeeCsv(csv, reg.Registry, sb) = 0 Then Throw New InvalidDataException($"Il registro {reg.Registry} scaricato dall'IEEE è vuoto.")
            Next
            Return sb.ToString()
        End Function

        ''' <summary>Le righe «Registry,Assignment,Organization Name,…» di un CSV dell'IEEE, aggiunte come righe manuf. Quante.</summary>
        Public Function AppendIeeeCsv(csv As String, registry As String, sb As StringBuilder) As Integer
            Dim digits = If(registry = "MA-M", 7, If(registry = "MA-S", 9, 6))
            Dim count = 0
            For Each raw In csv.Split(New Char() {ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries)
                Dim cells = CsvCells(raw)
                If cells.Count < 3 OrElse Not String.Equals(cells(0).Trim(), registry, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim hex = cells(1).Trim().ToUpperInvariant()
                If hex.Length <> digits OrElse Not Regex.IsMatch(hex, "^[0-9A-F]+$") Then Continue For
                Dim name = Regex.Replace(cells(2), "\s+", " ").Trim()
                If name = "" Then Continue For
                ' 0055DA0 (28 bit) → 00:55:DA:00/28; 001BC5000 (36 bit) → 00:1B:C5:00:00/36
                Dim padded = If(hex.Length Mod 2 = 1, hex & "0", hex)
                Dim prefix = String.Join(":", Enumerable.Range(0, padded.Length \ 2).Select(Function(i) padded.Substring(i * 2, 2)))
                If digits = 7 Then prefix &= "/28"
                If digits = 9 Then prefix &= "/36"
                Dim shortName = Regex.Replace(name.Split(" "c)(0), "[^A-Za-z0-9&\-]", "")
                If shortName.Length > 12 Then shortName = shortName.Substring(0, 12)
                If shortName = "" Then shortName = "-"
                sb.Append(prefix).Append(ControlChars.Tab).Append(shortName).Append(ControlChars.Tab).AppendLine(name)
                count += 1
            Next
            Return count
        End Function

        ''' <summary>Le celle di una riga CSV con le virgolette («"Hangzhou Hikvision Digital Technology Co.,Ltd."»).</summary>
        Private Function CsvCells(line As String) As List(Of String)
            Dim cells As New List(Of String)()
            Dim sb As New StringBuilder()
            Dim quoted = False
            Dim i = 0
            While i < line.Length
                Dim c = line(i)
                If quoted Then
                    If c = """"c Then
                        If i + 1 < line.Length AndAlso line(i + 1) = """"c Then
                            sb.Append(""""c)
                            i += 1
                        Else
                            quoted = False
                        End If
                    Else
                        sb.Append(c)
                    End If
                ElseIf c = """"c Then
                    quoted = True
                ElseIf c = ","c Then
                    cells.Add(sb.ToString())
                    sb.Clear()
                Else
                    sb.Append(c)
                End If
                i += 1
            End While
            cells.Add(sb.ToString())
            Return cells
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

        ''' <summary>
        ''' Una riga del formato manuf: prefisso, nome breve e nome completo separati da tab (le colonne si leggono dai tab,
        ''' così un nome breve con uno spazio non sposta il nome completo). Senza tab, colonne separate da spazi.
        ''' </summary>
        Private Sub ParseManufLine(line As String)
            If String.IsNullOrWhiteSpace(line) OrElse line.StartsWith("#"c) Then Return

            Dim rawPrefix As String
            Dim vendorName As String
            If line.Contains(ControlChars.Tab) Then
                Dim cells = line.Split(ControlChars.Tab)
                rawPrefix = cells(0).Trim()
                Dim shortName = If(cells.Length > 1, cells(1).Trim(), "")
                Dim fullName = If(cells.Length > 2, String.Join(" ", cells.Skip(2)).Trim(), "")
                vendorName = If(fullName <> "", fullName, shortName)
            Else
                Dim tokens = line.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                If tokens.Length < 2 Then Return
                rawPrefix = tokens(0)
                vendorName = String.Join(" ", tokens.Skip(If(tokens.Length >= 3, 2, 1)))
            End If
            If vendorName = "" Then Return

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

        ''' <summary>
        ''' MAC «amministrato localmente» (secondo bit del primo byte): di solito un indirizzo casuale per la privacy
        ''' (telefoni, tablet, PC recenti in Wi-Fi), oppure macchine virtuali e contenitori.
        ''' </summary>
        Public Function IsRandomMac(mac As String) As Boolean
            Dim clean = Regex.Replace(If(mac, ""), "[^0-9A-Fa-f]", "")
            If clean.Length < 2 Then Return False
            Return (Convert.ToInt32(clean.Substring(0, 2), 16) And 2) <> 0
        End Function

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

            ' 4. Indirizzi amministrati localmente: i contenitori Docker (02:42), altrimenti un MAC casuale per la privacy
            If cleanMac.StartsWith("0242", StringComparison.Ordinal) Then Return "Docker (contenitore)"
            If IsRandomMac(cleanMac) Then Return RandomMacLabel

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
            ' QEMU/KVM usa un prefisso amministrato localmente: senza questa riga sembrerebbe un MAC casuale
            AddKey("525400", "QEMU / KVM (macchina virtuale)")
            AddKey("080027", "Oracle VirtualBox (macchina virtuale)")
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
