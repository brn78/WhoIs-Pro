Imports System.Net
Imports System.Net.Sockets
Imports WhoIsPro.Models

Namespace Network
    Public Class DnsEngine
        Public Async Function QueryAllRecordsAsync(domain As String) As Task(Of List(Of DnsRecordInfo))
            Dim records As New List(Of DnsRecordInfo)()
            If String.IsNullOrWhiteSpace(domain) Then Return records

            Dim clean = domain.Trim().ToLowerInvariant().Replace("http://", "").Replace("https://", "").Split("/"c)(0)

            ' 1. Record A & AAAA
            Try
                Dim addresses = Await Dns.GetHostAddressesAsync(clean)
                For Each addr In addresses
                    Dim recType = If(addr.AddressFamily = AddressFamily.InterNetwork, "A", "AAAA")
                    records.Add(New DnsRecordInfo With {
                        .Host = clean,
                        .RecordType = recType,
                        .Value = addr.ToString()
                    })
                Next
            Catch
            End Try

            ' 2. Reverse PTR se IP
            Dim parsedIp As IPAddress = Nothing
            If IPAddress.TryParse(clean, parsedIp) Then
                Try
                    Dim entry = Await Dns.GetHostEntryAsync(parsedIp)
                    records.Add(New DnsRecordInfo With {
                        .Host = clean,
                        .RecordType = "PTR",
                        .Value = entry.HostName
                    })
                Catch
                End Try
            End If

            ' 3. Query avanzate tramite nslookup integrato (MX, NS, TXT)
            Try
                Dim nsRecords = Await RunNslookupAsync(clean, "MX")
                records.AddRange(nsRecords)
                Dim txtRecords = Await RunNslookupAsync(clean, "TXT")
                records.AddRange(txtRecords)
                Dim nsList = Await RunNslookupAsync(clean, "NS")
                records.AddRange(nsList)
            Catch
            End Try

            Return records
        End Function

        Private Async Function RunNslookupAsync(domain As String, qtype As String) As Task(Of List(Of DnsRecordInfo))
            Dim list As New List(Of DnsRecordInfo)()
            Try
                Dim psi As New ProcessStartInfo With {
                    .FileName = "nslookup",
                    .Arguments = $"-type={qtype} {domain}",
                    .RedirectStandardOutput = True,
                    .UseShellExecute = False,
                    .CreateNoWindow = True
                }

                Using proc = Process.Start(psi)
                    If proc IsNot Nothing Then
                        Dim output = Await proc.StandardOutput.ReadToEndAsync()
                        Await proc.WaitForExitAsync()

                        Dim lines = output.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                        For Each line In lines
                            Dim l = line.Trim()
                            If qtype = "MX" AndAlso l.Contains("mail exchanger =") Then
                                Dim parts = l.Split(New String() {"mail exchanger ="}, StringSplitOptions.None)
                                If parts.Length = 2 Then
                                    list.Add(New DnsRecordInfo With {
                                        .Host = domain,
                                        .RecordType = "MX",
                                        .Value = parts(1).Trim()
                                    })
                                End If
                            ElseIf qtype = "TXT" AndAlso l.Contains("text =") Then
                                Dim parts = l.Split(New String() {"text ="}, StringSplitOptions.None)
                                If parts.Length = 2 Then
                                    list.Add(New DnsRecordInfo With {
                                        .Host = domain,
                                        .RecordType = "TXT",
                                        .Value = parts(1).Trim().Trim(""""c)
                                    })
                                End If
                            ElseIf qtype = "NS" AndAlso l.Contains("nameserver =") Then
                                Dim parts = l.Split(New String() {"nameserver ="}, StringSplitOptions.None)
                                If parts.Length = 2 Then
                                    list.Add(New DnsRecordInfo With {
                                        .Host = domain,
                                        .RecordType = "NS",
                                        .Value = parts(1).Trim()
                                    })
                                End If
                            End If
                        Next
                    End If
                End Using
            Catch
            End Try
            Return list
        End Function
    End Class
End Namespace