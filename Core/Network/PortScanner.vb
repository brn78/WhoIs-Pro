Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports WhoIsPro.Models
Imports WhoIsPro.Utils

Namespace Network
    Public Class PortScanProgress
        Public Property ScannedPorts As Integer
        Public Property TotalPorts As Integer
        Public Property CurrentPort As Integer
        Public Property DiscoveredPort As PortInfo
        Public ReadOnly Property PercentComplete As Integer
            Get
                If TotalPorts = 0 Then Return 0
                Return CInt(Math.Min(100, Math.Max(0, (ScannedPorts * 100.0) / TotalPorts)))
            End Get
        End Property
    End Class

    Public Class PortScanner
        Public Property Concurrency As Integer = 100
        Public Property TimeoutMs As Integer = 1200
        Public Property GrabBanner As Boolean = True

        Public Async Function ScanPortsAsync(targetHost As String,
                                             ports As List(Of Integer),
                                             scanTcp As Boolean,
                                             scanUdp As Boolean,
                                             progress As IProgress(Of PortScanProgress),
                                             cancellationToken As CancellationToken) As Task(Of List(Of PortInfo))
            Dim results As New List(Of PortInfo)()
            Dim tasks As New List(Of Task)()
            Dim totalOperations As Integer = ports.Count * (If(scanTcp, 1, 0) + If(scanUdp, 1, 0))
            Dim scannedCount As Integer = 0
            Dim locker As New Object()

            Using semaphore As New SemaphoreSlim(Concurrency, Concurrency)
                For Each port In ports
                    If cancellationToken.IsCancellationRequested Then Exit For

                    If scanTcp Then
                        Await semaphore.WaitAsync(cancellationToken)
                        Dim p = port
                        tasks.Add(Task.Run(Async Function()
                                               Try
                                                   Dim res = Await ScanTcpPortAsync(targetHost, p, TimeoutMs, GrabBanner, cancellationToken)
                                                   Dim cNow As Integer
                                                   SyncLock locker
                                                       scannedCount += 1
                                                       cNow = scannedCount
                                                       If res IsNot Nothing AndAlso res.Status = "Open" Then
                                                           results.Add(res)
                                                       End If
                                                   End SyncLock

                                                   If progress IsNot Nothing Then
                                                       progress.Report(New PortScanProgress With {
                                                           .ScannedPorts = cNow,
                                                           .TotalPorts = totalOperations,
                                                           .CurrentPort = p,
                                                           .DiscoveredPort = res
                                                       })
                                                   End If
                                               Finally
                                                   semaphore.Release()
                                               End Try
                                           End Function, cancellationToken))
                    End If

                    If scanUdp Then
                        Await semaphore.WaitAsync(cancellationToken)
                        Dim p = port
                        tasks.Add(Task.Run(Async Function()
                                               Try
                                                   Dim res = Await ScanUdpPortAsync(targetHost, p, TimeoutMs, cancellationToken)
                                                   Dim cNow As Integer
                                                   SyncLock locker
                                                       scannedCount += 1
                                                       cNow = scannedCount
                                                       If res IsNot Nothing AndAlso (res.Status = "Open" OrElse res.Status = "Open|Filtered") Then
                                                           results.Add(res)
                                                       End If
                                                   End SyncLock

                                                   If progress IsNot Nothing Then
                                                       progress.Report(New PortScanProgress With {
                                                           .ScannedPorts = cNow,
                                                           .TotalPorts = totalOperations,
                                                           .CurrentPort = p,
                                                           .DiscoveredPort = res
                                                       })
                                                   End If
                                               Finally
                                                   semaphore.Release()
                                               End Try
                                           End Function, cancellationToken))
                    End If
                Next

                Await Task.WhenAll(tasks)
            End Using

            Return results.OrderBy(Function(p) p.PortNumber).ToList()
        End Function

        Private Async Function ScanTcpPortAsync(host As String, port As Integer, timeout As Integer, grabBanner As Boolean, token As CancellationToken) As Task(Of PortInfo)
            Dim sw = Stopwatch.StartNew()
            Dim client As New TcpClient()
            Try
                Dim connectTask = client.ConnectAsync(host, port)
                Dim delayTask = Task.Delay(timeout, token)
                Dim completedTask = Await Task.WhenAny(connectTask, delayTask)

                If completedTask Is connectTask AndAlso client.Connected Then
                    sw.Stop()
                    Dim bannerText As String = ""
                    If grabBanner Then
                        bannerText = Await TryGrabBannerAsync(client, host, port)
                    End If

                    Return New PortInfo With {
                        .Host = host,
                        .PortNumber = port,
                        .Protocol = "TCP",
                        .Status = "Open",
                        .ServiceName = PortDictionary.GetServiceName(port),
                        .Banner = bannerText,
                        .ResponseTimeMs = sw.ElapsedMilliseconds
                    }
                End If
            Catch
            Finally
                Try
                    client.Close()
                    client.Dispose()
                Catch
                End Try
            End Try

            Return New PortInfo With {
                .Host = host,
                .PortNumber = port,
                .Protocol = "TCP",
                .Status = "Closed",
                .ServiceName = PortDictionary.GetServiceName(port),
                .ResponseTimeMs = sw.ElapsedMilliseconds
            }
        End Function

        Private Async Function TryGrabBannerAsync(client As TcpClient, host As String, port As Integer) As Task(Of String)
            Try
                Dim stream = client.GetStream()
                stream.ReadTimeout = 1000
                stream.WriteTimeout = 1000

                ' Se HTTP o porte web, inviamo una richiesta HEAD per ottenere il Server banner
                If port = 80 OrElse port = 8080 OrElse port = 8000 OrElse port = 8123 OrElse port = 8008 Then
                    Dim req = Encoding.ASCII.GetBytes($"HEAD / HTTP/1.1" & vbCrLf & $"Host: {host}" & vbCrLf & "User-Agent: WhoIsPro-Scanner/1.0" & vbCrLf & "Connection: close" & vbCrLf & vbCrLf)
                    Await stream.WriteAsync(req, 0, req.Length)
                End If

                Dim buffer(1024) As Byte
                Dim readTask = stream.ReadAsync(buffer, 0, buffer.Length)
                Dim completed = Await Task.WhenAny(readTask, Task.Delay(800))
                If completed Is readTask Then
                    Dim bytesRead = Await readTask
                    If bytesRead > 0 Then
                        ' Solo testo leggibile: i servizi binari (Telnet, RDP…) mandano byte di controllo
                        Dim chars = Encoding.ASCII.GetString(buffer, 0, bytesRead).Select(Function(c) If(c = ControlChars.Cr OrElse c = ControlChars.Lf OrElse (c >= " "c AndAlso c < ChrW(127)), c, " "c))
                        Dim raw = Regex.Replace(New String(chars.ToArray()), " {2,}", " ").Trim()
                        If raw = "" Then Return $"Risposta binaria: {bytesRead} byte"
                        ' Estrai header o prima riga
                        Dim lines = raw.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                        If lines.Length > 0 Then
                            ' Cerca "Server:"
                            Dim serverLine = lines.FirstOrDefault(Function(l) l.StartsWith("Server:", StringComparison.OrdinalIgnoreCase))
                            If Not String.IsNullOrEmpty(serverLine) Then
                                Return serverLine.Trim()
                            End If
                            Return lines(0).Trim()
                        End If
                    End If
                End If
            Catch
            End Try
            Return ""
        End Function

        Private Async Function ScanUdpPortAsync(host As String, port As Integer, timeout As Integer, token As CancellationToken) As Task(Of PortInfo)
            Dim sw = Stopwatch.StartNew()
            Using udpClient As New UdpClient()
                Try
                    udpClient.Client.ReceiveTimeout = timeout
                    Dim ipAddresses = Await Dns.GetHostAddressesAsync(host)
                    Dim targetIp = ipAddresses.FirstOrDefault(Function(a) a.AddressFamily = AddressFamily.InterNetwork)
                    If targetIp Is Nothing Then Return Nothing

                    Dim endPoint As New IPEndPoint(targetIp, port)
                    Dim probeBytes = GetUdpProbe(port)

                    Await udpClient.SendAsync(probeBytes, probeBytes.Length, endPoint)

                    Dim receiveTask = udpClient.ReceiveAsync()
                    Dim delayTask = Task.Delay(timeout, token)
                    Dim completed = Await Task.WhenAny(receiveTask, delayTask)

                    If completed Is receiveTask Then
                        sw.Stop()
                        Dim recv = Await receiveTask
                        Dim banner As String = If(recv.Buffer IsNot Nothing AndAlso recv.Buffer.Length > 0, $"Risposta: {recv.Buffer.Length} byte", "")
                        Return New PortInfo With {
                            .Host = host,
                            .PortNumber = port,
                            .Protocol = "UDP",
                            .Status = "Open",
                            .ServiceName = PortDictionary.GetServiceName(port),
                            .Banner = banner,
                            .ResponseTimeMs = sw.ElapsedMilliseconds
                        }
                    End If
                Catch ex As SocketException
                    If ex.SocketErrorCode = SocketError.ConnectionReset OrElse ex.SocketErrorCode = SocketError.HostUnreachable OrElse ex.SocketErrorCode = SocketError.NetworkUnreachable Then
                        Return New PortInfo With {
                            .Host = host,
                            .PortNumber = port,
                            .Protocol = "UDP",
                            .Status = "Closed",
                            .ServiceName = PortDictionary.GetServiceName(port),
                            .ResponseTimeMs = sw.ElapsedMilliseconds
                        }
                    End If
                Catch
                End Try
            End Using

            Return New PortInfo With {
                .Host = host,
                .PortNumber = port,
                .Protocol = "UDP",
                .Status = "Open|Filtered",
                .ServiceName = PortDictionary.GetServiceName(port),
                .ResponseTimeMs = sw.ElapsedMilliseconds
            }
        End Function

        Private Function GetUdpProbe(port As Integer) As Byte()
            Select Case port
                Case 53 ' DNS Status Request
                    Return New Byte() {&H0, &H0, &H10, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0}
                Case 123 ' NTP Probe
                    Return New Byte() {&H1B, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0}
                Case 161 ' SNMP GetNext request
                    Return New Byte() {&H30, &H29, &H2, &H1, &H1, &H4, &H6, &H70, &H75, &H62, &H6C, &H69, &H63, &HA1, &H1C, &H2, &H4, &H0, &H0, &H0, &H0, &H2, &H1, &H0, &H2, &H1, &H0, &H30, &HE, &H30, &HC, &H6, &H8, &H2B, &H6, &H1, &H2, &H1, &H1, &H1, &H0, &H5, &H0}
                Case Else
                    Return New Byte() {&H0, &H0, &H0, &H0}
            End Select
        End Function
    End Class
End Namespace