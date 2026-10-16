Namespace Models
    Public Class TracerouteHop
        ''' <summary>Destinazione del traceroute a cui appartiene il salto (una sessione può contenerne più d'uno).</summary>
        Public Property Target As String = ""
        Public Property HopNumber As Integer = 0
        Public Property IPAddress As String = "*"
        Public Property Hostname As String = ""
        Public Property RoundTripTimes As New List(Of Long)()
        Public Property Status As String = "In Corso" ' OK, TimedOut, Error
    End Class
End Namespace