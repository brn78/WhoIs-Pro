Imports NUnit.Framework
Imports WhoIsPro.Utils

Namespace WhoIsPro.Tests
    ''' <summary>Catalogo delle icone dei dispositivi (Material Design Icons incluse nel programma): lettura e ricerca in italiano.</summary>
    Public Class IconCatalogTests

        <Test>
        Public Sub Catalog_LoadsFromTheProgram()
            Assert.That(IconCatalog.Count, Iz.GreaterThan(7000))
            Assert.That(IconCatalog.Version, Iz.EqualTo("7.4.47"))
            Assert.That(IconCatalog.Find("router-wireless"), Iz.Not.Null)
            Assert.That(IconCatalog.Find("router-wireless").Path, Does.StartWith("M"))
            Assert.That(IconCatalog.CanonicalName("non-esiste-proprio"), Iz.EqualTo(""))
        End Sub

        ''' <summary>Le icone associate ai tipi e alle categorie dei dispositivi (ui\core\categories.js) esistono tutte nel catalogo.</summary>
        <Test>
        Public Sub DeviceTypeIcons_AllExistInTheCatalog()
            Dim dir = New IO.DirectoryInfo(AppContext.BaseDirectory)
            While dir IsNot Nothing AndAlso Not IO.File.Exists(IO.Path.Combine(dir.FullName, "WhoIs Pro.vbproj"))
                dir = dir.Parent
            End While
            Assert.That(dir, Iz.Not.Null, "cartella del progetto non trovata")
            Dim js = IO.File.ReadAllText(IO.Path.Combine(dir.FullName, "ui", "core", "categories.js"))
            Dim names = Text.RegularExpressions.Regex.Matches(js, "(?:mdi: |/, )'([a-z0-9-]+)'").Select(Function(m) m.Groups(1).Value).Distinct().ToList()
            Assert.That(names.Count, Iz.GreaterThan(20), "nomi delle icone non letti da categories.js")
            For Each n In names
                Assert.That(IconCatalog.Find(n), Iz.Not.Null, $"icona «{n}» non nel catalogo")
            Next
        End Sub

        <Test>
        Public Sub Search_FindsInItalian()
            Assert.That(IconCatalog.Search("telecamera", "", "filled", 0, 60).Items.Select(Function(i) i.Name), Does.Contain("cctv"))
            Assert.That(IconCatalog.Search("stampante", "", "filled", 0, 60).Items.Select(Function(i) i.Name), Does.Contain("printer"))
            Dim rete = IconCatalog.Search("", "@rete", "all", 0, 600)
            Assert.That(rete.Items.Select(Function(i) i.Name), Does.Contain("router-wireless"))
        End Sub
    End Class
End Namespace
