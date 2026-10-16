Namespace Models
    Public Class ConnectionInfo
        Public Property Protocol As String = "TCP"
        Public Property LocalAddress As String = ""
        Public Property LocalPort As Integer = 0
        Public Property RemoteAddress As String = ""
        Public Property RemotePort As Integer = 0
        Public Property State As String = ""
        Public Property ProcessId As Integer = 0
        Public Property ProcessName As String = ""
        Public Property ProcessPath As String = ""
    End Class
End Namespace