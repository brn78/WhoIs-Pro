Option Strict On

Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading

Namespace Network.Scan

    ''' <summary>Esito di una richiesta NetBIOS NBSTAT: nome macchina, gruppo di lavoro o dominio e MAC (se dichiarato).</summary>
    Public Class NetBiosInfo
        Public Property Name As String
        Public Property Workgroup As String
        Public Property Mac As String
    End Class

    ''' <summary>
    ''' Interroga un host con NBSTAT (UDP 137, nome "*"), senza dipendere da nbtstat: rispondono i Windows con la
    ''' condivisione file attiva e i dispositivi Samba (NAS, server Linux, alcune stampanti). Silenziosa: senza risposta
    ''' o in caso di errore restituisce Nothing.
    ''' </summary>
    Public Module NetBiosResolver

#Region "Costanti"

        Private Const NetBiosPort As Integer = 137
        Private Const TypeNbstat As Integer = &H21
        Private Const QueryLength As Integer = 50
        Private Const NameEntryLength As Integer = 18

        ''' <summary>I nomi NetBIOS usano la code page OEM (850 per l'Europa occidentale); Latin-1 se non disponibile.</summary>
        Private ReadOnly OemEncoding As Encoding = If(CodePagesEncodingProvider.Instance.GetEncoding(850), Encoding.Latin1)

#End Region

#Region "Richiesta"

        ''' <summary>
        ''' Richiesta NBSTAT con timeout: la domanda parte subito e di nuovo a metà del tempo, contro la perdita di un
        ''' pacchetto UDP. Annullata con <paramref name="ct"/> propaga l'annullamento; ogni altro problema dà Nothing.
        ''' </summary>
        Public Async Function QueryAsync(address As IPAddress, timeoutMs As Integer, ct As CancellationToken) As Task(Of NetBiosInfo)
            If address Is Nothing OrElse address.AddressFamily <> AddressFamily.InterNetwork Then Return Nothing
            Dim id = CUShort(Random.Shared.Next(1, UShort.MaxValue))
            Dim query = BuildQuery(id)
            Dim endpoint As New IPEndPoint(address, NetBiosPort)
            Try
                Using udp As New UdpClient(AddressFamily.InterNetwork)
                    Using deadline = CancellationTokenSource.CreateLinkedTokenSource(ct)
                        deadline.CancelAfter(Math.Max(100, timeoutMs))
                        Await udp.SendAsync(query, endpoint, deadline.Token).ConfigureAwait(False)
                        ' il secondo invio gira da sé e si ferma con il token: non c'è bisogno di aspettarlo
                        Dim resend = ResendLaterAsync(udp, query, endpoint, Math.Max(50, timeoutMs \ 2), deadline.Token)
                        While True
                            Dim reply = Await udp.ReceiveAsync(deadline.Token).ConfigureAwait(False)
                            If reply.RemoteEndPoint.Address.Equals(address) Then
                                Dim info = Parse(reply.Buffer, id)
                                If info IsNot Nothing Then
                                    deadline.Cancel()
                                    Return info
                                End If
                            End If
                        End While
                    End Using
                End Using
            Catch ex As OperationCanceledException When ct.IsCancellationRequested
                Throw
            Catch
                ' tempo scaduto, porta chiusa (ICMP "port unreachable") o risposta illeggibile: nessun nome
            End Try
            Return Nothing
        End Function

        Private Async Function ResendLaterAsync(udp As UdpClient, query As Byte(), endpoint As IPEndPoint, delayMs As Integer, ct As CancellationToken) As Task
            Try
                Await Task.Delay(delayMs, ct).ConfigureAwait(False)
                Await udp.SendAsync(query, endpoint, ct).ConfigureAwait(False)
            Catch
                ' il secondo invio è solo un aiuto
            End Try
        End Function

        ''' <summary>Pacchetto NBSTAT: intestazione, nome "*" con codifica di primo livello, tipo NBSTAT, classe IN.</summary>
        Private Function BuildQuery(id As UShort) As Byte()
            Dim packet(QueryLength - 1) As Byte
            packet(0) = CByte(id >> 8)
            packet(1) = CByte(id And &HFF)
            packet(5) = 1                       ' una domanda
            packet(12) = &H20                   ' lunghezza del nome codificato (32 caratteri)
            Dim name(15) As Byte
            name(0) = AscW("*"c)                ' "*" seguito da 15 byte nulli
            For i = 0 To 15
                packet(13 + i * 2) = CByte(AscW("A"c) + (name(i) >> 4))
                packet(14 + i * 2) = CByte(AscW("A"c) + (name(i) And &HF))
            Next
            packet(45) = 0                      ' fine del nome
            packet(47) = TypeNbstat             ' tipo NBSTAT (0x0021)
            packet(49) = 1                      ' classe IN
            Return packet
        End Function

#End Region

#Region "Lettura della risposta"

        ''' <summary>
        ''' Risposta NBSTAT: elenco dei nomi registrati (15 caratteri, suffisso, flag) seguito dalle statistiche, i cui
        ''' primi 6 byte sono il MAC. Nome macchina = primo nome unico con suffisso 00 (o 20); gruppo = primo gruppo 00 (o 1E).
        ''' </summary>
        Private Function Parse(data As Byte(), id As UShort) As NetBiosInfo
            If data.Length < 12 OrElse data(0) <> CByte(id >> 8) OrElse data(1) <> CByte(id And &HFF) OrElse (data(2) And &H80) = 0 Then Return Nothing
            Dim answers = (CInt(data(6)) << 8) Or data(7)
            If answers = 0 Then Return Nothing

            Dim offset = SkipName(data, 12)
            If offset < 0 OrElse offset + 11 > data.Length OrElse ((CInt(data(offset)) << 8) Or data(offset + 1)) <> TypeNbstat Then Return Nothing
            offset += 10 ' tipo, classe, TTL, lunghezza dei dati

            Dim count As Integer = data(offset)
            offset += 1
            Dim workstation As String = Nothing, server As String = Nothing, group As String = Nothing, browserGroup As String = Nothing
            Dim parsed = 0
            While parsed < count AndAlso offset + NameEntryLength <= data.Length
                Dim name = DecodeName(data, offset)
                If name IsNot Nothing Then
                    Dim suffix = data(offset + 15)
                    Dim isGroup = (data(offset + 16) And &H80) <> 0
                    If Not isGroup AndAlso suffix = 0 Then
                        If workstation Is Nothing Then workstation = name
                    ElseIf Not isGroup AndAlso suffix = &H20 Then
                        If server Is Nothing Then server = name
                    ElseIf isGroup AndAlso suffix = 0 Then
                        If group Is Nothing Then group = name
                    ElseIf isGroup AndAlso suffix = &H1E Then
                        If browserGroup Is Nothing Then browserGroup = name
                    End If
                End If
                parsed += 1
                offset += NameEntryLength
            End While

            Dim mac As String = Nothing
            If parsed = count AndAlso offset + 6 <= data.Length Then
                Dim six(5) As Byte
                Array.Copy(data, offset, six, 0, 6)
                If Utils.MacVendorLookup.IsUsableUnicast(six) Then mac = Utils.MacVendorLookup.Normalize(Utils.MacVendorLookup.FormatMac(six))
            End If

            Dim machine = If(workstation, server)
            Dim workgroup = If(group, browserGroup)
            If machine Is Nothing AndAlso workgroup Is Nothing AndAlso mac Is Nothing Then Return Nothing
            Return New NetBiosInfo With {.Name = machine, .Workgroup = workgroup, .Mac = mac}
        End Function

        ''' <summary>Salta un nome DNS/NetBIOS (etichette o puntatore compresso); -1 se il pacchetto è troncato.</summary>
        Private Function SkipName(data As Byte(), offset As Integer) As Integer
            While offset < data.Length
                Dim length = data(offset)
                If length = 0 Then Return offset + 1
                If (length And &HC0) = &HC0 Then Return offset + 2
                offset += 1 + length
            End While
            Return -1
        End Function

        ''' <summary>Nome a 15 caratteri riempito di spazi; Nothing se vuoto o con caratteri di controllo (es. __MSBROWSE__).</summary>
        Private Function DecodeName(data As Byte(), offset As Integer) As String
            For i = offset To offset + 14
                If data(i) > 0 AndAlso data(i) < &H20 Then Return Nothing
            Next
            Dim name = OemEncoding.GetString(data, offset, 15).TrimEnd(" "c, ChrW(0)).Trim()
            Return If(name.Length = 0, Nothing, name)
        End Function

#End Region

    End Module

End Namespace
