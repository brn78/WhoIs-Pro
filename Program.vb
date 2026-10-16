Option Strict On

Imports System.Windows.Forms

''' <summary>WhoIs Pro: apre la finestra principale (UI\MainWindow), una pagina HTML dentro WebView2.</summary>
Friend Module Program

    <STAThread>
    Friend Sub Main()
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New UI.MainWindow())
    End Sub

End Module
