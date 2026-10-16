Imports System.Net
Imports System.Net.Sockets
Imports System.Net.NetworkInformation
Imports System.Runtime.InteropServices
Imports WhoIsPro.Models

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
                Dim maskBytes = SubnetMask.GetAddressBytes()
                Dim bits As Integer = 0
                For Each b In maskBytes
                    bits += Convert.ToString(b, 2).Count(Function(c) c = "1"c)
                Next
                Dim ipBytes = IPAddress.GetAddressBytes()
                Dim netBytes(3) As Byte
                For i = 0 To 3
                    netBytes(i) = CByte(ipBytes(i) And maskBytes(i))
                Next
                Dim netIp As New IPAddress(netBytes)
                Return $"{netIp}/{bits}"
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return $"{Name} - {IPAddress} ({SubnetCidr})"
        End Function
    End Class

    Public Module NetworkUtils
        <DllImport("iphlpapi.dll", ExactSpelling:=True)>
        Private Function SendARP(destIp As Integer, srcIp As Integer, pMacAddr As Byte(), ByRef phyAddrLen As Integer) As Integer
        End Function

        Public Function GetMacAddress(ip As IPAddress) As String
            Try
                Dim destIpBytes = ip.GetAddressBytes()
                If destIpBytes.Length <> 4 Then Return "" ' Solo IPv4 supportato per ARP

                Dim destIpInt As Integer = BitConverter.ToInt32(destIpBytes, 0)
                Dim mac(5) As Byte
                Dim macLen As Integer = mac.Length

                Dim retVal As Integer = SendARP(destIpInt, 0, mac, macLen)
                If retVal = 0 AndAlso macLen = 6 Then
                    Return String.Join(":", mac.Select(Function(b) b.ToString("X2")))
                End If
            Catch
            End Try
            Return ""
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

        Public Function ParseTargets(input As String) As List(Of IPAddress)
            Dim result As New List(Of IPAddress)()
            If String.IsNullOrWhiteSpace(input) Then Return result

            Dim targets = input.Split(New Char() {","c, ";"c, " "c}, StringSplitOptions.RemoveEmptyEntries)
            For Each target In targets
                Dim t = target.Trim()
                If t.Contains("/"c) Then
                    ' CIDR es. 192.168.1.0/24
                    result.AddRange(ParseCidr(t))
                ElseIf t.Contains("-"c) Then
                    ' Range es. 192.168.1.1-192.168.1.50 o 192.168.1.1-50
                    result.AddRange(ParseRange(t))
                Else
                    ' Singolo IP o dominio
                    Dim parsedIp As IPAddress = Nothing
                    If IPAddress.TryParse(t, parsedIp) Then
                        result.Add(parsedIp)
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
                    Dim baseIp As IPAddress = IPAddress.Parse(parts(0).Trim())
                    Dim prefixLen As Integer = Integer.Parse(parts(1).Trim())
                    If prefixLen >= 16 AndAlso prefixLen <= 32 Then
                        Dim ipBytes = baseIp.GetAddressBytes()
                        If BitConverter.IsLittleEndian Then Array.Reverse(ipBytes)
                        Dim uintIp As UInteger = BitConverter.ToUInt32(ipBytes, 0)

                        Dim mask As UInteger = If(prefixLen = 0, 0UI, &HFFFFFFFFUI << (32 - prefixLen))
                        Dim netStart As UInteger = uintIp And mask
                        Dim hostCount As UInteger = (1UI << (32 - prefixLen))

                        ' Limitazione di sicurezza a /16 (max 65536 IP)
                        Dim startOffset As UInteger = If(prefixLen >= 31, 0UI, 1UI)
                        Dim endOffset As UInteger = If(prefixLen >= 31, hostCount - 1UI, hostCount - 2UI)

                        For i As UInteger = startOffset To endOffset
                            Dim curIpNum = netStart + i
                            Dim curBytes = BitConverter.GetBytes(curIpNum)
                            If BitConverter.IsLittleEndian Then Array.Reverse(curBytes)
                            list.Add(New IPAddress(curBytes))
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
                    Dim startIpStr = parts(0).Trim()
                    Dim endIpStr = parts(1).Trim()

                    Dim startIp = IPAddress.Parse(startIpStr)
                    Dim endIp As IPAddress

                    If Not endIpStr.Contains("."c) Then
                        ' Formato abbreviato es. 192.168.1.1-50
                        Dim lastOctet As Integer = Integer.Parse(endIpStr)
                        Dim b = startIp.GetAddressBytes()
                        b(3) = CByte(lastOctet)
                        endIp = New IPAddress(b)
                    Else
                        endIp = IPAddress.Parse(endIpStr)
                    End If

                    Dim startBytes = startIp.GetAddressBytes()
                    Dim endBytes = endIp.GetAddressBytes()
                    If BitConverter.IsLittleEndian Then
                        Array.Reverse(startBytes)
                        Array.Reverse(endBytes)
                    End If

                    Dim startUint = BitConverter.ToUInt32(startBytes, 0)
                    Dim endUint = BitConverter.ToUInt32(endBytes, 0)

                    If startUint <= endUint AndAlso (endUint - startUint) <= 65536 Then
                        For u As UInteger = startUint To endUint
                            Dim curBytes = BitConverter.GetBytes(u)
                            If BitConverter.IsLittleEndian Then Array.Reverse(curBytes)
                            list.Add(New IPAddress(curBytes))
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