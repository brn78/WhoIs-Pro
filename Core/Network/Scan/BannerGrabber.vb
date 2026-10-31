Option Strict On

Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading

Namespace Network.Scan

    ''' <summary>
    ''' Lettura veloce dei banner sulle porte aperte: per i servizi web una GET / da cui si ricavano l'intestazione
    ''' Server e il titolo della pagina, per FTP, SSH, SMTP, POP3, IMAP e RTSP la prima riga di benvenuto.
    ''' Tempi stretti, nessuna eccezione propagata (salvo l'annullamento richiesto dal chiamante).
    ''' </summary>
    Public Module BannerGrabber

#Region "Porte gestite"

        ''' <summary>Porte su cui si prova una richiesta HTTP.</summary>
        Public ReadOnly HttpPorts As New HashSet(Of Integer) From {80, 81, 3000, 5000, 8000, 8006, 8008, 8080, 8081, 8123, 8888, 9000, 9090}
        ''' <summary>Porte i cui servizi si presentano da soli con una riga di benvenuto.</summary>
        Public ReadOnly GreetingPorts As New HashSet(Of Integer) From {21, 22, 25, 110, 143, 587}
        ''' <summary>Porte RTSP (telecamere): si chiede OPTIONS e si legge l'intestazione Server.</summary>
        Public ReadOnly RtspPorts As New HashSet(Of Integer) From {554, 8554}

        Private Const MaxHttpBytes As Integer = 32 * 1024
        Private Const MaxGreetingBytes As Integer = 512
        Private Const MaxBannerLength As Integer = 160

        Private ReadOnly TitlePattern As New Regex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase Or RegexOptions.Singleline Or RegexOptions.CultureInvariant Or RegexOptions.Compiled)
        Private ReadOnly RealmPattern As New Regex("realm\s*=\s*""?([^"",]+)", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant Or RegexOptions.Compiled)

        Public Function Supports(port As Integer) As Boolean
            Return HttpPorts.Contains(port) OrElse GreetingPorts.Contains(port) OrElse RtspPorts.Contains(port)
        End Function

#End Region

#Region "Lettura"

        ''' <summary>
        ''' Banner della porta entro <paramref name="budgetMs"/> millisecondi in tutto (connessione compresa);
        ''' Nothing se la porta non è gestita, non risponde o non manda testo leggibile.
        ''' </summary>
        Public Async Function GrabAsync(address As IPAddress, port As Integer, budgetMs As Integer, ct As CancellationToken) As Task(Of String)
            If address Is Nothing OrElse Not Supports(port) Then Return Nothing
            Using deadline = CancellationTokenSource.CreateLinkedTokenSource(ct)
                deadline.CancelAfter(Math.Clamp(budgetMs, 200, 10000))
                Try
                    Using client = Await TcpConnector.ConnectAsync(address, port, budgetMs, deadline.Token).ConfigureAwait(False)
                        If client Is Nothing Then Return Nothing
                        Dim stream = client.GetStream()
                        If HttpPorts.Contains(port) Then
                            Dim host = If(port = 80, address.ToString(), $"{address}:{port}")
                            Dim request = Encoding.ASCII.GetBytes($"GET / HTTP/1.0{vbCrLf}Host: {host}{vbCrLf}User-Agent: WhoIsPro{vbCrLf}Accept: text/html,*/*{vbCrLf}Connection: close{vbCrLf}{vbCrLf}")
                            Await stream.WriteAsync(request, deadline.Token).ConfigureAwait(False)
                            Dim response = Await ReadAsync(stream, MaxHttpBytes, AddressOf HasTitleEnd, deadline.Token, ct).ConfigureAwait(False)
                            Return If(response.Length = 0, Nothing, DescribeHttp(Encoding.UTF8.GetString(response)))
                        End If
                        If RtspPorts.Contains(port) Then
                            Dim request = Encoding.ASCII.GetBytes($"OPTIONS rtsp://{address}:{port}/ RTSP/1.0{vbCrLf}CSeq: 1{vbCrLf}User-Agent: WhoIsPro{vbCrLf}{vbCrLf}")
                            Await stream.WriteAsync(request, deadline.Token).ConfigureAwait(False)
                            Dim response = Await ReadAsync(stream, MaxGreetingBytes * 4, AddressOf HasHeaderEnd, deadline.Token, ct).ConfigureAwait(False)
                            If response.Length = 0 Then Return Nothing
                            Dim lines = Encoding.ASCII.GetString(response).Split(ControlChars.Lf)
                            Dim server = Header(lines, "Server")
                            Return If(server IsNot Nothing, "RTSP · Server: " & Clean(server, 80), Clean(lines(0), MaxBannerLength))
                        End If
                        Dim greeting = Await ReadAsync(stream, MaxGreetingBytes, AddressOf HasLineEnd, deadline.Token, ct).ConfigureAwait(False)
                        If greeting.Length = 0 Then Return Nothing
                        Return Clean(Encoding.UTF8.GetString(greeting).Split(ControlChars.Lf)(0), MaxBannerLength)
                    End Using
                Catch ex As OperationCanceledException When ct.IsCancellationRequested
                    Throw
                Catch
                    Return Nothing
                End Try
            End Using
        End Function

        ''' <summary>Legge finché arriva abbastanza, la connessione si chiude o il tempo finisce; restituisce quanto letto.</summary>
        Private Async Function ReadAsync(stream As NetworkStream, maxBytes As Integer, isEnough As Func(Of Byte(), Integer, Boolean),
                                         deadline As CancellationToken, ct As CancellationToken) As Task(Of Byte())
            Dim buffer(maxBytes - 1) As Byte
            Dim total = 0
            Try
                While total < maxBytes
                    Dim read = Await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), deadline).ConfigureAwait(False)
                    If read = 0 Then Exit While
                    total += read
                    If isEnough(buffer, total) Then Exit While
                End While
            Catch ex As OperationCanceledException When Not ct.IsCancellationRequested
                ' tempo finito: vale quanto arrivato finora
            Catch ex As IOException
                ' connessione chiusa dall'altra parte a metà lettura
            End Try
            Dim result(Math.Max(0, total - 1)) As Byte
            If total = 0 Then Return Array.Empty(Of Byte)()
            Array.Copy(buffer, result, total)
            Return result
        End Function

        Private Function HasLineEnd(buffer As Byte(), count As Integer) As Boolean
            Return Array.IndexOf(buffer, CByte(10), 0, count) >= 0
        End Function

        Private Function HasHeaderEnd(buffer As Byte(), count As Integer) As Boolean
            Return Encoding.ASCII.GetString(buffer, 0, count).Contains(vbCrLf & vbCrLf, StringComparison.Ordinal)
        End Function

        Private Function HasTitleEnd(buffer As Byte(), count As Integer) As Boolean
            Return Encoding.ASCII.GetString(buffer, 0, count).Contains("</title>", StringComparison.OrdinalIgnoreCase)
        End Function

#End Region

#Region "Interpretazione HTTP"

        ''' <summary>Riassunto della risposta web: "Server: nginx · Titolo: DiskStation" (o realm, rinvio, codice).</summary>
        Private Function DescribeHttp(response As String) As String
            Dim split = response.IndexOf(vbCrLf & vbCrLf, StringComparison.Ordinal)
            Dim separator = 4
            If split < 0 Then
                split = response.IndexOf(vbLf & vbLf, StringComparison.Ordinal)
                separator = 2
            End If
            Dim head = If(split >= 0, response.Substring(0, split), response)
            Dim body = If(split >= 0, response.Substring(split + separator), "")
            Dim lines = head.Split(ControlChars.Lf)
            Dim status = lines(0).Trim()
            If Not status.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) Then Return Clean(status, MaxBannerLength) ' sulla porta non c'è un server web

            Dim server = Header(lines, "Server")
            Dim location = Header(lines, "Location")
            Dim realm As String = Nothing
            Dim auth = Header(lines, "WWW-Authenticate")
            If auth IsNot Nothing Then
                Dim m = RealmPattern.Match(auth)
                If m.Success Then realm = Clean(m.Groups(1).Value, 60)
            End If
            Dim title As String = Nothing
            Dim tm = TitlePattern.Match(If(body.Length > 0, body, response))
            If tm.Success Then title = Clean(WebUtility.HtmlDecode(tm.Groups(1).Value), 100)

            Dim parts As New List(Of String)(3)
            If server IsNot Nothing Then parts.Add("Server: " & Clean(server, 80))
            If title IsNot Nothing Then
                parts.Add("Titolo: " & title)
            ElseIf realm IsNot Nothing Then
                parts.Add("Realm: " & realm)
            End If
            If title Is Nothing AndAlso location IsNot Nothing Then parts.Add("Rinvio: " & Clean(location, 80))
            If parts.Count = 0 Then
                Dim statusParts = status.Split(" "c, 3, StringSplitOptions.RemoveEmptyEntries)
                parts.Add(If(statusParts.Length > 1, "HTTP " & statusParts(1), "HTTP"))
            End If
            Return String.Join(" · ", parts)
        End Function

        Private Function Header(lines As String(), name As String) As String
            For Each line In lines.Skip(1)
                Dim colon = line.IndexOf(":"c)
                If colon > 0 AndAlso line.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase) Then
                    Dim value = line.Substring(colon + 1).Trim()
                    Return If(value.Length = 0, Nothing, value)
                End If
            Next
            Return Nothing
        End Function

#End Region

#Region "Pulizia del testo"

        ''' <summary>Solo testo stampabile su una riga, spazi compattati, lunghezza massima; Nothing se non resta nulla.</summary>
        Public Function Clean(text As String, maxLength As Integer) As String
            If text Is Nothing Then Return Nothing
            Dim sb As New StringBuilder(Math.Min(text.Length, maxLength + 1))
            Dim lastWasSpace = True
            For Each c In text
                Dim printable = Not Char.IsControl(c) AndAlso c <> ChrW(&HFFFD)
                If Not printable OrElse Char.IsWhiteSpace(c) Then
                    If Not lastWasSpace Then sb.Append(" "c)
                    lastWasSpace = True
                    Continue For
                End If
                sb.Append(c)
                lastWasSpace = False
                If sb.Length > maxLength Then Exit For
            Next
            Dim result = sb.ToString().Trim()
            If result.Length > maxLength Then result = result.Substring(0, maxLength - 1).TrimEnd() & "…"
            Return If(result.Length = 0, Nothing, result)
        End Function

#End Region

    End Module

End Namespace
