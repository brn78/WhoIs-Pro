Option Strict On

Imports System.ComponentModel
Imports System.IO
Imports System.Net
Imports System.Text
Imports WhoIsPro.Utils

Namespace Network

    ''' <summary>Opzioni della cattura: durata (0 = fino a «Ferma», al massimo 10 minuti) e filtri facoltativi.</summary>
    Public Class CaptureOptions
        Public Property Seconds As Integer = 30
        ''' <summary>Indirizzo IPv4/IPv6 o rete in notazione CIDR; vuoto = tutti.</summary>
        Public Property Ip As String = ""
        Public Property Port As Integer
        ''' <summary>"", "TCP", "UDP", "ICMP" o "ICMPv6".</summary>
        Public Property Protocol As String = ""
    End Class

    ''' <summary>Un pacchetto letto dalla cattura, già spiegato.</summary>
    Public Class PacketRow
        Public Property No As Integer
        ''' <summary>Secondi dall'inizio della cattura.</summary>
        Public Property T As Double
        Public Property Src As String = ""
        Public Property Dst As String = ""
        Public Property Proto As String = ""
        Public Property SrcPort As Integer
        Public Property DstPort As Integer
        Public Property Length As Integer
        Public Property Info As String = ""
    End Class

    Public Class TalkerStat
        Public Property Address As String = ""
        Public Property Packets As Integer
        Public Property Bytes As Long
    End Class

    ''' <summary>Esito di una cattura: totali, protocolli, chi parla di più e i pacchetti (i primi MaxRows).</summary>
    Public Class CaptureSummary
        Public Property File As String = ""
        Public Property Total As Integer
        Public Property Bytes As Long
        Public Property Seconds As Double
        Public Property Protocols As New Dictionary(Of String, Integer)()
        Public Property Talkers As New List(Of TalkerStat)()
        Public Property Packets As New List(Of PacketRow)()
        Public Property Truncated As Boolean
    End Class

    ''' <summary>
    ''' Cattura dei pacchetti con pktmon, lo strumento di Windows 10 e 11 (niente driver da installare). pktmon vuole i
    ''' diritti di amministratore: uno script PowerShell parte elevato (un solo consenso UAC), imposta i filtri, cattura
    ''' sulle schede di rete fino alla scadenza o al file «stop», converte in .pcapng e scrive l'esito in «done.txt».
    ''' Il programma resta senza diritti di amministratore e guarda la cartella.
    ''' </summary>
    Public NotInheritable Class PacketCapture
        Public Const MaxSeconds As Integer = 600
        Public Const MaxRows As Integer = 20000

        Public ReadOnly Property Folder As String
        Public ReadOnly Property EtlFile As String
        Public ReadOnly Property PcapFile As String
        Private ReadOnly _stopFile As String
        Private ReadOnly _startedFile As String
        Private ReadOnly _doneFile As String

        Public Sub New(dataFolder As String)
            Folder = Path.Combine(dataFolder, "Cattura")
            EtlFile = Path.Combine(Folder, "cattura.etl")
            PcapFile = Path.Combine(Folder, "cattura.pcapng")
            _stopFile = Path.Combine(Folder, "stop")
            _startedFile = Path.Combine(Folder, "started")
            _doneFile = Path.Combine(Folder, "done.txt")
        End Sub

        ''' <summary>Controlla le opzioni: nello script finiscono solo valori verificati (niente comandi infilati nei filtri).</summary>
        Public Shared Sub Validate(o As CaptureOptions)
            If o.Seconds < 0 OrElse o.Seconds > MaxSeconds Then Throw New ArgumentException($"La durata va da 1 a {MaxSeconds} secondi (0 = fino a «Ferma»).")
            If o.Ip <> "" Then
                Dim ip = o.Ip
                Dim slash = ip.IndexOf("/"c)
                Dim prefix As Integer
                If slash >= 0 Then
                    If Not Integer.TryParse(ip.Substring(slash + 1), prefix) OrElse prefix < 0 OrElse prefix > 128 Then Throw New ArgumentException("Rete non valida: es. 192.168.1.0/24.")
                    ip = ip.Substring(0, slash)
                End If
                Dim parsed As IPAddress = Nothing
                If Not IPAddress.TryParse(ip, parsed) Then Throw New ArgumentException("Indirizzo IP non valido (es. 192.168.1.40 oppure 192.168.1.0/24).")
            End If
            If o.Port < 0 OrElse o.Port > 65535 Then Throw New ArgumentException("Porta non valida: da 1 a 65535.")
            If Not {"", "TCP", "UDP", "ICMP", "ICMPv6"}.Contains(o.Protocol) Then Throw New ArgumentException("Protocollo non valido.")
        End Sub

        ''' <summary>Prepara lo script e lo avvia con i diritti di amministratore. Annullato il consenso: OperationCanceledException.</summary>
        Public Sub Start(o As CaptureOptions)
            Validate(o)
            Directory.CreateDirectory(Folder)
            For Each f In {EtlFile, PcapFile, _stopFile, _startedFile, _doneFile}
                Try
                    If File.Exists(f) Then File.Delete(f)
                Catch
                End Try
            Next
            Dim script = Path.Combine(Folder, "cattura.ps1")
            File.WriteAllText(script, BuildScript(o), New UTF8Encoding(True))
            Dim psi As New ProcessStartInfo With {
                .FileName = "powershell.exe",
                .Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{script}""",
                .UseShellExecute = True,
                .Verb = "runas",
                .WindowStyle = ProcessWindowStyle.Hidden
            }
            Try
                Process.Start(psi)
            Catch ex As Win32Exception When ex.NativeErrorCode = 1223
                Throw New OperationCanceledException("Consenso di Windows annullato: la cattura non è partita.")
            End Try
        End Sub

        Private Function BuildScript(o As CaptureOptions) As String
            Dim q = Function(s As String) "'" & s.Replace("'", "''") & "'"
            Dim filter As New List(Of String)()
            If o.Ip <> "" Then filter.Add($"-i {o.Ip}")
            If o.Port > 0 Then filter.Add($"-p {o.Port}")
            If o.Protocol <> "" Then filter.Add($"-t {o.Protocol}")
            Dim sb As New StringBuilder()
            sb.AppendLine("# Cattura dei pacchetti di WhoIs Pro (pktmon). Generato dal programma: si può cancellare.")
            ' pktmon scrive avvisi su stderr anche quando va tutto bene (es. «stop» senza cattura in corso): conta solo il
            ' codice di uscita, e l'esito finisce sempre in done.txt
            sb.AppendLine("$ErrorActionPreference = 'Continue'")
            sb.AppendLine($"$etl = {q(EtlFile)}; $pcap = {q(PcapFile)}; $stop = {q(_stopFile)}; $started = {q(_startedFile)}; $done = {q(_doneFile)}")
            sb.AppendLine("function Pkt { $out = & pktmon @args 2>&1 | ForEach-Object { ""$_"" }; [pscustomobject]@{ Code = $LASTEXITCODE; Text = ($out -join ' ').Trim() } }")
            sb.AppendLine("try {")
            sb.AppendLine("  Pkt stop | Out-Null")
            sb.AppendLine("  Pkt filter remove | Out-Null")
            If filter.Count > 0 Then
                sb.AppendLine($"  $r = Pkt filter add WhoIsPro {String.Join(" ", filter)}")
                sb.AppendLine("  if ($r.Code -ne 0) { throw ('filtro non accettato: ' + $r.Text) }")
            End If
            sb.AppendLine("  $r = Pkt start --capture --comp nics --pkt-size 0 -f $etl -s 300")
            sb.AppendLine("  if ($r.Code -ne 0) { throw $r.Text }")
            sb.AppendLine("  'ok' | Set-Content $started")
            sb.AppendLine($"  $limit = {If(o.Seconds = 0, MaxSeconds, o.Seconds)}; $t0 = Get-Date")
            sb.AppendLine("  while (-not (Test-Path $stop) -and ((Get-Date) - $t0).TotalSeconds -lt $limit) { Start-Sleep -Milliseconds 250 }")
            sb.AppendLine("  Pkt stop | Out-Null")
            sb.AppendLine("  $r = Pkt etl2pcap $etl --out $pcap")
            sb.AppendLine("  if ($r.Code -ne 0 -or -not (Test-Path $pcap)) { throw ('conversione non riuscita: ' + $r.Text) }")
            sb.AppendLine("  Pkt filter remove | Out-Null")
            sb.AppendLine("  Remove-Item $etl -ErrorAction SilentlyContinue")
            sb.AppendLine("  'ok' | Set-Content $done -Encoding UTF8")
            sb.AppendLine("} catch {")
            sb.AppendLine("  Pkt stop | Out-Null")
            sb.AppendLine("  Pkt filter remove | Out-Null")
            sb.AppendLine("  ('errore: ' + $_.Exception.Message) | Set-Content $done -Encoding UTF8")
            sb.AppendLine("}")
            Return sb.ToString()
        End Function

        Public Sub RequestStop()
            Try
                Directory.CreateDirectory(Folder)
                File.WriteAllText(_stopFile, "stop")
            Catch
            End Try
        End Sub

        ''' <summary>A che punto è: partita (dopo il consenso), finita (ok o errore), byte del file di cattura.</summary>
        Public Function Status() As (Started As Boolean, Done As Boolean, [Error] As String, Bytes As Long)
            Dim started = File.Exists(_startedFile)
            Dim bytes As Long = 0
            Try
                If File.Exists(EtlFile) Then bytes = New FileInfo(EtlFile).Length
            Catch
            End Try
            If Not File.Exists(_doneFile) Then Return (started, False, "", bytes)
            Dim text = ""
            Try
                text = File.ReadAllText(_doneFile).Trim()
            Catch
                Return (started, False, "", bytes)
            End Try
            If text = "" Then Return (started, False, "", bytes)
            Return (started, True, If(text.StartsWith("ok", StringComparison.OrdinalIgnoreCase), "", text.Replace("errore: ", "")), bytes)
        End Function

#Region "Lettura del .pcapng"

        ''' <summary>Legge un .pcapng (Ethernet) e spiega i pacchetti: totali, protocolli, chi parla di più, righe.</summary>
        Public Shared Function Read(file As String) As CaptureSummary
            Dim result As New CaptureSummary With {.File = file}
            Dim data = IO.File.ReadAllBytes(file)
            Dim talkers As New Dictionary(Of String, TalkerStat)()
            Dim tsResolution As Double = 0.000001
            Dim firstTs As Double = -1
            Dim lastTs As Double = 0
            Dim offset = 0
            While offset + 12 <= data.Length
                Dim type = BitConverter.ToUInt32(data, offset)
                Dim length = CInt(BitConverter.ToUInt32(data, offset + 4))
                If length < 12 OrElse offset + length > data.Length Then Exit While
                If type = 1UI Then
                    tsResolution = InterfaceResolution(data, offset, length)
                ElseIf type = 6UI Then
                    Dim tsHigh = BitConverter.ToUInt32(data, offset + 12)
                    Dim tsLow = BitConverter.ToUInt32(data, offset + 16)
                    Dim captured = CInt(BitConverter.ToUInt32(data, offset + 20))
                    Dim original = CInt(BitConverter.ToUInt32(data, offset + 24))
                    Dim ts = ((CDbl(tsHigh) * 4294967296.0) + tsLow) * tsResolution
                    If firstTs < 0 Then firstTs = ts
                    lastTs = ts
                    result.Total += 1
                    result.Bytes += original
                    Dim row = Decode(data, offset + 28, Math.Min(captured, length - 28))
                    row.No = result.Total
                    row.T = Math.Round(ts - firstTs, 6)
                    row.Length = original
                    Count(result.Protocols, row.Proto)
                    For Each who In {row.Src, row.Dst}
                        If who = "" Then Continue For
                        Dim s As TalkerStat = Nothing
                        If Not talkers.TryGetValue(who, s) Then
                            s = New TalkerStat With {.Address = who}
                            talkers(who) = s
                        End If
                        s.Packets += 1
                        s.Bytes += original
                    Next
                    If result.Packets.Count < MaxRows Then result.Packets.Add(row) Else result.Truncated = True
                End If
                offset += length
            End While
            result.Seconds = If(firstTs < 0, 0, Math.Round(lastTs - firstTs, 3))
            result.Talkers = talkers.Values.OrderByDescending(Function(t) t.Bytes).Take(12).ToList()
            Return result
        End Function

        ''' <summary>Risoluzione dei tempi dell'interfaccia (opzione if_tsresol, predefinita microsecondi).</summary>
        Private Shared Function InterfaceResolution(data As Byte(), block As Integer, length As Integer) As Double
            Dim p = block + 16
            While p + 4 <= block + length - 4
                Dim code = BitConverter.ToUInt16(data, p)
                Dim size = BitConverter.ToUInt16(data, p + 2)
                If code = 0 Then Exit While
                If code = 9 AndAlso size >= 1 Then
                    Dim v = data(p + 4)
                    Return If((v And &H80) <> 0, Math.Pow(2, -(v And &H7F)), Math.Pow(10, -v))
                End If
                p += 4 + ((size + 3) \ 4) * 4
            End While
            Return 0.000001
        End Function

        Private Shared Sub Count(map As Dictionary(Of String, Integer), key As String)
            Dim n As Integer
            map.TryGetValue(key, n)
            map(key) = n + 1
        End Sub

#End Region

#Region "Decodifica dei pacchetti"

        Private Shared Function U16(d As Byte(), p As Integer) As Integer
            Return (CInt(d(p)) << 8) Or d(p + 1)
        End Function

        ''' <summary>Un frame Ethernet: ARP, IPv4 o IPv6, poi TCP, UDP, ICMP; la riga con il protocollo e la spiegazione.</summary>
        Public Shared Function Decode(d As Byte(), start As Integer, size As Integer) As PacketRow
            Dim row As New PacketRow With {.Proto = "Altro"}
            Dim [end] = start + size
            If size < 14 Then Return row
            Dim p = start + 12
            Dim ethertype = U16(d, p)
            p += 2
            While (ethertype = &H8100 OrElse ethertype = &H88A8) AndAlso p + 4 <= [end]
                ethertype = U16(d, p + 2)
                p += 4
            End While
            Try
                Select Case ethertype
                    Case &H806 : DecodeArp(d, p, [end], row)
                    Case &H800 : DecodeIpv4(d, p, [end], row)
                    Case &H86DD : DecodeIpv6(d, p, [end], row)
                    Case &H88CC
                        row.Proto = "LLDP"
                        row.Info = "annuncio di un apparato di rete (LLDP)"
                    Case Else
                        row.Info = $"EtherType 0x{ethertype:X4}"
                End Select
            Catch ex As IndexOutOfRangeException
                row.Info &= If(row.Info = "", "", " · ") & "pacchetto troncato"
            Catch ex As ArgumentException
                row.Info &= If(row.Info = "", "", " · ") & "pacchetto troncato"
            End Try
            Return row
        End Function

        Private Shared Sub DecodeArp(d As Byte(), p As Integer, [end] As Integer, row As PacketRow)
            row.Proto = "ARP"
            If p + 28 > [end] Then Return
            Dim op = U16(d, p + 6)
            Dim sha = String.Join(":", Enumerable.Range(p + 8, 6).Select(Function(i) d(i).ToString("X2")))
            Dim spa = New IPAddress(New Byte() {d(p + 14), d(p + 15), d(p + 16), d(p + 17)}).ToString()
            Dim tpa = New IPAddress(New Byte() {d(p + 24), d(p + 25), d(p + 26), d(p + 27)}).ToString()
            row.Src = spa
            row.Dst = tpa
            row.Info = If(op = 1, If(spa = tpa, $"annuncio: {spa} è {sha}", $"chi ha {tpa}? lo chiede {spa}"), $"{spa} è {sha}")
        End Sub

        Private Shared Sub DecodeIpv4(d As Byte(), p As Integer, [end] As Integer, row As PacketRow)
            Dim ihl = (d(p) And &HF) * 4
            Dim proto = d(p + 9)
            row.Src = New IPAddress(New Byte() {d(p + 12), d(p + 13), d(p + 14), d(p + 15)}).ToString()
            row.Dst = New IPAddress(New Byte() {d(p + 16), d(p + 17), d(p + 18), d(p + 19)}).ToString()
            Dim total = U16(d, p + 2)
            Dim payloadEnd = Math.Min([end], p + Math.Max(ihl, total))
            DecodeTransport(d, proto, p + ihl, payloadEnd, row, False)
        End Sub

        Private Shared Sub DecodeIpv6(d As Byte(), p As Integer, [end] As Integer, row As PacketRow)
            Dim nextHeader = CInt(d(p + 6))
            Dim src(15) As Byte
            Dim dst(15) As Byte
            Array.Copy(d, p + 8, src, 0, 16)
            Array.Copy(d, p + 24, dst, 0, 16)
            row.Src = New IPAddress(src).ToString()
            row.Dst = New IPAddress(dst).ToString()
            Dim q = p + 40
            ' intestazioni di estensione: hop-by-hop, routing, frammento, opzioni di destinazione
            Dim guard = 0
            While (nextHeader = 0 OrElse nextHeader = 43 OrElse nextHeader = 44 OrElse nextHeader = 60) AndAlso q + 8 <= [end] AndAlso guard < 8
                Dim nh = CInt(d(q))
                q += If(nextHeader = 44, 8, (d(q + 1) + 1) * 8)
                nextHeader = nh
                guard += 1
            End While
            DecodeTransport(d, CByte(nextHeader), q, [end], row, True)
        End Sub

        Private Shared ReadOnly DnsTypes As New Dictionary(Of Integer, String) From {
            {1, "A"}, {2, "NS"}, {5, "CNAME"}, {6, "SOA"}, {12, "PTR"}, {15, "MX"}, {16, "TXT"}, {28, "AAAA"}, {33, "SRV"}, {65, "HTTPS"}, {255, "ANY"}}

        Private Shared Sub DecodeTransport(d As Byte(), proto As Byte, p As Integer, [end] As Integer, row As PacketRow, v6 As Boolean)
            Select Case proto
                Case 6
                    row.Proto = "TCP"
                    If p + 20 > [end] Then Return
                    row.SrcPort = U16(d, p)
                    row.DstPort = U16(d, p + 2)
                    Dim offset = (d(p + 12) >> 4) * 4
                    Dim flags = d(p + 13)
                    Dim names As New List(Of String)()
                    If (flags And &H2) <> 0 Then names.Add("SYN")
                    If (flags And &H10) <> 0 Then names.Add("ACK")
                    If (flags And &H8) <> 0 Then names.Add("PSH")
                    If (flags And &H1) <> 0 Then names.Add("FIN")
                    If (flags And &H4) <> 0 Then names.Add("RST")
                    Dim payload = p + offset
                    Dim service = ServiceOf(row.SrcPort, row.DstPort)
                    Dim app = AppLayerTcp(d, payload, [end])
                    If service = "HTTPS" OrElse app.StartsWith("TLS") Then row.Proto = "HTTPS"
                    If service = "HTTP" OrElse app.StartsWith("HTTP") Then row.Proto = "HTTP"
                    If row.Proto = "TCP" AndAlso service <> "" Then row.Proto = service
                    Dim what = If(app <> "", app, If((flags And &H2) <> 0 AndAlso (flags And &H10) = 0, "apertura della connessione", If((flags And &H4) <> 0, "connessione rifiutata o chiusa", If((flags And &H1) <> 0, "chiusura della connessione", If(payload < [end], $"{[end] - payload} byte di dati", "conferma")))))
                    row.Info = $"{row.SrcPort} → {row.DstPort} [{String.Join(", ", names)}] {what}"
                Case 17
                    row.Proto = "UDP"
                    If p + 8 > [end] Then Return
                    row.SrcPort = U16(d, p)
                    row.DstPort = U16(d, p + 2)
                    Dim service = ServiceOf(row.SrcPort, row.DstPort)
                    If service <> "" Then row.Proto = service
                    Dim payload = p + 8
                    If row.SrcPort = 53 OrElse row.DstPort = 53 OrElse row.SrcPort = 5353 OrElse row.DstPort = 5353 OrElse row.SrcPort = 5355 OrElse row.DstPort = 5355 Then
                        row.Proto = If(row.SrcPort = 5353 OrElse row.DstPort = 5353, "mDNS", If(row.SrcPort = 5355 OrElse row.DstPort = 5355, "LLMNR", "DNS"))
                        row.Info = DnsInfo(d, payload, [end])
                    ElseIf row.SrcPort = 1900 OrElse row.DstPort = 1900 Then
                        row.Proto = "SSDP"
                        row.Info = FirstLine(d, payload, [end])
                    ElseIf row.SrcPort = 67 OrElse row.SrcPort = 68 OrElse row.DstPort = 67 OrElse row.DstPort = 68 OrElse row.DstPort = 547 OrElse row.SrcPort = 547 Then
                        row.Proto = "DHCP"
                        row.Info = "richiesta o assegnazione di un indirizzo"
                    ElseIf row.SrcPort = 443 OrElse row.DstPort = 443 Then
                        row.Proto = "QUIC"
                        row.Info = "HTTP/3 (QUIC)"
                    End If
                    If row.Info = "" Then row.Info = $"{row.SrcPort} → {row.DstPort} · {[end] - payload} byte"
                Case 1
                    row.Proto = "ICMP"
                    If p >= [end] Then Return
                    row.Info = Select1(d(p), New Dictionary(Of Integer, String) From {{0, "risposta al ping"}, {8, "ping"}, {3, "destinazione irraggiungibile"}, {11, "TTL scaduto (traceroute)"}, {5, "reindirizzamento"}})
                Case 58
                    row.Proto = "ICMPv6"
                    If p >= [end] Then Return
                    row.Info = Select1(d(p), New Dictionary(Of Integer, String) From {{128, "ping"}, {129, "risposta al ping"}, {133, "richiesta di un router"}, {134, "annuncio del router"}, {135, "chi ha questo indirizzo? (NDP)"}, {136, "risposta NDP"}, {143, "iscrizione multicast (MLD)"}, {1, "destinazione irraggiungibile"}, {3, "hop limit scaduto"}})
                Case 2
                    row.Proto = "IGMP"
                    row.Info = "iscrizione a un gruppo multicast"
                Case Else
                    row.Proto = If(v6, "IPv6", "IPv4")
                    row.Info = $"protocollo IP {proto}"
            End Select
        End Sub

        Private Shared Function Select1(code As Byte, names As Dictionary(Of Integer, String)) As String
            Dim s As String = Nothing
            Return If(names.TryGetValue(code, s), s, $"tipo {code}")
        End Function

        ''' <summary>Servizio noto da una delle due porte (la più bassa vince: è quasi sempre quella del server).</summary>
        Private Shared Function ServiceOf(a As Integer, b As Integer) As String
            For Each port In {Math.Min(a, b), Math.Max(a, b)}
                Select Case port
                    Case 80, 8080 : Return "HTTP"
                    Case 443, 8443 : Return "HTTPS"
                    Case 22 : Return "SSH"
                    Case 53 : Return "DNS"
                    Case 123 : Return "NTP"
                    Case 137, 138, 139 : Return "NetBIOS"
                    Case 445 : Return "SMB"
                    Case 3389 : Return "RDP"
                    Case 554 : Return "RTSP"
                    Case 1883, 8883 : Return "MQTT"
                    Case 5353 : Return "mDNS"
                    Case 1900 : Return "SSDP"
                End Select
            Next
            Return ""
        End Function

        ''' <summary>Dentro TCP: la prima riga di una richiesta HTTP, oppure il nome del sito (SNI) di un TLS ClientHello.</summary>
        Private Shared Function AppLayerTcp(d As Byte(), p As Integer, [end] As Integer) As String
            If p >= [end] Then Return ""
            ' HTTP in chiaro
            Dim methods = {"GET ", "POST ", "PUT ", "HEAD ", "DELETE ", "HTTP/1."}
            For Each m In methods
                If [end] - p >= m.Length AndAlso Encoding.ASCII.GetString(d, p, m.Length) = m Then Return "HTTP " & FirstLine(d, p, [end])
            Next
            ' TLS: record di handshake (0x16), ClientHello (0x01) → estensione server_name
            If [end] - p > 43 AndAlso d(p) = &H16 AndAlso d(p + 5) = &H1 Then
                Dim q = p + 5 + 4 + 2 + 32          ' intestazione del record, del messaggio, versione, random
                If q >= [end] Then Return "TLS ClientHello"
                q += 1 + d(q)                         ' session id
                If q + 2 > [end] Then Return "TLS ClientHello"
                q += 2 + U16(d, q)                    ' cipher suites
                If q >= [end] Then Return "TLS ClientHello"
                q += 1 + d(q)                         ' compressione
                If q + 2 > [end] Then Return "TLS ClientHello"
                Dim extEnd = Math.Min([end], q + 2 + U16(d, q))
                q += 2
                While q + 4 <= extEnd
                    Dim type = U16(d, q)
                    Dim len = U16(d, q + 2)
                    If type = 0 AndAlso q + 9 <= extEnd Then
                        Dim nameLen = U16(d, q + 7)
                        If q + 9 + nameLen <= extEnd Then Return "TLS verso " & Encoding.ASCII.GetString(d, q + 9, nameLen)
                    End If
                    q += 4 + len
                End While
                Return "TLS ClientHello"
            End If
            If [end] - p > 5 AndAlso d(p) >= &H14 AndAlso d(p) <= &H17 AndAlso d(p + 1) = 3 Then Return If(d(p) = &H17, "TLS dati cifrati", "TLS")
            Return ""
        End Function

        Private Shared Function FirstLine(d As Byte(), p As Integer, [end] As Integer) As String
            Dim n = 0
            While p + n < [end] AndAlso n < 120 AndAlso d(p + n) <> 13 AndAlso d(p + n) <> 10
                n += 1
            End While
            Return Encoding.ASCII.GetString(d, p, n)
        End Function

        ''' <summary>DNS (anche mDNS e LLMNR): domanda o risposta, tipo e nome chiesto.</summary>
        Private Shared Function DnsInfo(d As Byte(), p As Integer, [end] As Integer) As String
            If p + 12 > [end] Then Return "DNS"
            Dim isAnswer = (d(p + 2) And &H80) <> 0
            Dim questions = U16(d, p + 4)
            Dim answers = U16(d, p + 6)
            Dim rcode = d(p + 3) And &HF
            Dim name = ""
            Dim qtype = 0
            If questions > 0 Then
                Dim q = p + 12
                Dim labels As New List(Of String)()
                Dim guard = 0
                While q < [end] AndAlso d(q) <> 0 AndAlso guard < 64
                    If (d(q) And &HC0) = &HC0 Then
                        q += 1
                        Exit While
                    End If
                    Dim len = d(q)
                    If q + 1 + len > [end] Then Exit While
                    labels.Add(Encoding.ASCII.GetString(d, q + 1, len))
                    q += 1 + len
                    guard += 1
                End While
                q += 1
                If q + 2 <= [end] Then qtype = U16(d, q)
                name = String.Join(".", labels)
            End If
            Dim t As String = Nothing
            Dim typeName = If(DnsTypes.TryGetValue(qtype, t), t, If(qtype = 0, "", $"tipo {qtype}"))
            If Not isAnswer Then Return $"domanda {typeName} {name}".Trim()
            If rcode = 3 Then Return $"risposta: {name} non esiste"
            Return $"risposta {typeName} {name} · {answers} {If(answers = 1, "record", "record")}".Trim()
        End Function

#End Region

    End Class

End Namespace
