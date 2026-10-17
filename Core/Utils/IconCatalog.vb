Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text

Namespace Utils

    ''' <summary>Un'icona del catalogo: nome MDI, tracciato SVG (24×24), categorie e alias ufficiali.</summary>
    Public NotInheritable Class IconEntry
        Public Property Name As String = ""
        Public Property Path As String = ""
        Public Property Categories As String() = Array.Empty(Of String)()
        Public Property Aliases As String() = Array.Empty(Of String)()
        Public Property IsOutline As Boolean
        ''' <summary>Parti del nome (lan-connect → lan, connect).</summary>
        Friend Property Parts As String() = Array.Empty(Of String)()
        ''' <summary>Parti degli alias.</summary>
        Friend Property AliasParts As String() = Array.Empty(Of String)()
        ''' <summary>Parole italiane e delle categorie con cui si trova.</summary>
        Friend Property Words As String() = Array.Empty(Of String)()
    End Class

    ''' <summary>Una categoria per la scelta: chiave, nome italiano e quante icone contiene.</summary>
    Public NotInheritable Class IconCategory
        Public Property Key As String = ""
        Public Property Label As String = ""
        Public Property Count As Integer
        ''' <summary>Raccolta pensata per la piattaforma (rete, sicurezza…), non una categoria ufficiale MDI.</summary>
        Public Property Curated As Boolean
    End Class

    ''' <summary>
    ''' Catalogo delle icone Material Design Icons 7.4.47 (Pictogrammers, licenza Apache 2.0), incluso nel programma: niente
    ''' internet. Il file <c>Assets\Icons\mdi-v7.4.47.mui.gz</c> (lo stesso di BlackHole Workspaces, dal progetto ModernFrameworkUI dello stesso autore)
    ''' (7.188 icone, senza marchi né icone deprecate): intestazione «MUI1», schema, versione, numero di icone e per ognuna
    ''' nome, tracciato, autore, versione, categorie, alias e stili, nel formato di BinaryWriter. Si cerca in italiano: le
    ''' parti dei nomi inglesi hanno le loro traduzioni (<see cref="ItalianTerms"/>), le categorie un nome italiano e ci sono
    ''' raccolte pensate per i dispositivi di rete, la sicurezza e gli impianti. Serve a scegliere l'icona di un dispositivo.
    ''' </summary>
    Public NotInheritable Class IconCatalog

        Private Const Magic As UInteger = &H3149554DUI
        Private Const ResourceName As String = "WhoIsPro.Icons.mdi.mui.gz"
        Private Shared ReadOnly State As New Lazy(Of CatalogState)(AddressOf Load, True)

        Private Sub New()
        End Sub

        Public Shared ReadOnly Property Version As String
            Get
                Return State.Value.Version
            End Get
        End Property

        Public Shared ReadOnly Property Count As Integer
            Get
                Return State.Value.Icons.Length
            End Get
        End Property

        Public Shared Function Categories() As IReadOnlyList(Of IconCategory)
            Return State.Value.Categories
        End Function

        ''' <summary>L'icona per nome o alias ufficiale; Nothing se non c'è.</summary>
        Public Shared Function Find(name As String) As IconEntry
            If String.IsNullOrWhiteSpace(name) Then Return Nothing
            Dim icon As IconEntry = Nothing
            Return If(State.Value.Lookup.TryGetValue(name.Trim(), icon), icon, Nothing)
        End Function

        ''' <summary>Il nome canonico di un'icona (anche da un alias); "" se non c'è.</summary>
        Public Shared Function CanonicalName(name As String) As String
            Dim icon = Find(name)
            Return If(icon Is Nothing, "", icon.Name)
        End Function

        ''' <summary>
        ''' Cerca per parole (italiane o inglesi, ognuna deve trovare qualcosa), categoria e forma (shape: "filled" piene,
        ''' "outline" a contorno). Senza parole e senza categoria propone le icone più utili per la piattaforma.
        ''' </summary>
        Public Shared Function Search(query As String, category As String, shape As String, offset As Integer, limit As Integer) As (Total As Integer, Items As List(Of IconEntry))
            Dim st = State.Value
            Dim tokens = Normalize(query).Split(" "c, StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray()
            Dim cat = If(category, "").Trim()
            Dim inCategory As Func(Of IconEntry, Boolean) = Function(i) True
            If cat <> "" Then
                Dim members As HashSet(Of String) = Nothing
                If st.Curated.TryGetValue(cat, members) Then
                    inCategory = Function(i) members.Contains(i.Name)
                Else
                    inCategory = Function(i) i.Categories.Contains(cat, StringComparer.OrdinalIgnoreCase)
                End If
            End If
            Dim v = If(shape, "").Trim().ToLowerInvariant()
            Dim pool = st.Icons.Where(Function(i) inCategory(i) AndAlso (v = "" OrElse v = "all" OrElse (v = "outline") = i.IsOutline))
            Dim ordered As List(Of IconEntry)
            If tokens.Length = 0 Then
                If cat = "" Then
                    ' Le icone consigliate, nell'ordine della raccolta, poi le altre
                    ordered = pool.OrderBy(Function(i) st.SuggestedRank(i.Name)).ThenBy(Function(i) i.Name, StringComparer.Ordinal).ToList()
                Else
                    ordered = pool.OrderBy(Function(i) i.Name.Length).ThenBy(Function(i) i.Name, StringComparer.Ordinal).ToList()
                End If
            Else
                ordered = pool.Select(Function(i) (Icon:=i, Score:=Score(i, tokens))).
                    Where(Function(x) x.Score < Integer.MaxValue).
                    OrderBy(Function(x) x.Score).ThenBy(Function(x) x.Icon.Parts.Length).ThenBy(Function(x) x.Icon.Name, StringComparer.Ordinal).
                    Select(Function(x) x.Icon).ToList()
            End If
            Dim skip = Math.Max(0, offset)
            Dim take = Math.Clamp(limit, 1, 600)
            Return (ordered.Count, ordered.Skip(skip).Take(take).ToList())
        End Function

        ''' <summary>Parole che non dicono niente di una scheda: articoli, preposizioni, congiunzioni, forme societarie.</summary>
        Private Shared ReadOnly NoiseWords As New HashSet(Of String)(StringComparer.Ordinal) From {
            "il", "lo", "la", "le", "gli", "un", "uno", "una", "di", "da", "in", "con", "su", "per", "tra", "fra", "del", "dello", "della",
            "dei", "degli", "delle", "al", "allo", "alla", "ai", "agli", "alle", "dal", "dallo", "dalla", "dai", "dagli", "dalle", "nel",
            "nello", "nella", "nei", "negli", "nelle", "sul", "sullo", "sulla", "sui", "sugli", "sulle", "ed", "od", "srl", "srls", "spa",
            "snc", "sas", "sapa", "scarl", "coop", "soc", "the", "of", "and", "for", "nr", "num"}

        ''' <summary>Ultime parti dei nomi delle varianti «di servizio» (spenta, togli, aggiungi, impostazioni…): non si propongono.</summary>
        Private Shared ReadOnly ModifierParts As New HashSet(Of String)(StringComparer.Ordinal) From {
            "off", "remove", "minus", "plus", "cancel", "alert", "check", "cog", "settings", "edit", "clock", "sync", "refresh", "question",
            "lock", "unlock", "search", "star", "heart", "eye", "marker", "arrow", "sound", "music", "bluetooth"}

        ''' <summary>
        ''' Le icone da proporre per i testi di una scheda (il nome, poi il tipo, la categoria, la mansione…), dalla più
        ''' pertinente: prima quelle che vanno bene per tutte le parole di un testo, poi parola per parola nell'ordine in cui
        ''' sono scritte (senza articoli, preposizioni, numeri e forme societarie). Così la categoria «Videosorveglianza» conta
        ''' prima delle parole sparse della descrizione. Solo le forme piene, senza doppioni: è
        ''' l'elenco che «Proponi» scorre a ogni clic.
        ''' </summary>
        Public Shared Function Propose(texts As IEnumerable(Of String), limit As Integer) As List(Of String)
            Dim take = Math.Clamp(limit, 1, 60)
            Dim result As New List(Of String)()
            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
            Dim clean = If(texts, Enumerable.Empty(Of String)()).
                Select(Function(t) String.Join(" ", Normalize(t).Split(" "c, StringSplitOptions.RemoveEmptyEntries).
                    Where(Function(w) w.Length >= 2 AndAlso Not NoiseWords.Contains(w) AndAlso Not w.All(AddressOf Char.IsDigit)))).
                Where(Function(t) t <> "").Distinct().Take(6).ToList()
            Dim add = Sub(query As String, count As Integer)
                          ' Una parola corta (bar, nvr) vale solo intera: per inizio di parola troverebbe di tutto
                          Dim exact = Not query.Contains(" "c) AndAlso query.Length <= 3
                          For Each i In Search(query, "", "filled", 0, 60).Items
                              If result.Count >= take OrElse count <= 0 Then Return
                              If i.Parts.Length > 1 AndAlso ModifierParts.Contains(i.Parts(i.Parts.Length - 1)) Then Continue For
                              If exact AndAlso Not (i.Parts.Contains(query) OrElse i.AliasParts.Contains(query) OrElse i.Words.Contains(query)) Then Continue For
                              If seen.Add(i.Name) Then result.Add(i.Name) : count -= 1
                          Next
                      End Sub
            For Each t In clean
                add(t, 6)
            Next
            For Each w In clean.SelectMany(Function(t) t.Split(" "c)).Where(Function(x) x.Length >= 3).Distinct().Take(10)
                add(w, 6)
            Next
            Return result
        End Function

        ''' <summary>
        ''' Punteggio di un'icona per le parole cercate (più basso = più pertinente; MaxValue = non corrisponde). Ogni parola
        ''' deve corrispondere: al nome (intera o inizio di una parte), agli alias, alle traduzioni italiane o alle categorie.
        ''' Le parole italiane lunghe valgono anche al singolare o al plurale (telecamere trova telecamera).
        ''' </summary>
        Private Shared Function Score(icon As IconEntry, tokens As String()) As Integer
            Dim total = 0
            For Each token In tokens
                Dim stem = StemOf(token)
                Dim best = Integer.MaxValue
                If icon.Parts.Contains(token) Then
                    best = 0
                ElseIf icon.Parts.Any(Function(p) p.StartsWith(token, StringComparison.Ordinal)) Then
                    best = 6
                ElseIf icon.AliasParts.Contains(token) Then
                    best = 10
                ElseIf icon.Words.Contains(token) Then
                    best = 14
                ElseIf token.Length >= 3 AndAlso icon.AliasParts.Any(Function(p) p.StartsWith(token, StringComparison.Ordinal)) Then
                    best = 18
                ElseIf token.Length >= 3 AndAlso icon.Words.Any(Function(w) w.StartsWith(stem, StringComparison.Ordinal)) Then
                    best = 24
                ElseIf token.Length >= 4 AndAlso icon.Name.Contains(token, StringComparison.Ordinal) Then
                    best = 40
                End If
                If best = Integer.MaxValue Then Return Integer.MaxValue
                total += best
            Next
            ' A parità, prima le forme piene e i nomi brevi: le varianti (outline, variant, off…) vengono dopo
            Return total + If(icon.IsOutline, 1, 0)
        End Function

        ''' <summary>La radice di una parola italiana lunga, senza la vocale finale (telecamere → telecamer).</summary>
        Private Shared Function StemOf(token As String) As String
            If token.Length >= 5 AndAlso "aeio".IndexOf(token(token.Length - 1)) >= 0 Then Return token.Substring(0, token.Length - 1)
            Return token
        End Function

        ''' <summary>Minuscolo, senza accenti, solo lettere e cifre separate da uno spazio.</summary>
        Public Shared Function Normalize(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return ""
            Dim decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            Dim sb As New StringBuilder(decomposed.Length)
            For Each ch In decomposed
                If CharUnicodeInfo.GetUnicodeCategory(ch) = UnicodeCategory.NonSpacingMark Then Continue For
                sb.Append(If(Char.IsLetterOrDigit(ch), ch, " "c))
            Next
            Return String.Join(" ", sb.ToString().Split(" "c, StringSplitOptions.RemoveEmptyEntries))
        End Function

#Region "Caricamento"

        Private NotInheritable Class CatalogState
            Public Version As String = ""
            Public Icons As IconEntry() = Array.Empty(Of IconEntry)()
            Public Lookup As New Dictionary(Of String, IconEntry)(StringComparer.OrdinalIgnoreCase)
            Public Categories As New List(Of IconCategory)()
            Public Curated As New Dictionary(Of String, HashSet(Of String))(StringComparer.OrdinalIgnoreCase)
            Public Suggested As New Dictionary(Of String, Integer)(StringComparer.Ordinal)

            Public Function SuggestedRank(name As String) As Integer
                Dim rank As Integer
                Return If(Suggested.TryGetValue(name, rank), rank, Integer.MaxValue)
            End Function
        End Class

        Private Shared Function Load() As CatalogState
            Dim st As New CatalogState()
            Dim italian = ParseTerms(ItalianTerms)
            Using stream = GetType(IconCatalog).Assembly.GetManifestResourceStream(ResourceName)
                If stream Is Nothing Then Throw New InvalidDataException("Catalogo delle icone non incluso nel programma.")
                Using gz As New GZipStream(stream, CompressionMode.Decompress)
                    Using reader As New BinaryReader(gz, New UTF8Encoding(False, True))
                        If reader.ReadUInt32() <> Magic Then Throw New InvalidDataException("Catalogo delle icone non valido.")
                        If reader.ReadInt32() <> 1 Then Throw New InvalidDataException("Schema del catalogo delle icone non supportato.")
                        st.Version = reader.ReadString()
                        Dim n = reader.ReadInt32()
                        If n <= 0 OrElse n > 100000 Then Throw New InvalidDataException("Catalogo delle icone non valido.")
                        Dim list As New List(Of IconEntry)(n)
                        For i = 1 To n
                            Dim name = reader.ReadString()
                            Dim path = reader.ReadString()
                            reader.ReadString() ' autore
                            reader.ReadString() ' versione in cui è comparsa
                            Dim cats = ReadArray(reader)
                            Dim aliases = ReadArray(reader)
                            ReadArray(reader) ' stili (parti del nome)
                            list.Add(Build(name, path, cats, aliases, italian))
                        Next
                        st.Icons = list.OrderBy(Function(x) x.Name, StringComparer.Ordinal).ToArray()
                    End Using
                End Using
            End Using
            For Each icon In st.Icons
                st.Lookup(icon.Name) = icon
            Next
            For Each icon In st.Icons
                For Each a In icon.Aliases
                    If Not st.Lookup.ContainsKey(a) Then st.Lookup(a) = icon
                Next
            Next
            ' Raccolte pensate per la piattaforma, poi le categorie ufficiali con il nome italiano
            For Each c In CuratedSets
                Dim members As New HashSet(Of String)(StringComparer.Ordinal)
                For Each icon In st.Icons
                    If icon.Parts.Any(Function(p) c.Parts.Contains(p)) OrElse c.Names.Contains(icon.Name) Then members.Add(icon.Name)
                Next
                st.Curated(c.Key) = members
                st.Categories.Add(New IconCategory With {.Key = c.Key, .Label = c.Label, .Count = members.Count, .Curated = True})
            Next
            For Each g In st.Icons.SelectMany(Function(i) i.Categories).GroupBy(Function(c) c, StringComparer.OrdinalIgnoreCase).
                    OrderBy(Function(x) CategoryLabel(x.Key), StringComparer.CurrentCultureIgnoreCase)
                st.Categories.Add(New IconCategory With {.Key = g.Key, .Label = CategoryLabel(g.Key), .Count = g.Count()})
            Next
            Dim rank = 0
            For Each s In SuggestedNames.Split({" "c, ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries)
                Dim icon = Find(st, s)
                If icon IsNot Nothing AndAlso Not st.Suggested.ContainsKey(icon.Name) Then st.Suggested(icon.Name) = rank : rank += 1
            Next
            Return st
        End Function

        Private Shared Function Find(st As CatalogState, name As String) As IconEntry
            Dim icon As IconEntry = Nothing
            Return If(st.Lookup.TryGetValue(name, icon), icon, Nothing)
        End Function

        Private Shared Function ReadArray(reader As BinaryReader) As String()
            Dim n = reader.ReadInt32()
            If n < 0 OrElse n > 1000 Then Throw New InvalidDataException("Catalogo delle icone non valido.")
            Dim values(n - 1) As String
            For i = 0 To n - 1
                values(i) = reader.ReadString()
            Next
            Return values
        End Function

        Private Shared Function Build(name As String, path As String, cats As String(), aliases As String(), italian As Dictionary(Of String, String())) As IconEntry
            Dim parts = name.Split("-"c, StringSplitOptions.RemoveEmptyEntries)
            Dim aliasParts = aliases.SelectMany(Function(a) a.Split("-"c, StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray()
            Dim words As New HashSet(Of String)(StringComparer.Ordinal)
            For Each p In parts.Concat(aliasParts)
                Dim it As String() = Nothing
                If italian.TryGetValue(p, it) Then words.UnionWith(it)
            Next
            ' Coppie di parti con un significato loro (access-point, motion-sensor…)
            For i = 0 To parts.Length - 2
                Dim it As String() = Nothing
                If italian.TryGetValue(parts(i) & "-" & parts(i + 1), it) Then words.UnionWith(it)
            Next
            For Each c In cats
                words.UnionWith(Normalize(c).Split(" "c, StringSplitOptions.RemoveEmptyEntries))
                words.UnionWith(Normalize(CategoryLabel(c)).Split(" "c, StringSplitOptions.RemoveEmptyEntries))
            Next
            Return New IconEntry With {
                .Name = name, .Path = path, .Categories = cats, .Aliases = aliases,
                .IsOutline = parts.Contains("outline"), .Parts = parts, .AliasParts = aliasParts, .Words = words.ToArray()}
        End Function

        ''' <summary>Righe «inglese: parole italiane» (anche «a-b: …» per due parti insieme).</summary>
        Private Shared Function ParseTerms(text As String) As Dictionary(Of String, String())
            Dim map As New Dictionary(Of String, String())(StringComparer.Ordinal)
            For Each line In text.Split({ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries)
                Dim colon = line.IndexOf(":"c)
                If colon <= 0 Then Continue For
                Dim key = line.Substring(0, colon).Trim().ToLowerInvariant()
                Dim words = Normalize(line.Substring(colon + 1)).Split(" "c, StringSplitOptions.RemoveEmptyEntries)
                Dim existing As String() = Nothing
                map(key) = If(map.TryGetValue(key, existing), existing.Concat(words).Distinct().ToArray(), words)
            Next
            Return map
        End Function

        Private Shared Function CategoryLabel(key As String) As String
            Dim label As String = Nothing
            Return If(CategoryLabels.TryGetValue(key, label), label, key)
        End Function

#End Region

#Region "Dati: categorie, raccolte, consigliate, traduzioni"

        Private Shared ReadOnly CategoryLabels As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"Account / User", "Persone e account"}, {"Agriculture", "Agricoltura"}, {"Alert / Error", "Avvisi ed errori"},
            {"Alpha / Numeric", "Lettere e numeri"}, {"Animal", "Animali"}, {"Arrange", "Disposizione"}, {"Arrow", "Frecce"},
            {"Audio", "Audio"}, {"Automotive", "Auto e veicoli"}, {"Banking", "Banca e pagamenti"}, {"Battery", "Batterie"},
            {"Cellphone / Phone", "Telefoni"}, {"Clothing", "Abbigliamento"}, {"Cloud", "Cloud"}, {"Color", "Colori"},
            {"Currency", "Valute"}, {"Database", "Database"}, {"Date / Time", "Data e ora"}, {"Developer / Languages", "Sviluppo software"},
            {"Device / Tech", "Dispositivi e tecnologia"}, {"Drawing / Art", "Disegno e arte"}, {"Edit / Modify", "Modifica"},
            {"Emoji", "Emoji"}, {"Files / Folders", "File e cartelle"}, {"Food / Drink", "Cibo e bevande"}, {"Form", "Moduli"},
            {"Gaming / RPG", "Giochi"}, {"Geographic Information System", "Mappe e territorio"}, {"Hardware / Tools", "Attrezzi"},
            {"Health / Beauty", "Salute e bellezza"}, {"Holiday", "Feste"}, {"Home Automation", "Casa e domotica"}, {"Lock", "Lucchetti e accessi"},
            {"Math", "Matematica"}, {"Medical / Hospital", "Medicina e ospedale"}, {"Music", "Musica"}, {"Nature", "Natura"},
            {"Navigation", "Navigazione"}, {"Notification", "Notifiche"}, {"People / Family", "Persone e famiglia"},
            {"Photography", "Fotografia"}, {"Places", "Luoghi"}, {"Printer", "Stampanti"}, {"Religion", "Religione"},
            {"Science", "Scienza"}, {"Settings", "Impostazioni"}, {"Shape", "Forme"}, {"Shopping", "Acquisti"}, {"Sport", "Sport"},
            {"Text / Content / Format", "Testo e formato"}, {"Tooltip", "Fumetti"}, {"Transportation + Flying", "Trasporti aerei"},
            {"Transportation + Other", "Altri trasporti"}, {"Transportation + Road", "Trasporti su strada"},
            {"Transportation + Water", "Trasporti via acqua"}, {"Vector", "Vettoriale"}, {"Video / Movie", "Video e film"},
            {"View", "Viste"}, {"Weather", "Meteo"}}

        Private NotInheritable Class CuratedSet
            Public Key As String
            Public Label As String
            Public Parts As HashSet(Of String)
            Public Names As HashSet(Of String)
            Public Sub New(key As String, label As String, parts As String, Optional names As String = "")
                Me.Key = key : Me.Label = label
                Me.Parts = New HashSet(Of String)(parts.Split(" "c, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal)
                Me.Names = New HashSet(Of String)(names.Split(" "c, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal)
            End Sub
        End Class

        ''' <summary>Raccolte per i lavori della piattaforma: icone che hanno nel nome una di queste parti (o sono nell'elenco).</summary>
        Private Shared ReadOnly CuratedSets As CuratedSet() = {
            New CuratedSet("@rete", "★ Rete e informatica", "network lan ethernet wifi wireless router switch server ip web wan vpn nas dns firewall modem antenna signal access desktop laptop monitor cloud console terminal usb harddisk hdd ssd memory chip cpu sim serial-port remote cast broadcast satellite cellphone tablet qrcode barcode",
                          "access-point access-point-network router-wireless router-network server-network nas lan-connect lan-disconnect lan-pending ip-network ip-network-outline web-box"),
            New CuratedSet("@sicurezza", "★ Sicurezza e videosorveglianza", "cctv camera cam webcam shield lock alarm siren police fingerprint badge doorbell motion security guard key smoke fire extinguisher incognito eye",
                          "motion-sensor smoke-detector fire-alert alarm-light alarm-bell door-closed-lock gate-open shield-home"),
            New CuratedSet("@energia", "★ Elettricità ed energia", "flash lightning power plug socket electric electricity battery solar fuse lightbulb led meter transmission sine generator wind turbine ev charging",
                          "transmission-tower home-lightning-bolt solar-panel solar-power"),
            New CuratedSet("@clima", "★ Clima e impianti", "thermostat hvac fan air radiator heat heater snowflake thermometer water pipe valve pump boiler fire gas humidity sprinkler faucet",
                          "air-conditioner air-filter water-boiler heat-pump"),
            New CuratedSet("@edifici", "★ Edifici e luoghi", "home house office domain factory warehouse store storefront hospital school church city garage greenhouse office-building stadium hotel bank",
                          "office-building warehouse factory"),
            New CuratedSet("@attrezzi", "★ Attrezzi e lavori", "tools wrench hammer screwdriver saw drill toolbox ladder hard-hat crane excavator shovel tape ruler level paint brush",
                          "hard-hat account-hard-hat")}

        ''' <summary>Le icone proposte per prime, senza ricerca: le più utili per clienti, impianti, dispositivi e lavoro.</summary>
        Private Const SuggestedNames As String = "
            account account-tie account-group domain office-building home factory warehouse store hospital-building school
            lan lan-connect network access-point router-wireless router-network server server-network nas switch ethernet wifi web cloud ip-network desktop-classic laptop monitor printer cellphone tablet phone-classic
            cctv camera webcam shield-lock lock alarm-light motion-sensor fingerprint smoke-detector fire-extinguisher fire badge-account key gate door-closed-lock doorbell-video
            flash lightning-bolt power-plug power-socket-eu battery solar-panel lightbulb transmission-tower meter-electric
            thermostat fan air-conditioner radiator thermometer water water-boiler pipe valve snowflake
            car truck van-utility bus forklift tow-truck tools wrench hammer screwdriver toolbox ladder hard-hat
            file-document folder clipboard-check calendar clock email phone message chart-line cash currency-eur cart tag star heart flag map-marker
            leaf tree flower paw coffee food silverware-fork-knife medical-bag pill heart-pulse school-outline"

        ''' <summary>
        ''' Traduzioni delle parti dei nomi inglesi: «parte: parole italiane». Le parole senza accenti (si confrontano senza).
        ''' Servono a trovare in italiano; le parole inglesi del nome valgono comunque.
        ''' </summary>
        Private Const ItalianTerms As String = "
access: accesso varco
access-point: punto di accesso wifi rete antenna
account: utente persona cliente profilo account
air: aria
air-conditioner: condizionatore climatizzatore clima split impianto climatizzazione
air-filter: filtro aria
air-purifier: purificatore aria
airplane: aereo volo
alarm: allarme sveglia
alarm-light: lampeggiante allarme impianto antintrusione
alarm-panel: centrale allarme antifurto impianto antintrusione
alert: avviso attenzione allarme
alpha: lettera
ambulance: ambulanza soccorso
animal: animale
antenna: antenna segnale
apple: mela
application: applicazione programma finestra
archive: archivio scatola
arrow: freccia
attachment: allegato graffetta
audio: audio suono
auto: automatico
backup: copia sicurezza salvataggio
badge: tesserino badge distintivo
bag: borsa
ball: palla
bank: banca
barcode: codice barre
barrier: barriera sbarra
basket: cestino carrello
bathroom: bagno
battery: batteria pila accumulatore
beach: spiaggia mare
bed: letto camera
bedroom: camera letto
beer: birra
bell: campanello campana notifica
bicycle: bicicletta bici
bike: bicicletta bici moto
bin: cestino
birthday: compleanno
blinds: tapparella tende persiane
bluetooth: bluetooth
boat: barca nave
bolt: fulmine bullone
book: libro manuale
bookmark: segnalibro
boiler: caldaia
bottle: bottiglia
box: scatola pacco
brain: cervello idea
bread: pane
bridge: ponte
briefcase: valigetta lavoro ufficio borsa
brightness: luminosita
broadcast: trasmissione antenna
brush: pennello
bucket: secchio
bug: insetto errore
building: edificio palazzo
bulb: lampadina
bus: autobus pullman
business: azienda affari
button: pulsante
cable: cavo
cake: torta
calculator: calcolatrice
calendar: calendario data appuntamento
call: chiamata telefonata
camera: telecamera fotocamera videocamera
camcorder: videocamera
cancel: annulla
car: auto automobile macchina veicolo
card: scheda carta tessera
cart: carrello acquisti
cash: contanti denaro soldi
cast: trasmetti
cctv: telecamera videosorveglianza tvcc sorveglianza impianto nvr dvr registratore videoregistratore
ceiling: soffitto
cellphone: cellulare telefono smartphone
certificate: certificato attestato
chair: sedia
charging: ricarica
chart: grafico
chat: chat messaggio conversazione
check: spunta conferma fatto verificato
chef: cuoco cucina
chess: scacchi
child: bambino figlio
chip: chip processore
church: chiesa
circle: cerchio
city: citta
clipboard: appunti lista modulo
clock: orologio ora
close: chiudi
closet: armadio
cloud: nuvola cloud
coffee: caffe bar caffetteria
cog: ingranaggio impostazioni configurazione
coin: moneta
comment: commento
compass: bussola
computer: computer pc
connection: connessione
console: console terminale
contact: contatto
contactless: contactless pagamento
contrast: contrasto
cookie: biscotto
counter: contatore
cpu: processore cpu
crane: gru
credit: credito
crown: corona
cube: cubo
cup: tazza
currency: valuta moneta
cursor: cursore
cut: taglia forbici
dashboard: cruscotto pannello
database: database archivio dati
delete: elimina cancella cestino
desk: scrivania
desktop: computer fisso pc postazione
details: dettagli
detector: rilevatore sensore
devices: dispositivi apparecchi
dialpad: tastiera telefono
dimmer: regolatore luce
dishwasher: lavastoviglie
disk: disco
dns: dns rete
doctor: medico dottore
document: documento
dog: cane
dollar: dollaro
domain: azienda edificio sede
door: porta
doorbell: campanello citofono videocitofono
download: scarica
drill: trapano
drink: bevanda
drive: unita disco
drop: goccia
dryer: asciugatrice
earth: terra mondo
edit: modifica
egg: uovo
elevator: ascensore
email: posta email messaggio
emergency: emergenza
energy: energia
engine: motore
error: errore
escalator: scala mobile
ethernet: rete cavo ethernet lan
electric: elettrico elettricita impianto elettrico
electricity: elettricita elettrico corrente
eur: euro
euro: euro
ev: auto elettrica colonnina ricarica
event: evento
excavator: escavatore scavatrice
exit: uscita
extinguisher: estintore antincendio
eye: occhio vista
face: viso volto faccia
factory: fabbrica stabilimento industria
family: famiglia
fan: ventilatore ventola
farm: fattoria azienda agricola
faucet: rubinetto
file: file documento
filter: filtro
fingerprint: impronta digitale biometrico
fire: fuoco fiamma incendio antincendio
firewall: firewall sicurezza rete
fish: pesce
flag: bandiera
flash: fulmine flash elettricita elettrico elettricista
flask: provetta laboratorio
floor: piano pavimento
flower: fiore
folder: cartella
food: cibo
forklift: muletto carrello elevatore
form: modulo
fridge: frigorifero frigo
fruit: frutta
fuel: carburante benzina gasolio
furniture: mobili arredamento
fuse: fusibile
game: gioco
garage: garage box autorimessa
garden: giardino
gas: gas
gate: cancello
gauge: manometro indicatore
gear: ingranaggio
generator: generatore gruppo elettrogeno
gift: regalo
glasses: occhiali
globe: mondo globo internet
gps: gps posizione
graph: grafico
grass: erba prato
greenhouse: serra
group: gruppo
guitar: chitarra
hammer: martello
hand: mano
handshake: stretta di mano accordo
hard: duro
hard-hat: elmetto casco cantiere operaio tecnico
harddisk: disco fisso hard disk
hdd: disco fisso
headphones: cuffie
headset: cuffie auricolari
heart: cuore preferito salute
heat: calore riscaldamento
heater: stufa riscaldamento
help: aiuto
history: cronologia storia
home: casa abitazione principale
hospital: ospedale
hotel: albergo hotel
house: casa abitazione villa
hvac: climatizzazione riscaldamento ventilazione impianto termico
human: persona umano
humidity: umidita
image: immagine foto
inbox: posta in arrivo
industrial: industriale
information: informazione dettagli
internet: internet rete web
invoice: fattura
ip: indirizzo ip rete
iron: ferro stiro
key: chiave
keyboard: tastiera
kitchen: cucina
ladder: scala
lamp: lampada
lan: rete lan cavo
laptop: portatile notebook computer
laundry: lavanderia
lawn: prato
leaf: foglia
led: led luce
library: biblioteca libreria
light: luce
lightbulb: lampadina luce idea
lightning: fulmine elettricita elettrico
link: collegamento link
list: elenco lista
location: posizione luogo
lock: lucchetto blocco sicurezza chiuso
map: mappa cartina
marker: segnaposto indicatore
medical: medico sanitario
meter: contatore misuratore
memory: memoria ram
message: messaggio
meter-electric: contatore elettrico impianto elettrico
microphone: microfono
microwave: microonde
mobile: cellulare mobile
modem: modem router
money: soldi denaro
monitor: monitor schermo
moon: luna notte
motion: movimento
motion-sensor: sensore di movimento rilevatore volumetrico
motorbike: moto motocicletta
mountain: montagna
mouse: mouse topo
movie: film
music: musica
nas: nas archivio rete
network: rete connessione
newspaper: giornale notizie
note: nota appunto
notebook: quaderno taccuino
office: ufficio
office-building: ufficio palazzo edificio sede condominio palazzina
oil: olio
outlet: presa
oven: forno
package: pacco spedizione
paint: vernice pittura
palette: tavolozza colori
panel: pannello quadro
paper: carta
parking: parcheggio
password: password
paw: zampa animale
pen: penna
pencil: matita modifica
people: persone gente
percent: percentuale sconto
person: persona
phone: telefono
pill: pillola farmaco medicina
pipe: tubo tubazione
pizza: pizza
plane: aereo
plant: pianta
plug: spina presa elettrico
plumbing: idraulica impianto idraulico
police: polizia
pool: piscina
power: alimentazione energia accensione corrente
printer: stampante
pump: pompa
puzzle: puzzle
qrcode: codice qr
radiator: radiatore termosifone
radio: radio
rain: pioggia
receipt: ricevuta scontrino
recycle: riciclo
refrigerator: frigorifero
remote: telecomando remoto
report: rapporto relazione
restaurant: ristorante
road: strada
robot: robot
rocket: razzo
roof: tetto
room: stanza camera
router: router rete modem
ruler: righello misura
safe: cassaforte
satellite: satellite parabola
saw: sega
scale: bilancia
school: scuola
screen: schermo
screwdriver: cacciavite
security: sicurezza
sensor: sensore
server: server rete
shield: scudo protezione sicurezza
shop: negozio
shopping: acquisti spesa
shovel: pala
shower: doccia
signal: segnale
sim: sim scheda
siren: sirena allarme
smoke: fumo
smoke-detector: rilevatore di fumo antincendio
snowflake: neve fiocco freddo
socket: presa elettrico
sofa: divano
silverware: posate ristorante pizzeria trattoria mensa
solar: solare fotovoltaico
solar-panel: pannello solare fotovoltaico impianto fotovoltaico
speaker: altoparlante cassa diffusore
sprinkler: irrigatore sprinkler antincendio impianto
stairs: scale
star: stella preferito
store: negozio
storefront: negozio vetrina
sun: sole
switch: interruttore switch
sync: sincronizza
table: tabella tavolo
tablet: tablet
tag: etichetta
tank: serbatoio cisterna
tape: nastro metro
taxi: taxi
telephone: telefono
television: televisione tv
thermometer: termometro temperatura
thermostat: termostato cronotermostato
ticket: biglietto ticket
timer: timer
toilet: bagno wc
toolbox: cassetta attrezzi
tools: attrezzi utensili elettroutensili trapano avvitatore
tow-truck: carro attrezzi
tower: torre traliccio
tractor: trattore
traffic: traffico semaforo
train: treno
transmission-tower: traliccio linea elettrica elettrico
trash: cestino spazzatura
tree: albero
truck: camion autocarro furgone
tv: televisione tv
umbrella: ombrello
usb: usb chiavetta
user: utente
vacuum: aspirapolvere
valve: valvola
van: furgone
video: video
videocam: videocamera
vpn: vpn rete privata
wall: muro parete
wallet: portafoglio
wan: rete internet
warehouse: magazzino deposito
washer: lavatrice
washing: lavaggio
watch: orologio
water: acqua
water-boiler: scaldabagno caldaia
weather: meteo tempo
web: web internet sito
webcam: webcam telecamera
wifi: wifi rete senza fili wireless
wind: vento
window: finestra
wireless: wireless senza fili
woman: donna
wrench: chiave inglese manutenzione riparazione
"

#End Region

    End Class

End Namespace
