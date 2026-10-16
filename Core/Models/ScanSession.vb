Namespace Models
    Public Class ScanSession
        Public Property SessionId As String = Guid.NewGuid().ToString("N")
        Public Property SessionTitle As String = "Sessione Scansione WhoIs Pro"
        Public Property CreatedAt As DateTime = DateTime.Now
        Public Property ModifiedAt As DateTime = DateTime.Now
        Public Property ChecksumSHA256 As String = ""
        Public Property Notes As String = ""
        Public Property Hosts As New List(Of HostInfo)()
        Public Property Ports As New List(Of PortInfo)()
        Public Property WhoisEntries As New List(Of WhoisResult)()
        Public Property DnsRecords As New List(Of DnsRecordInfo)()
        Public Property TracerouteHops As New List(Of TracerouteHop)()
    End Class
End Namespace