Option Strict On

Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Runtime.InteropServices

Namespace Network

    ''' <summary>Un indirizzo IPv6 di una scheda di questo PC, con il suo tipo.</summary>
    Public Class Ipv6Address
        Public Property Address As String = ""
        Public Property PrefixLength As Integer
        ''' <summary>"globale", "temporaneo" (privacy, cambia da solo), "locale" (ULA, fd00::/8) oppure "link-local" (fe80::).</summary>
        Public Property Kind As String = ""
    End Class

    ''' <summary>
    ''' IPv6: gli indirizzi delle schede di questo PC e quelli dei dispositivi della rete, presi dalla tabella dei vicini di
    ''' Windows (il corrispondente IPv6 dell'ARP) e associati per MAC agli host trovati con la scansione IPv4.
    ''' </summary>
    Public Module Ipv6

        ''' <summary>Indirizzo senza l'indice della scheda («fe80::1%12» → «fe80::1»).</summary>
        Public Function Plain(address As IPAddress) As String
            If address Is Nothing Then Return ""
            If address.AddressFamily <> AddressFamily.InterNetworkV6 Then Return address.ToString()
            Return New IPAddress(address.GetAddressBytes()).ToString()
        End Function

        Public Function KindOf(address As IPAddress, Optional temporary As Boolean = False) As String
            If address.IsIPv6LinkLocal Then Return "link-local"
            Dim first = address.GetAddressBytes()(0)
            If (first And &HFE) = &HFC Then Return "locale"
            If temporary Then Return "temporaneo"
            Return "globale"
        End Function

        ''' <summary>Indirizzi IPv6 di una scheda: prima i globali, poi temporanei, locali e link-local.</summary>
        Public Function AddressesOf(props As IPInterfaceProperties) As List(Of Ipv6Address)
            Dim list As New List(Of Ipv6Address)()
            For Each uni In props.UnicastAddresses
                If uni.Address.AddressFamily <> AddressFamily.InterNetworkV6 OrElse IPAddress.IsLoopback(uni.Address) Then Continue For
                Dim temporary = False
                Dim prefix = 64
                Try
                    temporary = uni.SuffixOrigin = SuffixOrigin.Random
                    prefix = uni.PrefixLength
                Catch
                End Try
                list.Add(New Ipv6Address With {.Address = Plain(uni.Address), .PrefixLength = prefix, .Kind = KindOf(uni.Address, temporary)})
            Next
            Dim order = New String() {"globale", "temporaneo", "locale", "link-local"}
            Return list.OrderBy(Function(a) Array.IndexOf(order, a.Kind)).ThenBy(Function(a) a.Address).ToList()
        End Function

        ''' <summary>Vero se la scheda ha un IPv6 utilizzabile su Internet (globale o temporaneo).</summary>
        Public Function HasPublic(list As IEnumerable(Of Ipv6Address)) As Boolean
            Return list.Any(Function(a) a.Kind = "globale" OrElse a.Kind = "temporaneo")
        End Function

#Region "Tabella dei vicini (GetIpNetTable2)"

        <DllImport("iphlpapi.dll")>
        Private Function GetIpNetTable2(family As UShort, ByRef table As IntPtr) As Integer
        End Function

        <DllImport("iphlpapi.dll")>
        Private Sub FreeMibTable(memory As IntPtr)
        End Sub

        Private Const AF_INET6 As UShort = 23
        ' MIB_IPNET_ROW2: SOCKADDR_INET (28) · InterfaceIndex (4) · InterfaceLuid (8, allineato a 8) · PhysicalAddress[32] ·
        ' PhysicalAddressLength (4) · State (4) · Flags (1, poi allineamento) · ReachabilityTime (4) = 88 byte
        Private Const RowSize As Integer = 88
        Private Const OffsetAddress As Integer = 8      ' sin6_addr dentro SOCKADDR_IN6
        Private Const OffsetMac As Integer = 40
        Private Const OffsetMacLength As Integer = 72
        Private Const OffsetState As Integer = 76
        ' NL_NEIGHBOR_STATE: 0 irraggiungibile, 1 incompleto, 2 verifica, 3 attesa, 4 non recente, 5 raggiungibile, 6 permanente
        Private Const StateProbe As Integer = 2
        Private Const StateReachable As Integer = 5

        ''' <summary>
        ''' Vicini IPv6 di questo PC: MAC («AA:BB:…», come gli host della scansione) → indirizzi IPv6. Solo i vicini con un MAC
        ''' vero e visti di recente; niente multicast né voci permanenti.
        ''' </summary>
        Public Function Neighbors() As Dictionary(Of String, List(Of String))
            Dim result As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
            Dim table As IntPtr = IntPtr.Zero
            Try
                If GetIpNetTable2(AF_INET6, table) <> 0 OrElse table = IntPtr.Zero Then Return result
                Dim count = Marshal.ReadInt32(table)
                Dim rows = table + 8      ' NumEntries e allineamento a 8 byte
                For i = 0 To count - 1
                    Dim row = rows + i * RowSize
                    Dim state = Marshal.ReadInt32(row + OffsetState)
                    If state < StateProbe OrElse state > StateReachable Then Continue For
                    If Marshal.ReadInt32(row + OffsetMacLength) <> 6 Then Continue For
                    Dim mac(5) As Byte
                    Marshal.Copy(row + OffsetMac, mac, 0, 6)
                    If mac.All(Function(b) b = 0) OrElse (mac(0) = &H33 AndAlso mac(1) = &H33) Then Continue For   ' 33:33:… = multicast
                    Dim raw(15) As Byte
                    Marshal.Copy(row + OffsetAddress, raw, 0, 16)
                    Dim address As New IPAddress(raw)
                    If raw(0) = &HFF OrElse IPAddress.IsLoopback(address) Then Continue For
                    Dim key = String.Join(":", mac.Select(Function(b) b.ToString("X2")))
                    Dim list As List(Of String) = Nothing
                    If Not result.TryGetValue(key, list) Then
                        list = New List(Of String)()
                        result(key) = list
                    End If
                    Dim text = address.ToString()
                    If Not list.Contains(text) Then list.Add(text)
                Next
            Catch
                ' tabella non disponibile (IPv6 spento): nessun vicino
            Finally
                If table <> IntPtr.Zero Then FreeMibTable(table)
            End Try
            ' prima i globali, poi i locali e i link-local
            For Each k In result.Keys.ToList()
                result(k) = result(k).OrderBy(Function(a) If(a.StartsWith("fe80", StringComparison.OrdinalIgnoreCase), 2, If(a.StartsWith("fd", StringComparison.OrdinalIgnoreCase) OrElse a.StartsWith("fc", StringComparison.OrdinalIgnoreCase), 1, 0))).ToList()
            Next
            Return result
        End Function

        ''' <summary>
        ''' Un ping a tutti i nodi IPv6 (ff02::1) su ogni scheda collegata: i dispositivi rispondono e finiscono nella tabella
        ''' dei vicini. Si aspetta un attimo che le risposte arrivino.
        ''' </summary>
        Public Async Function WakeNeighborsAsync() As Task
            Dim pings As New List(Of Task)()
            For Each nic In NetworkInterface.GetAllNetworkInterfaces()
                If nic.OperationalStatus <> OperationalStatus.Up OrElse nic.NetworkInterfaceType = NetworkInterfaceType.Loopback Then Continue For
                If Not nic.Supports(NetworkInterfaceComponent.IPv6) Then Continue For
                Dim index As Integer
                Try
                    index = nic.GetIPProperties().GetIPv6Properties().Index
                Catch
                    Continue For
                End Try
                Dim target As IPAddress = Nothing
                If Not IPAddress.TryParse($"ff02::1%{index}", target) Then Continue For
                pings.Add(Task.Run(Async Function()
                                       Try
                                           Using p As New Ping()
                                               Await p.SendPingAsync(target, 800)
                                           End Using
                                       Catch
                                       End Try
                                   End Function))
            Next
            Await Task.WhenAll(pings)
            Await Task.Delay(700)
        End Function

#End Region

    End Module

End Namespace
