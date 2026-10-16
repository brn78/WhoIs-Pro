Namespace Models
    Public Class PortInfo
        Public Property Host As String = ""
        Public Property PortNumber As Integer = 0
        Public Property Protocol As String = "TCP"
        Public Property Status As String = "Closed" ' Open, Closed, Filtered
        Public Property ServiceName As String = "Sconosciuto"
        Public Property Banner As String = ""
        Public Property ResponseTimeMs As Long = 0
        Public Property Timestamp As DateTime = DateTime.Now

        Public Overrides Function ToString() As String
            Return $"{Protocol} {PortNumber}: {Status} ({ServiceName})"
        End Function
    End Class
End Namespace