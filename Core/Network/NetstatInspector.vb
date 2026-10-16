Imports System.Net
Imports System.Runtime.InteropServices
Imports WhoIsPro.Models

Namespace Network
    Public Module NetstatInspector
        <DllImport("iphlpapi.dll", SetLastError:=True)>
        Private Function GetExtendedTcpTable(pTcpTable As IntPtr, ByRef pdwSize As Integer, bOrder As Boolean, ulAf As UInteger, TableClass As Integer, Reserved As UInteger) As UInteger
        End Function

        <DllImport("iphlpapi.dll", SetLastError:=True)>
        Private Function GetExtendedUdpTable(pUdpTable As IntPtr, ByRef pdwSize As Integer, bOrder As Boolean, ulAf As UInteger, TableClass As Integer, Reserved As UInteger) As UInteger
        End Function

        Private Const AF_INET As UInteger = 2
        Private Const TCP_TABLE_OWNER_PID_ALL As Integer = 5
        Private Const UDP_TABLE_OWNER_PID As Integer = 1

        <StructLayout(LayoutKind.Sequential)>
        Private Structure MIB_TCPROW_OWNER_PID
            Public state As UInteger
            Public localAddr As UInteger
            Public localPort As UInteger
            Public remoteAddr As UInteger
            Public remotePort As UInteger
            Public owningPid As UInteger
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure MIB_UDPROW_OWNER_PID
            Public localAddr As UInteger
            Public localPort As UInteger
            Public owningPid As UInteger
        End Structure

        Public Function GetActiveConnections() As List(Of ConnectionInfo)
            Dim connections As New List(Of ConnectionInfo)()
            Dim procCache As New Dictionary(Of Integer, String)()

            ' 1. TCP Connections con PID
            Dim tcpSize As Integer = 0
            GetExtendedTcpTable(IntPtr.Zero, tcpSize, True, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0)
            Dim pTcpTable As IntPtr = Marshal.AllocHGlobal(tcpSize)

            Try
                If GetExtendedTcpTable(pTcpTable, tcpSize, True, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) = 0 Then
                    Dim numEntries As Integer = Marshal.ReadInt32(pTcpTable)
                    Dim ptrRow As IntPtr = IntPtr.Add(pTcpTable, 4)

                    For i = 0 To numEntries - 1
                        Dim row As MIB_TCPROW_OWNER_PID = Marshal.PtrToStructure(Of MIB_TCPROW_OWNER_PID)(ptrRow)
                        Dim localIp As New IPAddress(BitConverter.GetBytes(row.localAddr))
                        Dim remoteIp As New IPAddress(BitConverter.GetBytes(row.remoteAddr))
                        Dim localPort As Integer = ConvertPort(row.localPort)
                        Dim remotePort As Integer = ConvertPort(row.remotePort)
                        Dim pid As Integer = CInt(row.owningPid)

                        Dim procName = GetProcessName(pid, procCache)

                        connections.Add(New ConnectionInfo With {
                            .Protocol = "TCP",
                            .LocalAddress = localIp.ToString(),
                            .LocalPort = localPort,
                            .RemoteAddress = remoteIp.ToString(),
                            .RemotePort = remotePort,
                            .State = GetTcpStateString(row.state),
                            .ProcessId = pid,
                            .ProcessName = procName
                        })

                        ptrRow = IntPtr.Add(ptrRow, Marshal.SizeOf(GetType(MIB_TCPROW_OWNER_PID)))
                    Next
                End If
            Finally
                Marshal.FreeHGlobal(pTcpTable)
            End Try

            ' 2. UDP Listeners con PID
            Dim udpSize As Integer = 0
            GetExtendedUdpTable(IntPtr.Zero, udpSize, True, AF_INET, UDP_TABLE_OWNER_PID, 0)
            Dim pUdpTable As IntPtr = Marshal.AllocHGlobal(udpSize)

            Try
                If GetExtendedUdpTable(pUdpTable, udpSize, True, AF_INET, UDP_TABLE_OWNER_PID, 0) = 0 Then
                    Dim numEntries As Integer = Marshal.ReadInt32(pUdpTable)
                    Dim ptrRow As IntPtr = IntPtr.Add(pUdpTable, 4)

                    For i = 0 To numEntries - 1
                        Dim row As MIB_UDPROW_OWNER_PID = Marshal.PtrToStructure(Of MIB_UDPROW_OWNER_PID)(ptrRow)
                        Dim localIp As New IPAddress(BitConverter.GetBytes(row.localAddr))
                        Dim localPort As Integer = ConvertPort(row.localPort)
                        Dim pid As Integer = CInt(row.owningPid)

                        Dim procName = GetProcessName(pid, procCache)

                        connections.Add(New ConnectionInfo With {
                            .Protocol = "UDP",
                            .LocalAddress = localIp.ToString(),
                            .LocalPort = localPort,
                            .RemoteAddress = "*",
                            .RemotePort = 0,
                            .State = "LISTENING",
                            .ProcessId = pid,
                            .ProcessName = procName
                        })

                        ptrRow = IntPtr.Add(ptrRow, Marshal.SizeOf(GetType(MIB_UDPROW_OWNER_PID)))
                    Next
                End If
            Finally
                Marshal.FreeHGlobal(pUdpTable)
            End Try

            Return connections
        End Function

        Private Function ConvertPort(portNetworkOrder As UInteger) As Integer
            Dim bytes = BitConverter.GetBytes(portNetworkOrder)
            Return (CInt(bytes(0)) << 8) Or CInt(bytes(1))
        End Function

        Private Function GetProcessName(pid As Integer, cache As Dictionary(Of Integer, String)) As String
            If pid = 0 Then Return "System Idle Process"
            If pid = 4 Then Return "System Kernel"
            If cache.ContainsKey(pid) Then Return cache(pid)

            Try
                Dim proc = Process.GetProcessById(pid)
                Dim name = proc.ProcessName
                cache(pid) = name
                Return name
            Catch
                cache(pid) = "N/D"
                Return "N/D"
            End Try
        End Function

        Private Function GetTcpStateString(state As UInteger) As String
            Select Case state
                Case 1 : Return "CLOSED"
                Case 2 : Return "LISTENING"
                Case 3 : Return "SYN_SENT"
                Case 4 : Return "SYN_RECEIVED"
                Case 5 : Return "ESTABLISHED"
                Case 6 : Return "FIN_WAIT_1"
                Case 7 : Return "FIN_WAIT_2"
                Case 8 : Return "CLOSE_WAIT"
                Case 9 : Return "CLOSING"
                Case 10 : Return "LAST_ACK"
                Case 11 : Return "TIME_WAIT"
                Case 12 : Return "DELETE_TCB"
                Case Else : Return "UNKNOWN"
            End Select
        End Function
    End Module
End Namespace