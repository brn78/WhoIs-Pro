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
                .gpu = ReadRegistry("SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", "DriverDesc", ""),
                .serialPorts = SerialPorts()
            }
        End Function

        ''' <summary>Porta seriale presente ora: nome (COM3), dispositivo e collegamento (USB, Bluetooth, scheda madre…).</summary>
        Public Class SerialPortInfo
            Public Property Port As String = ""
            Public Property Device As String = ""
            Public Property Bus As String = ""
            Public Property Driver As String = ""
        End Class

        Private Const PortsClassGuid As String = "{4d36e978-e325-11ce-bfc1-08002be10318}"

        ''' <summary>
        ''' Porte COM presenti (HKLM\HARDWARE\DEVICEMAP\SERIALCOMM) con il nome del dispositivo preso dall'elenco dei
        ''' dispositivi (HKLM\SYSTEM\CurrentControlSet\Enum, classe Ports), senza WMI.
        ''' </summary>
        Public Shared Function SerialPorts() As List(Of SerialPortInfo)
            Dim list As New List(Of SerialPortInfo)()
            Dim present As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) ' COM3 -> \Device\VCP0
            Try
                Using key = Registry.LocalMachine.OpenSubKey("HARDWARE\DEVICEMAP\SERIALCOMM")
                    If key IsNot Nothing Then
                        For Each name In key.GetValueNames()
                            Dim port = TryCast(key.GetValue(name), String)
                            If Not String.IsNullOrEmpty(port) Then present(port) = name
                        Next
                    End If
                End Using
            Catch
            End Try
            If present.Count = 0 Then Return list

            ' Dispositivi della classe Ports: Enum\<bus>\<dispositivo>\<istanza>, con PortName in "Device Parameters"
            Dim devices As New Dictionary(Of String, SerialPortInfo)(StringComparer.OrdinalIgnoreCase)
            Try
                Using enumKey = Registry.LocalMachine.OpenSubKey("SYSTEM\CurrentControlSet\Enum")
                    If enumKey IsNot Nothing Then
                        For Each bus In enumKey.GetSubKeyNames()
                            Using busKey = enumKey.OpenSubKey(bus)
                                If busKey Is Nothing Then Continue For
                                For Each device In busKey.GetSubKeyNames()
                                    Using deviceKey = busKey.OpenSubKey(device)
                                        If deviceKey Is Nothing Then Continue For
                                        For Each instance In deviceKey.GetSubKeyNames()
                                            Using instanceKey = deviceKey.OpenSubKey(instance)
                                                If instanceKey Is Nothing Then Continue For
                                                Dim classGuid = TryCast(instanceKey.GetValue("ClassGUID"), String)
                                                If Not String.Equals(classGuid, PortsClassGuid, StringComparison.OrdinalIgnoreCase) Then Continue For
                                                Dim portName As String = Nothing
                                                Using parameters = instanceKey.OpenSubKey("Device Parameters")
                                                    portName = TryCast(parameters?.GetValue("PortName"), String)
                                                End Using
                                                If String.IsNullOrEmpty(portName) OrElse devices.ContainsKey(portName) Then Continue For
                                                Dim friendly = If(TryCast(instanceKey.GetValue("FriendlyName"), String), TryCast(instanceKey.GetValue("DeviceDesc"), String))
                                                devices(portName) = New SerialPortInfo With {
                                                    .Port = portName,
                                                    .Device = CleanName(friendly, portName),
                                                    .Bus = BusName(bus),
                                                    .Driver = CleanName(TryCast(instanceKey.GetValue("Mfg"), String), "")
                                                }
                                            End Using
                                        Next
                                    End Using
                                Next
                            End Using
                        Next
                    End If
                End Using
            Catch
            End Try

            For Each port In present.Keys.OrderBy(Function(p) PortNumber(p)).ThenBy(Function(p) p)
                Dim info As SerialPortInfo = Nothing
                If Not devices.TryGetValue(port, info) Then
                    info = New SerialPortInfo With {.Port = port, .Device = "Porta seriale", .Bus = If(present(port).IndexOf("Serial", StringComparison.OrdinalIgnoreCase) >= 0, "Scheda madre", "")}
                End If
                list.Add(info)
            Next
            Return list
        End Function

        ''' <summary>"@oem12.inf,%ftdi%;USB Serial Port (COM3)" → "USB Serial Port".</summary>
        Private Shared Function CleanName(value As String, portName As String) As String
            Dim s = If(value, "")
            Dim semicolon = s.LastIndexOf(";"c)
            If s.StartsWith("@") AndAlso semicolon >= 0 Then s = s.Substring(semicolon + 1)
            If portName <> "" Then s = s.Replace($"({portName})", "").Trim()
            Return s.Trim()
        End Function

        Private Shared Function BusName(bus As String) As String
            Select Case bus.ToUpperInvariant()
                Case "USB", "FTDIBUS", "USBSER" : Return "USB"
                Case "BTHENUM", "BTHMODEM" : Return "Bluetooth"
                Case "ACPI", "ROOT", "PCI" : Return "Scheda madre"
                Case Else : Return bus
            End Select
        End Function

        Private Shared Function PortNumber(port As String) As Integer
            Dim n As Integer
            Return If(Integer.TryParse(port.Replace("COM", "", StringComparison.OrdinalIgnoreCase), n), n, 999)
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
