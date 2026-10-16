Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Threading
Imports WhoIsPro.Models
Imports WhoIsPro.Utils

Namespace Network
    Public Class ScanProgress
        Public Property ScannedCount As Integer
        Public Property TotalCount As Integer
        Public Property CurrentIp As String
        Public Property DiscoveredHost As HostInfo
        Public ReadOnly Property PercentComplete As Integer
            Get
                If TotalCount = 0 Then Return 0
                Return CInt(Math.Min(100, Math.Max(0, (ScannedCount * 100.0) / TotalCount)))
            End Get
        End Property
    End Class

    Public Class NetworkScanner
        Public Property Concurrency As Integer = 80
        Public Property TimeoutMs As Integer = 900
        Public Property ResolveHostnames As Boolean = True

        Public Async Function ScanRangeAsync(targets As List(Of IPAddress),
                                             progress As IProgress(Of ScanProgress),
                                             cancellationToken As CancellationToken) As Task(Of List(Of HostInfo))
            Dim discoveredHosts As New List(Of HostInfo)()
            Dim total As Integer = targets.Count
            Dim scanned As Integer = 0
            Dim locker As New Object()

            Using semaphore As New SemaphoreSlim(Concurrency, Concurrency)
                Dim tasks As New List(Of Task)()

                For Each ip In targets
                    If cancellationToken.IsCancellationRequested Then Exit For
                    Await semaphore.WaitAsync(cancellationToken)

                    Dim currentIp = ip
                    Dim scanTask As Task = Task.Run(Async Function()
                                            Try
                                                Dim host = Await PingAndInspectHostAsync(currentIp, TimeoutMs, ResolveHostnames, cancellationToken)
                                                Dim countNow As Integer
                                                SyncLock locker
                                                    scanned += 1
                                                    countNow = scanned
                                                    If host IsNot Nothing Then
                                                        discoveredHosts.Add(host)
                                                    End If
                                                End SyncLock

                                                If progress IsNot Nothing Then
                                                    progress.Report(New ScanProgress With {
                                                        .ScannedCount = countNow,
                                                        .TotalCount = total,
                                                        .CurrentIp = currentIp.ToString(),
                                                        .DiscoveredHost = host
                                                    })
                                                End If
                                            Finally
                                                semaphore.Release()
                                            End Try
                                        End Function, cancellationToken)
                    tasks.Add(scanTask)
                Next

                Await Task.WhenAll(tasks)
            End Using

            Return discoveredHosts.OrderBy(Function(h) IPAddressToLong(h.IPAddress)).ToList()
        End Function

        Private Async Function PingAndInspectHostAsync(ip As IPAddress, timeout As Integer, resolveDns As Boolean, token As CancellationToken) As Task(Of HostInfo)
            Dim isAlive As Boolean = False
            Dim rtt As Long = 0
            Dim ttl As Integer = 0

            ' 1. Tentativo ARP (immediato e infallibile su rete locale IPv4)
            Dim mac As String = NetworkUtils.GetMacAddress(ip)
            Dim isLocalArpAlive As Boolean = Not String.IsNullOrEmpty(mac)

            ' 2. Tentativo ICMP Ping
            Try
                Using pingSender As New Ping()
                    Dim reply = Await pingSender.SendPingAsync(ip, timeout)
                    If reply.Status = IPStatus.Success Then
                        isAlive = True
                        rtt = reply.RoundtripTime
                        If reply.Options IsNot Nothing Then
                            ttl = reply.Options.Ttl
                        End If
                    End If
                End Using
            Catch
            End Try

            ' Se risponde a Ping o ad ARP, l'host è attivo
            If Not isAlive AndAlso Not isLocalArpAlive Then
                Return Nothing
            End If

            Dim hostInfo As New HostInfo With {
                .IPAddress = ip.ToString(),
                .Status = "Online",
                .ResponseTimeMs = rtt,
                .TTL = ttl,
                .MacAddress = mac
            }

            ' Calcolo OS stimato da TTL
            If ttl > 0 Then
                If ttl <= 64 Then
                    hostInfo.OSGuess = "Linux / Android / iOS / macOS"
                ElseIf ttl <= 128 Then
                    hostInfo.OSGuess = "Windows (PC / Server)"
                Else
                    hostInfo.OSGuess = "Cisco / Router / Network Device"
                End If
            Else
                hostInfo.OSGuess = If(isLocalArpAlive, "Host LAN", "Dispositivo Remoto")
            End If

            ' MAC Vendor lookup
            If Not String.IsNullOrEmpty(mac) Then
                hostInfo.MacVendor = MacVendorLookup.GetVendor(mac)
            Else
                hostInfo.MacVendor = "N/D (Remoto / Routed)"
            End If

            ' Risoluzione Hostname
            If resolveDns Then
                Try
                    Dim hostEntry = Await Dns.GetHostEntryAsync(ip)
                    hostInfo.Hostname = hostEntry.HostName
                Catch
                    hostInfo.Hostname = ""
                End Try
            End If

            ' Classificazione Intelligente Tipologia Dispositivo
            Dim classification = DeviceClassifier.Classify(hostInfo.MacVendor, hostInfo.Hostname, Nothing, ttl, mac)
            hostInfo.DeviceType = classification.DeviceType
            hostInfo.DeviceCategory = classification.Category
            hostInfo.DeviceIcon = classification.Icon

            Return hostInfo
        End Function

        Private Shared Function IPAddressToLong(ipStr As String) As Long
            Dim ip As IPAddress = Nothing
            If IPAddress.TryParse(ipStr, ip) Then
                Dim bytes = ip.GetAddressBytes()
                If bytes.Length = 4 Then
                    Return (CLng(bytes(0)) << 24) Or (CLng(bytes(1)) << 16) Or (CLng(bytes(2)) << 8) Or CLng(bytes(3))
                End If
            End If
            Return 0
        End Function
    End Class
End Namespace