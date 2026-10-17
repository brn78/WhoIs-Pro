Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text.Encodings.Web
Imports System.Text.Json
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms
Imports Microsoft.Win32

Namespace UI

    ''' <summary>
    ''' Finestra di WhoIs Pro: la pagina ui\index.html (incorporata nel programma e servita come https://whoispro.invalid)
    ''' dentro WebView2. La pagina chiede le operazioni con postMessage({id, cmd, args}); AppBridge le esegue e la risposta
    ''' torna con {id, ok, result | error}. Le scansioni in corso mandano alla pagina eventi {event, data} (PostEvent).
    ''' Nessuna pagina esterna: gli altri indirizzi si aprono nel browser. Dati di WebView2 e posizione della finestra
    ''' stanno in %LOCALAPPDATA%\WhoIsPro.
    ''' </summary>
    Public NotInheritable Class MainWindow
        Inherits Form

        Private Const Origin As String = "https://whoispro.invalid"
        Private Const ContentPolicy As String =
            "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'none'; " &
            "object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'"
        ''' <summary>Voci del menu del tasto destro che restano (modifica del testo).</summary>
        Private Shared ReadOnly MenuItemsKept As String() = {"cut", "copy", "paste", "pasteAndMatchStyle", "selectAll", "undo", "redo", "emoji"}
        Private Shared ReadOnly ReplyOptions As New JsonSerializerOptions With {
            .PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            .Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }

        Private ReadOnly _view As New WebView2 With {.Dock = DockStyle.Fill}
        Private ReadOnly _bridge As AppBridge
        Private ReadOnly _tray As New NotifyIcon()
        Private ReadOnly _trayMenu As New ContextMenuStrip()
        Private _ready As Boolean
        Private _startInTray As Boolean
        Private _allowVisible As Boolean
        Private _closeToTray As Boolean
        Private _exiting As Boolean

        ''' <param name="startInTray">Avvio automatico con Windows: solo l'icona nell'area di notifica (se attiva), la finestra si apre con un clic.</param>
        Public Sub New(Optional startInTray As Boolean = False)
            SuspendLayout()
            Text = "WhoIs Pro"
            AutoScaleDimensions = New SizeF(96.0F, 96.0F)
            AutoScaleMode = AutoScaleMode.Dpi
            ClientSize = New Size(1380, 880)
            MinimumSize = New Size(1000, 680)
            StartPosition = FormStartPosition.CenterScreen
            Using stream = GetType(MainWindow).Assembly.GetManifestResourceStream("WhoIsPro.ico")
                If stream IsNot Nothing Then Icon = New Icon(stream)
            End Using
            ApplyThemeColors()
            Controls.Add(_view)
            _bridge = New AppBridge(Me)
            ResumeLayout(False)
            AddHandler SystemEvents.UserPreferenceChanged, AddressOf OnUserPreferenceChanged

            ' Icona nell'area di notifica: Apri, i profili di rete da applicare al volo ed Esci; doppio clic per aprire.
            ' Il menu si ricostruisce a ogni apertura, così mostra i profili e la configurazione delle schede di adesso.
            BuildTrayMenu()
            AddHandler _trayMenu.Opening, Sub() BuildTrayMenu()
            _tray.Icon = Icon
            _tray.Text = "WhoIs Pro"
            _tray.ContextMenuStrip = _trayMenu
            AddHandler _tray.DoubleClick, Sub() ShowFromTray()
            AddHandler _tray.BalloonTipClicked, Sub() ShowFromTray()
            AddHandler _tray.MouseClick, Sub(s, e) If e.Button = MouseButtons.Left Then ShowFromTray()
            Dim settings = _bridge.Settings
            ApplyTraySettings(settings)
            ApplyTheme(settings.Theme)
            _startInTray = startInTray AndAlso settings.TrayIcon AndAlso settings.StartMinimized
        End Sub

#Region "Area di notifica"

        ''' <summary>Impostazioni dell'icona (anche quando cambiano dalla pagina Impostazioni).</summary>
        Public Sub ApplyTraySettings(settings As Config.AppSettings)
            _tray.Visible = settings.TrayIcon
            _closeToTray = settings.TrayIcon AndAlso settings.CloseToTray
        End Sub

        ''' <summary>
        ''' Menu dell'icona: Apri (Riduci con la finestra aperta), i profili di rete (con la spunta su quello che la scheda ha adesso), Torna a DHCP per le
        ''' schede dei profili che hanno un indirizzo statico, Esci. Applicare chiede il consenso di Windows (UAC).
        ''' </summary>
        Private Sub BuildTrayMenu()
            _trayMenu.Items.Clear()
            ' Con la finestra aperta la prima voce la riduce nell'area di notifica, altrimenti la apre
            Dim openItem = If(Visible AndAlso WindowState <> FormWindowState.Minimized,
                              New ToolStripMenuItem("Riduci nell'area di notifica", Nothing, Sub() HideToTray()),
                              New ToolStripMenuItem("Apri WhoIs Pro", Nothing, Sub() ShowFromTray()))
            openItem.Font = New Font(openItem.Font, FontStyle.Bold)
            _trayMenu.Items.Add(openItem)
            _trayMenu.Items.Add(New ToolStripSeparator())

            Dim data = _bridge.TrayProfiles()
            Dim title = New ToolStripMenuItem("Profili di rete") With {.Enabled = False}
            _trayMenu.Items.Add(title)
            If data.Profiles.Count = 0 Then
                _trayMenu.Items.Add(New ToolStripMenuItem("Crea un profilo…", Nothing, Sub()
                                                                                   ShowFromTray()
                                                                                   PostEvent("navigate", New With {.page = "settings", .tab = "network"})
                                                                               End Sub))
            End If
            For Each p In data.Profiles.OrderBy(Function(x) x.Adapter).ThenBy(Function(x) x.Name)
                Dim profile = p
                Dim nic = data.Adapters.FirstOrDefault(Function(a) String.Equals(a.Name, profile.Adapter, StringComparison.OrdinalIgnoreCase))
                Dim active = nic IsNot Nothing AndAlso If(profile.Dhcp, nic.Dhcp, Not nic.Dhcp AndAlso nic.Ip = profile.Ip)
                Dim detail = If(profile.Dhcp, "DHCP", profile.Ip)
                Dim item = New ToolStripMenuItem($"{profile.Name}   ({profile.Adapter} · {detail})", Nothing,
                                                 Async Sub() Await _bridge.ApplyFromTrayAsync(profile)) With {.Checked = active}
                If nic Is Nothing Then
                    item.Enabled = False
                    item.Text &= " · scheda assente"
                End If
                _trayMenu.Items.Add(item)
            Next
            ' Torna a DHCP: per le schede dei profili che adesso hanno un indirizzo statico
            Dim statics = data.Adapters.Where(Function(a) a.Up AndAlso Not a.Dhcp AndAlso
                                                  data.Profiles.Any(Function(p) String.Equals(p.Adapter, a.Name, StringComparison.OrdinalIgnoreCase))).ToList()
            For Each a In statics
                Dim adapter = a.Name
                _trayMenu.Items.Add(New ToolStripMenuItem($"Torna a DHCP   ({adapter})", Nothing,
                    Async Sub() Await _bridge.ApplyFromTrayAsync(New Config.NetworkProfile With {.Name = "DHCP", .Adapter = adapter, .Dhcp = True})))
            Next

            _trayMenu.Items.Add(New ToolStripSeparator())
            _trayMenu.Items.Add("Esci", Nothing, Sub() ExitFromTray())
        End Sub

        ''' <summary>Avviso dall'icona anche con la finestra aperta: l'esito di un'azione scelta dal menu dell'icona.</summary>
        Public Sub ShowTrayMessage(title As String, text As String, icon As ToolTipIcon)
            If Not _tray.Visible Then Return
            _tray.BalloonTipTitle = title
            _tray.BalloonTipText = If(text = "", " ", text)
            _tray.BalloonTipIcon = icon
            _tray.ShowBalloonTip(8000)
        End Sub

        ''' <summary>Con /tray la finestra non compare finché non la si apre dall'icona (SetVisibleCore).</summary>
        Protected Overrides Sub SetVisibleCore(value As Boolean)
            If _startInTray AndAlso Not _allowVisible Then
                If Not IsHandleCreated Then CreateHandle()
                value = False
            End If
            MyBase.SetVisibleCore(value)
        End Sub

        ''' <summary>
        ''' Avviso di Windows dall'icona nell'area di notifica (per esempio un dispositivo nuovo nella rete), solo se la
        ''' finestra non si vede: quando è aperta lo dice la pagina. Un clic sull'avviso riapre la finestra.
        ''' </summary>
        Public Sub ShowNotice(title As String, text As String)
            If Not _tray.Visible Then Return
            If Visible AndAlso WindowState <> FormWindowState.Minimized Then Return
            _tray.BalloonTipTitle = title
            _tray.BalloonTipText = text
            _tray.BalloonTipIcon = ToolTipIcon.Info
            _tray.ShowBalloonTip(10000)
        End Sub

        Public Sub ShowFromTray()
            _allowVisible = True
            Show()
            If WindowState = FormWindowState.Minimized Then WindowState = FormWindowState.Normal
            Activate()
        End Sub

        Private Sub HideToTray()
            SavePlacement()
            Hide()
        End Sub

        Private Sub ExitFromTray()
            _exiting = True
            If Not Visible Then ShowFromTray()
            Close()
        End Sub

#End Region

        ''' <summary>
        ''' %LOCALAPPDATA%\WhoIsPro: dati di WebView2, impostazioni, posizione della finestra e database OUI aggiornato.
        ''' La variabile d'ambiente WHOISPRO_DATA_FOLDER la sostituisce: serve per provare una copia di sviluppo accanto a
        ''' quella installata senza condividerne i dati (WebView2 non accetta due istanze con argomenti diversi).
        ''' </summary>
        Public Shared ReadOnly Property LocalFolder As String
            Get
                Dim custom = Environment.GetEnvironmentVariable("WHOISPRO_DATA_FOLDER")
                If Not String.IsNullOrWhiteSpace(custom) Then Return custom
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhoIsPro")
            End Get
        End Property

        Protected Overrides Sub OnHandleCreated(e As EventArgs)
            MyBase.OnHandleCreated(e)
            ApplyTitleBarTheme()
        End Sub

        Protected Overrides Async Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)
            RestorePlacement()
            Try
                Dim options As CoreWebView2EnvironmentOptions = Nothing
#If DEBUG Then
                ' solo nella versione Debug: WHOISPRO_DEBUG_PORT=9333 apre la porta di debug remoto di WebView2 (prove automatiche della pagina)
                Dim debugPort = System.Environment.GetEnvironmentVariable("WHOISPRO_DEBUG_PORT")
                If Not String.IsNullOrEmpty(debugPort) AndAlso Integer.TryParse(debugPort, Nothing) Then
                    options = New CoreWebView2EnvironmentOptions($"--remote-debugging-port={debugPort}")
                End If
#End If
                Dim environment = Await CoreWebView2Environment.CreateAsync(Nothing, Path.Combine(LocalFolder, "WebView2"), options)
                Await _view.EnsureCoreWebView2Async(environment)
            Catch ex As WebView2RuntimeNotFoundException
                If MessageBox.Show(Me, "Per la finestra di WhoIs Pro serve Microsoft Edge WebView2 Runtime, di solito già presente in Windows 10 e 11." & vbCrLf & vbCrLf &
                                   "Aprire la pagina per scaricarlo?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
                    OpenExternal("https://go.microsoft.com/fwlink/p/?LinkId=2124703")
                End If
                Close()
                Return
            Catch ex As Exception
                MessageBox.Show(Me, $"La finestra non si apre: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error)
                Close()
                Return
            End Try

            Dim core = _view.CoreWebView2
#If DEBUG Then
            Dim devTools = True
#Else
            Dim devTools = System.Environment.GetEnvironmentVariable("WHOISPRO_DEVTOOLS") = "1"
#End If
            With core.Settings
                .AreDevToolsEnabled = devTools
                .AreBrowserAcceleratorKeysEnabled = devTools
                .IsStatusBarEnabled = False
                .AreHostObjectsAllowed = False
                .IsPasswordAutosaveEnabled = False
                .IsGeneralAutofillEnabled = False
                .IsSwipeNavigationEnabled = False
                .IsZoomControlEnabled = True
            End With
            core.AddWebResourceRequestedFilter(Origin & "/*", CoreWebView2WebResourceContext.All)
            AddHandler core.WebResourceRequested, AddressOf ServeResource
            AddHandler core.WebMessageReceived, AddressOf OnWebMessage
            AddHandler core.NavigationStarting, AddressOf OnNavigationStarting
            AddHandler core.NewWindowRequested, Sub(sender, args)
                                                     args.Handled = True
                                                     OpenExternal(args.Uri)
                                                 End Sub
            AddHandler core.ContextMenuRequested, AddressOf OnContextMenu
            _ready = True
            core.Navigate(Origin & "/index.html")
        End Sub

#Region "Pagina"

        ''' <summary>File della pagina dalle risorse incorporate (ui\…, anche nelle sottocartelle core\ e pages\).</summary>
        Private Sub ServeResource(sender As Object, e As CoreWebView2WebResourceRequestedEventArgs)
            Dim environment = _view.CoreWebView2.Environment
            Dim name = Uri.UnescapeDataString(New Uri(e.Request.Uri).AbsolutePath).TrimStart("/"c)
            If name = "" Then name = "index.html"
            Dim assembly = GetType(MainWindow).Assembly
            Dim stream = If(assembly.GetManifestResourceStream("ui/" & name), assembly.GetManifestResourceStream("ui/" & name.Replace("/"c, "\"c)))
            If stream Is Nothing Then
                e.Response = environment.CreateWebResourceResponse(Nothing, 404, "Not Found", "")
                Return
            End If
            Dim headers = $"Content-Type: {ContentType(name)}{vbCrLf}Cache-Control: no-store{vbCrLf}X-Content-Type-Options: nosniff"
            If name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) Then headers &= $"{vbCrLf}Content-Security-Policy: {ContentPolicy}"
            e.Response = environment.CreateWebResourceResponse(stream, 200, "OK", headers)
        End Sub

        Private Shared Function ContentType(name As String) As String
            Select Case Path.GetExtension(name).ToLowerInvariant()
                Case ".html" : Return "text/html; charset=utf-8"
                Case ".css" : Return "text/css; charset=utf-8"
                Case ".js" : Return "text/javascript; charset=utf-8"
                Case ".svg" : Return "image/svg+xml"
                Case ".png" : Return "image/png"
                Case ".ico" : Return "image/x-icon"
                Case Else : Return "application/octet-stream"
            End Select
        End Function

        Private Async Sub OnWebMessage(sender As Object, e As CoreWebView2WebMessageReceivedEventArgs)
            If Not e.Source.StartsWith(Origin & "/", StringComparison.OrdinalIgnoreCase) Then Return
            Dim id = 0
            Dim command = ""
            Dim args As JsonElement = Nothing
            Try
                Using doc = JsonDocument.Parse(e.TryGetWebMessageAsString())
                    id = doc.RootElement.GetProperty("id").GetInt32()
                    command = doc.RootElement.GetProperty("cmd").GetString()
                    Dim value As JsonElement
                    If doc.RootElement.TryGetProperty("args", value) Then args = value.Clone()
                End Using
            Catch ex As Exception
                Return
            End Try
            Dim reply As Object
            Try
                Dim result = Await _bridge.RunAsync(command, args)
                reply = New With {.id = id, .ok = True, .result = result}
            Catch ex As Exception
                reply = New With {.id = id, .ok = False, .error = AppBridge.Describe(ex)}
            End Try
            Post(reply)
        End Sub

        ''' <summary>Evento per la pagina (avanzamento di una scansione, fine, messaggi): solo dal thread della finestra.</summary>
        Public Sub PostEvent(name As String, data As Object)
            Post(New With {.event = name, .data = data})
        End Sub

        Private Sub Post(message As Object)
            If Not _ready OrElse IsDisposed OrElse _view.CoreWebView2 Is Nothing Then Return
            Try
                _view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, ReplyOptions))
            Catch ex As Exception
                ' finestra in chiusura
            End Try
        End Sub

        Private Sub OnNavigationStarting(sender As Object, e As CoreWebView2NavigationStartingEventArgs)
            If e.Uri.StartsWith(Origin & "/", StringComparison.OrdinalIgnoreCase) Then Return
            e.Cancel = True
            OpenExternal(e.Uri)
        End Sub

        ''' <summary>Menu del tasto destro: solo le voci per il testo (niente Indietro, Ricarica, Stampa…).</summary>
        Private Sub OnContextMenu(sender As Object, e As CoreWebView2ContextMenuRequestedEventArgs)
            Dim items = e.MenuItems
            For i = items.Count - 1 To 0 Step -1
                Dim item = items(i)
                If item.Kind <> CoreWebView2ContextMenuItemKind.Separator AndAlso Not MenuItemsKept.Contains(item.Name) Then items.RemoveAt(i)
            Next
            For i = items.Count - 1 To 0 Step -1
                If items(i).Kind = CoreWebView2ContextMenuItemKind.Separator AndAlso
                   (i = 0 OrElse i = items.Count - 1 OrElse items(i - 1).Kind = CoreWebView2ContextMenuItemKind.Separator) Then items.RemoveAt(i)
            Next
            If items.Count = 0 Then e.Handled = True
        End Sub

        Public Shared Sub OpenExternal(uri As String)
            If Not (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) OrElse uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) Then Return
            Try
                Process.Start(New ProcessStartInfo(uri) With {.UseShellExecute = True})
            Catch ex As Exception
            End Try
        End Sub

#End Region

#Region "Tema e posizione"

        <DllImport("dwmapi.dll")>
        Private Shared Function DwmSetWindowAttribute(hwnd As IntPtr, attribute As Integer, ByRef value As Integer, size As Integer) As Integer
        End Function

        Private _theme As String = "auto"

        ''' <summary>Tema scelto nelle impostazioni ("auto", "light", "dark"): sfondo della finestra e barra del titolo; la pagina lo applica da sé.</summary>
        Public Sub ApplyTheme(theme As String)
            _theme = If(theme, "auto")
            ApplyThemeColors()
            If IsHandleCreated Then ApplyTitleBarTheme()
        End Sub

        ''' <summary>Tema delle app di Windows (Impostazioni > Personalizzazione > Colori), salvo scelta esplicita.</summary>
        Private Function AppsUseDarkTheme() As Boolean
            If _theme = "dark" Then Return True
            If _theme = "light" Then Return False
            Try
                Using key = Registry.CurrentUser.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                    Dim value = key?.GetValue("AppsUseLightTheme")
                    Return TypeOf value Is Integer AndAlso CInt(value) = 0
                End Using
            Catch
                Return False
            End Try
        End Function

        ''' <summary>Sfondo uguale a quello della pagina (niente lampo bianco all'avvio nel tema scuro).</summary>
        Private Sub ApplyThemeColors()
            BackColor = If(AppsUseDarkTheme(), Color.FromArgb(10, 15, 23), Color.FromArgb(242, 244, 248))
            _view.DefaultBackgroundColor = BackColor
        End Sub

        ''' <summary>Barra del titolo scura con il tema scuro (DWMWA_USE_IMMERSIVE_DARK_MODE).</summary>
        Private Sub ApplyTitleBarTheme()
            Dim dark = If(AppsUseDarkTheme(), 1, 0)
            Try
                DwmSetWindowAttribute(Handle, 20, dark, 4)
            Catch ex As Exception
            End Try
        End Sub

        Private Sub OnUserPreferenceChanged(sender As Object, e As UserPreferenceChangedEventArgs)
            If e.Category <> UserPreferenceCategory.General OrElse IsDisposed Then Return
            BeginInvoke(Sub()
                            ApplyThemeColors()
                            ApplyTitleBarTheme()
                        End Sub)
        End Sub

        Private NotInheritable Class Placement
            Public Property X As Integer
            Public Property Y As Integer
            Public Property Width As Integer
            Public Property Height As Integer
            Public Property Maximized As Boolean
        End Class

        Private Shared ReadOnly Property PlacementFile As String
            Get
                Return Path.Combine(LocalFolder, "finestra.json")
            End Get
        End Property

        ''' <summary>Posizione e dimensioni dell'ultima volta, se cadono su uno schermo; altrimenti al centro, entro lo schermo.</summary>
        Private Sub RestorePlacement()
            Try
                If Not Visible AndAlso _startInTray Then Return
                If File.Exists(PlacementFile) Then
                    Dim p = JsonSerializer.Deserialize(Of Placement)(File.ReadAllText(PlacementFile))
                    Dim bounds As New Rectangle(p.X, p.Y, p.Width, p.Height)
                    If p.Width >= MinimumSize.Width AndAlso p.Height >= MinimumSize.Height AndAlso
                       Screen.AllScreens.Any(Function(s) Rectangle.Intersect(s.WorkingArea, bounds).Width >= 200 AndAlso Rectangle.Intersect(s.WorkingArea, bounds).Height >= 120) Then
                        Me.Bounds = bounds
                        If p.Maximized Then WindowState = FormWindowState.Maximized
                        Return
                    End If
                End If
            Catch ex As Exception
            End Try
            Dim area = Screen.FromControl(Me).WorkingArea
            If Width > area.Width OrElse Height > area.Height Then
                Size = New Size(Math.Min(Width, area.Width * 92 \ 100), Math.Min(Height, area.Height * 92 \ 100))
            End If
            Location = New Point(area.Left + (area.Width - Width) \ 2, area.Top + (area.Height - Height) \ 2)
        End Sub

        Private Sub SavePlacement()
            Try
                Dim b = If(WindowState = FormWindowState.Normal, Bounds, RestoreBounds)
                Directory.CreateDirectory(LocalFolder)
                File.WriteAllText(PlacementFile, JsonSerializer.Serialize(New Placement With {
                    .X = b.X, .Y = b.Y, .Width = b.Width, .Height = b.Height, .Maximized = WindowState = FormWindowState.Maximized
                }))
            Catch ex As Exception
            End Try
        End Sub

#End Region

        ''' <summary>Il setup dell'aggiornamento è partito: la finestra si chiude senza domande (la pagina ha già fatto salvare).</summary>
        Public Sub CloseForUpdate()
            _closingForUpdate = True
            Close()
        End Sub

        Private _closingForUpdate As Boolean

        ''' <summary>Sessione con dati non salvati: si chiede se salvarla prima di chiudere.</summary>
        Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
            MyBase.OnFormClosing(e)
            If e.Cancel Then Return
            ' La X nasconde nell'area di notifica (impostazione); Esci dal menu dell'icona e lo spegnimento chiudono davvero
            If _closeToTray AndAlso Not _exiting AndAlso Not _closingForUpdate AndAlso e.CloseReason = CloseReason.UserClosing Then
                e.Cancel = True
                SavePlacement()
                Hide()
                Return
            End If
            If _ready AndAlso Not _closingForUpdate AndAlso _bridge.HasUnsavedData Then
                Dim answer = MessageBox.Show(Me, "La sessione ha dati non salvati. Salvarla prima di chiudere?", Text,
                                             MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
                If answer = DialogResult.Cancel OrElse (answer = DialogResult.Yes AndAlso Not _bridge.SaveBeforeClosing()) Then
                    e.Cancel = True
                    Return
                End If
            End If
            SavePlacement()
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                RemoveHandler SystemEvents.UserPreferenceChanged, AddressOf OnUserPreferenceChanged
                _tray.Visible = False
                _tray.Dispose()
                _trayMenu.Dispose()
                _bridge.Dispose()
                _view.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
