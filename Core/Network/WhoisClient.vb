Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions
Imports WhoIsPro.Models

Namespace Network
    Public Class WhoisClient
        Private ReadOnly httpClient As New HttpClient()

        Private ReadOnly TldServers As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"it", "whois.nic.it"},
            {"com", "whois.verisign-grs.com"},
            {"net", "whois.verisign-grs.com"},
            {"org", "whois.pir.org"},
            {"info", "whois.afilias.net"},
            {"biz", "whois.biz"},
            {"eu", "whois.eu"},
            {"de", "whois.denic.de"},
            {"fr", "whois.nic.fr"},
            {"uk", "whois.nominet.uk"},
            {"co.uk", "whois.nominet.uk"},
            {"ch", "whois.nic.ch"},
            {"es", "whois.nic.es"},
            {"nl", "whois.domain-registry.nl"},
            {"io", "whois.nic.io"},
            {"ai", "whois.nic.ai"},
            {"me", "whois.nic.me"},
            {"tv", "tvwhois.verisign-grs.com"},
            {"cc", "ccwhois.verisign-grs.com"},
            {"dev", "whois.nic.google"},
            {"app", "whois.nic.google"},
            {"cloud", "whois.nic.cloud"},
            {"online", "whois.nic.online"}
        }

        Public Async Function QueryAsync(target As String, Optional preferredServer As String = "") As Task(Of WhoisResult)
            Dim sw = Stopwatch.StartNew()
            Dim res As New WhoisResult With {
                .QueryTarget = target.Trim()
            }

            If String.IsNullOrWhiteSpace(target) Then
                res.ErrorMessage = "Target WHOIS non specificato"
                Return res
            End If

            Dim cleanTarget As String = target.Trim().ToLowerInvariant().Replace("http://", "").Replace("https://", "").Split("/"c)(0)
            res.QueryTarget = cleanTarget

            Dim isIp As Boolean = False
            Dim parsedIp As IPAddress = Nothing
            If IPAddress.TryParse(cleanTarget, parsedIp) Then
                isIp = True
            End If

            Dim initialServer As String = preferredServer
            If String.IsNullOrWhiteSpace(initialServer) Then
                If isIp Then
                    initialServer = "whois.arin.net"
                Else
                    Dim tld = GetTld(cleanTarget)
                    If TldServers.ContainsKey(tld) Then
                        initialServer = TldServers(tld)
                    Else
                        initialServer = "whois.iana.org"
                    End If
                End If
            End If

            Dim needFallback As Boolean = False
            Dim initialErrorMsg As String = ""

            Try
                ' 1. Query al server primario
                Dim raw = Await RawTcpQueryAsync(initialServer, cleanTarget)
                res.ServerUsed = initialServer
                res.RawText = raw

                ' 2. Controllo Referrals (es. Verisign -> Registrar Whois Server)
                Dim referralServer = ExtractReferral(raw)
                If Not String.IsNullOrEmpty(referralServer) AndAlso Not referralServer.Equals(initialServer, StringComparison.OrdinalIgnoreCase) Then
                    res.ReferralServer = referralServer
                    Try
                        Dim secondaryRaw = Await RawTcpQueryAsync(referralServer, cleanTarget)
                        If Not String.IsNullOrWhiteSpace(secondaryRaw) AndAlso secondaryRaw.Length > 100 Then
                            res.RawText = secondaryRaw & vbCrLf & vbCrLf & "=== QUERY INIZIALE TLD DA " & initialServer & " ===" & vbCrLf & raw
                            res.ServerUsed = $"{initialServer} -> {referralServer}"
                        End If
                    Catch
                    End Try
                End If

                ' 3. Parsing Campi Standard
                ParseWhoisFields(res)
                res.IsSuccess = True
            Catch ex As Exception
                needFallback = True
                initialErrorMsg = ex.Message
            End Try

            If needFallback Then
                ' Fallback RDAP fuori dal Catch per rispetto sintassi VB.NET Await
                Try
                    Dim rdapSuccess = Await TryRdapFallbackAsync(res, cleanTarget, isIp)
                    If rdapSuccess Then
                        res.IsSuccess = True
                    Else
                        res.ErrorMessage = $"Errore query WHOIS: {initialErrorMsg}"
                    End If
                Catch rdapEx As Exception
                    res.ErrorMessage = $"Errore query WHOIS (Porta 43 e RDAP): {initialErrorMsg} / {rdapEx.Message}"
                End Try
            End If

            sw.Stop()
            res.QueryTimeMs = sw.ElapsedMilliseconds
            Return res
        End Function

        Private Async Function RawTcpQueryAsync(server As String, query As String) As Task(Of String)
            Using client As New TcpClient()
                client.ReceiveTimeout = 6000
                client.SendTimeout = 6000
                Await client.ConnectAsync(server, 43)

                Using stream = client.GetStream()
                    Using reader As New StreamReader(stream, Encoding.UTF8)
                        Dim queryBytes = Encoding.ASCII.GetBytes(query & vbCrLf)
                        Await stream.WriteAsync(queryBytes, 0, queryBytes.Length)

                        Dim sb As New StringBuilder()
                        Dim buffer(4096) As Char
                        Dim bytesRead As Integer

                        While True
                            bytesRead = Await reader.ReadAsync(buffer, 0, buffer.Length)
                            If bytesRead <= 0 Then Exit While
                            sb.Append(buffer, 0, bytesRead)
                        End While

                        Return sb.ToString()
                    End Using
                End Using
            End Using
        End Function

        Private Function ExtractReferral(raw As String) As String
            Dim match = Regex.Match(raw, "Registrar WHOIS Server:\s*([^\r\n]+)", RegexOptions.IgnoreCase)
            If match.Success Then Return match.Groups(1).Value.Trim()

            match = Regex.Match(raw, "whois:\s*([^\r\n]+)", RegexOptions.IgnoreCase)
            If match.Success Then Return match.Groups(1).Value.Trim()

            match = Regex.Match(raw, "ReferralServer:\s*rwhois://([^\r\n:]+)", RegexOptions.IgnoreCase)
            If match.Success Then Return match.Groups(1).Value.Trim()

            Return ""
        End Function

        Private Sub ParseWhoisFields(res As WhoisResult)
            Dim lines = res.RawText.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            For Each line In lines
                Dim l = line.Trim()
                If l.StartsWith("Registrar:", StringComparison.OrdinalIgnoreCase) Then
                    res.Registrar = l.Substring(10).Trim()
                ElseIf l.StartsWith("Creation Date:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("Created:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("Registration Time:", StringComparison.OrdinalIgnoreCase) Then
                    Dim val = l.Substring(l.IndexOf(":"c) + 1).Trim()
                    If res.CreationDate = "N/D" Then res.CreationDate = val
                ElseIf l.StartsWith("Registry Expiry Date:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("Expiration Date:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("Expire:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("paid-till:", StringComparison.OrdinalIgnoreCase) Then
                    Dim val = l.Substring(l.IndexOf(":"c) + 1).Trim()
                    If res.ExpirationDate = "N/D" Then res.ExpirationDate = val
                ElseIf l.StartsWith("Updated Date:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("Last Update:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("Modified:", StringComparison.OrdinalIgnoreCase) Then
                    Dim val = l.Substring(l.IndexOf(":"c) + 1).Trim()
                    If res.UpdatedDate = "N/D" Then res.UpdatedDate = val
                ElseIf l.StartsWith("Registrant Organization:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("org-name:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("OrgName:", StringComparison.OrdinalIgnoreCase) Then
                    Dim val = l.Substring(l.IndexOf(":"c) + 1).Trim()
                    If res.RegistrantOrg = "N/D" Then res.RegistrantOrg = val
                ElseIf l.StartsWith("Registrant Country:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("Country:", StringComparison.OrdinalIgnoreCase) Then
                    Dim val = l.Substring(l.IndexOf(":"c) + 1).Trim()
                    If res.Country = "N/D" Then res.Country = val
                ElseIf l.StartsWith("Name Server:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("nserver:", StringComparison.OrdinalIgnoreCase) Then
                    Dim ns = l.Substring(l.IndexOf(":"c) + 1).Trim().ToLowerInvariant().Split(" "c)(0)
                    If Not res.NameServers.Contains(ns) Then res.NameServers.Add(ns)
                ElseIf l.StartsWith("Domain Status:", StringComparison.OrdinalIgnoreCase) OrElse l.StartsWith("status:", StringComparison.OrdinalIgnoreCase) Then
                    Dim st = l.Substring(l.IndexOf(":"c) + 1).Trim().Split(" "c)(0)
                    If Not res.DomainStatus.Contains(st) Then res.DomainStatus.Add(st)
                End If
            Next
        End Sub

        Private Async Function TryRdapFallbackAsync(res As WhoisResult, target As String, isIp As Boolean) As Task(Of Boolean)
            Dim rdapUrl As String
            If isIp Then
                rdapUrl = $"https://rdap.arin.net/registry/ip/{target}"
            Else
                rdapUrl = $"https://rdap.org/domain/{target}"
            End If

            httpClient.Timeout = TimeSpan.FromSeconds(6)
            Dim response = Await httpClient.GetAsync(rdapUrl)
            If response.IsSuccessStatusCode Then
                Dim json = Await response.Content.ReadAsStringAsync()
                res.ServerUsed = $"RDAP HTTP ({rdapUrl})"
                res.RawText = json
                res.Registrar = "RDAP Response (JSON Formatted)"
                Return True
            End If
            Return False
        End Function

        Private Function GetTld(domain As String) As String
            Dim parts = domain.Split("."c)
            If parts.Length >= 2 Then
                If parts.Length >= 3 AndAlso (parts(parts.Length - 2) = "co" OrElse parts(parts.Length - 2) = "com") Then
                    Return parts(parts.Length - 2) & "." & parts(parts.Length - 1)
                End If
                Return parts(parts.Length - 1)
            End If
            Return ""
        End Function
    End Class
End Namespace