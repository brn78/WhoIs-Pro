Option Strict On

Imports System.Net
Imports System.Net.Sockets
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions
Imports System.Threading

Namespace Network.Scan

#Region "Indirizzi IPv4"

    ''' <summary>Indirizzi IPv4 come numeri (a.b.c.d = a·2^24 + b·2^16 + c·2^8 + d), per intervalli e maschere.</summary>
    Public Module Ipv4
        Public Function ToUInt32(address As IPAddress) As UInteger
            Dim b = address.MapToIPv4().GetAddressBytes()
            Return (CUInt(b(0)) << 24) Or (CUInt(b(1)) << 16) Or (CUInt(b(2)) << 8) Or CUInt(b(3))
        End Function

        Public Function ToAddress(value As UInteger) As IPAddress
            Return New IPAddress({CByte(value >> 24), CByte((value >> 16) And &HFFUI), CByte((value >> 8) And &HFFUI), CByte(value And &HFFUI)})
        End Function

        Public Function Format(value As UInteger) As String
            Return $"{value >> 24}.{(value >> 16) And &HFFUI}.{(value >> 8) And &HFFUI}.{value And &HFFUI}"
        End Function

        Public Function Mask(prefix As Integer) As UInteger
            If prefix <= 0 Then Return 0UI
            If prefix >= 32 Then Return UInteger.MaxValue
            Return UInteger.MaxValue << (32 - prefix)
        End Function

        Public Function PrefixFromMask(mask As IPAddress) As Integer
            If mask Is Nothing Then Return 24
            Dim value = ToUInt32(mask)
            Return If(value = 0, 0, Numerics.BitOperations.PopCount(value))
        End Function

        ''' <summary>Indirizzo autoassegnato (169.254.x.x): nessun DHCP, nessuna rete vera.</summary>
        Public Function IsApipa(value As UInteger) As Boolean
            Return (value And &HFFFF0000UI) = &HA9FE0000UI
        End Function

        ''' <summary>Solo la forma con quattro numeri: IPAddress.TryParse accetterebbe anche "192.168.1" come 192.168.0.1.</summary>
        Public Function TryParse(text As String, ByRef value As UInteger) As Boolean
            value = 0UI
            If String.IsNullOrEmpty(text) Then Return False
            Dim startIdx = 0
            Dim endIdx = text.Length - 1
            While startIdx <= endIdx AndAlso Char.IsWhiteSpace(text(startIdx))
                startIdx += 1
            End While
            While endIdx >= startIdx AndAlso Char.IsWhiteSpace(text(endIdx))
                endIdx -= 1
            End While
            If startIdx > endIdx Then Return False

            Dim acc As UInteger = 0UI
            Dim octet As Integer = 0
            Dim digits As Integer = 0
            Dim dots As Integer = 0

            For i = startIdx To endIdx
                Dim ch = text(i)
                If ch >= "0"c AndAlso ch <= "9"c Then
                    If digits > 0 AndAlso octet = 0 Then Return False ' evita ambiguità ottale (es. 010)
                    octet = octet * 10 + (AscW(ch) - AscW("0"c))
                    If octet > 255 Then Return False
                    digits += 1
                ElseIf ch = "."c Then
                    If digits = 0 OrElse dots = 3 Then Return False
                    acc = (acc << 8) Or CUInt(octet)
                    octet = 0
                    digits = 0
                    dots += 1
                Else
                    Return False
                End If
            Next

            If dots <> 3 OrElse digits = 0 Then Return False
            value = (acc << 8) Or CUInt(octet)
            Return True
        End Function
    End Module

#End Region

#Region "Connessione TCP rapida"

    ''' <summary>
    ''' Connessione TCP con timeout. Dove Windows lo consente (SIO_TCP_INITIAL_RTO) il SYN non viene ritrasmesso, così una
    ''' porta chiusa risponde subito invece di far ripetere il tentativo per circa un secondo.
    ''' </summary>
    Friend Module TcpConnector
        Private Const SioTcpInitialRto As Integer = &H98000011
        ''' <summary>TCP_INITIAL_RTO_PARAMETERS: Rtt non specificato (0xFFFF), nessuna ritrasmissione del SYN (0xFE).</summary>
        Private ReadOnly NoSynRetransmissions As Byte() = {&HFF, &HFF, &HFE, &H0}
        Private _initialRtoSupported As Integer = 1

        ''' <summary>Client connesso, oppure Nothing se la porta è chiusa, filtrata o il tempo scade.</summary>
        Public Async Function ConnectAsync(address As IPAddress, port As Integer, timeoutMs As Integer, ct As CancellationToken) As Task(Of TcpClient)
            Dim client As New TcpClient(AddressFamily.InterNetwork)
            Try
                FailFastOnReset(client.Client)
                Using deadline = CancellationTokenSource.CreateLinkedTokenSource(ct)
                    deadline.CancelAfter(Math.Max(50, timeoutMs))
                    Await client.ConnectAsync(address, port, deadline.Token).ConfigureAwait(False)
                    If client.Connected Then Return client
                End Using
            Catch ex As OperationCanceledException When ct.IsCancellationRequested
                client.Dispose()
                Throw
            Catch
                ' porta chiusa, filtrata o tempo scaduto
            End Try
            client.Dispose()
            Return Nothing
        End Function

        Private Sub FailFastOnReset(socket As Socket)
            If Volatile.Read(_initialRtoSupported) = 0 Then Return
            Try
                socket.IOControl(SioTcpInitialRto, NoSynRetransmissions, Nothing)
            Catch
                Volatile.Write(_initialRtoSupported, 0) ' versione di Windows senza l'opzione: comportamento normale
            End Try
        End Sub
    End Module

#End Region

#Region "Interop iphlpapi"

    ''' <summary>Funzioni di IP Helper (iphlpapi.dll) usate dalla scansione; nessuna richiede privilegi amministrativi.</summary>
    Friend Module IpHelper
        Private Const AfInet As UShort = 2
        ' Layout di MIB_IPNET_TABLE2 / MIB_IPNET_ROW2 (identico a 32 e 64 bit): righe da 88 byte dopo 8 byte di intestazione
        Private Const NeighborTableHeader As Integer = 8
        Private Const NeighborRowSize As Integer = 88
        Private Const RowAddressOffset As Integer = 4
        Private Const RowInterfaceIndexOffset As Integer = 28
        Private Const RowPhysicalAddressOffset As Integer = 40
        Private Const RowPhysicalLengthOffset As Integer = 72
        Private Const RowStateOffset As Integer = 76

        <DllImport("iphlpapi.dll", ExactSpelling:=True)>
        Private Function SendARP(destIp As UInteger, srcIp As UInteger, macAddress As Byte(), ByRef macAddressLength As UInteger) As Integer
        End Function

        <DllImport("iphlpapi.dll", ExactSpelling:=True)>
        Private Function GetIpNetTable2(family As UShort, ByRef table As IntPtr) As Integer
        End Function

        <DllImport("iphlpapi.dll", ExactSpelling:=True)>
        Private Sub FreeMibTable(memory As IntPtr)
        End Sub

        <DllImport("iphlpapi.dll", ExactSpelling:=True)>
        Private Function GetBestInterface(destAddress As UInteger, ByRef bestIfIndex As UInteger) As Integer
        End Function

        ''' <summary>Stato di una voce della cache dei vicini (NL_NEIGHBOR_STATE).</summary>
        Public Enum NeighborState
            Unreachable = 0
            Incomplete = 1
            Probe = 2
            Delay = 3
            Stale = 4
            Reachable = 5
            Permanent = 6
        End Enum

        ''' <summary>Voce della tabella ARP: indirizzo (come numero), MAC, stato e scheda.</summary>
        Public Class Neighbor
            Public Property Address As UInteger
            Public Property Mac As Byte()
            Public Property State As NeighborState
            Public Property InterfaceIndex As Integer

            Public ReadOnly Property HasUsableMac As Boolean
                Get
                    Return Utils.MacVendorLookup.IsUsableUnicast(Mac)
                End Get
            End Property

            ''' <summary>Voce valida: raggiungibile, in verifica o vecchia ma non ancora smentita.</summary>
            Public ReadOnly Property IsPresent As Boolean
                Get
                    Return State = NeighborState.Reachable OrElse State = NeighborState.Stale OrElse State = NeighborState.Delay OrElse
                           State = NeighborState.Probe OrElse State = NeighborState.Permanent
                End Get
            End Property
        End Class

        ''' <summary>
        ''' MAC con una richiesta ARP (o dalla cache se già presente) inviata dalla scheda che ha l'indirizzo
        ''' <paramref name="source"/> (0 = scelta di Windows). Blocca il thread: chiamare dal pool.
        ''' </summary>
        Public Function SendArp(address As UInteger, Optional source As UInteger = 0) As String
            Try
                Dim mac(7) As Byte
                Dim length = CUInt(mac.Length)
                Dim nativeSource = If(source = 0, 0UI, ToNative(source))
                If SendARP(ToNative(address), nativeSource, mac, length) = 0 AndAlso length >= 6 Then
                    Dim six = mac.Take(6).ToArray()
                    If Utils.MacVendorLookup.IsUsableUnicast(six) Then Return Utils.MacVendorLookup.Normalize(Utils.MacVendorLookup.FormatMac(six))
                End If
            Catch
                ' iphlpapi non disponibile o indirizzo non valido
            End Try
            Return Nothing
        End Function

        ''' <summary>Tabella dei vicini IPv4 di tutte le schede; Nothing se non è leggibile.</summary>
        Public Function ReadNeighbors() As List(Of Neighbor)
            Dim table As IntPtr = IntPtr.Zero
            Try
                If GetIpNetTable2(AfInet, table) <> 0 OrElse table = IntPtr.Zero Then Return Nothing
                Dim count = Marshal.ReadInt32(table)
                Dim list As New List(Of Neighbor)(count)
                For i = 0 To count - 1
                    Dim row = table + NeighborTableHeader + i * NeighborRowSize
                    If CUShort(Marshal.ReadInt16(row)) <> AfInet Then Continue For
                    Dim address = (CUInt(Marshal.ReadByte(row, RowAddressOffset)) << 24) Or
                                  (CUInt(Marshal.ReadByte(row, RowAddressOffset + 1)) << 16) Or
                                  (CUInt(Marshal.ReadByte(row, RowAddressOffset + 2)) << 8) Or
                                  CUInt(Marshal.ReadByte(row, RowAddressOffset + 3))
                    Dim length = Math.Clamp(Marshal.ReadInt32(row, RowPhysicalLengthOffset), 0, 32)
                    Dim mac(Math.Max(0, length - 1)) As Byte
                    If length = 0 Then mac = Array.Empty(Of Byte)() Else Marshal.Copy(row + RowPhysicalAddressOffset, mac, 0, length)
                    list.Add(New Neighbor With {
                        .Address = address, .Mac = mac,
                        .State = CType(Marshal.ReadInt32(row, RowStateOffset), NeighborState),
                        .InterfaceIndex = Marshal.ReadInt32(row, RowInterfaceIndexOffset)
                    })
                Next
                Return list
            Catch
                Return Nothing
            Finally
                If table <> IntPtr.Zero Then FreeMibTable(table)
            End Try
        End Function

        ''' <summary>Indice della scheda che Windows userebbe per raggiungere <paramref name="destination"/> (rotta predefinita).</summary>
        Public Function BestInterfaceIndex(destination As IPAddress) As Integer?
            Try
                Dim index As UInteger
                If GetBestInterface(ToNative(Ipv4.ToUInt32(destination)), index) = 0 Then Return CInt(index)
            Catch
            End Try
            Return Nothing
        End Function

        ''' <summary>IPAddr di Windows: i quattro byte in ordine di rete letti come intero della macchina.</summary>
        Private Function ToNative(address As UInteger) As UInteger
            Return If(BitConverter.IsLittleEndian, Buffers.Binary.BinaryPrimitives.ReverseEndianness(address), address)
        End Function
    End Module

#End Region

End Namespace
