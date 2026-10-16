Option Strict On

Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.NetworkInformation
Imports System.Threading
Imports WhoIsPro.Models

Namespace Network

    ''' <summary>Avanzamento del test: fase, messaggio, valore del momento (Mbit/s o ms) e percentuale totale.</summary>
    Public Class PerfProgress
        ''' <summary>"latency", "dns", "download", "upload".</summary>
        Public Property Phase As String = ""
        Public Property Message As String = ""
        Public Property Value As Double
        Public Property Percent As Integer
    End Class

    ''' <summary>
    ''' Test delle prestazioni di rete, circa 20 secondi: ping in serie verso il gateway (rete locale) e verso 1.1.1.1
    ''' (Internet) con latenza, jitter e perdita; tempo di risposta del DNS su nomi mai chiesti (niente cache); download e
    ''' upload su più connessioni insieme verso i server di prova di Cloudflare (gli stessi di speed.cloudflare.com),
    ''' scartando il primo secondo e mezzo, quando le connessioni stanno ancora accelerando.
    ''' </summary>
    Public NotInheritable Class PerfTester

        Private Const DownUrl As String = "https://speed.cloudflare.com/__down?bytes=50000000"
        Private Const UpUrl As String = "https://speed.cloudflare.com/__up"
        Private Const InternetTarget As String = "1.1.1.1"
        Private Const Pings As Integer = 20
        Private Const PingIntervalMs As Integer = 200
        Private Shared ReadOnly WarmUp As TimeSpan = TimeSpan.FromSeconds(1.5)

        Public Property DownloadSeconds As Double = 8
        Public Property UploadSeconds As Double = 6
        Public Property Streams As Integer = 4

        Private Shared ReadOnly Http As New HttpClient(New SocketsHttpHandler With {
            .MaxConnectionsPerServer = 16,
            .PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            .AutomaticDecompression = DecompressionMethods.None
        }) With {.Timeout = Timeout.InfiniteTimeSpan}

        Public Async Function RunAsync(gateway As IPAddress, progress As IProgress(Of PerfProgress), token As CancellationToken) As Task(Of PerfResult)
            Dim result As New PerfResult()
            Dim watch = Stopwatch.StartNew()

            ' 1. latenza: rete locale e Internet insieme (4 secondi)
            Report(progress, "latency", "Latenza verso il gateway e verso Internet…", 0, 2)
            Dim localTask = If(gateway Is Nothing, Task.FromResult(Of LatencyStats)(Nothing), PingSeriesAsync(gateway, token))
            Dim internetTask = PingSeriesAsync(IPAddress.Parse(InternetTarget), token)
            result.Local = Await localTask
            result.Internet = Await internetTask
            token.ThrowIfCancellationRequested()

            ' 2. DNS
            Report(progress, "dns", "Risposta del server DNS…", 0, 22)
            result.DnsMs = Await DnsTimeAsync(token)

            ' 3. download
            Report(progress, "download", "Download…", 0, 28)
            Dim down = Await DownloadAsync(progress, token)
            result.DownloadMbps = down.Mbps
            result.Server = down.Server

            ' 4. upload
            Report(progress, "upload", "Upload…", 0, 66)
            result.UploadMbps = Await UploadAsync(progress, token)

            result.Seconds = Math.Round(watch.Elapsed.TotalSeconds, 1)
            Report(progress, "done", "Test completato", 0, 100)
            Return result
        End Function

        Private Shared Sub Report(progress As IProgress(Of PerfProgress), phase As String, message As String, value As Double, percent As Integer)
            progress?.Report(New PerfProgress With {.Phase = phase, .Message = message, .Value = Math.Round(value, 1), .Percent = Math.Max(0, Math.Min(100, percent))})
        End Sub

        ''' <summary>20 ping, uno ogni 200 ms: minimo, media, massimo, jitter (variazione tra ping consecutivi) e perdita.</summary>
        Public Shared Async Function PingSeriesAsync(target As IPAddress, token As CancellationToken) As Task(Of LatencyStats)
            Dim times As New List(Of Double)()
            Dim previous As Double = -1
            Dim deltas As New List(Of Double)()
            Using pinger As New Ping()
                For i = 1 To Pings
                    token.ThrowIfCancellationRequested()
                    Dim tick = Stopwatch.StartNew()
                    Try
                        Dim reply = Await pinger.SendPingAsync(target, 1000)
                        If reply.Status = IPStatus.Success Then
                            Dim ms As Double = reply.RoundtripTime
                            times.Add(ms)
                            If previous >= 0 Then deltas.Add(Math.Abs(ms - previous))
                            previous = ms
                        End If
                    Catch ex As PingException
                    End Try
                    Dim wait = PingIntervalMs - CInt(tick.ElapsedMilliseconds)
                    If wait > 0 AndAlso i < Pings Then Await Task.Delay(wait, token)
                Next
            End Using
            Return New LatencyStats With {
                .Target = target.ToString(), .Sent = Pings, .Received = times.Count,
                .MinMs = If(times.Count > 0, times.Min(), 0), .AvgMs = If(times.Count > 0, Math.Round(times.Average(), 1), 0), .MaxMs = If(times.Count > 0, times.Max(), 0),
                .JitterMs = If(deltas.Count > 0, Math.Round(deltas.Average(), 1), 0),
                .LossPct = Math.Round((Pings - times.Count) * 100.0 / Pings, 1)
            }
        End Function

        ''' <summary>Tre nomi a caso (mai chiesti, quindi niente cache): tempo medio perché il server DNS risponda.</summary>
        Private Shared Async Function DnsTimeAsync(token As CancellationToken) As Task(Of Double)
            Dim times As New List(Of Double)()
            For i = 1 To 3
                token.ThrowIfCancellationRequested()
                Dim name = $"whoispro-{Guid.NewGuid():N}.example.com"
                Dim w = Stopwatch.StartNew()
                Try
                    Await Dns.GetHostAddressesAsync(name, token)
                Catch ex As Net.Sockets.SocketException
                    ' «nome inesistente» è la risposta attesa: conta il tempo
                End Try
                times.Add(w.Elapsed.TotalMilliseconds)
            Next
            Return Math.Round(times.Average(), 0)
        End Function

        ''' <summary>Download su più connessioni per DownloadSeconds: Mbit/s dopo il riscaldamento e il centro dati usato.</summary>
        Private Async Function DownloadAsync(progress As IProgress(Of PerfProgress), token As CancellationToken) As Task(Of (Mbps As Double, Server As String))
            Dim total As Long = 0
            Dim server = ""
            Using deadline = CancellationTokenSource.CreateLinkedTokenSource(token)
                deadline.CancelAfter(TimeSpan.FromSeconds(DownloadSeconds))
                Dim clock = Stopwatch.StartNew()
                Dim workers As New List(Of Task)()
                For s = 1 To Streams
                    workers.Add(Task.Run(Async Function()
                                             Dim buffer(65535) As Byte
                                             While Not deadline.IsCancellationRequested
                                                 Try
                                                     Using request As New HttpRequestMessage(HttpMethod.Get, DownUrl)
                                                         request.Headers.CacheControl = New Headers.CacheControlHeaderValue With {.NoCache = True}
                                                         Using response = Await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                                                             response.EnsureSuccessStatusCode()
                                                             If server = "" Then server = ColoOf(response)
                                                             Using body = Await response.Content.ReadAsStreamAsync(deadline.Token)
                                                                 Do
                                                                     Dim n = Await body.ReadAsync(buffer, 0, buffer.Length, deadline.Token)
                                                                     If n <= 0 Then Exit Do
                                                                     Interlocked.Add(total, n)
                                                                 Loop
                                                             End Using
                                                         End Using
                                                     End Using
                                                 Catch ex As Exception
                                                     ' scadenza, connessione chiusa o errore del server: si ferma solo questa connessione
                                                     Exit While
                                                 End Try
                                             End While
                                         End Function))
                Next
                Dim mbps = Await MeasureAsync(Function() Interlocked.Read(total), clock, DownloadSeconds, "download", 28, 66, progress, deadline.Token)
                Await Task.WhenAll(workers)
                token.ThrowIfCancellationRequested()
                Return (mbps, server)
            End Using
        End Function

        ''' <summary>Upload su più connessioni per UploadSeconds (dati a caso, in blocchi): Mbit/s dopo il riscaldamento.</summary>
        Private Async Function UploadAsync(progress As IProgress(Of PerfProgress), token As CancellationToken) As Task(Of Double)
            Dim total As Long = 0
            Dim chunk(65535) As Byte
            Random.Shared.NextBytes(chunk)
            Using deadline = CancellationTokenSource.CreateLinkedTokenSource(token)
                deadline.CancelAfter(TimeSpan.FromSeconds(UploadSeconds))
                Dim clock = Stopwatch.StartNew()
                Dim workers As New List(Of Task)()
                For s = 1 To Math.Max(2, Streams - 1)
                    workers.Add(Task.Run(Async Function()
                                             While Not deadline.IsCancellationRequested
                                                 Try
                                                     Using content As New CountingContent(chunk, 20 * 1024 * 1024, Sub(n) Interlocked.Add(total, n), deadline.Token)
                                                         Using response = Await Http.PostAsync(UpUrl, content, deadline.Token)
                                                         End Using
                                                     End Using
                                                 Catch ex As Exception
                                                     ' scadenza, connessione chiusa o errore del server: si ferma solo questa connessione
                                                     Exit While
                                                 End Try
                                             End While
                                         End Function))
                Next
                Dim mbps = Await MeasureAsync(Function() Interlocked.Read(total), clock, UploadSeconds, "upload", 66, 98, progress, deadline.Token)
                Await Task.WhenAll(workers)
                token.ThrowIfCancellationRequested()
                Return mbps
            End Using
        End Function

        ''' <summary>
        ''' Ogni 250 ms: velocità dell'ultimo secondo per la pagina; alla fine la media dopo il riscaldamento (Mbit/s).
        ''' </summary>
        Private Shared Async Function MeasureAsync(read As Func(Of Long), clock As Stopwatch, seconds As Double, phase As String,
                                                   fromPct As Integer, toPct As Integer, progress As IProgress(Of PerfProgress), token As CancellationToken) As Task(Of Double)
            Dim samples As New List(Of (T As Double, Bytes As Long))()
            Dim warmBytes As Long = -1
            Dim warmTime As Double = 0
            While Not token.IsCancellationRequested
                Try
                    Await Task.Delay(250, token)
                Catch ex As OperationCanceledException
                    Exit While
                End Try
                Dim t = clock.Elapsed.TotalSeconds
                Dim b = read()
                samples.Add((t, b))
                If warmBytes < 0 AndAlso t >= WarmUp.TotalSeconds Then
                    warmBytes = b
                    warmTime = t
                End If
                Dim window = samples.Where(Function(x) x.T >= t - 1).ToList()
                Dim now As Double = 0
                If window.Count >= 2 Then now = (window.Last().Bytes - window.First().Bytes) * 8 / 1000000.0 / Math.Max(0.001, window.Last().T - window.First().T)
                Report(progress, phase, If(phase = "download", "Download…", "Upload…"), now, fromPct + CInt((toPct - fromPct) * Math.Min(1, t / seconds)))
            End While
            Dim endTime = clock.Elapsed.TotalSeconds
            Dim endBytes = read()
            If warmBytes < 0 OrElse endTime - warmTime < 0.5 Then
                Return Math.Round(endBytes * 8 / 1000000.0 / Math.Max(0.001, endTime), 1)
            End If
            Return Math.Round((endBytes - warmBytes) * 8 / 1000000.0 / (endTime - warmTime), 1)
        End Function

        ''' <summary>«Cloudflare · MXP» dall'intestazione cf-ray (le ultime lettere sono il centro dati).</summary>
        Private Shared Function ColoOf(response As HttpResponseMessage) As String
            Dim values As IEnumerable(Of String) = Nothing
            If response.Headers.TryGetValues("cf-ray", values) Then
                Dim ray = values.FirstOrDefault()
                Dim dash = If(ray, "").LastIndexOf("-"c)
                If dash > 0 Then Return $"Cloudflare · {ray.Substring(dash + 1)}"
            End If
            Return "Cloudflare"
        End Function

        ''' <summary>Corpo dell'upload: lo stesso blocco ripetuto fino a size byte (o alla scadenza), contando quanto parte.</summary>
        Private NotInheritable Class CountingContent
            Inherits HttpContent

            Private ReadOnly _chunk As Byte()
            Private ReadOnly _size As Long
            Private ReadOnly _sent As Action(Of Integer)
            Private ReadOnly _token As CancellationToken

            Public Sub New(chunk As Byte(), size As Long, sent As Action(Of Integer), token As CancellationToken)
                _chunk = chunk
                _size = size
                _sent = sent
                _token = token
                Headers.ContentType = New Headers.MediaTypeHeaderValue("application/octet-stream")
            End Sub

            Protected Overrides Async Function SerializeToStreamAsync(stream As Stream, context As TransportContext) As Task
                Dim written As Long = 0
                While written < _size
                    _token.ThrowIfCancellationRequested()
                    Dim n = CInt(Math.Min(_chunk.Length, _size - written))
                    Await stream.WriteAsync(_chunk, 0, n, _token)
                    written += n
                    _sent(n)
                End While
            End Function

            Protected Overrides Function TryComputeLength(ByRef length As Long) As Boolean
                length = _size
                Return True
            End Function
        End Class

    End Class

End Namespace
