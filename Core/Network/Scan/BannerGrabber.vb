Option Strict On

Imports System.IO
Imports System.Net
Imports System.Net.Security
Imports System.Net.Sockets
Imports System.Security.Cryptography.X509Certificates
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading

Namespace Network.Scan

    ''' <summary>
    ''' Lettura veloce dei banner sulle porte aperte: per i servizi web (HTTP e HTTPS) una GET / da cui si ricavano
    ''' l'intestazione Server e il titolo della pagina, per FTP, SSH, SMTP, POP3, IMAP e RTSP la prima riga di benvenuto.
    ''' Tempi stretti, nessuna eccezione propagata (salvo l'annullamento richiesto dal chiamante).
    ''' </summary>
    Public Module BannerGrabber

#Region "Porte gestite"

        ''' <summary>Porte su cui si prova una richiesta HTTP.</summary>
        Public ReadOnly HttpPorts As New HashSet(Of Integer) From {80, 81, 3000, 5000, 8000, 8006, 8008, 8080, 8081, 8123, 8888, 9000, 9090}
        ''' <summary>Porte su cui si prova una richiesta HTTPS (TLS senza verifica del certificato).</summary>
        Public ReadOnly HttpsPorts As New HashSet(Of Integer) From {443, 8443}
        ''' <summary>Porte i cui servizi si presentano da soli con una riga di benvenuto.</summary>
        Public ReadOnly GreetingPorts As New HashSet(Of Integer) From {21, 22, 25, 110, 143, 587}
        ''' <summary>Porte RTSP (telecamere): si chiede OPTIONS e si legge l'intestazione Server.</summary>
        Public ReadOnly RtspPorts As New HashSet(Of Integer) From {554, 8554}

        ''' <summary>Tempo massimo per la stretta di mano TLS sulle porte HTTPS; il resto del tempo della fase vale per la risposta.</summary>
        Private Const TlsHandshakeMs As Integer = 500
        Private Const MaxHttpBytes As Integer = 32 * 1024
        Private Const MaxGreetingBytes As Integer = 512
        Private Const MaxBannerLength As Integer = 160

        Private ReadOnly TitlePattern As New Regex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase Or RegexOptions.Singleline Or RegexOptions.CultureInvariant Or RegexOptions.Compiled)
        Private ReadOnly ScriptRedirectPattern As New Regex("(?:location(?:\.href)?\s*=\s*|location\.replace\(\s*|http-equiv\s*=\s*[""']?refresh[""']?[^>]*?url\s*=\s*)[""']?([^""'\s;)>]+)", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant Or RegexOptions.Compiled)
        Private ReadOnly RealmPattern As New Regex("realm\s*=\s*""?([^"",]+)", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant Or RegexOptions.Compiled)

        Public Function Supports(port As Integer) As Boolean
            Return HttpPorts.Contains(port) OrElse HttpsPorts.Contains(port) OrElse GreetingPorts.Contains(port) OrElse RtspPorts.Contains(port)
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
                        Return Await GrabFromConnectedClientAsync(client, address.ToString(), port, deadline.Token, ct).ConfigureAwait(False)
                    End Using
                Catch ex As OperationCanceledException When ct.IsCancellationRequested
                    Throw
                Catch
                    Return Nothing
                End Try
            End Using
        End Function

        ''' <summary>
        ''' Legge il banner da una connessione TCP già stabilita (HTTP, HTTPS con certificato, RTSP o benvenuto testuale).
        ''' </summary>
        Public Async Function GrabFromConnectedClientAsync(client As TcpClient, targetHost As String, port As Integer, deadlineToken As CancellationToken, callerToken As CancellationToken) As Task(Of String)
            Dim stream = client.GetStream()
            If HttpsPorts.Contains(port) Then
                Using ssl As New SslStream(stream, leaveInnerStreamOpen:=False)
                    Dim sslOptions As New SslClientAuthenticationOptions With {
                        .TargetHost = targetHost,
                        .RemoteCertificateValidationCallback = Function(sender, cert, chain, errors) True
                    }
                    ' Porta aperta ma servizio muto (certi router): la stretta di mano non finirebbe mai e consumerebbe tutto il
                    ' tempo della fase. In LAN finisce in pochi decimi: oltre il tetto si lascia perdere
                    Using handshake = CancellationTokenSource.CreateLinkedTokenSource(deadlineToken)
                        handshake.CancelAfter(TlsHandshakeMs)
                        Try
                            Await ssl.AuthenticateAsClientAsync(sslOptions, handshake.Token).ConfigureAwait(False)
                        Catch ex As OperationCanceledException When callerToken.IsCancellationRequested
                            Throw
                        Catch
                            Return Nothing
                        End Try
                    End Using
                    Dim certHint = TryGetCertificateHint(ssl.RemoteCertificate, targetHost)
                    Await ssl.WriteAsync(HttpRequest(targetHost, port, 443, "/"), deadlineToken).ConfigureAwait(False)
                    Dim response = Await ReadAsync(ssl, MaxHttpBytes, AddressOf HasTitleEnd, deadlineToken, callerToken).ConfigureAwait(False)
                    If response.Length = 0 Then Return If(certHint IsNot Nothing, "Certificato: " & certHint, Nothing)
                    Return DescribeHttp(Encoding.UTF8.GetString(response), certHint)
                End Using
            End If
            If HttpPorts.Contains(port) Then
                Await stream.WriteAsync(HttpRequest(targetHost, port, 80, "/"), deadlineToken).ConfigureAwait(False)
                Dim response = Await ReadAsync(stream, MaxHttpBytes, AddressOf HasTitleEnd, deadlineToken, callerToken).ConfigureAwait(False)
                Return If(response.Length = 0, Nothing, DescribeHttp(Encoding.UTF8.GetString(response)))
            End If
            If RtspPorts.Contains(port) Then
                Dim request = Encoding.ASCII.GetBytes($"OPTIONS rtsp://{targetHost}:{port}/ RTSP/1.0{vbCrLf}CSeq: 1{vbCrLf}User-Agent: WhoIsPro{vbCrLf}{vbCrLf}")
                Await stream.WriteAsync(request, deadlineToken).ConfigureAwait(False)
                Dim response = Await ReadAsync(stream, MaxGreetingBytes * 4, AddressOf HasHeaderEnd, deadlineToken, callerToken).ConfigureAwait(False)
                If response.Length = 0 Then Return Nothing
                Dim lines = Encoding.ASCII.GetString(response).Split(ControlChars.Lf)
                Dim server = Header(lines, "Server")
                Return If(server IsNot Nothing, "RTSP · Server: " & Clean(server, 80), Clean(lines(0), MaxBannerLength))
            End If
            Dim greeting = Await ReadAsync(stream, MaxGreetingBytes, AddressOf HasLineEnd, deadlineToken, callerToken).ConfigureAwait(False)
            If greeting.Length = 0 Then Return Nothing
            Return Clean(Encoding.UTF8.GetString(greeting).Split(ControlChars.Lf)(0), MaxBannerLength)
        End Function

        ''' <summary>
        ''' Interroga /ISAPI/System/deviceInfo su HTTP: i dispositivi Hikvision rispondono con 401 Unauthorized e
        ''' WWW-Authenticate Digest/Basic il cui realm contiene spesso il codice modello esatto (es. "DS-2CD2532F-I").
        ''' </summary>
        Public Async Function GrabHikvisionRealmAsync(address As IPAddress, port As Integer, budgetMs As Integer, ct As CancellationToken) As Task(Of String)
            If address Is Nothing OrElse port < 1 OrElse port > 65535 Then Return Nothing
            Using deadline = CancellationTokenSource.CreateLinkedTokenSource(ct)
                deadline.CancelAfter(Math.Clamp(budgetMs, 200, 5000))
                Try
                    Using client = Await TcpConnector.ConnectAsync(address, port, budgetMs, deadline.Token).ConfigureAwait(False)
                        If client Is Nothing Then Return Nothing
                        Dim stream = client.GetStream()
                        Await stream.WriteAsync(HttpRequest(address.ToString(), port, 80, "/ISAPI/System/deviceInfo"), deadline.Token).ConfigureAwait(False)
                        Dim response = Await ReadAsync(stream, MaxGreetingBytes * 4, AddressOf HasHeaderEnd, deadline.Token, ct).ConfigureAwait(False)
                        If response.Length = 0 Then Return Nothing
                        Dim lines = Encoding.UTF8.GetString(response).Split(ControlChars.Lf)
                        Dim auth = Header(lines, "WWW-Authenticate")
                        If auth Is Nothing Then Return Nothing
                        Dim m = RealmPattern.Match(auth)
                        Return If(m.Success, Clean(m.Groups(1).Value, 60), Nothing)
                    End Using
                Catch ex As OperationCanceledException When ct.IsCancellationRequested
                    Throw
                Catch
                    Return Nothing
                End Try
            End Using
        End Function

        ''' <summary>Richiesta GET minima in HTTP/1.0 (chiusura dopo la risposta); l'intestazione Host porta la porta solo se non è quella standard.</summary>
        Private Function HttpRequest(targetHost As String, port As Integer, standardPort As Integer, path As String) As Byte()
            Dim host = If(port = standardPort, targetHost, $"{targetHost}:{port}")
            Return Encoding.ASCII.GetBytes($"GET {path} HTTP/1.0{vbCrLf}Host: {host}{vbCrLf}User-Agent: WhoIsPro{vbCrLf}Accept: text/html,*/*{vbCrLf}Connection: close{vbCrLf}{vbCrLf}")
        End Function

        ''' <summary>Legge finché arriva abbastanza, la connessione si chiude o il tempo finisce; restituisce quanto letto.</summary>
        Private Async Function ReadAsync(stream As Stream, maxBytes As Integer, isEnough As Func(Of Byte(), Integer, Boolean),
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
            Dim text = Encoding.ASCII.GetString(buffer, 0, count)
            Dim close = text.IndexOf("</title>", StringComparison.OrdinalIgnoreCase)
            If close < 0 Then Return False
            ' titolo vuoto (<title></title>): si continua a leggere per trovare un eventuale rinvio via script
            Dim open = text.LastIndexOf("<title", close, StringComparison.OrdinalIgnoreCase)
            Dim start = If(open >= 0, text.IndexOf(">"c, open), -1)
            If start >= 0 AndAlso start < close AndAlso text.Substring(start + 1, close - start - 1).Trim().Length > 0 Then Return True
            Return text.IndexOf("</script>", close, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   text.IndexOf("</html>", close, StringComparison.OrdinalIgnoreCase) >= 0
        End Function

#End Region

#Region "Interpretazione HTTP"

        ''' <summary>Riassunto della risposta web: "Server: nginx · Titolo: DiskStation" (o realm, certificato, rinvio, codice).</summary>
        Private Function DescribeHttp(response As String, Optional certHint As String = Nothing) As String
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
            ' Sulla porta non c'è un server web (es. protocollo binario Hikvision SDK sulla 8000): si scarta invece di restituire byte spuri
            If Not status.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) Then Return Nothing

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
            ElseIf certHint IsNot Nothing Then
                parts.Add("Certificato: " & certHint)
            End If
            If title Is Nothing AndAlso location Is Nothing Then
                ' pagine vuote che rimandano via script o meta refresh (es. Hikvision: window.location.href = "/doc/page/login.asp?_")
                Dim sm = ScriptRedirectPattern.Match(body)
                If sm.Success Then location = sm.Groups(1).Value
            End If
            If title Is Nothing AndAlso location IsNot Nothing Then parts.Add("Rinvio: " & Clean(location, 80))
            If parts.Count = 0 Then
                Dim statusParts = status.Split(" "c, 3, StringSplitOptions.RemoveEmptyEntries)
                parts.Add(If(statusParts.Length > 1, "HTTP " & statusParts(1), "HTTP"))
            End If
            Return String.Join(" · ", parts)
        End Function

        Private Function TryGetCertificateHint(cert As X509Certificate, hostIp As String) As String
            If cert Is Nothing OrElse String.IsNullOrWhiteSpace(cert.Subject) Then Return Nothing
            Dim org As String = Nothing
            Dim cn As String = Nothing
            For Each part In cert.Subject.Split(","c)
                Dim kv = part.Split("="c, 2)
                If kv.Length <> 2 Then Continue For
                Dim key = kv(0).Trim()
                Dim val = kv(1).Trim().Trim(""""c)
                If val.Length = 0 Then Continue For
                If key.Equals("O", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not val.Equals("Default Company Ltd", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not val.Equals("Internet Widgits Pty Ltd", StringComparison.OrdinalIgnoreCase) Then
                    org = val
                ElseIf key.Equals("CN", StringComparison.OrdinalIgnoreCase) AndAlso
                       Not val.Equals(hostIp, StringComparison.OrdinalIgnoreCase) AndAlso
                       Not val.Equals("localhost", StringComparison.OrdinalIgnoreCase) AndAlso
                       Not val.Equals("*", StringComparison.Ordinal) Then
                    cn = val
                End If
            Next
            If org IsNot Nothing AndAlso cn IsNot Nothing AndAlso Not cn.Contains(org, StringComparison.OrdinalIgnoreCase) Then
                Return Clean($"{org} ({cn})", 80)
            End If
            Return Clean(If(org, cn), 80)
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
