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
        ''' <summary>Nome dato dall'utente (es. "Router di casa"): resta anche nel registro dei dispositivi, legato al MAC.</summary>
        Public Property CustomName As String = ""
        ''' <summary>Icona scelta dall'utente (nome Material Design Icons, es. "router-wireless"; "" = quella del tipo) e il suo colore (#rrggbb, "" = del tipo).</summary>
        Public Property Icon As String = ""
        Public Property IconColor As String = ""
        ''' <summary>Indirizzi IPv6 dello stesso dispositivo (stesso MAC), dalla tabella dei vicini di Windows.</summary>
        Public Property Ipv6 As New List(Of String)()

        Public Overrides Function ToString() As String
            Return $"{IPAddress} ({If(String.IsNullOrEmpty(Hostname), "No Hostname", Hostname)}) - {DeviceType}"
        End Function
    End Class
End Namespace