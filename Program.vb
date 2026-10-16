Option Strict On

Imports System.Windows.Forms

''' <summary>
''' WhoIs Pro: apre la finestra principale (MainWindow), una pagina HTML dentro WebView2. Con /tray (avvio automatico
''' con Windows) parte solo l'icona nell'area di notifica, se è attiva nelle impostazioni.
''' </summary>
Friend Module Program

    <STAThread>
    Friend Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Dim tray = args.Any(Function(a) String.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase))
        Application.Run(New UI.MainWindow(startInTray:=tray))
    End Sub

End Module
