Imports System.Net
Imports System.Net.Sockets
Imports System.Net.NetworkInformation
Imports System.Text.RegularExpressions
Imports WhoIsPro.Models
Imports WhoIsPro.Network.Scan

Namespace Utils
    Public Class LocalAdapterInfo
        Public Property Name As String
        Public Property Description As String
        Public Property IPAddress As IPAddress
        Public Property SubnetMask As IPAddress
        Public Property Gateway As IPAddress
        Public Property MacAddress As String
        ''' <summary>Indirizzi IPv6 della stessa scheda.</summary>
        Public Property Ipv6 As New List(Of Network.Ipv6Address)()

        Public ReadOnly Property SubnetCidr As String
            Get
                If IPAddress Is Nothing OrElse SubnetMask Is Nothing Then Return ""
                Dim bits = Ipv4.PrefixFromMask(SubnetMask)
                Dim net = Ipv4.ToUInt32(IPAddress) And Ipv4.Mask(bits)
                Return $"{Ipv4.Format(net)}/{bits}"
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"{Name} - {IPAddress} ({SubnetCidr})"
        End Function
    End Class

    Public Module NetworkUtils
        Public Function GetMacAddress(ip As IPAddress) As String
            If ip Is Nothing OrElse ip.AddressFamily <> AddressFamily.InterNetwork Then Return ""
            Return If(IpHelper.SendArp(Ipv4.ToUInt32(ip)), "")
        End Function

        Public Function GetLocalAdapters() As List(Of LocalAdapterInfo)
            Dim list As New List(Of LocalAdapterInfo)()
            Try
                For Each nic In NetworkInterface.GetAllNetworkInterfaces()
                    If nic.OperationalStatus = OperationalStatus.Up AndAlso
                       nic.NetworkInterfaceType <> NetworkInterfaceType.Loopback Then
                        Dim ipProps = nic.GetIPProperties()
                        For Each uni In ipProps.UnicastAddresses
                            If uni.Address.AddressFamily = AddressFamily.InterNetwork Then
                                Dim gw = ipProps.GatewayAddresses.FirstOrDefault(Function(g) g.Address.AddressFamily = AddressFamily.InterNetwork)
                                Dim info As New LocalAdapterInfo With {
                                    .Name = nic.Name,
                                    .Description = nic.Description,
                                    .IPAddress = uni.Address,
                                    .SubnetMask = uni.IPv4Mask,
                                    .Gateway = If(gw IsNot Nothing, gw.Address, Nothing),
                                    .MacAddress = MacVendorLookup.FormatMac(nic.GetPhysicalAddress().GetAddressBytes()),
                                    .Ipv6 = Network.Ipv6.AddressesOf(ipProps)
                                }
                                list.Add(info)
                            End If
                        Next
                    End If
                Next
            Catch
            End Try
            Return list
        End Function

        Private ReadOnly ThreeOctetsPattern As New Regex("^\d{1,3}\.\d{1,3}\.\d{1,3}\.?\*?$", RegexOptions.CultureInvariant Or RegexOptions.Compiled)
        Private ReadOnly SpacedSeparatorPattern As New Regex("\s*([/-])\s*", RegexOptions.CultureInvariant Or RegexOptions.Compiled)

        ''' <summary>
        ''' Le parti di un bersaglio con più reti o intervalli («192.168.1.0/24, 10.0.0.0/24»): separatori virgola, punto e
        ''' virgola o spazio; gli spazi attorno a «/» e «-» non contano. Usata dal controllo della pagina e dal motore di scansione.
        ''' </summary>
        Public Function SplitTargets(input As String) As String()
            If String.IsNullOrWhiteSpace(input) Then Return Array.Empty(Of String)()
            Return SpacedSeparatorPattern.Replace(input.Trim(), "$1").Split(New Char() {","c, ";"c, " "c}, StringSplitOptions.RemoveEmptyEntries Or StringSplitOptions.TrimEntries)
        End Function

        Public Function ParseTargets(input As String) As List(Of IPAddress)
            Dim result As New List(Of IPAddress)()
            For Each target In SplitTargets(input)
                Dim t = target.Trim()
                If ThreeOctetsPattern.IsMatch(t) Then
                    ' Forma abbreviata es. "192.168.30" o "192.168.30.*" -> /24
                    result.AddRange(ParseCidr(t.TrimEnd("."c, "*"c).TrimEnd("."c) & ".0/24"))
                ElseIf t.Contains("/"c) Then
                    ' CIDR es. 192.168.1.0/24
                    result.AddRange(ParseCidr(t))
                ElseIf t.Contains("-"c) Then
                    ' Range es. 192.168.1.1-192.168.1.50 o 192.168.1.1-50
                    result.AddRange(ParseRange(t))
                Else
                    ' Singolo IP, indirizzo di rete .0 (-> /24) o dominio
                    Dim parsedIp As IPAddress = Nothing
                    If IPAddress.TryParse(t, parsedIp) AndAlso parsedIp.AddressFamily = AddressFamily.InterNetwork Then
                        Dim b = parsedIp.GetAddressBytes()
                        If b(3) = 0 Then
                            result.AddRange(ParseCidr($"{parsedIp}/24"))
                        Else
                            result.Add(parsedIp)
                        End If
                    Else
                        Try
                            Dim resolved = Dns.GetHostAddresses(t)
                            For Each rip In resolved
                                If rip.AddressFamily = AddressFamily.InterNetwork Then
                                    result.Add(rip)
                                End If
                            Next
                        Catch
                        End Try
                    End If
                End If
            Next

            Return result.Distinct().ToList()
        End Function

        Private Function ParseCidr(cidr As String) As List(Of IPAddress)
            Dim list As New List(Of IPAddress)()
            Try
                Dim parts = cidr.Split("/"c)
                If parts.Length = 2 Then
                    Dim uintIp As UInteger = 0
                    Dim prefixLen As Integer = 0
                    If Ipv4.TryParse(parts(0), uintIp) AndAlso Integer.TryParse(parts(1).Trim(), prefixLen) AndAlso
                       prefixLen >= 16 AndAlso prefixLen <= 32 Then
                        Dim netStart As UInteger = uintIp And Ipv4.Mask(prefixLen)
                        Dim hostCount As UInteger = (1UI << (32 - prefixLen))

                        ' Limitazione di sicurezza a /16 (max 65536 IP)
                        Dim startOffset As UInteger = If(prefixLen >= 31, 0UI, 1UI)
                        Dim endOffset As UInteger = If(prefixLen >= 31, hostCount - 1UI, hostCount - 2UI)

                        For i As UInteger = startOffset To endOffset
                            list.Add(Ipv4.ToAddress(netStart + i))
                        Next
                    End If
                End If
            Catch
            End Try
            Return list
        End Function

        Private Function ParseRange(rangeStr As String) As List(Of IPAddress)
            Dim list As New List(Of IPAddress)()
            Try
                Dim parts = rangeStr.Split("-"c)
                If parts.Length = 2 Then
                    Dim startUint As UInteger = 0
                    If Not Ipv4.TryParse(parts(0), startUint) Then Return list

                    Dim endIpStr = parts(1).Trim()
                    Dim endUint As UInteger = 0

                    If Not endIpStr.Contains("."c) Then
                        ' Formato abbreviato es. 192.168.1.1-50
                        Dim lastOctet As Integer = 0
                        If Not Integer.TryParse(endIpStr, lastOctet) OrElse lastOctet < 0 OrElse lastOctet > 255 Then Return list
                        endUint = (startUint And &HFFFFFF00UI) Or CUInt(lastOctet)
                    ElseIf Not Ipv4.TryParse(endIpStr, endUint) Then
                        Return list
                    End If

                    If startUint <= endUint AndAlso (endUint - startUint) <= 65536UI Then
                        For u As UInteger = startUint To endUint
                            list.Add(Ipv4.ToAddress(u))
                        Next
                    End If
                End If
            Catch
            End Try
            Return list
        End Function

        ''' <summary>
        ''' Filtro intelligente multi-token per la ricerca di host (IP, MAC, Produttore, Tipologia, Hostname, OS).
        ''' Supporta token multipli con logica AND e ricerca MAC flessibile (con o senza separatori).
        ''' </summary>
        Public Function MatchesHostFilter(h As HostInfo, query As String) As Boolean
            If String.IsNullOrWhiteSpace(query) Then Return True
            If h Is Nothing Then Return False

            Dim tokens = query.Split({" "c, ","c, ";"c}, StringSplitOptions.RemoveEmptyEntries)
            If tokens.Length = 0 Then Return True

            For Each token In tokens
                Dim cleanToken = token.Trim()
                If cleanToken.Length = 0 Then Continue For

                Dim tokenMatched = False

                ' Verifica Indirizzo IP
                If Not String.IsNullOrEmpty(h.IPAddress) AndAlso h.IPAddress.IndexOf(cleanToken, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    tokenMatched = True
                ' Verifica Hostname
                ElseIf Not String.IsNullOrEmpty(h.Hostname) AndAlso h.Hostname.IndexOf(cleanToken, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    tokenMatched = True
                ' Verifica Produttore MAC
                ElseIf Not String.IsNullOrEmpty(h.MacVendor) AndAlso h.MacVendor.IndexOf(cleanToken, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    tokenMatched = True
                ' Verifica Tipologia Dispositivo
                ElseIf Not String.IsNullOrEmpty(h.DeviceType) AndAlso h.DeviceType.IndexOf(cleanToken, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    tokenMatched = True
                ' Verifica Sistema Operativo Stimato
                ElseIf Not String.IsNullOrEmpty(h.OSGuess) AndAlso h.OSGuess.IndexOf(cleanToken, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    tokenMatched = True
                ' Verifica Indirizzo MAC (formato standard o senza separatori)
                ElseIf Not String.IsNullOrEmpty(h.MacAddress) Then
                    If h.MacAddress.IndexOf(cleanToken, StringComparison.OrdinalIgnoreCase) >= 0 Then
                        tokenMatched = True
                    Else
                        Dim strippedMac = h.MacAddress.Replace(":", "").Replace("-", "").Replace(".", "")
                        Dim strippedToken = cleanToken.Replace(":", "").Replace("-", "").Replace(".", "")
                        If strippedToken.Length > 0 AndAlso strippedMac.IndexOf(strippedToken, StringComparison.OrdinalIgnoreCase) >= 0 Then
                            tokenMatched = True
                        End If
                    End If
                End If

                If Not tokenMatched Then
                    Return False
                End If
            Next

            Return True
        End Function
    End Module
End Namespace