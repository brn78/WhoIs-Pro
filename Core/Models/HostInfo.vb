Namespace Models
    Public Class HostInfo
        Public Property IPAddress As String = ""
        Public Property Hostname As String = ""
        Public Property MacAddress As String = ""
        Public Property MacVendor As String = "Sconosciuto"
        Public Property DeviceType As String = "Dispositivo Generico"
        Public Property DeviceCategory As String = "Altro"
        Public Property DeviceIcon As String = "💻"
        Public Property Status As String = "Online"
        Public Property ResponseTimeMs As Long = 0
        Public Property TTL As Integer = 0
        Public Property OSGuess As String = "Sconosciuto"
        Public Property OpenPortsCount As Integer = 0
        Public Property FirstSeen As DateTime = DateTime.Now
        Public Property LastSeen As DateTime = DateTime.Now
        Public Property Notes As String = ""

        Public Overrides Function ToString() As String
            Return $"{IPAddress} ({If(String.IsNullOrEmpty(Hostname), "No Hostname", Hostname)}) - {DeviceType}"
        End Function
    End Class
End Namespace