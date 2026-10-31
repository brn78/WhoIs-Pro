Namespace Models
    ''' <summary>Host della sessione: quello che la scansione della rete ha trovato (e che finisce nel file .whoispro).</summary>
    Public Class HostInfo
        Public Property IPAddress As String = ""
        ''' <summary>Nome DNS inverso.</summary>
        Public Property Hostname As String = ""
        ''' <summary>Nome NetBIOS (risposta NBSTAT su UDP 137).</summary>
        Public Property NetBiosName As String = ""
        ''' <summary>Gruppo di lavoro o dominio dichiarato via NetBIOS.</summary>
        Public Property Workgroup As String = ""
        Public Property MacAddress As String = ""
        Public Property MacVendor As String = "Sconosciuto"
        Public Property DeviceType As String = "Dispositivo Generico"
        Public Property DeviceCategory As String = "Altro"
        Public Property DeviceIcon As String = "💻"
        Public Property Status As String = "Online"
        Public Property RespondsToPing As Boolean = True
        Public Property ResponseTimeMs As Long = 0
        Public Property TTL As Integer = 0
        Public Property OSGuess As String = "Sconosciuto"
        ''' <summary>Porte TCP aperte trovate dalla scansione della rete (i dettagli sono nell'elenco delle porte della sessione).</summary>
        Public Property OpenPorts As New List(Of Integer)()
        Public Property OpenPortsCount As Integer = 0
        Public Property IsGateway As Boolean
        Public Property IsLocalMachine As Boolean
        Public Property FirstSeen As DateTime = DateTime.Now
        Public Property LastSeen As DateTime = DateTime.Now
        Public Property Notes As String = ""

        Public Overrides Function ToString() As String
            Return $"{IPAddress} ({If(String.IsNullOrEmpty(Hostname), "No Hostname", Hostname)}) - {DeviceType}"
        End Function
    End Class
End Namespace
