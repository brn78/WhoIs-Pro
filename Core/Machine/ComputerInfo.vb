Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices
Imports Microsoft.Win32

Namespace Machine

    ''' <summary>Processore, memoria, dischi e sistema di questo PC, per la pagina Computer. Solo API di Windows: niente WMI.</summary>
    Public NotInheritable Class ComputerInfo

        <StructLayout(LayoutKind.Sequential)>
        Private Structure MEMORYSTATUSEX
            Public dwLength As UInteger
            Public dwMemoryLoad As UInteger
            Public ullTotalPhys As ULong
            Public ullAvailPhys As ULong
            Public ullTotalPageFile As ULong
            Public ullAvailPageFile As ULong
            Public ullTotalVirtual As ULong
            Public ullAvailVirtual As ULong
            Public ullAvailExtendedVirtual As ULong
        End Structure

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function GlobalMemoryStatusEx(ByRef buffer As MEMORYSTATUSEX) As Boolean
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function GetSystemTimes(ByRef idle As ComTypes.FILETIME, ByRef kernel As ComTypes.FILETIME, ByRef user As ComTypes.FILETIME) As Boolean
        End Function

        Private Shared ReadOnly Lock As New Object()
        Private Shared _prevIdle As Long
        Private Shared _prevTotal As Long
        Private Shared _cpuUsage As Double

        Private Sub New()
        End Sub

        ''' <summary>Uso del processore dall'ultima chiamata (0-100); la prima volta non si sa ancora (0).</summary>
        Public Shared Function CpuUsage() As Double
            Dim idle, kernel, user As ComTypes.FILETIME
            If Not GetSystemTimes(idle, kernel, user) Then Return 0
            Dim i = ToLong(idle)
            Dim total = ToLong(kernel) + ToLong(user)
            SyncLock Lock
                If _prevTotal > 0 AndAlso total > _prevTotal Then
                    _cpuUsage = Math.Max(0, Math.Min(100, (1 - (i - _prevIdle) / CDbl(total - _prevTotal)) * 100))
                End If
                _prevIdle = i
                _prevTotal = total
                Return Math.Round(_cpuUsage, 1)
            End SyncLock
        End Function

        Private Shared Function ToLong(ft As ComTypes.FILETIME) As Long
            Return (CLng(ft.dwHighDateTime) << 32) Or (CLng(ft.dwLowDateTime) And &HFFFFFFFFL)
        End Function

        ''' <summary>Fotografia di tutto: processore, memoria, dischi, sistema, scheda video.</summary>
        Public Shared Function Snapshot() As Object
            Dim mem As New MEMORYSTATUSEX With {.dwLength = CUInt(Marshal.SizeOf(GetType(MEMORYSTATUSEX)))}
            GlobalMemoryStatusEx(mem)
            Dim disks = DriveInfo.GetDrives().Where(Function(d) d.DriveType = DriveType.Fixed OrElse d.DriveType = DriveType.Removable OrElse d.DriveType = DriveType.Network).
                Select(Function(d)
                           Try
                               If Not d.IsReady Then Return Nothing
                               Return New With {
                                   .name = d.Name.TrimEnd("\"c),
                                   .label = d.VolumeLabel,
                                   .format = d.DriveFormat,
                                   .type = DriveTypeName(d.DriveType),
                                   .total = d.TotalSize,
                                   .free = d.AvailableFreeSpace,
                                   .used = d.TotalSize - d.AvailableFreeSpace
                               }
                           Catch
                               Return Nothing
                           End Try
                       End Function).Where(Function(x) x IsNot Nothing).ToList()
            Return New With {
                .cpu = New With {
                    .name = ReadRegistry("HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "Processore").Trim(),
                    .mhz = ReadRegistryInt("HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz"),
                    .logical = Environment.ProcessorCount,
                    .usage = CpuUsage(),
                    .arch = RuntimeInformation.OSArchitecture.ToString()
                },
                .memory = New With {
                    .total = mem.ullTotalPhys,
                    .available = mem.ullAvailPhys,
                    .used = mem.ullTotalPhys - mem.ullAvailPhys,
                    .percent = mem.dwMemoryLoad,
                    .pageTotal = mem.ullTotalPageFile,
                    .pageAvailable = mem.ullAvailPageFile
                },
                .disks = disks,
                .os = New With {
                    .name = OsName(),
                    .version = ReadRegistry("SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", ""),
                    .build = $"{ReadRegistry("SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", "")}.{ReadRegistryInt("SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR")}",
                    .uptimeSeconds = Environment.TickCount64 \ 1000,
                    .machine = Environment.MachineName,
                    .user = Environment.UserName,
                    .domain = Environment.UserDomainName,
                    .dotnet = RuntimeInformation.FrameworkDescription,
                    .bootAt = DateTime.Now.AddMilliseconds(-Environment.TickCount64)
                },
                .gpu = ReadRegistry("SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", "DriverDesc", "")
            }
        End Function

        Private Shared Function OsName() As String
            Dim name = ReadRegistry("SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "Windows")
            Dim build = ReadRegistryInt("SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber")
            If build = 0 Then Integer.TryParse(ReadRegistry("SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", "0"), build)
            ' Windows 11 si presenta ancora come "Windows 10" nel registro
            If build >= 22000 Then name = name.Replace("Windows 10", "Windows 11")
            Return name
        End Function

        Private Shared Function DriveTypeName(t As DriveType) As String
            Select Case t
                Case DriveType.Fixed : Return "Disco fisso"
                Case DriveType.Removable : Return "Rimovibile"
                Case DriveType.Network : Return "Di rete"
                Case Else : Return t.ToString()
            End Select
        End Function

        Private Shared Function ReadRegistry(key As String, value As String, fallback As String) As String
            Try
                Using k = Registry.LocalMachine.OpenSubKey(key)
                    Dim v = k?.GetValue(value)
                    Return If(v Is Nothing, fallback, v.ToString())
                End Using
            Catch
                Return fallback
            End Try
        End Function

        Private Shared Function ReadRegistryInt(key As String, value As String) As Integer
            Try
                Using k = Registry.LocalMachine.OpenSubKey(key)
                    Dim v = k?.GetValue(value)
                    If TypeOf v Is Integer Then Return CInt(v)
                    Dim n As Integer
                    If v IsNot Nothing AndAlso Integer.TryParse(v.ToString(), n) Then Return n
                End Using
            Catch
            End Try
            Return 0
        End Function

    End Class

End Namespace
