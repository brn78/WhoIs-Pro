Namespace Models
    Public Class DnsRecordInfo
        Public Property Host As String = ""
        Public Property RecordType As String = "A" ' A, AAAA, CNAME, MX, TXT, NS, PTR
        Public Property Value As String = ""
        Public Property TTL As Integer = 0
        Public Property Priority As Integer = 0
    End Class
End Namespace