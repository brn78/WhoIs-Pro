Option Strict On

Imports System.ComponentModel
Imports System.IO
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Text
Imports WhoIsPro.Config

Namespace Machine

    ''' <summary>Configurazione IPv4 attuale di una scheda, letta da Windows.</summary>
    Public Class AdapterConfig
        Public Property Name As String = ""
        Public Property Description As String = ""
        Public Property Up As Boolean
        Public Property Dhcp As Boolean
        Public Property Ip As String = ""
        Public Property Mask As String = ""
        Public Property Gateway As String = ""
        Public Property Dns As New List(Of String)()
        Public Property Mac As String = ""
    End Class

    ''' <summary>
    ''' Cambio rapido dell'indirizzo IP di una scheda (profili di rete): legge la configurazione attuale e la cambia con
    ''' netsh in una finestra elevata (Windows chiede il consenso UAC), così il programma stesso resta senza diritti di
    ''' amministratore.
    ''' </summary>
    Public NotInheritable Class NetworkConfig

        Private Sub New()
        End Sub

        ''' <summary>Tutte le schede fisiche e virtuali (anche scollegate), con la configurazione IPv4 attuale.</summary>
        Public Shared Function ReadAll() As List(Of AdapterConfig)
            Dim list As New List(Of AdapterConfig)()
            For Each nic In NetworkInterface.GetAllNetworkInterfaces()
                If Not IsRealAdapter(nic) Then Continue For
                list.Add(ReadConfig(nic))
            Next
            Return list.OrderByDescending(Function(a) a.Up).ThenBy(Function(a) a.Name).ToList()
        End Function

        ''' <summary>La configurazione IPv4 attuale di una scheda (anche per il traffico della Panoramica).</summary>
        Friend Shared Function ReadConfig(nic As NetworkInterface) As AdapterConfig
            Dim cfg As New AdapterConfig With {
                .Name = nic.Name,
                .Description = nic.Description,
                .Up = nic.OperationalStatus = OperationalStatus.Up,
                .Mac = String.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(Function(b) b.ToString("X2")))
            }
            Try
                Dim props = nic.GetIPProperties()
                Try
                    cfg.Dhcp = props.GetIPv4Properties().IsDhcpEnabled
                Catch
                End Try
                Dim uni = props.UnicastAddresses.FirstOrDefault(Function(u) u.Address.AddressFamily = AddressFamily.InterNetwork)
                If uni IsNot Nothing Then
                    cfg.Ip = uni.Address.ToString()
                    cfg.Mask = If(uni.IPv4Mask?.ToString(), "")
                End If
                Dim gw = props.GatewayAddresses.FirstOrDefault(Function(g) g.Address.AddressFamily = AddressFamily.InterNetwork)
                If gw IsNot Nothing Then cfg.Gateway = gw.Address.ToString()
                cfg.Dns = props.DnsAddresses.Where(Function(d) d.AddressFamily = AddressFamily.InterNetwork).Select(Function(d) d.ToString()).ToList()
            Catch
            End Try
            Return cfg
        End Function

        Private Shared ReadOnly HiddenDescriptions As String() = {
            "WAN Miniport", "Wi-Fi Direct Virtual", "Microsoft Kernel Debug", "Pseudo-Interface", "Loopback", "Teredo", "ISATAP",
            "Bluetooth Device", "Filter", "Npcap", "Hyper-V Virtual Ethernet Adapter #", "QoS Packet"
        }

        ''' <summary>Solo le schede che ha senso configurare: Ethernet e Wi-Fi fisiche o virtuali, niente miniport, tunnel e filtri.</summary>
        Friend Shared Function IsRealAdapter(nic As NetworkInterface) As Boolean
            Select Case nic.NetworkInterfaceType
                Case NetworkInterfaceType.Loopback, NetworkInterfaceType.Tunnel, NetworkInterfaceType.Ppp, NetworkInterfaceType.Unknown
                    Return False
            End Select
            Dim d = If(nic.Description, "")
            Return Not HiddenDescriptions.Any(Function(h) d.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)
        End Function

        ''' <summary>Controlla il profilo prima di applicarlo: nome della scheda, indirizzi e maschera.</summary>
        Public Shared Sub Validate(p As NetworkProfile)
            If String.IsNullOrWhiteSpace(p.Adapter) Then Throw New ArgumentException("Scegli la scheda di rete.")
            If p.Adapter.IndexOfAny({""""c, ControlChars.Cr, ControlChars.Lf}) >= 0 Then Throw New ArgumentException("Nome della scheda non valido.")
            If p.Dhcp Then Return
            If Not IsIPv4(p.Ip) Then Throw New ArgumentException("Indirizzo IP non valido (es. 192.168.1.50).")
            If Not IsIPv4(p.Mask) Then Throw New ArgumentException("Maschera non valida (es. 255.255.255.0).")
            If p.Gateway <> "" AndAlso Not IsIPv4(p.Gateway) Then Throw New ArgumentException("Gateway non valido.")
            If p.Dns1 <> "" AndAlso Not IsIPv4(p.Dns1) Then Throw New ArgumentException("DNS primario non valido.")
            If p.Dns2 <> "" AndAlso Not IsIPv4(p.Dns2) Then Throw New ArgumentException("DNS secondario non valido.")
        End Sub

        ''' <summary>Comandi netsh che applicano il profilo (anche per mostrarli nella pagina).</summary>
        Public Shared Function Commands(p As NetworkProfile) As List(Of String)
            Dim name = $"name=""{p.Adapter}"""
            Dim list As New List(Of String)()
            If p.Dhcp Then
                list.Add($"netsh interface ipv4 set address {name} source=dhcp")
                list.Add($"netsh interface ipv4 set dnsservers {name} source=dhcp")
            Else
                Dim gateway = If(p.Gateway = "", "gateway=none", $"gateway={p.Gateway} gwmetric=1")
                list.Add($"netsh interface ipv4 set address {name} source=static address={p.Ip} mask={p.Mask} {gateway}")
                If p.Dns1 = "" Then
                    list.Add($"netsh interface ipv4 set dnsservers {name} source=static address=none validate=no")
                Else
                    list.Add($"netsh interface ipv4 set dnsservers {name} source=static address={p.Dns1} validate=no")
                    If p.Dns2 <> "" Then list.Add($"netsh interface ipv4 add dnsservers {name} address={p.Dns2} index=2 validate=no")
                End If
            End If
            Return list
        End Function

        ''' <summary>
        ''' Applica il profilo con una finestra elevata (consenso UAC) e aspetta la fine; restituisce il codice di uscita
        ''' di netsh (0 = tutto bene). Se l'utente nega il consenso solleva OperationCanceledException.
        ''' </summary>
        Public Shared Function Apply(p As NetworkProfile, workFolder As String) As Integer
            Validate(p)
            Directory.CreateDirectory(workFolder)
            Dim script = Path.Combine(workFolder, "rete.cmd")
            Dim sb As New StringBuilder()
            sb.AppendLine("@echo off")
            sb.AppendLine("chcp 65001 >nul")
            For Each c In Commands(p)
                sb.AppendLine(c & " || exit /b 1")
            Next
            sb.AppendLine("exit /b 0")
            File.WriteAllText(script, sb.ToString(), New UTF8Encoding(False))
            Dim psi As New ProcessStartInfo("cmd.exe") With {
                .Arguments = $"/c """"{script}""""",
                .UseShellExecute = True,
                .Verb = "runas",
                .WindowStyle = ProcessWindowStyle.Hidden,
                .WorkingDirectory = workFolder
            }
            Try
                Using proc = Process.Start(psi)
                    proc.WaitForExit(60000)
                    Return If(proc.HasExited, proc.ExitCode, -1)
                End Using
            Catch ex As Win32Exception When ex.NativeErrorCode = 1223
                Throw New OperationCanceledException("Consenso negato: la configurazione della scheda non è stata cambiata.")
            End Try
        End Function

        Public Shared Function IsIPv4(value As String) As Boolean
            Dim ip As IPAddress = Nothing
            Return Not String.IsNullOrWhiteSpace(value) AndAlso IPAddress.TryParse(value.Trim(), ip) AndAlso ip.AddressFamily = AddressFamily.InterNetwork
        End Function

    End Class

End Namespace
