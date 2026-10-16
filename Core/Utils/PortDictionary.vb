Namespace Utils
    Public Class PortEntry
        Public Property Port As Integer
        Public Property Protocol As String ' TCP, UDP, BOTH
        Public Property ServiceName As String
        Public Property Description As String
    End Class

    Public Class PortPreset
        Public Sub New(key As String, name As String, ports As List(Of Integer))
            Me.Key = key
            Me.Name = name
            Me.Ports = ports
        End Sub
        Public ReadOnly Property Key As String
        Public ReadOnly Property Name As String
        Public ReadOnly Property Ports As List(Of Integer)
        ''' <summary>Porte come testo da mettere nel campo ("21,22,…"), oppure "1-65535" per tutte.</summary>
        Public ReadOnly Property Text As String
            Get
                Return If(Key = "ALL", "1-65535", String.Join(",", Ports))
            End Get
        End Property
    End Class

    Public Module PortDictionary
        Private ReadOnly Ports As New Dictionary(Of Integer, PortEntry)()

        Sub New()
            Add(20, "TCP", "FTP-DATA", "File Transfer Protocol (Dati)")
            Add(21, "TCP", "FTP", "File Transfer Protocol (Controllo)")
            Add(22, "TCP", "SSH", "Secure Shell / SCP / SFTP")
            Add(23, "TCP", "Telnet", "Telnet non cifrato")
            Add(25, "TCP", "SMTP", "Simple Mail Transfer Protocol")
            Add(53, "BOTH", "DNS", "Domain Name System")
            Add(67, "UDP", "DHCP-Server", "Dynamic Host Configuration Protocol")
            Add(68, "UDP", "DHCP-Client", "DHCP Client")
            Add(69, "UDP", "TFTP", "Trivial File Transfer Protocol")
            Add(80, "TCP", "HTTP", "Hypertext Transfer Protocol (Web)")
            Add(88, "TCP", "Kerberos", "Autenticazione Kerberos")
            Add(110, "TCP", "POP3", "Post Office Protocol v3")
            Add(123, "UDP", "NTP", "Network Time Protocol")
            Add(135, "TCP", "MSRPC", "Microsoft RPC Endpoint Mapper")
            Add(137, "UDP", "NetBIOS-NS", "NetBIOS Name Service")
            Add(138, "UDP", "NetBIOS-DGM", "NetBIOS Datagram")
            Add(139, "TCP", "NetBIOS-SSN", "NetBIOS Session Service")
            Add(143, "TCP", "IMAP", "Internet Message Access Protocol")
            Add(161, "UDP", "SNMP", "Simple Network Management Protocol")
            Add(162, "UDP", "SNMP-Trap", "SNMP Traps")
            Add(389, "TCP", "LDAP", "Lightweight Directory Access Protocol")
            Add(443, "TCP", "HTTPS", "HTTP Secure (TLS/SSL)")
            Add(445, "TCP", "SMB / CIFS", "Microsoft-DS Condivisione File")
            Add(465, "TCP", "SMTPS", "SMTP sicuro su SSL")
            Add(514, "UDP", "Syslog", "System Logging Protocol")
            Add(515, "TCP", "LPD / LPR", "Line Printer Daemon")
            Add(554, "TCP", "RTSP", "Real-Time Streaming Protocol (Telecamere IP)")
            Add(587, "TCP", "SMTP-Submission", "Invio Posta con autenticazione")
            Add(631, "TCP", "IPP", "Internet Printing Protocol (CUPS)")
            Add(636, "TCP", "LDAPS", "LDAP su SSL")
            Add(993, "TCP", "IMAPS", "IMAP su SSL")
            Add(995, "TCP", "POP3S", "POP3 su SSL")
            Add(1080, "TCP", "SOCKS", "SOCKS Proxy")
            Add(1194, "UDP", "OpenVPN", "OpenVPN Default Port")
            Add(1433, "TCP", "MSSQL", "Microsoft SQL Server")
            Add(1434, "UDP", "MSSQL-Monitor", "Microsoft SQL Monitor")
            Add(1521, "TCP", "Oracle-DB", "Oracle Database Listener")
            Add(1723, "TCP", "PPTP", "Point-to-Point Tunneling Protocol")
            Add(1883, "TCP", "MQTT", "MQTT Message Queue (IoT / Domotica)")
            Add(1900, "UDP", "SSDP / UPnP", "Simple Service Discovery Protocol")
            Add(2049, "TCP", "NFS", "Network File System")
            Add(2375, "TCP", "Docker-REST", "Docker REST API (non protetta)")
            Add(2376, "TCP", "Docker-TLS", "Docker REST API protetta")
            Add(3000, "TCP", "NodeJS / WebOS", "Node.js / Grafana / LG WebOS")
            Add(3306, "TCP", "MySQL / MariaDB", "MySQL Database Server")
            Add(3389, "TCP", "RDP", "Remote Desktop Protocol (Windows)")
            Add(5000, "TCP", "UPnP / Synology", "Synology DSM Web / UPnP")
            Add(5001, "TCP", "Synology-HTTPS", "Synology DSM HTTPS")
            Add(5060, "BOTH", "SIP", "Session Initiation Protocol (VoIP)")
            Add(5353, "UDP", "mDNS / Bonjour", "Multicast DNS (Apple/IoT)")
            Add(5432, "TCP", "PostgreSQL", "PostgreSQL Database Server")
            Add(5672, "TCP", "RabbitMQ", "RabbitMQ AMQP Broker")
            Add(5900, "TCP", "VNC", "Virtual Network Computing")
            Add(5985, "TCP", "WinRM-HTTP", "Windows Remote Management")
            Add(5986, "TCP", "WinRM-HTTPS", "Windows Remote Management SSL")
            Add(6379, "TCP", "Redis", "Redis In-Memory Database")
            Add(6443, "TCP", "Kubernetes", "Kubernetes API Server")
            Add(8000, "TCP", "HTTP-Alt", "Common HTTP Dev Server")
            Add(8001, "TCP", "Samsung-TV", "Samsung Smart TV Remote Control")
            Add(8002, "TCP", "Samsung-TV-SSL", "Samsung Smart TV Remote SSL")
            Add(8008, "TCP", "Chromecast-HTTP", "Google Cast HTTP")
            Add(8009, "TCP", "Chromecast-Cast", "Google Cast V2")
            Add(8080, "TCP", "HTTP-Proxy", "HTTP Proxy / Tomcat / Spring")
            Add(8123, "TCP", "Home-Assistant", "Home Assistant Web GUI & API")
            Add(8443, "TCP", "HTTPS-Alt", "Alternative HTTPS / UniFi Controller")
            Add(8883, "TCP", "MQTTS", "MQTT sicuro con TLS")
            Add(9000, "TCP", "Portainer / SonarQube", "Portainer / SonarQube UI")
            Add(9090, "TCP", "Prometheus", "Prometheus Monitoring")
            Add(9092, "TCP", "Kafka", "Apache Kafka Broker")
            Add(9100, "TCP", "JetDirect / Printer", "HP JetDirect Stampa RAW")
            Add(9200, "TCP", "Elasticsearch", "Elasticsearch REST API")
            Add(27017, "TCP", "MongoDB", "MongoDB Database Server")
        End Sub

        Private Sub Add(port As Integer, proto As String, name As String, desc As String)
            Ports(port) = New PortEntry With {
                .Port = port,
                .Protocol = proto,
                .ServiceName = name,
                .Description = desc
            }
        End Sub

        Public Function GetServiceName(port As Integer) As String
            If Ports.ContainsKey(port) Then
                Return Ports(port).ServiceName
            End If
            Return "Sconosciuto"
        End Function

        Public Function GetDescription(port As Integer) As String
            If Ports.ContainsKey(port) Then
                Return Ports(port).Description
            End If
            Return "Porta utente generica"
        End Function

        ''' <summary>Elenco porte come lo scrive l'utente: "22, 80, 443, 8000-8100" (virgole, spazi o punti e virgola; intervalli con il trattino).</summary>
        Public Function ParsePorts(input As String) As List(Of Integer)
            Dim list As New List(Of Integer)()
            If String.IsNullOrWhiteSpace(input) Then Return list

            Dim tokens = input.Split(New Char() {","c, ";"c, " "c, ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries)
            For Each tok In tokens
                Dim t = tok.Trim()
                If t.Contains("-"c) Then
                    Dim parts = t.Split("-"c)
                    Dim startPort As Integer, endPort As Integer
                    If parts.Length = 2 AndAlso Integer.TryParse(parts(0), startPort) AndAlso Integer.TryParse(parts(1), endPort) Then
                        For p = Math.Max(1, startPort) To Math.Min(65535, endPort)
                            list.Add(p)
                        Next
                    End If
                Else
                    Dim singlePort As Integer
                    If Integer.TryParse(t, singlePort) AndAlso singlePort >= 1 AndAlso singlePort <= 65535 Then
                        list.Add(singlePort)
                    End If
                End If
            Next

            Return list.Distinct().OrderBy(Function(x) x).ToList()
        End Function

        ''' <summary>Gruppi di porte proposti nella pagina Porte: chiave, nome e porte.</summary>
        Public Function GetPresets() As List(Of PortPreset)
            Return New List(Of PortPreset) From {
                New PortPreset("TOP20", "Le 20 più comuni", GetPresetPorts("TOP20")),
                New PortPreset("TOP100", "Le 100 più comuni", GetPresetPorts("TOP100")),
                New PortPreset("WEB", "Servizi web", GetPresetPorts("WEB")),
                New PortPreset("DATABASE", "Database", GetPresetPorts("DATABASE")),
                New PortPreset("MAIL", "Posta elettronica", GetPresetPorts("MAIL")),
                New PortPreset("REMOTE", "Accesso remoto", GetPresetPorts("REMOTE")),
                New PortPreset("IOT", "Domotica e IoT", GetPresetPorts("IOT")),
                New PortPreset("ALL", "Tutte (1-65535)", New List(Of Integer)())
            }
        End Function

        Public Function GetPresetPorts(presetName As String) As List(Of Integer)
            Select Case presetName.ToUpperInvariant()
                Case "TOP20"
                    Return New List(Of Integer) From {21, 22, 23, 25, 53, 80, 110, 135, 139, 143, 443, 445, 993, 995, 1433, 1521, 3306, 3389, 5432, 8080}
                Case "TOP100"
                    Return New List(Of Integer) From {
                        20, 21, 22, 23, 25, 53, 67, 68, 69, 80, 88, 110, 111, 119, 123, 135, 137, 138, 139, 143,
                        161, 162, 179, 389, 443, 445, 465, 500, 514, 515, 520, 554, 587, 631, 636, 873, 990, 993, 995,
                        1080, 1194, 1433, 1434, 1521, 1723, 1883, 1900, 2049, 2082, 2083, 2181, 2222, 2375, 2376, 3000,
                        3128, 3306, 3389, 4000, 5000, 5001, 5060, 5353, 5432, 5672, 5900, 5985, 5986, 6000, 6379, 6443,
                        7001, 8000, 8001, 8008, 8009, 8080, 8081, 8123, 8443, 8883, 8888, 9000, 9090, 9092, 9100, 9200,
                        9418, 9999, 10000, 11211, 27017, 27018, 50000
                    }
                Case "WEB"
                    Return New List(Of Integer) From {80, 443, 8000, 8008, 8080, 8081, 8443, 8888, 9000, 9090}
                Case "DATABASE"
                    Return New List(Of Integer) From {1433, 1521, 3306, 5432, 6379, 9200, 27017, 27018}
                Case "MAIL"
                    Return New List(Of Integer) From {25, 110, 143, 465, 587, 993, 995}
                Case "REMOTE"
                    Return New List(Of Integer) From {22, 23, 3389, 5900, 5985, 5986}
                Case "IOT"
                    Return New List(Of Integer) From {1883, 8883, 8123, 8001, 8002, 8008, 8009, 554, 5000, 5001, 9100, 5353, 1900}
                Case Else
                    Return New List(Of Integer) From {21, 22, 23, 25, 53, 80, 110, 135, 139, 143, 443, 445, 993, 995, 1433, 3306, 3389, 8080, 8123}
            End Select
        End Function
    End Module
End Namespace