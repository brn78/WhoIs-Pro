Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Text
Imports System.Threading
Imports WhoIsPro.Models

Namespace Network
    Public Class TracerouteEngine
        Public Property MaxHops As Integer = 30
        Public Property TimeoutMs As Integer = 1500

        Public Async Function TraceAsync(targetHost As String,
                                         progress As IProgress(Of TracerouteHop),
                                         cancellationToken As CancellationToken) As Task(Of List(Of TracerouteHop))
            Dim hops As New List(Of TracerouteHop)()
            If String.IsNullOrWhiteSpace(targetHost) Then Return hops

            Dim targetIp As IPAddress = Nothing
            Try
                Dim addresses = Await Dns.GetHostAddressesAsync(targetHost)
                targetIp = addresses.FirstOrDefault(Function(a) a.AddressFamily = Sockets.AddressFamily.InterNetwork)
                If targetIp Is Nothing Then Return hops
            Catch
                Return hops
            End Try

            Dim buffer = Encoding.ASCII.GetBytes("WhoIsProTraceroutePayloadPacket")

            For ttl = 1 To MaxHops
                If cancellationToken.IsCancellationRequested Then Exit For

                Dim hopInfo As New TracerouteHop With {
                    .HopNumber = ttl
                }

                Using ping As New Ping()
                    Dim options As New PingOptions(ttl, True)
                    Dim reachedTarget As Boolean = False
                    Dim hopIp As IPAddress = Nothing

                    ' 3 sonde per hop
                    For p = 1 To 3
                        If cancellationToken.IsCancellationRequested Then Exit For
                        Try
                            ' Per le risposte "TTL scaduto" Windows non riporta il tempo: lo misura il cronometro
                            Dim sw = Stopwatch.StartNew()
                            Dim reply = Await ping.SendPingAsync(targetIp, TimeoutMs, buffer, options)
                            sw.Stop()
                            If reply.Status = IPStatus.Success Then
                                hopInfo.RoundTripTimes.Add(reply.RoundtripTime)
                                hopIp = reply.Address
                                reachedTarget = True
                            ElseIf reply.Status = IPStatus.TtlExpired Then
                                hopInfo.RoundTripTimes.Add(If(reply.RoundtripTime > 0, reply.RoundtripTime, sw.ElapsedMilliseconds))
                                hopIp = reply.Address
                            Else
                                hopInfo.RoundTripTimes.Add(-1) ' Timeout
                            End If
                        Catch
                            hopInfo.RoundTripTimes.Add(-1)
                        End Try
                    Next

                    If hopIp IsNot Nothing Then
                        hopInfo.IPAddress = hopIp.ToString()
                        Try
                            Dim entry = Await Dns.GetHostEntryAsync(hopIp)
                            hopInfo.Hostname = entry.HostName
                        Catch
                            hopInfo.Hostname = ""
                        End Try
                        hopInfo.Status = "OK"
                    Else
                        hopInfo.IPAddress = "* * *"
                        hopInfo.Hostname = "Richiesta scaduta"
                        hopInfo.Status = "TimedOut"
                    End If

                    hops.Add(hopInfo)
                    If progress IsNot Nothing Then
                        progress.Report(hopInfo)
                    End If

                    If reachedTarget Then Exit For
                End Using
            Next

            Return hops
        End Function
    End Class
End Namespace