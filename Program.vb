Option Strict On

Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' WhoIs Pro: apre la finestra principale (MainWindow), una pagina HTML dentro WebView2. Con /tray (avvio automatico
''' con Windows) parte solo l'icona nell'area di notifica, se è attiva nelle impostazioni.
''' Istanza singola: una seconda apertura porta in primo piano la finestra già aperta (anche dall'area di notifica)
''' e si chiude. Il nome del mutex dipende dalla cartella dei dati, così una copia di sviluppo con
''' WHOISPRO_DATA_FOLDER convive con quella installata.
''' </summary>
Friend Module Program

    Private _instanceMutex As Mutex

    <STAThread>
    Friend Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Dim tray = args.Any(Function(a) String.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase))

        Dim key = InstanceKey()
        Dim createdNew As Boolean
        _instanceMutex = New Mutex(True, "Local\WhoIsPro." & key, createdNew)
        If Not createdNew Then
            ' Già aperto: con /tray (avvio automatico) non si fa nulla, altrimenti si chiede alla finestra di mostrarsi
            If Not tray Then SignalShow(key)
            Return
        End If

        Dim window = New UI.MainWindow(startInTray:=tray)
        StartShowListener(key, window)
        Application.Run(window)
        GC.KeepAlive(_instanceMutex)
    End Sub

    ''' <summary>Impronta corta della cartella dei dati: due copie con cartelle diverse sono istanze diverse.</summary>
    Private Function InstanceKey() As String
        Dim folder = UI.MainWindow.LocalFolder.TrimEnd("\"c, "/"c).ToLowerInvariant()
        Dim hash = SHA256.HashData(Encoding.UTF8.GetBytes(folder))
        Return Convert.ToHexString(hash, 0, 8)
    End Function

    Private Function ShowEventName(key As String) As String
        Return "Local\WhoIsPro.Show." & key
    End Function

    Private Sub SignalShow(key As String)
        Try
            Using ev = EventWaitHandle.OpenExisting(ShowEventName(key))
                ev.Set()
            End Using
        Catch
            ' L'altra istanza sta ancora partendo o sta chiudendo: niente da fare
        End Try
    End Sub

    ''' <summary>Thread in attesa del segnale di un'altra apertura: porta la finestra in primo piano sul thread della UI.</summary>
    Private Sub StartShowListener(key As String, window As UI.MainWindow)
        Dim ev = New EventWaitHandle(False, EventResetMode.AutoReset, ShowEventName(key))
        Dim listener = New Thread(
            Sub()
                While True
                    ev.WaitOne()
                    If window.IsDisposed Then Exit While
                    Try
                        window.BeginInvoke(Sub() window.ShowFromTray())
                    Catch
                        Exit While
                    End Try
                End While
            End Sub) With {.IsBackground = True, .Name = "WhoIsPro.SingleInstance"}
        listener.Start()
    End Sub

End Module
