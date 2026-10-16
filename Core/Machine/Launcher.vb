Option Strict On

Imports System.Text.RegularExpressions

Namespace Machine

    ''' <summary>
    ''' Apre un host con gli strumenti di Windows (menu contestuali delle righe): browser, Esplora risorse (cartelle
    ''' condivise), Desktop remoto, SSH, ping, FTP. Solo i tipi elencati e solo con un indirizzo o nome ben formato.
    ''' </summary>
    Public NotInheritable Class Launcher

        Private Shared ReadOnly HostPattern As New Regex("^[A-Za-z0-9][A-Za-z0-9.\-]{0,253}$", RegexOptions.CultureInvariant)

        Private Sub New()
        End Sub

        Public Shared Sub Run(kind As String, host As String, port As Integer)
            host = If(host, "").Trim()
            If Not HostPattern.IsMatch(host) Then Throw New ArgumentException("Indirizzo o nome dell'host non valido.")
            If port < 0 OrElse port > 65535 Then Throw New ArgumentException("Porta non valida.")
            Select Case kind
                Case "http", "https"
                    Dim standard = If(kind = "http", 80, 443)
                    Shell($"{kind}://{host}{If(port > 0 AndAlso port <> standard, ":" & port, "")}/")
                Case "ftp"
                    Shell($"ftp://{host}{If(port > 0 AndAlso port <> 21, ":" & port, "")}/")
                Case "explorer"
                    Start("explorer.exe", $"\\{host}")
                Case "rdp"
                    Start("mstsc.exe", $"/v:{host}{If(port > 0 AndAlso port <> 3389, ":" & port, "")}")
                Case "ssh"
                    Start("cmd.exe", $"/k title SSH {host} & ssh {host}{If(port > 0 AndAlso port <> 22, " -p " & port, "")}")
                Case "ping"
                    Start("cmd.exe", $"/k title Ping {host} & ping -t {host}")
                Case "telnet"
                    Start("cmd.exe", $"/k title Telnet {host} & telnet {host}{If(port > 0, " " & port, "")}")
                Case Else
                    Throw New ArgumentException($"Operazione sconosciuta: {kind}")
            End Select
        End Sub

        Private Shared Sub Shell(target As String)
            Process.Start(New ProcessStartInfo(target) With {.UseShellExecute = True})
        End Sub

        Private Shared Sub Start(file As String, arguments As String)
            Process.Start(New ProcessStartInfo(file, arguments) With {.UseShellExecute = True})
        End Sub

    End Class

End Namespace
