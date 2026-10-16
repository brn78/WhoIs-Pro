Namespace Models
    ''' <summary>Una scansione fatta nella sessione (rete, porte o traceroute): cosa, quando, quanto è durata e con che esito.</summary>
    Public Class ScanRecord
        ''' <summary>"network", "ports" oppure "trace".</summary>
        Public Property Kind As String = ""
        Public Property Target As String = ""
        Public Property At As DateTime = DateTime.Now
        Public Property Seconds As Double
        ''' <summary>Host attivi, porte aperte oppure salti, secondo il tipo.</summary>
        Public Property Found As Integer
        Public Property Cancelled As Boolean
        Public Property [Error] As String = ""
    End Class
End Namespace
