Option Strict On

Imports System.Net.NetworkInformation

Namespace Machine

    ''' <summary>
    ''' Traffico delle schede di rete collegate, per la Panoramica: byte ricevuti e inviati dall'avvio di Windows e
    ''' velocità calcolata dalla lettura precedente. Contatori di Windows (GetIPStatistics): niente diritti di
    ''' amministratore e niente pacchetti catturati.
    ''' </summary>
    Public NotInheritable Class TrafficMonitor

        Private Structure Sample
            Public Rx As Long
            Public Tx As Long
            Public Ticks As Long
            Public RxBps As Double
            Public TxBps As Double
        End Structure

        Private Shared ReadOnly Lock As New Object()
        Private Shared ReadOnly Last As New Dictionary(Of String, Sample)()

        ''' <summary>Sotto questo intervallo la velocità non si ricalcola: due letture ravvicinate darebbero numeri a caso.</summary>
        Private Const MinSeconds As Double = 0.5

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Le schede di rete del PC con la loro configurazione IPv4, la velocità del collegamento e il traffico; anche quelle
        ''' non collegate (connected = False: il Wi-Fi spento, il cavo staccato), dopo quelle collegate.
        ''' </summary>
        Public Shared Function Read() As List(Of Object)
            Dim rows As New List(Of (Connected As Boolean, Gateway As Boolean, Total As Long, Row As Object))()
            Dim nowTicks = Stopwatch.GetTimestamp()
            For Each nic In NetworkInterface.GetAllNetworkInterfaces()
                If Not NetworkConfig.IsRealAdapter(nic) Then Continue For
                Dim rx As Long
                Dim tx As Long
                Try
                    Dim stats = nic.GetIPStatistics()
                    rx = stats.BytesReceived
                    tx = stats.BytesSent
                Catch
                    Continue For
                End Try

                Dim current As New Sample With {.Rx = rx, .Tx = tx, .Ticks = nowTicks}
                SyncLock Lock
                    Dim prev As Sample
                    If Last.TryGetValue(nic.Id, prev) Then
                        Dim seconds = (nowTicks - prev.Ticks) / CDbl(Stopwatch.Frequency)
                        If seconds < MinSeconds Then
                            current = prev
                        Else
                            current.RxBps = Math.Max(0, (rx - prev.Rx) / seconds)
                            current.TxBps = Math.Max(0, (tx - prev.Tx) / seconds)
                        End If
                    End If
                    Last(nic.Id) = current
                End SyncLock

                Dim cfg = NetworkConfig.ReadConfig(nic)
                ' collegata: attiva e con un indirizzo utilizzabile (anche una scheda solo IPv6 è una rete)
                Dim connected = nic.OperationalStatus = OperationalStatus.Up AndAlso
                                (cfg.Ip <> "" OrElse Network.Ipv6.HasPublic(cfg.Ipv6))
                Dim speed As Long = 0
                If connected Then
                    Try
                        speed = nic.Speed
                    Catch
                    End Try
                End If
                rows.Add((connected, cfg.Gateway <> "", rx + tx, New With {
                    .id = nic.Id,
                    .connected = connected,
                    .name = cfg.Name,
                    .description = cfg.Description,
                    .type = TypeName(nic.NetworkInterfaceType),
                    .up = cfg.Up,
                    .dhcp = cfg.Dhcp,
                    .ip = cfg.Ip,
                    .mask = cfg.Mask,
                    .gateway = cfg.Gateway,
                    .dns = cfg.Dns,
                    .mac = cfg.Mac,
                    .ipv6 = cfg.Ipv6,
                    .gateway6 = cfg.Gateway6,
                    .dns6 = cfg.Dns6,
                    .speedMbps = If(speed > 0, speed \ 1000000L, 0L),
                    .rxBps = Math.Round(current.RxBps),
                    .txBps = Math.Round(current.TxBps),
                    .rxTotal = rx,
                    .txTotal = tx
                }))
            Next
            ' prima le collegate, tra queste quelle con il gateway (la rete vera), poi quelle con più traffico
            Return rows.OrderByDescending(Function(r) r.Connected).ThenByDescending(Function(r) r.Gateway).
                ThenByDescending(Function(r) r.Total).Select(Function(r) r.Row).ToList()
        End Function

        Private Shared Function TypeName(t As NetworkInterfaceType) As String
            Select Case t
                Case NetworkInterfaceType.Wireless80211 : Return "Wi-Fi"
                Case NetworkInterfaceType.Ethernet, NetworkInterfaceType.GigabitEthernet, NetworkInterfaceType.FastEthernetT, NetworkInterfaceType.FastEthernetFx, NetworkInterfaceType.Ethernet3Megabit
                    Return "Ethernet"
                Case Else : Return t.ToString()
            End Select
        End Function

    End Class

End Namespace
