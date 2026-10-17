Option Strict On

Imports System.IO
Imports System.Text.Json
Imports Microsoft.Win32

Namespace Config

    ''' <summary>Configurazione IPv4 di una scheda di rete da applicare al volo (per esempio quella di un sito/cliente).</summary>
    Public Class NetworkProfile
        Public Property Id As String = Guid.NewGuid().ToString("N")
        Public Property Name As String = ""
        ''' <summary>Nome della scheda in Windows (es. "Ethernet").</summary>
        Public Property Adapter As String = ""
        Public Property Dhcp As Boolean = True
        Public Property Ip As String = ""
        Public Property Mask As String = "255.255.255.0"
        Public Property Gateway As String = ""
        Public Property Dns1 As String = ""
        Public Property Dns2 As String = ""
    End Class

    ''' <summary>Impostazioni del programma (impostazioni.json nella cartella dei dati).</summary>
    Public Class AppSettings
        ''' <summary>Parte con Windows (voce in HKCU\...\Run, con /tray): lo stato vero è nel registro.</summary>
        Public Property AutoStart As Boolean
        ''' <summary>Icona nell'area di notifica accanto all'orologio.</summary>
        Public Property TrayIcon As Boolean
        ''' <summary>La X della finestra la nasconde nell'area di notifica invece di chiudere il programma.</summary>
        Public Property CloseToTray As Boolean
        ''' <summary>All'avvio con Windows resta solo l'icona (la finestra si apre con un clic).</summary>
        Public Property StartMinimized As Boolean = True
        ''' <summary>Tema della finestra: "auto" (come Windows), "light" o "dark".</summary>
        Public Property Theme As String = "auto"
        ''' <summary>All'apertura del programma parte la scansione della rete, dopo un avviso con il conto alla rovescia.</summary>
        Public Property AutoScan As Boolean
        ''' <summary>Secondi di attesa prima della scansione all'avvio (0-600): intanto l'avviso permette di annullarla.</summary>
        Public Property AutoScanDelay As Integer = 10
        ''' <summary>All'avvio, se c'è una versione nuova, la scarica e la installa da sola (solo la copia installata col setup).</summary>
        Public Property AutoInstallUpdates As Boolean = True
        ''' <summary>All'avvio, con Internet, aggiorna il database dei produttori dai registri dell'IEEE se ha più di un giorno.</summary>
        Public Property AutoUpdateOui As Boolean = True
        Public Property Profiles As New List(Of NetworkProfile)()
        ''' <summary>Ultima sessione salvata o aperta: all'avvio si riapre se il file c'è ancora (vuoto dopo «Nuova sessione»).</summary>
        Public Property LastSession As String = ""
    End Class

    ''' <summary>Lettura e salvataggio delle impostazioni, e voce di avvio automatico nel registro.</summary>
    Public NotInheritable Class SettingsStore

        Private Const FileName As String = "impostazioni.json"
        Private Const RunKey As String = "Software\Microsoft\Windows\CurrentVersion\Run"
        Private Const RunValue As String = "WhoIsPro"
        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {.WriteIndented = True, .PropertyNameCaseInsensitive = True}

        Private Sub New()
        End Sub

        Public Shared Function Load(folder As String) As AppSettings
            Try
                Dim path = IO.Path.Combine(folder, FileName)
                If File.Exists(path) Then
                    Dim s = JsonSerializer.Deserialize(Of AppSettings)(File.ReadAllText(path), JsonOptions)
                    If s IsNot Nothing Then
                        If s.Profiles Is Nothing Then s.Profiles = New List(Of NetworkProfile)()
                        s.AutoStart = AutoStartEnabled()
                        Return s
                    End If
                End If
            Catch
            End Try
            Return New AppSettings With {.AutoStart = AutoStartEnabled()}
        End Function

        Public Shared Sub Save(folder As String, settings As AppSettings)
            Directory.CreateDirectory(folder)
            Dim path = IO.Path.Combine(folder, FileName)
            Dim temp = path & ".tmp"
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions))
            File.Move(temp, path, overwrite:=True)
        End Sub

        ''' <summary>Vero se nel registro c'è la voce di avvio automatico che punta a questo programma.</summary>
        Public Shared Function AutoStartEnabled() As Boolean
            Try
                Using key = Registry.CurrentUser.OpenSubKey(RunKey)
                    Dim value = TryCast(key?.GetValue(RunValue), String)
                    Return Not String.IsNullOrEmpty(value) AndAlso value.IndexOf(ExePath(), StringComparison.OrdinalIgnoreCase) >= 0
                End Using
            Catch
                Return False
            End Try
        End Function

        ''' <summary>Aggiunge o toglie la voce di avvio automatico (solo per l'utente corrente, senza diritti di amministratore).</summary>
        Public Shared Sub SetAutoStart(enabled As Boolean)
            Using key = Registry.CurrentUser.CreateSubKey(RunKey, writable:=True)
                If enabled Then
                    key.SetValue(RunValue, $"""{ExePath()}"" /tray")
                Else
                    key.DeleteValue(RunValue, throwOnMissingValue:=False)
                End If
            End Using
        End Sub

        Private Shared Function ExePath() As String
            Return If(Environment.ProcessPath, IO.Path.Combine(AppContext.BaseDirectory, "WhoIsPro.exe"))
        End Function

    End Class

End Namespace
