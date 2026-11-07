Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports NUnit.Framework
Imports WhoIsPro.UI

Namespace WhoIsPro.Tests

    ''' <summary>
    ''' Istantanea del menu dell'icona nell'area di notifica (strumento di sviluppo, non una prova: si lancia a mano con
    ''' --filter Snapshot_TrayMenu). Costruisce il menu come fa la finestra e lo salva in PNG, menu e sottomenu dei profili,
    ''' in WHOISPRO_SNAPSHOT_DIR; vuole WHOISPRO_DATA_FOLDER su una cartella di prova per non toccare i dati veri.
    ''' </summary>
    <Explicit("Istantanea del menu, non prova"), Apartment(ApartmentState.STA)>
    Public Class TrayMenuSnapshotTests

        <Test>
        Public Sub Snapshot_TrayMenu()
            Dim folder = Environment.GetEnvironmentVariable("WHOISPRO_SNAPSHOT_DIR")
            If String.IsNullOrWhiteSpace(folder) OrElse String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WHOISPRO_DATA_FOLDER")) Then
                Assert.Ignore("Servono WHOISPRO_SNAPSHOT_DIR (dove salvare i PNG) e WHOISPRO_DATA_FOLDER (una cartella dati di prova).")
            End If
            Directory.CreateDirectory(folder)
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
            Application.EnableVisualStyles()
            Using window As New MainWindow()
                Dim build = GetType(MainWindow).GetMethod("BuildTrayMenu", BindingFlags.NonPublic Or BindingFlags.Instance)
                Dim menuField = GetType(MainWindow).GetField("_trayMenu", BindingFlags.NonPublic Or BindingFlags.Instance)
                build.Invoke(window, Nothing)
                Dim menu = DirectCast(menuField.GetValue(window), ContextMenuStrip)
                menu.Show(New Point(40, 40))
                Application.DoEvents()
                Using bmp As New Bitmap(menu.Width, menu.Height)
                    menu.DrawToBitmap(bmp, New Rectangle(0, 0, menu.Width, menu.Height))
                    bmp.Save(Path.Combine(folder, "tray-menu.png"), ImageFormat.Png)
                End Using
                Dim profiles = menu.Items.OfType(Of ToolStripMenuItem)().FirstOrDefault(Function(i) i.Text = "Profili di rete")
                If profiles IsNot Nothing Then
                    profiles.ShowDropDown()
                    Application.DoEvents()
                    Dim dd = profiles.DropDown
                    Using bmp As New Bitmap(dd.Width, dd.Height)
                        dd.DrawToBitmap(bmp, New Rectangle(0, 0, dd.Width, dd.Height))
                        bmp.Save(Path.Combine(folder, "tray-submenu.png"), ImageFormat.Png)
                    End Using
                    TestContext.Out.WriteLine("VOCI sottomenu: " & String.Join(" | ", dd.Items.OfType(Of ToolStripItem)().Select(Function(i) If(TypeOf i Is ToolStripSeparator, "---", i.Text))))
                End If
                TestContext.Out.WriteLine("VOCI menu: " & String.Join(" | ", menu.Items.OfType(Of ToolStripItem)().Select(Function(i) If(TypeOf i Is ToolStripSeparator, "---", i.Text & If(i.Image Is Nothing, "", " [icona]")))))
                menu.Close()
            End Using
        End Sub

    End Class

End Namespace
