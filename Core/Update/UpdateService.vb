Option Strict On

Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Microsoft.Win32

Namespace Update

    ''' <summary>
    ''' Aggiornamento al volo dalla repo pubblica su GitHub. Una volta al giorno (o con «Controlla ora») chiede l'ultima
    ''' versione pubblicata e la confronta con quella in esecuzione; «Scarica e installa» scarica il setup della release,
    ''' ne controlla dimensione e impronta SHA-256 pubblicate da GitHub, prodotto e versione, poi lo avvia in modo
    ''' silenzioso: il setup chiude il programma, sostituisce i file e lo riapre. La richiesta a GitHub non porta nessun
    ''' dato; l'ultimo esito resta in release-check.json nella cartella dei dati, così dopo un riavvio l'avviso c'è subito.
    ''' </summary>
    Public NotInheritable Class UpdateService
        Implements IDisposable

        Public Const Repository As String = "brn78/WhoIs-Pro"
        Public Const ReleasesPage As String = "https://github.com/" & Repository & "/releases/latest"
        ''' <summary>I collegamenti mostrati devono portare alla repo pubblica, qualunque cosa dica la risposta.</summary>
        Public Const TrustedLinkPrefix As String = "https://github.com/" & Repository & "/"
        Private Const LatestReleaseUrl As String = "https://api.github.com/repos/" & Repository & "/releases/latest"
        Private Const SetupProductName As String = "WhoIs Pro"
        Private Const SetupDescription As String = "Installazione di WhoIs Pro"
        Private Const RegistryPath As String = "Software\BrunoLeonardi\WhoIsPro"
        Private Const MaxNotesLength As Integer = 6000
        Private Const MaxSetupBytes As Long = 256L * 1024 * 1024
        Private Const TimeoutSeconds As Integer = 20

        Private Shared ReadOnly CheckInterval As TimeSpan = TimeSpan.FromHours(24)
        Private Shared ReadOnly RetryInterval As TimeSpan = TimeSpan.FromHours(6)
        Private Shared ReadOnly ManualInterval As TimeSpan = TimeSpan.FromSeconds(15)
        Private Shared ReadOnly DownloadTimeout As TimeSpan = TimeSpan.FromMinutes(20)
        Private Shared ReadOnly SetupNamePattern As New Regex("^WhoIsPro-[\d.]+-Setup\.exe$", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
        Private Shared ReadOnly Sha256Pattern As New Regex("^sha256:([0-9a-f]{64})$", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)

        Private ReadOnly _folder As String
        Private ReadOnly _http As HttpClient
        Private ReadOnly _gate As New SemaphoreSlim(1, 1)
        Private _record As ReleaseRecord
        Private _checking As Boolean
        Private _download As DownloadState

        ''' <param name="dataFolder">Cartella dei dati del programma (%LOCALAPPDATA%\WhoIsPro).</param>
        Public Sub New(dataFolder As String)
            _folder = dataFolder
            ' GitHub rimanda gli allegati delle release a un altro indirizzo: si seguono i reindirizzamenti
            Dim handler As New SocketsHttpHandler With {.AllowAutoRedirect = True, .MaxAutomaticRedirections = 5, .ConnectTimeout = TimeSpan.FromSeconds(TimeoutSeconds)}
            _http = New HttpClient(handler) With {.Timeout = Timeout.InfiniteTimeSpan}
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"WhoIsPro/{CurrentVersion}")
            _record = If(Load(), New ReleaseRecord())
            CleanUp()
        End Sub

        ''' <summary>Versione in esecuzione (major.minor.build).</summary>
        Public Shared ReadOnly Property CurrentVersion As Version
            Get
                Dim v = GetType(UpdateService).Assembly.GetName().Version
                Return If(v Is Nothing, New Version(1, 0, 0), New Version(v.Major, v.Minor, Math.Max(0, v.Build)))
            End Get
        End Property

        Private ReadOnly Property RecordPath As String
            Get
                Return Path.Combine(_folder, "release-check.json")
            End Get
        End Property

        Private ReadOnly Property SetupFolder As String
            Get
                Return Path.Combine(_folder, "updates")
            End Get
        End Property

        ''' <summary>Il programma è stato installato con il setup (cartella registrata = cartella in esecuzione): si può aggiornare da qui.</summary>
        Public Shared Function InstalledBySetup() As Boolean
            For Each root In {Registry.CurrentUser, Registry.LocalMachine}
                Try
                    Using key = root.OpenSubKey(RegistryPath)
                        Dim installDir = TryCast(key?.GetValue("InstallDir"), String)
                        If Not String.IsNullOrWhiteSpace(installDir) AndAlso
                           String.Equals(TrimSlash(installDir), TrimSlash(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase) Then Return True
                    End Using
                Catch
                End Try
            Next
            Return False
        End Function

        ''' <summary>Un controllo al giorno; dopo uno non riuscito si riprova prima.</summary>
        Public ReadOnly Property IsDue As Boolean
            Get
                Dim record = Volatile.Read(_record)
                If Not record.AttemptedAt.HasValue Then Return True
                Dim elapsed = DateTime.Now - record.AttemptedAt.Value
                If elapsed < TimeSpan.Zero Then Return True
                Return elapsed >= If(record.Error = "", CheckInterval, RetryInterval)
            End Get
        End Property

        ''' <summary>Ultimo esito del controllo e stato dello scaricamento, per la pagina.</summary>
        Public Function Status() As ReleaseStatus
            Dim record = Volatile.Read(_record)
            Dim download = Volatile.Read(_download)
            Return New ReleaseStatus With {
                .Current = CurrentVersion.ToString(),
                .Installed = InstalledBySetup(),
                .Checking = _checking,
                .CheckedAt = Stamp(record.CheckedAt),
                .AttemptedAt = Stamp(record.AttemptedAt),
                .Error = record.Error,
                .Available = IsNewer(record.Version),
                .Version = record.Version,
                .Title = record.Title,
                .Notes = record.Notes,
                .PublishedAt = Stamp(record.PublishedAt),
                .PageUrl = If(record.PageUrl = "", ReleasesPage, record.PageUrl),
                .SetupName = record.SetupName,
                .SetupSize = record.SetupSize,
                .HasSetup = record.SetupUrl <> "" AndAlso record.SetupSha256 <> "",
                .Download = download?.Copy()
            }
        End Function

#Region "Controllo delle versioni"

        ''' <summary>
        ''' Chiede a GitHub l'ultima versione pubblicata e restituisce l'esito. Se il controllo non riesce resta valida
        ''' l'ultima versione nota, con il motivo dell'errore.
        ''' </summary>
        Public Async Function CheckAsync(manual As Boolean, cancel As CancellationToken) As Task(Of ReleaseStatus)
            Await _gate.WaitAsync(cancel)
            Try
                Dim previous = Volatile.Read(_record)
                If manual AndAlso previous.AttemptedAt.HasValue Then
                    Dim elapsed = DateTime.Now - previous.AttemptedAt.Value
                    If elapsed >= TimeSpan.Zero AndAlso elapsed < ManualInterval AndAlso previous.Error = "" Then Return Status()
                End If
                _checking = True
                Dim found As ReleaseRecord = Nothing
                Dim problem As String = Nothing
                Try
                    found = Await FetchAsync(cancel)
                Catch ex As OperationCanceledException When cancel.IsCancellationRequested
                    Throw
                Catch ex As Exception
                    problem = Describe(ex)
                End Try
                Dim updated As ReleaseRecord
                If found Is Nothing Then
                    updated = previous.Copy()
                    updated.Error = problem
                Else
                    updated = found
                    updated.CheckedAt = DateTime.Now
                End If
                updated.AttemptedAt = DateTime.Now
                Volatile.Write(_record, updated)
                Save(updated)
                ' Lo stato restituito deve già dire che il controllo è finito
                _checking = False
                Return Status()
            Finally
                _checking = False
                _gate.Release()
            End Try
        End Function

        Private Async Function FetchAsync(cancel As CancellationToken) As Task(Of ReleaseRecord)
            Using request As New HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl)
                request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json")
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28")
                Using timeout As New CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds))
                    Using linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token)
                        Using response = Await _http.SendAsync(request, linked.Token)
                            If Not response.IsSuccessStatusCode Then Throw New HttpRequestException(Nothing, Nothing, response.StatusCode)
                            Dim body = Await response.Content.ReadAsByteArrayAsync(linked.Token)
                            If body.Length > 2 * 1024 * 1024 Then Throw New InvalidDataException("risposta troppo grande")
                            Using doc = JsonDocument.Parse(body)
                                Return ReadRelease(doc.RootElement)
                            End Using
                        End Using
                    End Using
                End Using
            End Using
        End Function

        ''' <summary>Legge la risposta di GitHub (GET /repos/{owner}/{repo}/releases/latest).</summary>
        Private Shared Function ReadRelease(root As JsonElement) As ReleaseRecord
            If root.ValueKind <> JsonValueKind.Object Then Throw New InvalidDataException("risposta non riconosciuta")
            Dim tag = ReadString(root, "tag_name")
            Dim version = ParseVersion(tag)
            If version Is Nothing Then Throw New InvalidDataException($"versione ""{tag}"" non riconosciuta")

            Dim notes = ReadString(root, "body").Replace(vbCrLf, vbLf).Trim()
            If notes.Length > MaxNotesLength Then notes = notes.Substring(0, MaxNotesLength).TrimEnd() & "…"
            Dim record As New ReleaseRecord With {
                .Version = version.ToString(),
                .Title = Truncate(ReadString(root, "name"), 120),
                .Notes = notes,
                .PageUrl = TrustedLink(ReadString(root, "html_url"))
            }
            Dim published As DateTimeOffset
            If DateTimeOffset.TryParse(ReadString(root, "published_at"), Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AssumeUniversal, published) Then
                record.PublishedAt = published.LocalDateTime
            End If

            Dim assets As JsonElement
            If root.TryGetProperty("assets", assets) AndAlso assets.ValueKind = JsonValueKind.Array Then
                For Each asset In assets.EnumerateArray()
                    If asset.ValueKind <> JsonValueKind.Object Then Continue For
                    Dim name = ReadString(asset, "name")
                    Dim url = TrustedLink(ReadString(asset, "browser_download_url"))
                    If url = "" OrElse Not SetupNamePattern.IsMatch(name) Then Continue For
                    record.SetupName = name
                    record.SetupUrl = url
                    Dim size As JsonElement
                    If asset.TryGetProperty("size", size) AndAlso size.ValueKind = JsonValueKind.Number Then size.TryGetInt64(record.SetupSize)
                    Dim digest = Sha256Pattern.Match(ReadString(asset, "digest"))
                    If digest.Success Then record.SetupSha256 = digest.Groups(1).Value.ToLowerInvariant()
                    Exit For
                Next
            End If
            Return record
        End Function

        ''' <summary>Versione di un'etichetta come "v2.1.0" (major.minor.build), oppure Nothing.</summary>
        Public Shared Function ParseVersion(tag As String) As Version
            Dim value = If(tag, "").Trim()
            If value.StartsWith("v", StringComparison.OrdinalIgnoreCase) Then value = value.Substring(1)
            Dim suffix = value.IndexOfAny({"-"c, "+"c, " "c})
            If suffix >= 0 Then value = value.Substring(0, suffix)
            Dim parsed As Version = Nothing
            If Not Version.TryParse(value, parsed) Then Return Nothing
            Return New Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build))
        End Function

        Private Shared Function IsNewer(published As String) As Boolean
            Dim parsed As Version = Nothing
            Return Version.TryParse(If(published, ""), parsed) AndAlso parsed > CurrentVersion
        End Function

#End Region

#Region "Scarica e installa"

        ''' <summary>
        ''' Scarica il setup della versione annunciata, lo controlla e lo avvia. L'avanzamento arriva da progress
        ''' (download, verify, install); quando restituisce il setup è già partito e il programma deve chiudersi.
        ''' </summary>
        Public Async Function InstallAsync(progress As IProgress(Of DownloadState), cancel As CancellationToken) As Task(Of String)
            Dim record = Volatile.Read(_record)
            If Not IsNewer(record.Version) OrElse record.SetupUrl = "" Then
                Throw New InvalidOperationException("Non c'è una versione nuova da scaricare: premi «Controlla ora».")
            End If
            If Not record.SetupUrl.StartsWith(TrustedLinkPrefix, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException("Il setup indicato non è nella pagina ufficiale del programma su GitHub.")
            End If
            If record.SetupSha256 = "" Then
                Throw New InvalidOperationException("GitHub non ha indicato l'impronta SHA-256 del setup: per sicurezza scaricalo dalla pagina delle release.")
            End If
            If Volatile.Read(_download) IsNot Nothing AndAlso Volatile.Read(_download).Phase <> "error" Then
                Throw New InvalidOperationException("Lo scaricamento è già in corso.")
            End If

            Dim state As New DownloadState With {.Phase = "download", .Version = record.Version, .Total = record.SetupSize}
            Volatile.Write(_download, state)
            Dim report = Sub() progress?.Report(state.Copy())
            Directory.CreateDirectory(SetupFolder)
            Dim file = Path.Combine(SetupFolder, $"download-{Guid.NewGuid():N}.tmp")
            Try
                report()
                Using timeout As New CancellationTokenSource(DownloadTimeout)
                    Using linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token)
                        Using response = Await _http.GetAsync(record.SetupUrl, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                            If Not response.IsSuccessStatusCode Then
                                Throw New HttpRequestException($"GitHub ha risposto con l'errore {CInt(response.StatusCode)} allo scaricamento del setup: riprova più tardi.", Nothing, response.StatusCode)
                            End If
                            Dim length = response.Content.Headers.ContentLength
                            If length.HasValue AndAlso length.Value > 0 Then state.Total = length.Value
                            If state.Total > MaxSetupBytes Then Throw New InvalidDataException("Il file è troppo grande per essere il setup del programma.")
                            Using source = Await response.Content.ReadAsStreamAsync(linked.Token)
                                Using target As New FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                                    Dim buffer(81919) As Byte
                                    Dim lastReport = DateTime.MinValue
                                    Do
                                        Dim read = Await source.ReadAsync(buffer.AsMemory(0, buffer.Length), linked.Token)
                                        If read = 0 Then Exit Do
                                        Await target.WriteAsync(buffer.AsMemory(0, read), linked.Token)
                                        state.Done += read
                                        If state.Done > MaxSetupBytes Then Throw New InvalidDataException("Il file è troppo grande per essere il setup del programma.")
                                        If (DateTime.Now - lastReport).TotalMilliseconds > 150 Then
                                            lastReport = DateTime.Now
                                            report()
                                        End If
                                    Loop
                                End Using
                            End Using
                        End Using
                    End Using
                End Using

                ' Lo stesso file pubblicato su GitHub, e davvero il setup della versione annunciata
                state.Phase = "verify"
                report()
                If record.SetupSize > 0 AndAlso state.Done <> record.SetupSize Then
                    Throw New InvalidDataException($"Il file scaricato non è completo ({state.Done:N0} byte invece di {record.SetupSize:N0}): riprova.")
                End If
                Dim hash As String
                Using stream = IO.File.OpenRead(file)
                    hash = Convert.ToHexString(Await SHA256.HashDataAsync(stream, cancel)).ToLowerInvariant()
                End Using
                If Not String.Equals(hash, record.SetupSha256, StringComparison.OrdinalIgnoreCase) Then
                    Throw New InvalidDataException("L'impronta SHA-256 del file scaricato non è quella pubblicata su GitHub: il setup non è stato avviato.")
                End If
                Dim setupVersion = Inspect(file)
                Dim expected = ParseVersion(record.Version)
                If expected IsNot Nothing AndAlso setupVersion <> expected Then
                    Throw New InvalidDataException($"Il setup scaricato è della versione {setupVersion}, non della {expected} annunciata: non è stato avviato.")
                End If

                Dim setup = Path.Combine(SetupFolder, $"WhoIsPro-{setupVersion}-Setup.exe")
                IO.File.Move(file, setup, overwrite:=True)
                state.Phase = "install"
                report()
                Launch(setup)
                Return setup
            Catch ex As Exception
                state.Phase = "error"
                state.Error = DescribeDownload(ex)
                report()
                TryDelete(file)
                Throw New InvalidOperationException(state.Error, ex)
            End Try
        End Function

        ''' <summary>Controlla che il file sia il setup di WhoIs Pro e ne restituisce la versione.</summary>
        Private Shared Function Inspect(file As String) As Version
            Dim header(1) As Byte
            Using stream = IO.File.OpenRead(file)
                If stream.Read(header, 0, 2) <> 2 OrElse header(0) <> &H4D OrElse header(1) <> &H5A Then
                    Throw New InvalidDataException("Il file scaricato non è un programma di installazione di Windows.")
                End If
            End Using
            Dim info = FileVersionInfo.GetVersionInfo(file)
            If Not String.Equals(If(info.ProductName, "").Trim(), SetupProductName, StringComparison.Ordinal) OrElse
               Not String.Equals(If(info.FileDescription, "").Trim(), SetupDescription, StringComparison.Ordinal) Then
                Throw New InvalidDataException("Il file scaricato non è il setup di WhoIs Pro.")
            End If
            Dim parsed As Version = Nothing
            If Not Version.TryParse(If(info.ProductVersion, "").Trim(), parsed) Then Throw New InvalidDataException("La versione del setup non è leggibile.")
            Dim setupVersion As New Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build))
            If setupVersion < CurrentVersion Then
                Throw New InvalidOperationException($"Il setup è della versione {setupVersion}, precedente a quella in uso ({CurrentVersion}).")
            End If
            Return setupVersion
        End Function

        ''' <summary>Setup in modalità silenziosa: chiude il programma (il programma si chiude da sé subito dopo), sostituisce i file e lo riapre.</summary>
        Private Sub Launch(setup As String)
            Dim log = Path.Combine(SetupFolder, $"setup-{DateTime.Now:yyyyMMdd_HHmmss}.log")
            Dim psi As New ProcessStartInfo(setup) With {
                .Arguments = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RIAVVIA=1 /LOG=""{log}""",
                .UseShellExecute = True,
                .WorkingDirectory = SetupFolder
            }
            Using started = Process.Start(psi)
            End Using
        End Sub

        Private Shared Function DescribeDownload(ex As Exception) As String
            If TypeOf ex Is TaskCanceledException OrElse TypeOf ex Is OperationCanceledException Then
                Return "Lo scaricamento del setup ha impiegato troppo: controlla la connessione a Internet e riprova."
            End If
            Dim http = TryCast(ex, HttpRequestException)
            If http IsNot Nothing AndAlso Not http.StatusCode.HasValue Then
                Return "GitHub non è raggiungibile: controlla la connessione a Internet, oppure scarica il setup dalla pagina delle release."
            End If
            Return ex.Message
        End Function

        ''' <summary>All'avvio: via i setup scaricati le volte precedenti (decine di MB) e i log più vecchi.</summary>
        Private Sub CleanUp()
            Try
                If Not Directory.Exists(SetupFolder) Then Return
                For Each file In Directory.EnumerateFiles(SetupFolder, "*.exe").Concat(Directory.EnumerateFiles(SetupFolder, "*.tmp"))
                    TryDelete(file)
                Next
                For Each old In New DirectoryInfo(SetupFolder).EnumerateFiles("setup-*.log").OrderByDescending(Function(f) f.LastWriteTimeUtc).Skip(5)
                    TryDelete(old.FullName)
                Next
            Catch
            End Try
        End Sub

#End Region

#Region "Utilità"

        Private Shared Function ReadString(element As JsonElement, name As String) As String
            Dim value As JsonElement
            If element.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.String Then Return value.GetString()
            Return ""
        End Function

        Private Shared Function TrustedLink(url As String) As String
            Return If(url.StartsWith(TrustedLinkPrefix, StringComparison.OrdinalIgnoreCase), url, "")
        End Function

        Private Shared Function Truncate(value As String, max As Integer) As String
            value = value.Trim()
            Return If(value.Length > max, value.Substring(0, max), value)
        End Function

        Private Shared Function Stamp(value As DateTime?) As String
            Return If(value.HasValue, value.Value.ToString("yyyy-MM-ddTHH:mm:ss"), "")
        End Function

        Private Shared Function TrimSlash(folder As String) As String
            Return Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        End Function

        Private Shared Sub TryDelete(file As String)
            Try
                If IO.File.Exists(file) Then IO.File.Delete(file)
            Catch
            End Try
        End Sub

        ''' <summary>Motivo del controllo non riuscito, in parole comprensibili.</summary>
        Private Shared Function Describe(ex As Exception) As String
            Dim http = TryCast(ex, HttpRequestException)
            If http IsNot Nothing Then
                If Not http.StatusCode.HasValue Then
                    Return "GitHub (api.github.com) non è raggiungibile: controlla la connessione a Internet."
                End If
                Select Case http.StatusCode.Value
                    Case HttpStatusCode.NotFound
                        Return "Su GitHub non risulta ancora nessuna versione pubblicata."
                    Case HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests
                        Return "GitHub ha rifiutato la richiesta (troppe richieste da questa connessione): riprova tra un'ora."
                    Case Else
                        Return $"GitHub ha risposto con l'errore {CInt(http.StatusCode.Value)}: riprova più tardi."
                End Select
            End If
            If TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is TimeoutException Then
                Return $"GitHub non ha risposto entro {TimeoutSeconds} secondi: controlla la connessione a Internet."
            End If
            If TypeOf ex Is JsonException OrElse TypeOf ex Is InvalidDataException Then
                Return $"La risposta di GitHub non è leggibile ({ex.Message})."
            End If
            Return ex.Message
        End Function

        Private Function Load() As ReleaseRecord
            Try
                If Not IO.File.Exists(RecordPath) Then Return Nothing
                Return JsonSerializer.Deserialize(Of ReleaseRecord)(IO.File.ReadAllText(RecordPath))
            Catch
                Return Nothing
            End Try
        End Function

        Private Sub Save(record As ReleaseRecord)
            Try
                Directory.CreateDirectory(_folder)
                Dim temp = RecordPath & ".tmp"
                IO.File.WriteAllText(temp, JsonSerializer.Serialize(record, New JsonSerializerOptions With {.WriteIndented = True}))
                IO.File.Move(temp, RecordPath, overwrite:=True)
            Catch
            End Try
        End Sub

#End Region

        Public Sub Dispose() Implements IDisposable.Dispose
            _http.Dispose()
        End Sub

        Private NotInheritable Class ReleaseRecord
            Public Property CheckedAt As DateTime?
            Public Property AttemptedAt As DateTime?
            Public Property [Error] As String = ""
            Public Property Version As String = ""
            Public Property Title As String = ""
            Public Property Notes As String = ""
            Public Property PublishedAt As DateTime?
            Public Property PageUrl As String = ""
            Public Property SetupName As String = ""
            Public Property SetupUrl As String = ""
            Public Property SetupSize As Long
            Public Property SetupSha256 As String = ""

            Public Function Copy() As ReleaseRecord
                Return DirectCast(MemberwiseClone(), ReleaseRecord)
            End Function
        End Class

    End Class

    ''' <summary>Ultima versione pubblicata, esito del controllo e scaricamento in corso, per la pagina.</summary>
    Public Class ReleaseStatus
        Public Property Current As String = ""
        ''' <summary>Installato con il setup: «Scarica e installa» funziona; altrimenti (copia portabile) si apre la pagina delle release.</summary>
        Public Property Installed As Boolean
        Public Property Checking As Boolean
        Public Property CheckedAt As String = ""
        Public Property AttemptedAt As String = ""
        Public Property [Error] As String = ""
        Public Property Available As Boolean
        Public Property Version As String = ""
        Public Property Title As String = ""
        Public Property Notes As String = ""
        Public Property PublishedAt As String = ""
        Public Property PageUrl As String = ""
        Public Property SetupName As String = ""
        Public Property SetupSize As Long
        Public Property HasSetup As Boolean
        Public Property Download As DownloadState
    End Class

    ''' <summary>Avanzamento di «Scarica e installa»: phase = download, verify, install o error.</summary>
    Public Class DownloadState
        Public Property Phase As String = ""
        Public Property Version As String = ""
        Public Property Done As Long
        Public Property Total As Long
        Public Property [Error] As String = ""

        Public Function Copy() As DownloadState
            Return DirectCast(MemberwiseClone(), DownloadState)
        End Function
    End Class

End Namespace
