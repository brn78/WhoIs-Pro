Imports System.IO
Imports System.Text
Imports NUnit.Framework
Imports WhoIsPro.Network

Namespace WhoIsPro.Tests
    ''' <summary>Lettura dei .pcapng di pktmon e spiegazione dei pacchetti (ARP, DNS, TLS con il nome del sito, ICMP).</summary>
    Public Class PacketCaptureTests
        Private testDir As String

        <SetUp>
        Public Sub Setup()
            testDir = Path.Combine(Path.GetTempPath(), "WhoIsProPcap_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(testDir)
        End Sub

        <TearDown>
        Public Sub TearDown()
            Try
                If Directory.Exists(testDir) Then Directory.Delete(testDir, True)
            Catch
            End Try
        End Sub

        Private Shared Function Hex(text As String) As Byte()
            Dim clean = text.Replace(" ", "")
            Return Enumerable.Range(0, clean.Length \ 2).Select(Function(i) Convert.ToByte(clean.Substring(i * 2, 2), 16)).ToArray()
        End Function

        Private Shared Sub Block(w As BinaryWriter, type As UInteger, body As Byte())
            Dim padded = ((body.Length + 3) \ 4) * 4
            Dim total = 12 + padded
            w.Write(type)
            w.Write(CUInt(total))
            w.Write(body)
            w.Write(New Byte(padded - body.Length - 1) {})
            w.Write(CUInt(total))
        End Sub

        ''' <summary>Un .pcapng minimo: intestazione, interfaccia Ethernet e i frame dati, uno ogni 100 ms.</summary>
        Private Function MakePcap(ParamArray frames As Byte()()) As String
            Dim file = Path.Combine(testDir, "prova.pcapng")
            Using fs = IO.File.Create(file), w As New BinaryWriter(fs)
                Dim shb As New MemoryStream()
                Using bw As New BinaryWriter(shb)
                    bw.Write(&H1A2B3C4DUI) : bw.Write(CUShort(1)) : bw.Write(CUShort(0)) : bw.Write(-1L)
                End Using
                Block(w, &HA0D0D0AUI, shb.ToArray())
                Block(w, 1UI, New Byte() {1, 0, 0, 0, 0, 0, 0, 0})   ' linktype 1 (Ethernet), snaplen 0
                Dim ts As Long = 1700000000000000L
                For Each f In frames
                    Dim epb As New MemoryStream()
                    Using bw As New BinaryWriter(epb)
                        bw.Write(0UI) : bw.Write(CUInt(ts >> 32)) : bw.Write(CUInt(ts And &HFFFFFFFFL)) : bw.Write(CUInt(f.Length)) : bw.Write(CUInt(f.Length)) : bw.Write(f)
                    End Using
                    Block(w, 6UI, epb.ToArray())
                    ts += 100000
                Next
            End Using
            Return file
        End Function

        Private Const Eth4 As String = "ffffffffffff 3ca62f112201 0800"

        Private Shared Function Ipv4(proto As Byte, src As String, dst As String, payload As Byte()) As Byte()
            Dim total = 20 + payload.Length
            Dim h = Hex($"45 00 {total:X4} 0000 0000 40 {proto:X2} 0000")
            Dim s = Net.IPAddress.Parse(src).GetAddressBytes()
            Dim d = Net.IPAddress.Parse(dst).GetAddressBytes()
            Return Hex(Eth4).Concat(h).Concat(s).Concat(d).Concat(payload).ToArray()
        End Function

        <Test>
        Public Sub Read_ExplainsArpDnsTlsAndPing()
            Dim arp = Hex("ffffffffffff 3ca62f112201 0806 0001 0800 06 04 0001 3ca62f112201 c0a80101 000000000000 c0a80128")
            ' DNS: domanda A per example.com, da 192.168.1.80:50000 a 192.168.1.1:53
            Dim dnsPayload = Hex("1234 0100 0001 0000 0000 0000 07 6578616d706c65 03 636f6d 00 0001 0001")
            Dim udp = Hex($"C350 0035 {8 + dnsPayload.Length:X4} 0000").Concat(dnsPayload).ToArray()
            Dim dns = Ipv4(17, "192.168.1.80", "192.168.1.1", udp)
            ' TLS ClientHello con SNI «example.com» verso la porta 443
            Dim sni = Encoding.ASCII.GetBytes("example.com")
            Dim ext = Hex($"0000 {sni.Length + 5:X4} {sni.Length + 3:X4} 00 {sni.Length:X4}").Concat(sni).ToArray()
            Dim hello = Hex("0303").Concat(New Byte(31) {}).Concat(Hex("00 0002 1301 01 00")).Concat(Hex($"{ext.Length:X4}")).Concat(ext).ToArray()
            Dim hs = Hex($"01 00{hello.Length:X4}").Concat(hello).ToArray()
            Dim record = Hex($"16 0301 {hs.Length:X4}").Concat(hs).ToArray()
            Dim tcp = Hex("D431 01BB 00000001 00000000 50 18 FFFF 0000 0000").Concat(record).ToArray()
            Dim tls = Ipv4(6, "192.168.1.80", "93.184.215.14", tcp)
            Dim ping = Ipv4(1, "192.168.1.80", "1.1.1.1", Hex("08 00 0000 0001 0001"))

            Dim s = PacketCapture.Read(MakePcap(arp, dns, tls, ping))
            Assert.That(s.Total, Iz.EqualTo(4))
            Assert.That(s.Seconds, Iz.EqualTo(0.3).Within(0.0001))
            Assert.That(s.Packets(0).Proto, Iz.EqualTo("ARP"))
            Assert.That(s.Packets(0).Info, Does.Contain("chi ha 192.168.1.40"))
            Assert.That(s.Packets(1).Proto, Iz.EqualTo("DNS"))
            Assert.That(s.Packets(1).Info, Iz.EqualTo("domanda A example.com"))
            Assert.That(s.Packets(1).DstPort, Iz.EqualTo(53))
            Assert.That(s.Packets(2).Proto, Iz.EqualTo("HTTPS"))
            Assert.That(s.Packets(2).Info, Does.Contain("TLS verso example.com"))
            Assert.That(s.Packets(3).Proto, Iz.EqualTo("ICMP"))
            Assert.That(s.Packets(3).Info, Iz.EqualTo("ping"))
            Assert.That(s.Protocols("DNS"), Iz.EqualTo(1))
            Assert.That(s.Talkers.First().Address, Iz.EqualTo("192.168.1.80"))
        End Sub

        <Test>
        Public Sub Validate_RejectsInjectedFilters()
            Assert.Throws(Of ArgumentException)(Sub() PacketCapture.Validate(New CaptureOptions With {.Ip = "1.2.3.4; Remove-Item C:\"}))
            Assert.Throws(Of ArgumentException)(Sub() PacketCapture.Validate(New CaptureOptions With {.Protocol = "TCP -x"}))
            Assert.Throws(Of ArgumentException)(Sub() PacketCapture.Validate(New CaptureOptions With {.Seconds = 5000}))
            Assert.DoesNotThrow(Sub() PacketCapture.Validate(New CaptureOptions With {.Ip = "192.168.1.0/24", .Port = 443, .Protocol = "TCP"}))
            Assert.DoesNotThrow(Sub() PacketCapture.Validate(New CaptureOptions With {.Ip = "fe80::1"}))
        End Sub

        <Test>
        Public Sub Read_RealPktmonSample_WhenPresent()
            ' cattura vera di pktmon, se c'è accanto ai test (non obbligatoria)
            Dim sample = Path.Combine(TestContext.CurrentContext.TestDirectory, "prova.pcapng")
            If Not IO.File.Exists(sample) Then Assert.Ignore("Nessuna cattura di esempio")
            Dim s = PacketCapture.Read(sample)
            Assert.That(s.Total, Iz.GreaterThan(0))
            TestContext.Out.WriteLine(String.Join(", ", s.Protocols.Select(Function(kv) $"{kv.Key} {kv.Value}")))
        End Sub
    End Class
End Namespace
