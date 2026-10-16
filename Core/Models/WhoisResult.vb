Namespace Models
    Public Class WhoisResult
        Public Property QueryTarget As String = ""
        Public Property ServerUsed As String = ""
        Public Property RawText As String = ""
        Public Property Registrar As String = "N/D"
        Public Property CreationDate As String = "N/D"
        Public Property ExpirationDate As String = "N/D"
        Public Property UpdatedDate As String = "N/D"
        Public Property RegistrantOrg As String = "N/D"
        Public Property Country As String = "N/D"
        Public Property NameServers As New List(Of String)()
        Public Property DomainStatus As New List(Of String)()
        Public Property ReferralServer As String = ""
        Public Property IsSuccess As Boolean = False
        Public Property ErrorMessage As String = ""
        Public Property QueryTimeMs As Long = 0
        Public Property QueriedAt As DateTime = DateTime.Now
    End Class
End Namespace