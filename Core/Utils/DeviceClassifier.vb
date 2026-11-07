Option Strict On

Imports System.Text.RegularExpressions
Imports WhoIsPro.Network.Scan

Namespace Utils

    ''' <summary>Esito della classificazione: tipo (frase breve), categoria e icona (emoji per i report di testo).</summary>
    Public Class DeviceClassification
        Public Property DeviceType As String = "Dispositivo"
        Public Property Category As String = DeviceClassifier.CategoryOther
        Public Property Icon As String = "💻"
    End Class

    ''' <summary>
    ''' Riconosce il tipo di dispositivo dai segnali raccolti in scansione: porte aperte, nomi (DNS e NetBIOS),
    ''' produttore del MAC, TTL e banner. Le regole vanno dalla più affidabile alla più generica: prima il ruolo
    ''' certo (gateway, hypervisor), poi l'identità del dispositivo (stampante, NAS, telecamera...), poi il ruolo
    ''' dedotto dai servizi (controller di dominio, server Windows o Linux) e infine produttore e TTL.
    ''' </summary>
    Public Module DeviceClassifier

#Region "Categorie e icone"

        Public Const CategoryNetwork As String = "Rete"
        Public Const CategoryWorkstation As String = "Postazione"
        Public Const CategoryServer As String = "Server"
        Public Const CategoryVirtualization As String = "Virtualizzazione"
        Public Const CategoryPrinter As String = "Stampante"
        Public Const CategoryTv As String = "Smart TV"
        Public Const CategoryCamera As String = "Telecamera"
        Public Const CategoryHome As String = "Domotica"
        Public Const CategoryMobile As String = "Mobile"
        Public Const CategoryStorage As String = "Storage"
        Public Const CategoryConsole As String = "Console"
        Public Const CategoryAlarm As String = "Centrale d'allarme"
        Public Const CategoryOther As String = "Altro"

        ''' <summary>Tutte le categorie, nell'ordine in cui conviene presentarle.</summary>
        Public ReadOnly Categories As IReadOnlyList(Of String) = {
            CategoryNetwork, CategoryServer, CategoryVirtualization, CategoryWorkstation, CategoryStorage, CategoryPrinter,
            CategoryCamera, CategoryAlarm, CategoryTv, CategoryHome, CategoryMobile, CategoryConsole, CategoryOther
        }

        Private ReadOnly EmojiByCategory As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {CategoryNetwork, "🌐"}, {CategoryWorkstation, "💻"}, {CategoryServer, "🖥️"}, {CategoryVirtualization, "🧊"},
            {CategoryPrinter, "🖨️"}, {CategoryTv, "📺"}, {CategoryCamera, "📹"}, {CategoryAlarm, "🚨"}, {CategoryHome, "🏠"},
            {CategoryMobile, "📱"}, {CategoryStorage, "🗄️"}, {CategoryConsole, "🎮"}, {CategoryOther, "🔌"}
        }

        ''' <summary>Icona (emoji) della categoria, per i report di testo e CSV.</summary>
        Public Function IconFor(category As String) As String
            Dim icon As String = Nothing
            Return If(category IsNot Nothing AndAlso EmojiByCategory.TryGetValue(category, icon), icon, EmojiByCategory(CategoryOther))
        End Function

#End Region

#Region "Classificazione"

        ''' <summary>Tipo, categoria e icona del dispositivo a partire da tutto quello che la scansione ha raccolto.</summary>
        Public Function Classify(host As DiscoveredHost) As DeviceClassification
            If host Is Nothing Then Throw New ArgumentNullException(NameOf(host))
            Dim s As New Signals(host)
            Dim rules As Func(Of Signals, Result)() = {
                AddressOf Gateway, AddressOf Hypervisor, AddressOf HikvisionDevice, AddressOf Alarm, AddressOf Automation, AddressOf Printer3D, AddressOf Printer, AddressOf Nas, AddressOf Camera,
                AddressOf GameConsole, AddressOf MediaPlayer, AddressOf VoiceAssistant, AddressOf HomeAutomation, AddressOf Ups,
                AddressOf Voip, AddressOf NetworkGear, AddressOf Apple, AddressOf RaspberryPi, AddressOf Mobile, AddressOf ServerRole,
                AddressOf Windows, AddressOf Unix, AddressOf ByVendor, AddressOf ByTtl
            }
            Dim result As Result = Nothing
            For Each rule In rules
                result = rule(s)
                If result IsNot Nothing Then Exit For
            Next
            If result Is Nothing Then result = New Result("Dispositivo", CategoryOther)
            result = ApplyVirtualMachine(s, result)
            Return New DeviceClassification With {.DeviceType = result.Type, .Category = result.Category, .Icon = IconFor(result.Category)}
        End Function

        ''' <summary>Classificazione dai soli dati della sessione (produttore, nome, porte, TTL): usata dalla riclassificazione e dai test.</summary>
        Public Function Classify(macVendor As String, hostname As String, openPorts As IEnumerable(Of Integer), ttl As Integer, mac As String) As DeviceClassification
            Dim host As New DiscoveredHost With {
                .Vendor = MacVendorLookup.CleanVendor(macVendor),
                .HostName = If(String.IsNullOrWhiteSpace(hostname), Nothing, hostname),
                .Mac = MacVendorLookup.Normalize(mac),
                .Ttl = If(ttl > 0, ttl, CType(Nothing, Integer?))
            }
            If openPorts IsNot Nothing Then
                For Each p In openPorts
                    host.Ports.Add(New OpenPort(p, "TCP", PortDictionary.ServiceNameOrNull(p), Nothing))
                Next
            End If
            Return Classify(host)
        End Function

        Private NotInheritable Class Result
            Public Sub New(type As String, category As String)
                Me.Type = type
                Me.Category = category
            End Sub
            Public ReadOnly Property Type As String
            Public ReadOnly Property Category As String
        End Class

        Private Function Join(label As String, brand As Brand) As String
            Return If(brand Is Nothing, label, $"{label} {brand.Name}")
        End Function

#End Region

#Region "Regole: ruolo certo"

        Private Function Gateway(s As Signals) As Result
            If Not s.IsGateway Then Return Nothing
            Dim firewall = s.FindBrand(Kind.Firewall)
            If firewall IsNot Nothing Then Return New Result($"Firewall / gateway {firewall.Name}", CategoryNetwork)
            Return New Result(Join("Router / gateway", s.FindBrand(Kind.Network)), CategoryNetwork)
        End Function

        Private Function Hypervisor(s As Signals) As Result
            ' solo porte, nomi e banner: il produttore del MAC (Proxmox, Nutanix, VMware) è anche quello delle VM ospitate
            If s.Port(2179) Then Return New Result("Host Hyper-V", CategoryVirtualization)
            If s.Port(8006) OrElse s.InNamesOrBanners("proxmox") Then Return New Result("Host Proxmox VE", CategoryVirtualization)
            If s.InNamesOrBanners("esxi") OrElse (s.Port(902) AndAlso s.Port(443) AndAlso Not s.Port(445) AndAlso Not s.TtlWindows) Then Return New Result("Host VMware ESXi", CategoryVirtualization)
            If s.InNamesOrBanners("vcenter", "vsphere") Then Return New Result("VMware vCenter", CategoryVirtualization)
            If s.InNamesOrBanners("xcp-ng", "xenserver", "citrix hypervisor") Then Return New Result("Host XenServer / XCP-ng", CategoryVirtualization)
            If s.Port(9440) OrElse s.InNamesOrBanners("nutanix") Then Return New Result("Nutanix (Prism)", CategoryVirtualization)
            If s.Port(902) AndAlso s.WindowsLike Then Return New Result("Postazione con VMware Workstation", CategoryVirtualization)
            If s.WindowsLike AndAlso (s.Port(5985) OrElse s.Port(445)) AndAlso s.InNames("=hyperv", "hyper-v", "=hv", "hvhost", "vmhost") Then Return New Result("Host Hyper-V", CategoryVirtualization)
            Return Nothing
        End Function

#End Region

#Region "Regole: identità del dispositivo"

        Private ReadOnly HikvisionModelPattern As New Regex("\b((?:i?ds|hm|ipc|nvr|dvr)-[a-z0-9/]{2,16}(?:-[a-z0-9/]{1,12})*)\b", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant Or RegexOptions.Compiled)
        ''' <summary>Codici che usa solo Hikvision (DS-2CD…, DS-7608…, DS-K1T…, DS-PWA…, DS-3E…, HM-TD…): bastano da soli; «nvr-sala» o «ds-server» no.</summary>
        Private ReadOnly HikvisionOwnModelPattern As New Regex("^(?:i?DS-(?:2[A-Z]{2}\d|[1-9]\d{2,3}[A-Z]|K[0-9A-Z]|P[WH]A|3E\d|19A|TC[GM])|HM-TD)", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant Or RegexOptions.Compiled)
        Private ReadOnly HikvisionNvrChannelsPattern As New Regex("^(?:i?ds)-(?:71|76|77|86|96)(04|08|16|32|64|128)", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant Or RegexOptions.Compiled)
        Private ReadOnly SadpFirmwareBannerPattern As New Regex("(?:\bFW|\bFirmware)\s+(V\d[^\r\n·|]*)", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant Or RegexOptions.Compiled)

        ''' <summary>
        ''' Dispositivi Hikvision (telecamere IP/PTZ/termiche/ANPR, NVR, DVR Turbo HD, videocitofoni, terminali controllo
        ''' accessi MinMoe, centrali AX PRO, switch PoE): usa i metadati SADP se presenti, oppure il codice modello in nomi/banner.
        ''' </summary>
        Private Function HikvisionDevice(s As Signals) As Result
            Dim isHik = s.Sadp IsNot Nothing OrElse
                        s.InAny("hikvision", "app-webs", "dnvrs-webs", "dvrdvs-webs", "hikvision-webs", "/doc/page/login.asp", "hikvision sdk", "realm: ip camera(")
            Dim model = ExtractHikvisionModel(s)
            If Not isHik Then
                ' senza altri indizi serve un codice che usa solo Hikvision, e un produttore del MAC che non dica il contrario
                Dim vendorBrand = s.VendorBrand(Kind.Any)
                If model Is Nothing OrElse Not HikvisionOwnModelPattern.IsMatch(model) OrElse
                   (vendorBrand IsNot Nothing AndAlso vendorBrand.Name <> "Hikvision") Then Return Nothing
            End If

            If model IsNot Nothing Then
                Dim upper = model.ToUpperInvariant()
                ' Centrali d'allarme AX PRO / PHA
                If upper.StartsWith("DS-PWA", StringComparison.Ordinal) OrElse upper.StartsWith("DS-PHA", StringComparison.Ordinal) OrElse upper.StartsWith("DS-19A", StringComparison.Ordinal) Then
                    Return New Result($"Centrale d'allarme Hikvision AX PRO ({model})", CategoryAlarm)
                End If
                ' Terminali presenze / riconoscimento facciale MinMoe e centraline controllo accessi
                If upper.StartsWith("DS-K1T", StringComparison.Ordinal) OrElse upper.StartsWith("DS-K1A", StringComparison.Ordinal) Then
                    Return New Result($"Terminale controllo accessi Hikvision {model}", CategoryHome)
                End If
                If upper.StartsWith("DS-K26", StringComparison.Ordinal) OrElse upper.StartsWith("DS-K28", StringComparison.Ordinal) OrElse upper.StartsWith("DS-K2M", StringComparison.Ordinal) Then
                    Return New Result($"Centralina controllo accessi Hikvision {model}", CategoryHome)
                End If
                ' Videocitofonia IP (posti esterni KV/KD/KB e monitor interni KH)
                If upper.StartsWith("DS-KV", StringComparison.Ordinal) OrElse upper.StartsWith("DS-KD", StringComparison.Ordinal) OrElse upper.StartsWith("DS-KB", StringComparison.Ordinal) Then
                    Return New Result($"Posto esterno videocitofono Hikvision {model}", CategoryCamera)
                End If
                If upper.StartsWith("DS-KH", StringComparison.Ordinal) Then
                    Return New Result($"Monitor videocitofono Hikvision {model}", CategoryCamera)
                End If
                ' Switch di rete PoE Hikvision
                If upper.StartsWith("DS-3E", StringComparison.Ordinal) Then
                    Return New Result($"Switch PoE Hikvision {model}", CategoryNetwork)
                End If
                ' Telecamere PTZ / Speed Dome
                If upper.StartsWith("DS-2DE", StringComparison.Ordinal) OrElse upper.StartsWith("DS-2DF", StringComparison.Ordinal) OrElse
                   upper.StartsWith("IDS-2DE", StringComparison.Ordinal) OrElse upper.StartsWith("IDS-2DF", StringComparison.Ordinal) OrElse
                   upper.StartsWith("DS-2PT", StringComparison.Ordinal) Then
                    Return New Result($"Telecamera PTZ Hikvision {model}", CategoryCamera)
                End If
                ' Telecamere termiche
                If upper.StartsWith("DS-2TD", StringComparison.Ordinal) OrElse upper.StartsWith("IDS-2TD", StringComparison.Ordinal) OrElse upper.StartsWith("HM-TD", StringComparison.Ordinal) Then
                    Return New Result($"Telecamera termica Hikvision {model}", CategoryCamera)
                End If
                ' Telecamere lettura targhe (ANPR) / Traffico
                If upper.StartsWith("IDS-2CD7A", StringComparison.Ordinal) OrElse upper.StartsWith("DS-2CD7A", StringComparison.Ordinal) OrElse
                   upper.StartsWith("DS-2CD4A26", StringComparison.Ordinal) OrElse upper.StartsWith("IDS-TCM", StringComparison.Ordinal) OrElse
                   upper.StartsWith("DS-TCG", StringComparison.Ordinal) Then
                    Return New Result($"Telecamera ANPR Hikvision {model}", CategoryCamera)
                End If
                ' Decoder / Video Wall
                If upper.StartsWith("DS-69", StringComparison.Ordinal) OrElse upper.StartsWith("DS-64", StringComparison.Ordinal) Then
                    Return New Result($"Decoder video Hikvision {model}", CategoryCamera)
                End If
                ' Videoregistratori NVR (DS-76xxNI, DS-77xxNI, DS-86xxNI, DS-96xxNI, DS-71xxNI, NVR-...)
                Dim isNvrModel = upper.StartsWith("NVR-", StringComparison.Ordinal) OrElse
                                 ((upper.StartsWith("DS-71", StringComparison.Ordinal) OrElse upper.StartsWith("DS-76", StringComparison.Ordinal) OrElse
                                   upper.StartsWith("DS-77", StringComparison.Ordinal) OrElse upper.StartsWith("DS-86", StringComparison.Ordinal) OrElse
                                   upper.StartsWith("DS-96", StringComparison.Ordinal) OrElse upper.StartsWith("IDS-77", StringComparison.Ordinal) OrElse
                                   upper.StartsWith("IDS-96", StringComparison.Ordinal)) AndAlso
                                  (upper.Contains("NI", StringComparison.Ordinal) OrElse upper.Contains("NXI", StringComparison.Ordinal) OrElse Not upper.Contains("H", StringComparison.Ordinal)))
                If isNvrModel Then
                    Dim ch = InferHikvisionChannels(model, s.Sadp)
                    Dim chSuffix = If(ch > 0, $" ({ch} canali)", "")
                    Return New Result($"Videoregistratore di rete (NVR) Hikvision {model}{chSuffix}", CategoryCamera)
                End If
                ' Videoregistratori DVR Turbo HD (DS-71xxH, DS-72xxH, DS-73xxH, DS-81xxH, DS-90xxH, DVR-...)
                Dim isDvrModel = upper.StartsWith("DVR-", StringComparison.Ordinal) OrElse
                                 upper.StartsWith("DS-72", StringComparison.Ordinal) OrElse upper.StartsWith("DS-73", StringComparison.Ordinal) OrElse
                                 upper.StartsWith("DS-81", StringComparison.Ordinal) OrElse upper.StartsWith("DS-90", StringComparison.Ordinal) OrElse
                                 upper.StartsWith("IDS-72", StringComparison.Ordinal) OrElse upper.StartsWith("IDS-73", StringComparison.Ordinal) OrElse
                                 (upper.StartsWith("DS-71", StringComparison.Ordinal) AndAlso upper.Contains("H", StringComparison.Ordinal))
                If isDvrModel Then
                    Return New Result($"Videoregistratore DVR Hikvision {model}", CategoryCamera)
                End If
                ' Telecamere IP (DS-2CD..., iDS-2CD..., DS-2CV..., DS-2XM..., IPC-...)
                Return New Result($"Telecamera IP Hikvision {model}", CategoryCamera)
            End If

            ' Senza codice modello: distinguiamo NVR/DVR da telecamera IP grazie a banner e nomi
            If Not s.WindowsLike Then
                Dim isRecorder = s.InNamesOrBanners("=nvr", "=dvr", "dvrdvs-webs", "recorder", "videoregistratore", "rtsp/1.0 404") OrElse
                                 (s.Sadp IsNot Nothing AndAlso s.Sadp.TotalChannels > 1)
                Return New Result(If(isRecorder, "Videoregistratore di rete (NVR) Hikvision", "Telecamera IP Hikvision"), CategoryCamera)
            End If
            Return Nothing
        End Function

        Private Function ExtractHikvisionModel(s As Signals) As String
            If s.Sadp IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(s.Sadp.Model) Then
                Dim m = HikvisionModelPattern.Match(s.Sadp.Model)
                If m.Success Then Return NormalizeModelCase(m.Groups(1).Value)
                Return s.Sadp.Model.Trim()
            End If
            For Each source In {s.Banners, s.Names}
                If String.IsNullOrEmpty(source) Then Continue For
                Dim m = HikvisionModelPattern.Match(source)
                If m.Success Then Return NormalizeModelCase(m.Groups(1).Value)
            Next
            Return Nothing
        End Function

        Private Function NormalizeModelCase(raw As String) As String
            Dim upper = raw.Trim().ToUpperInvariant()
            If upper.StartsWith("IDS-", StringComparison.Ordinal) Then Return "i" & upper.Substring(1)
            Return upper
        End Function

        Private Function InferHikvisionChannels(model As String, sadp As SadpDeviceInfo) As Integer
            If sadp IsNot Nothing AndAlso sadp.DigitalChannels > 0 Then Return sadp.DigitalChannels
            Dim m = HikvisionNvrChannelsPattern.Match(model)
            Dim ch As Integer
            If m.Success AndAlso Integer.TryParse(m.Groups(1).Value, ch) Then Return ch
            Return 0
        End Function

        ''' <summary>Centrali d'allarme: dal produttore della scheda di rete (a parole intere: «inim» non è Minimax) o dal nome.</summary>
        Private Function Alarm(s As Signals) As Result
            Dim brand = s.FindBrand(Kind.Alarm)
            If brand Is Nothing AndAlso Not s.InNames("allarme", "alarm") Then Return Nothing
            Return New Result(Join("Centrale d'allarme", brand), CategoryAlarm)
        End Function

        ''' <summary>Automazione industriale e di edificio: Modbus TCP (502) oppure un produttore di PLC, KNX e controllori.</summary>
        Private Function Automation(s As Signals) As Result
            Dim brand = s.VendorBrand(Kind.Automation)
            If brand IsNot Nothing AndAlso brand.IsOnly(Kind.Automation) Then Return New Result($"Automazione / Domotica ({brand.Name})", CategoryHome)
            If s.Port(502) Then Return New Result(If(brand Is Nothing, "Automazione / PLC (Modbus)", $"Automazione / PLC {brand.Name} (Modbus)"), CategoryHome)
            Return Nothing
        End Function

        Private Function Printer3D(s As Signals) As Result
            Dim brand = s.FindBrand(Kind.Printer3D)
            If brand IsNot Nothing Then Return New Result($"Stampante 3D {brand.Name}", CategoryPrinter)
            If s.Port(7125) Then Return New Result("Stampante 3D (Klipper)", CategoryPrinter)
            Return Nothing
        End Function

        Private ReadOnly PrinterDefaultName As New Regex("^(brw|brn|npi|hp|rnp|xrx|et|sec|epson)[0-9a-f]{6,12}$", RegexOptions.CultureInvariant Or RegexOptions.Compiled)

        Private Function Printer(s As Signals) As Result
            Dim computerLike = s.WindowsLike OrElse s.AnyPort(3389, 5985, 5900) OrElse (s.Port(22) AndAlso s.Port(445))
            Dim printPorts = s.Port(9100) OrElse s.Port(515) OrElse (s.Port(631) AndAlso Not s.Port(22) AndAlso Not s.Port(445))
            Dim printText = s.InNamesOrBanners("printer", "stampante", "laserjet", "officejet", "deskjet", "designjet", "pagewide",
                "jetdirect", "web image monitor", "command center", "centreware", "embedded web server", "remote ui", "epson_linux",
                "debut/", "km-mfp", "=mfp", "=mfc", "imagerunner", "workcentre", "bizhub", "ecosys", "taskalfa", "aficio")
            Dim defaultNameBrand = PrinterBrandFromDefaultName(s)
            Dim vendorBrand = s.VendorBrand(Kind.Printer)
            Dim printerOnlyVendor = vendorBrand IsNot Nothing AndAlso vendorBrand.IsOnly(Kind.Printer)
            If (printPorts AndAlso Not computerLike) OrElse printText OrElse defaultNameBrand IsNot Nothing OrElse printerOnlyVendor Then
                Return New Result(Join("Stampante di rete", If(defaultNameBrand, s.FindBrand(Kind.Printer))), CategoryPrinter)
            End If
            Return Nothing
        End Function

        ''' <summary>Nomi predefiniti delle stampanti: BRW/BRN (Brother), NPI e HP (HP), RNP (Ricoh), XRX (Xerox), ET (Lexmark), SEC (Samsung).</summary>
        Private Function PrinterBrandFromDefaultName(s As Signals) As Brand
            For Each label In s.Labels
                Dim m = PrinterDefaultName.Match(label)
                If Not m.Success Then Continue For
                Dim brand As String
                Select Case m.Groups(1).Value
                    Case "brw", "brn" : brand = "Brother"
                    Case "npi", "hp" : brand = "HP"
                    Case "rnp" : brand = "Ricoh"
                    Case "xrx" : brand = "Xerox"
                    Case "et" : brand = "Lexmark"
                    Case "sec" : brand = "Samsung"
                    Case Else : brand = "Epson"
                End Select
                Return Brands.First(Function(b) b.Name = brand)
            Next
            Return Nothing
        End Function

        Private Function Nas(s As Signals) As Result
            Dim brand = s.FindBrand(Kind.Nas)
            If brand IsNot Nothing Then
                ' Netgear fa soprattutto apparati di rete: è un NAS solo se lo dicono i nomi o le condivisioni di file
                Dim netgearNas = s.InNamesOrBanners("readynas") OrElse s.AnyPort(139, 445, 548, 2049)
                If brand.Name <> "Netgear" Then Return New Result($"NAS {brand.Name}", CategoryStorage)
                If netgearNas Then Return New Result("NAS Netgear ReadyNAS", CategoryStorage)
            End If
            If s.Port(5000) AndAlso s.Port(5001) AndAlso Not s.WindowsLike Then Return New Result("NAS Synology", CategoryStorage)
            Return Nothing
        End Function

        Private Function Camera(s As Signals) As Result
            Dim brand = s.FindBrand(Kind.Camera)
            Dim cameraOnlyBrand = brand IsNot Nothing AndAlso brand.IsOnly(Kind.Camera)
            Dim cameraText = s.InNamesOrBanners("camera", "ipcam", "webcam", "netcam", "=cam", "=nvr", "=dvr", "network camera",
                "ip camera", "doorbell", "telecamera", "videocitofono")
            Dim cameraPorts = s.AnyPort(554, 8554, 37777) OrElse (s.Port(8000) AndAlso brand IsNot Nothing AndAlso brand.Name = "Hikvision")
            If Not cameraOnlyBrand AndAlso Not cameraText AndAlso Not (cameraPorts AndAlso Not s.WindowsLike) Then Return Nothing
            If brand Is Nothing AndAlso s.Port(37777) Then brand = Brands.First(Function(b) b.Name = "Dahua")
            Dim recorder = s.InNamesOrBanners("=nvr", "=dvr", "recorder", "videoregistratore")
            Return New Result(Join(If(recorder, "Videoregistratore di rete (NVR)", "Telecamera IP"), brand), CategoryCamera)
        End Function

        Private Function GameConsole(s As Signals) As Result
            If s.InAny("sony interactive", "playstation", "=ps4", "=ps5") Then Return New Result("Console Sony PlayStation", CategoryConsole)
            If s.InAny("xbox") Then Return New Result("Console Microsoft Xbox", CategoryConsole)
            If s.InAny("nintendo") Then Return New Result("Console Nintendo", CategoryConsole)
            If s.InAny("valve corporation", "steamdeck", "steam deck") Then Return New Result("Console Valve Steam Deck", CategoryConsole)
            Return Nothing
        End Function

        Private Function MediaPlayer(s As Signals) As Result
            Dim tvName = s.InNames("=tv", "smarttv", "smart-tv", "lgtv", "bravia", "tizen", "webos", "androidtv", "android-tv",
                "googletv", "google-tv", "=kodi", "libreelec", "=osmc", "mibox", "mi-box", "=tvbox")
            If (s.InVendor("samsung") OrElse s.InNames("samsung")) AndAlso (s.AnyPort(8001, 8002) OrElse s.InNames("=tv", "tizen")) Then Return New Result("Smart TV Samsung (Tizen)", CategoryTv)
            If (s.InVendor("lg electronics", "lg innotek") OrElse s.InNames("=lg", "webos", "lgtv")) AndAlso (s.AnyPort(3000, 3001) OrElse s.InNames("=tv", "webos", "lgtv")) Then Return New Result("Smart TV LG (webOS)", CategoryTv)
            If s.InNames("firetv", "fire-tv", "firestick", "fire tv") OrElse (s.InVendor("amazon") AndAlso s.InNames("=fire")) Then Return New Result("Amazon Fire TV", CategoryTv)
            If s.InNames("appletv", "apple-tv", "apple tv") Then Return New Result("Apple TV", CategoryTv)
            If s.InAny("=roku") Then Return New Result("Lettore multimediale Roku", CategoryTv)
            If s.InNames("=shield") OrElse (s.InVendor("nvidia") AndAlso s.Port(8009)) Then Return New Result("NVIDIA Shield TV", CategoryTv)

            Dim cast = (s.Port(8008) AndAlso s.Port(8009)) OrElse (s.Port(8009) AndAlso s.InVendor("google")) OrElse s.InNamesOrBanners("chromecast")
            If cast Then
                If s.InVendor("google") OrElse s.InNames("chromecast", "google") Then
                    If s.InNames("=nest", "googlehome", "google-home", "=mini", "=hub", "=speaker", "=audio") Then Return New Result("Assistente vocale Google Nest", CategoryHome)
                    Return New Result("Chromecast / Google TV", CategoryTv)
                End If
                Dim tvBrand = s.FindBrand(Kind.Tv)
                If tvBrand IsNot Nothing Then Return New Result($"Smart TV {tvBrand.Name} (Google Cast)", CategoryTv)
                Dim speaker = s.FindBrand(Kind.Voice)
                If speaker IsNot Nothing Then Return New Result($"Diffusore {speaker.Name} (Google Cast)", CategoryHome)
                Return New Result("Dispositivo Google Cast", CategoryTv)
            End If

            If tvName Then Return New Result(Join("Smart TV", s.FindBrand(Kind.Tv)), CategoryTv)
            Dim pureTv = s.VendorBrand(Kind.Tv)
            If pureTv IsNot Nothing AndAlso pureTv.IsOnly(Kind.Tv) Then Return New Result($"Smart TV {pureTv.Name}", CategoryTv)
            Return Nothing
        End Function

        Private Function VoiceAssistant(s As Signals) As Result
            If s.InVendor("amazon") AndAlso s.InNames("kindle") Then Return New Result("Tablet Amazon Kindle / Fire", CategoryMobile)
            If s.InNames("=echo", "alexa") OrElse s.InVendor("amazon") Then Return New Result("Assistente vocale Amazon Echo", CategoryHome)
            If s.InNames("homepod") Then Return New Result("Diffusore Apple HomePod", CategoryHome)
            If s.InNames("googlehome", "google-home", "nest-mini", "nest-hub", "nest-audio", "home-mini") Then Return New Result("Assistente vocale Google Nest", CategoryHome)
            If s.InNames("=nest") AndAlso (s.InVendor("google") OrElse s.RandomMac OrElse s.Vendor.Length = 0) Then Return New Result("Dispositivo Google Nest", CategoryHome)
            If s.InAny("sonos") OrElse (s.Port(1400) AndAlso Not s.WindowsLike) Then Return New Result("Diffusore Sonos", CategoryHome)
            Dim speaker = s.VendorBrand(Kind.Voice)
            If speaker IsNot Nothing AndAlso speaker.IsOnly(Kind.Voice) Then Return New Result($"Diffusore smart {speaker.Name}", CategoryHome)
            Return Nothing
        End Function

        Private Function HomeAutomation(s As Signals) As Result
            If s.Port(8123) OrElse s.InAny("home assistant", "homeassistant", "home-assistant", "hassio", "=hass") Then Return New Result("Home Assistant", CategoryHome)
            If s.Port(1880) OrElse s.InNamesOrBanners("node-red") Then Return New Result("Server domotico Node-RED", CategoryHome)
            If s.InNamesOrBanners("openhab") Then Return New Result("Server domotico openHAB", CategoryHome)
            If s.InNamesOrBanners("domoticz") Then Return New Result("Server domotico Domoticz", CategoryHome)
            If s.InNamesOrBanners("iobroker") Then Return New Result("Server domotico ioBroker", CategoryHome)
            If s.InNamesOrBanners("homebridge") Then Return New Result("Server domotico Homebridge", CategoryHome)
            If s.InAny("zigbee2mqtt", "deconz", "phoscon", "conbee", "zigbee") Then Return New Result("Gateway Zigbee", CategoryHome)
            If s.AnyPort(1883, 8883) Then Return New Result("Broker MQTT / gateway IoT", CategoryHome)
            If s.Port(6053) OrElse s.InNamesOrBanners("esphome") Then Return New Result("Modulo ESPHome", CategoryHome)
            If s.InNamesOrBanners("tasmota") Then Return New Result("Modulo Tasmota", CategoryHome)
            If s.InNamesOrBanners("=wled") Then Return New Result("Controller LED WLED", CategoryHome)
            If s.InAny("shelly", "allterco") Then Return New Result("Dispositivo Shelly", CategoryHome)
            If s.InAny("sonoff", "itead") Then Return New Result("Dispositivo Sonoff", CategoryHome)
            If s.InAny("signify", "philips lighting", "philips hue", "philips-hue", "hue-bridge", "hue bridge") Then Return New Result("Bridge Philips Hue", CategoryHome)
            If s.InVendor("tuya") OrElse s.Port(6668) Then Return New Result("Dispositivo smart Tuya", CategoryHome)
            ' firmware originale eWeLink (Sonoff): controllo locale sulla 8081, senza interfaccia web sulla 80
            If s.Port(8081) AndAlso s.InVendor("espressif", "itead") AndAlso Not s.AnyPort(80, 443, 6053) Then Return New Result("Dispositivo Sonoff (eWeLink)", CategoryHome)
            If s.InAny("espressif", "esp32", "esp8266", "=esp") Then Return New Result("Modulo IoT ESP32 / ESP8266", CategoryHome)
            ' inverter fotovoltaici: dal produttore del MAC, dal titolo della pagina web o dal certificato TLS
            If s.InAny("fronius") Then Return New Result("Inverter fotovoltaico Fronius", CategoryHome)
            If s.InAny("solaredge") Then Return New Result("Inverter fotovoltaico SolarEdge", CategoryHome)
            If s.InAny("sma solar", "sunny boy", "sunny tripower", "sunny portal") Then Return New Result("Inverter fotovoltaico SMA", CategoryHome)
            If s.InNamesOrBanners("vantage tpt", "infusion controller", "titolo: ic-ii") Then Return New Result("Automazione / Domotica Vantage", CategoryHome)
            Dim brand = s.FindBrand(Kind.Home)
            If brand IsNot Nothing AndAlso brand.IsOnly(Kind.Home) Then Return New Result($"Dispositivo domotico {brand.Name}", CategoryHome)
            Return Nothing
        End Function

        Private Function Ups(s As Signals) As Result
            Dim brand = s.FindBrand(Kind.Ups)
            If brand Is Nothing AndAlso Not s.InNamesOrBanners("=ups", "powerchute", "network management card") Then Return Nothing
            Return New Result($"{Join("UPS", brand)} (scheda di rete)", CategoryOther)
        End Function

        Private Function Voip(s As Signals) As Result
            Dim brand = s.VendorBrand(Kind.Voip)
            If brand IsNot Nothing AndAlso brand.IsOnly(Kind.Voip) Then Return New Result($"Telefono VoIP {brand.Name}", CategoryOther)
            If s.InNamesOrBanners("=pbx", "3cx", "freepbx", "asterisk", "centralino", "issabel", "yeastar") Then Return New Result("Centralino VoIP", CategoryServer)
            If s.Port(5060) AndAlso Not s.WindowsLike Then Return New Result(Join("Dispositivo VoIP (SIP)", s.FindBrand(Kind.Voip)), CategoryOther)
            Return Nothing
        End Function

        Private Function NetworkGear(s As Signals) As Result
            Dim brand = s.FindBrand(Kind.Network)
            Dim networkFirst = brand IsNot Nothing AndAlso Not brand.Has(Kind.Phone Or Kind.Computer)
            Dim networkText = s.InNamesOrBanners("router", "=switch", "access point", "accesspoint", "repeater", "extender",
                "ripetitore", "=mesh", "routeros", "openwrt", "dd-wrt", "=luci", "=airport", "=ap", "=sw", "edgeos", "unifi", "=uap",
                "=usw", "fritz", "keenetic", "edgeswitch", "cloudkey")
            If Not networkFirst AndAlso Not networkText AndAlso Not (brand IsNot Nothing AndAlso (s.TtlNetwork OrElse s.Port(53))) Then Return Nothing
            Dim role As String
            If s.InNamesOrBanners("cloudkey", "unifi network", "unifi os") Then
                role = "Controller di rete"
            ElseIf s.InNamesOrBanners("access point", "accesspoint", "=ap", "=uap", "=wap") Then
                role = "Access point"
            ElseIf s.InNamesOrBanners("repeater", "extender", "ripetitore") Then
                role = "Ripetitore Wi-Fi"
            ElseIf s.InNamesOrBanners("=switch", "=sw", "=usw", "edgeswitch") Then
                role = "Switch"
            ElseIf s.InNamesOrBanners("router", "routeros", "edgerouter", "openwrt", "fritz") Then
                role = "Router"
            Else
                role = "Apparato di rete"
            End If
            Return New Result(Join(role, brand), CategoryNetwork)
        End Function

        Private Function Apple(s As Signals) As Result
            Dim isApple = s.InVendor("apple") OrElse s.InNames("iphone", "ipad", "macbook", "imac", "=mac", "mac-mini", "macmini", "mac-pro",
                "mac-studio", "apple") OrElse s.InBanners("airtunes")
            If Not isApple Then Return Nothing
            If s.InNames("iphone") Then Return New Result("Smartphone Apple iPhone", CategoryMobile)
            If s.InNames("ipad") Then Return New Result("Tablet Apple iPad", CategoryMobile)
            If s.InNames("=watch") Then Return New Result("Apple Watch", CategoryMobile)
            If s.InNames("macbook") Then Return New Result("Portatile Apple MacBook", CategoryWorkstation)
            If s.InNames("imac", "=mac", "mac-mini", "macmini", "mac-pro", "mac-studio") OrElse s.AnyPort(22, 88, 445, 548, 3283, 5900) OrElse
               (s.Port(5000) AndAlso s.Port(7000)) Then Return New Result("Computer Apple Mac", CategoryWorkstation)
            If s.Port(62078) Then Return New Result("Smartphone / tablet Apple", CategoryMobile)
            If s.Port(7000) Then Return New Result("Apple TV / HomePod (AirPlay)", CategoryTv)
            Return New Result("Dispositivo Apple (iPhone / iPad / Mac)", CategoryMobile)
        End Function

        Private Function RaspberryPi(s As Signals) As Result
            If Not s.InAny("raspberry") Then Return Nothing
            If s.InBanners("pi-hole") Then Return New Result("Server DNS Pi-hole (Raspberry Pi)", CategoryServer)
            If s.Port(53) Then Return New Result("Server DNS (Raspberry Pi)", CategoryServer)
            If s.AnyPort(22, 80, 443, 445, 3000, 8080, 9000) Then Return New Result("Microcomputer Raspberry Pi (server)", CategoryServer)
            Return New Result("Microcomputer Raspberry Pi", CategoryHome)
        End Function

        Private Function Mobile(s As Signals) As Result
            Dim brand = s.FindBrand(Kind.Phone)
            Dim tablet = s.InNames("=tab", "tablet", "kindle", "mediapad", "matepad", "galaxytab", "galaxy-tab")
            Dim phone = s.InNames("android", "galaxy", "=sm", "redmi", "=poco", "pixel", "oneplus", "huawei", "honor", "oppo",
                "=vivo", "realme", "=moto", "motorola", "nokia", "xiaomi", "smartphone", "=phone", "telefono", "cellulare")
            If tablet Then Return New Result(Join("Tablet", brand), CategoryMobile)
            If phone Then Return New Result(If(brand Is Nothing, "Smartphone Android", $"Smartphone Android {brand.Name}"), CategoryMobile)
            Dim vendor = s.VendorBrand(Kind.Phone)
            If vendor IsNot Nothing AndAlso vendor.IsOnly(Kind.Phone) Then Return New Result($"Smartphone {vendor.Name}", CategoryMobile)
            Return Nothing
        End Function

#End Region

#Region "Regole: ruolo dai servizi"

        Private Function ServerRole(s As Signals) As Result
            Dim ldap = s.AnyPort(389, 636, 3268)
            If (s.Port(88) AndAlso ldap) OrElse s.Port(3268) Then Return New Result(If(s.TtlUnix OrElse s.InBanners("samba"), "Controller di dominio Samba", "Controller di dominio Windows"), CategoryServer)
            If s.Port(1433) Then Return New Result("Server Microsoft SQL Server", CategoryServer)
            If s.InBanners("microsoft-iis") Then Return New Result("Server web IIS", CategoryServer)
            If s.AnyPort(25, 465, 587) AndAlso s.AnyPort(110, 143, 993, 995) Then Return New Result(If(s.WindowsLike AndAlso s.Port(443), "Server di posta Exchange", "Server di posta"), CategoryServer)
            If s.Port(3306) Then Return New Result("Server database MySQL / MariaDB", CategoryServer)
            If s.Port(5432) Then Return New Result("Server database PostgreSQL", CategoryServer)
            If s.Port(1521) Then Return New Result("Server database Oracle", CategoryServer)
            If s.Port(27017) Then Return New Result("Server database MongoDB", CategoryServer)
            If s.Port(6379) Then Return New Result("Server Redis", CategoryServer)
            If s.Port(32400) Then Return New Result("Server multimediale Plex", CategoryServer)
            If s.AnyPort(2375, 2376) Then Return New Result("Host Docker", CategoryServer)
            If s.Port(6443) Then Return New Result("Nodo Kubernetes", CategoryServer)
            If Not s.WindowsLike AndAlso s.AnyPort(548, 873, 2049) AndAlso s.AnyPort(139, 445) Then Return New Result("NAS / file server", CategoryStorage)
            If s.Port(53) AndAlso Not s.WindowsLike Then
                Dim flavor = If(s.InBanners("pi-hole"), " Pi-hole", If(s.InBanners("adguard"), " AdGuard Home", ""))
                Return New Result($"Server DNS{flavor}", CategoryServer)
            End If
            Return Nothing
        End Function

        Private Function Windows(s As Signals) As Result
            If Not s.WindowsLike Then Return Nothing
            Dim clientName = s.Labels.Any(Function(l) l.StartsWith("desktop-", StringComparison.Ordinal) OrElse l.StartsWith("laptop-", StringComparison.Ordinal)) OrElse
                             s.InNames("notebook", "portatile", "=pc", "=laptop", "=desktop", "=workstation")
            Dim serverName = s.Labels.Any(Function(l) l.StartsWith("win-", StringComparison.Ordinal)) OrElse
                             s.InNames("=srv", "server", "=dc", "=sql", "=fs", "=rds", "=exch", "=hv")
            Dim serverPorts = s.AnyPort(5985, 5986) OrElse (s.Port(3389) AndAlso s.Port(445) AndAlso s.Port(135))
            If Not clientName AndAlso (serverName OrElse serverPorts) Then Return New Result("Server Windows", CategoryServer)
            Dim laptop = s.InNames("laptop", "notebook", "portatile", "surface")
            Return New Result(If(laptop, "Portatile Windows", "Postazione Windows"), CategoryWorkstation)
        End Function

        Private Function Unix(s As Signals) As Result
            If s.WindowsLike Then Return Nothing
            Dim distro = Distribution(s.Banners)
            Dim bsd = distro = "FreeBSD" OrElse distro = "OpenBSD"
            Dim system = If(bsd, distro, If(distro Is Nothing, "Linux", $"Linux ({distro})"))
            If s.Port(22) AndAlso s.AnyPort(80, 443) Then Return New Result($"Server {system}", CategoryServer)
            If s.Port(22) AndAlso (s.TtlUnix OrElse distro IsNot Nothing OrElse s.InBanners("openssh")) Then Return New Result(If(distro Is Nothing, "Sistema Linux / Unix", $"Sistema {system}"), CategoryServer)
            Return Nothing
        End Function

#End Region

#Region "Regole: produttore e TTL"

        Private Function ByVendor(s As Signals) As Result
            Dim computer = s.VendorBrand(Kind.Computer)
            If computer IsNot Nothing AndAlso Not computer.Has(Kind.Phone) Then Return New Result($"Computer {computer.Name}", CategoryWorkstation)
            If s.InVendor("samsung") Then Return New Result("Dispositivo Samsung (TV / smartphone)", CategoryTv)
            If s.InVendor("lg electronics", "lg innotek") Then Return New Result("Smart TV / apparecchio LG", CategoryTv)
            If s.RandomMac Then Return New Result("Smartphone / tablet (MAC casuale)", CategoryMobile)
            ' Google: in casa sono quasi sempre altoparlanti e display Nest, Chromecast / Google TV o i punti Wi-Fi Nest
            If s.InVendor("google") Then Return New Result("Dispositivo Google (Nest / Chromecast / Google TV)", CategoryHome)
            If s.InVendor("nvidia") Then Return New Result("Dispositivo NVIDIA (Shield / Jetson)", CategoryTv)
            Dim brand = s.VendorBrand(Kind.Any)
            If brand IsNot Nothing Then Return New Result($"Dispositivo {brand.Name}", CategoryOther)
            Return Nothing
        End Function

        Private Function ByTtl(s As Signals) As Result
            If s.TtlUnix Then Return New Result("Dispositivo Linux / Unix / mobile", CategoryOther)
            If s.TtlNetwork Then Return New Result("Apparato di rete", CategoryNetwork)
            Return Nothing
        End Function

        ''' <summary>Macchina virtuale riconosciuta dal MAC: al ruolo si aggiunge l'hypervisor, oppure diventa "Macchina virtuale".</summary>
        Private Function ApplyVirtualMachine(s As Signals, result As Result) As Result
            If s.VmKind Is Nothing OrElse result.Category = CategoryVirtualization Then Return result
            If result.Category = CategoryServer OrElse result.Category = CategoryNetwork OrElse result.Category = CategoryStorage Then
                Return New Result($"{result.Type} (VM {s.VmKind})", result.Category)
            End If
            Dim os = If(s.TtlWindows OrElse result.Type.Contains("Windows", StringComparison.Ordinal), " (Windows)", If(s.TtlUnix, " (Linux)", ""))
            Return New Result($"Macchina virtuale {s.VmKind}{os}", CategoryVirtualization)
        End Function

        ''' <summary>Prefissi MAC assegnati dagli hypervisor alle schede delle macchine virtuali.</summary>
        Private Function VirtualMachineKind(mac As String) As String
            Dim n = MacVendorLookup.Normalize(mac)
            If n Is Nothing Then Return Nothing
            Select Case n.Substring(0, 8)
                Case "00:15:5d" : Return "Hyper-V"
                Case "00:0c:29", "00:50:56", "00:05:69", "00:1c:14" : Return "VMware"
                Case "08:00:27" : Return "VirtualBox"
                Case "52:54:00" : Return "KVM"
                Case "bc:24:11" : Return "Proxmox"
                Case "00:16:3e" : Return "Xen"
                Case "00:1c:42" : Return "Parallels"
                Case Else : Return Nothing
            End Select
        End Function

#End Region

#Region "Sistema operativo"

        ''' <summary>
        ''' Stima del sistema: prima i banner (più precisi), poi il TTL (≤64 Linux/Unix o dispositivo, ~128 Windows,
        ''' ~255 apparato di rete), infine le porte tipiche di Windows quando il TTL manca. Se il dispositivo è già
        ''' classificato come apparecchio (domotica, TV, telecamera, stampante...), un TTL da Windows o da apparato di
        ''' rete indica un firmware embedded (es. i moduli ESP8266 usano TTL 128).
        ''' </summary>
        Public Function GuessOs(host As DiscoveredHost) As String
            If host Is Nothing Then Throw New ArgumentNullException(NameOf(host))
            If host.Sadp IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(host.Sadp.SoftwareVersion) Then
                Return "Firmware " & host.Sadp.SoftwareVersion.Trim()
            End If
            Dim rawBanners = String.Join(" | ", host.Ports.Select(Function(p) p.Banner).Where(Function(b) Not String.IsNullOrWhiteSpace(b)))
            If rawBanners.Length > 0 Then
                Dim fwMatch = SadpFirmwareBannerPattern.Match(rawBanners)
                If fwMatch.Success Then Return "Firmware " & fwMatch.Groups(1).Value.Trim()
            End If
            Dim banners = rawBanners.ToLowerInvariant()
            If banners.Length > 0 Then
                If banners.Contains("microsoft-iis") OrElse banners.Contains("openssh_for_windows") OrElse banners.Contains("microsoft-httpapi") Then Return "Windows"
                If banners.Contains("pfsense") Then Return "FreeBSD (pfSense)"
                If banners.Contains("opnsense") Then Return "FreeBSD (OPNsense)"
                If banners.Contains("synology") Then Return "Linux (Synology DSM)"
                If banners.Contains("qnap") Then Return "Linux (QNAP QTS)"
                If banners.Contains("proxmox") Then Return "Linux (Proxmox VE)"
                If banners.Contains("esxi") Then Return "VMware ESXi"
                If banners.Contains("routeros") OrElse banners.Contains("mikrotik") Then Return "RouterOS (MikroTik)"
                If banners.Contains("ssh-2.0-cisco") Then Return "Cisco IOS"
                Dim distro = Distribution(banners)
                If distro IsNot Nothing Then Return If(distro = "FreeBSD" OrElse distro = "OpenBSD", distro, $"Linux ({distro})")
                If banners.Contains("dropbear") Then Return "Linux embedded"
            End If
            Dim ttl = If(host.Ttl, 0)
            If ttl > 0 AndAlso ttl <= 64 Then
                Dim vendor = If(host.Vendor, MacVendorLookup.Lookup(host.Mac))
                Return If(vendor IsNot Nothing AndAlso vendor.Contains("Apple", StringComparison.OrdinalIgnoreCase), "Apple (macOS / iOS)", "Linux/Unix o dispositivo")
            End If
            Dim appliance = host.Category = CategoryHome OrElse host.Category = CategoryTv OrElse host.Category = CategoryCamera OrElse
                            host.Category = CategoryPrinter OrElse host.Category = CategoryAlarm
            If ttl > 64 AndAlso ttl <= 128 Then Return If(appliance OrElse host.Category = CategoryNetwork, "Firmware embedded", "Windows")
            If ttl > 128 Then Return If(appliance OrElse host.Category = CategoryConsole, "Firmware embedded", "Apparato di rete")
            Dim ports = host.Ports.Select(Function(p) p.Port).ToHashSet()
            If ports.Contains(135) OrElse ports.Contains(5985) OrElse (ports.Contains(3389) AndAlso ports.Contains(445)) Then Return "Windows"
            Return Nothing
        End Function

        ''' <summary>Distribuzione citata nei banner (SSH, HTTP).</summary>
        Private Function Distribution(banners As String) As String
            If String.IsNullOrEmpty(banners) Then Return Nothing
            Dim known = {("raspbian", "Raspberry Pi OS"), ("ubuntu", "Ubuntu"), ("debian", "Debian"), ("centos", "CentOS"),
                         ("red hat", "Red Hat"), ("rhel", "Red Hat"), ("fedora", "Fedora"), ("almalinux", "AlmaLinux"),
                         ("rocky", "Rocky Linux"), ("suse", "SUSE"), ("alpine", "Alpine"), ("freebsd", "FreeBSD"), ("openbsd", "OpenBSD")}
            For Each k In known
                If banners.Contains(k.Item1, StringComparison.Ordinal) Then Return k.Item2
            Next
            Return Nothing
        End Function

#End Region

#Region "Marchi"

        <Flags>
        Private Enum Kind
            None = 0
            Network = 1 << 0
            Firewall = 1 << 1
            Printer = 1 << 2
            Printer3D = 1 << 3
            Nas = 1 << 4
            Camera = 1 << 5
            Tv = 1 << 6
            Voice = 1 << 7
            Home = 1 << 8
            Phone = 1 << 9
            Console = 1 << 10
            Ups = 1 << 11
            Computer = 1 << 12
            Voip = 1 << 13
            Alarm = 1 << 14
            Automation = 1 << 15
            Any = -1
        End Enum

        ''' <summary>Marchio con le sue chiavi di riconoscimento (minuscole; "=" davanti chiede la parola intera).</summary>
        Private NotInheritable Class Brand
            Public Sub New(name As String, kinds As Kind, ParamArray keys As String())
                Me.Name = name
                Me.Kinds = kinds
                Me.Keys = keys
            End Sub
            Public ReadOnly Property Name As String
            Public ReadOnly Property Kinds As Kind
            Public ReadOnly Property Keys As String()
            Public Function Has(kind As Kind) As Boolean
                Return (Kinds And kind) <> 0
            End Function
            ''' <summary>Il marchio fa solo questo tipo di prodotto.</summary>
            Public Function IsOnly(kind As Kind) As Boolean
                Return (Kinds And Not kind) = 0
            End Function
            Public Function MatchesText(text As String) As Boolean
                Return text.Length > 0 AndAlso Keys.Any(Function(k) Matches(text, k))
            End Function
        End Class

        ''' <summary>Marchi noti, dal più specifico al più generico (il primo che corrisponde vince).</summary>
        Private ReadOnly Brands As Brand() = {
            New Brand("Fortinet", Kind.Firewall, "fortinet", "fortigate"),
            New Brand("SonicWall", Kind.Firewall, "sonicwall"),
            New Brand("WatchGuard", Kind.Firewall, "watchguard"),
            New Brand("Sophos", Kind.Firewall, "sophos"),
            New Brand("Palo Alto", Kind.Firewall, "palo alto"),
            New Brand("Check Point", Kind.Firewall, "check point", "checkpoint"),
            New Brand("pfSense", Kind.Firewall, "pfsense", "netgate"),
            New Brand("OPNsense", Kind.Firewall, "opnsense", "deciso"),
            New Brand("AVM FRITZ!Box", Kind.Network, "avm audiovisuelles", "avm gmbh", "fritz"),
            New Brand("Keenetic", Kind.Network, "keenetic"),
            New Brand("Cisco Meraki", Kind.Network, "meraki"),
            New Brand("Ubiquiti", Kind.Network Or Kind.Camera, "ubiquiti", "=ubnt", "unifi", "edgerouter", "edgeswitch", "cloudkey"),
            New Brand("MikroTik", Kind.Network, "mikrotik", "routerboard", "routeros"),
            New Brand("TP-Link", Kind.Network Or Kind.Camera Or Kind.Home, "tp-link", "tplink", "tp link", "tapo", "=kasa", "=deco"),
            New Brand("Netgear", Kind.Network Or Kind.Nas, "netgear", "readynas", "=orbi"),
            New Brand("Zyxel", Kind.Network, "zyxel"),
            New Brand("D-Link", Kind.Network Or Kind.Camera, "d-link", "dlink"),
            New Brand("Linksys", Kind.Network, "linksys"),
            New Brand("Cisco", Kind.Network, "cisco"),
            New Brand("Aruba", Kind.Network, "aruba"),
            New Brand("Ruckus", Kind.Network, "ruckus"),
            New Brand("Juniper", Kind.Network, "juniper"),
            New Brand("DrayTek", Kind.Network, "draytek"),
            New Brand("Teltonika", Kind.Network, "teltonika"),
            New Brand("Tenda", Kind.Network, "tenda technology", "=tenda"),
            New Brand("Mercusys", Kind.Network, "mercusys"),
            New Brand("EnGenius", Kind.Network, "engenius"),
            New Brand("Edimax", Kind.Network, "edimax"),
            New Brand("Cambium", Kind.Network, "cambium"),
            New Brand("Extreme Networks", Kind.Network, "extreme networks"),
            New Brand("eero", Kind.Network, "=eero"),
            New Brand("Devolo", Kind.Network, "devolo"),
            New Brand("Technicolor", Kind.Network, "technicolor", "vantiva"),
            New Brand("Sagemcom", Kind.Network, "sagemcom"),
            New Brand("Sercomm", Kind.Network, "sercomm"),
            New Brand("Arcadyan", Kind.Network, "arcadyan"),
            New Brand("Askey", Kind.Network, "askey"),
            New Brand("Hitron", Kind.Network, "hitron"),
            New Brand("Arris", Kind.Network, "=arris"),
            New Brand("Fiberhome", Kind.Network, "fiberhome"),
            New Brand("Grandstream", Kind.Voip Or Kind.Network, "grandstream"),
            New Brand("ZTE", Kind.Network Or Kind.Phone, "zte corporation", "=zte"),
            New Brand("Huawei", Kind.Phone, "huawei device"),
            New Brand("Huawei", Kind.Network Or Kind.Phone, "huawei"),
            New Brand("Paradox", Kind.Alarm, "paradox"),
            New Brand("Bentel", Kind.Alarm, "bentel"),
            New Brand("Inim", Kind.Alarm, "=inim"),
            New Brand("Ksenia", Kind.Alarm, "ksenia"),
            New Brand("Ajax", Kind.Alarm, "ajax systems"),
            New Brand("Risco", Kind.Alarm, "=risco"),
            New Brand("Texecom", Kind.Alarm, "texecom"),
            New Brand("Elkron", Kind.Alarm, "elkron"),
            New Brand("Tecnoalarm", Kind.Alarm, "tecnoalarm"),
            New Brand("Honeywell", Kind.Alarm, "honeywell security"),
            New Brand("Hesa", Kind.Alarm, "=hesa"),
            New Brand("Urmet", Kind.Alarm, "urmet"),
            New Brand("Siemens", Kind.Automation, "siemens"),
            New Brand("Schneider Electric", Kind.Automation, "schneider"),
            New Brand("ABB", Kind.Automation, "=abb"),
            New Brand("Loxone", Kind.Automation, "loxone"),
            New Brand("KNX", Kind.Automation, "=knx"),
            New Brand("WAGO", Kind.Automation, "=wago"),
            New Brand("Phoenix Contact", Kind.Automation, "phoenix contact"),
            New Brand("Beckhoff", Kind.Automation, "beckhoff"),
            New Brand("Nokia", Kind.Network Or Kind.Phone, "nokia", "hmd global"),
            New Brand("Asus", Kind.Network Or Kind.Computer Or Kind.Phone, "asustek", "=asus"),
            New Brand("Yealink", Kind.Voip, "yealink"),
            New Brand("Snom", Kind.Voip, "=snom"),
            New Brand("Polycom", Kind.Voip, "polycom"),
            New Brand("Fanvil", Kind.Voip, "fanvil"),
            New Brand("Gigaset", Kind.Voip, "gigaset"),
            New Brand("Avaya", Kind.Voip, "avaya"),
            New Brand("Mitel", Kind.Voip, "mitel"),
            New Brand("Prusa", Kind.Printer3D, "prusa"),
            New Brand("Bambu Lab", Kind.Printer3D, "bambu"),
            New Brand("Creality", Kind.Printer3D, "creality"),
            New Brand("Ultimaker", Kind.Printer3D, "ultimaker"),
            New Brand("Formlabs", Kind.Printer3D, "formlabs"),
            New Brand("Anycubic", Kind.Printer3D, "anycubic"),
            New Brand("Elegoo", Kind.Printer3D, "elegoo"),
            New Brand("Flashforge", Kind.Printer3D, "flashforge"),
            New Brand("Snapmaker", Kind.Printer3D, "snapmaker"),
            New Brand("Raise3D", Kind.Printer3D, "raise3d"),
            New Brand("OctoPrint", Kind.Printer3D, "octoprint", "=octopi"),
            New Brand("Klipper", Kind.Printer3D, "klipper", "moonraker", "mainsail", "fluidd"),
            New Brand("Duet3D", Kind.Printer3D, "duet3d", "duet web control"),
            New Brand("HPE", Kind.Computer Or Kind.Network, "hewlett packard enterprise", "=hpe", "=ilo"),
            New Brand("HP", Kind.Printer Or Kind.Computer, "hewlett packard", "hewlett-packard", "hp inc", "=hp", "laserjet", "officejet", "deskjet", "designjet", "pagewide", "jetdirect"),
            New Brand("Brother", Kind.Printer, "brother"),
            New Brand("Epson", Kind.Printer, "epson"),
            New Brand("Canon", Kind.Printer, "canon"),
            New Brand("Kyocera", Kind.Printer, "kyocera", "ecosys", "taskalfa"),
            New Brand("Ricoh", Kind.Printer, "ricoh", "aficio"),
            New Brand("Xerox", Kind.Printer, "xerox", "fujifilm business"),
            New Brand("Lexmark", Kind.Printer, "lexmark"),
            New Brand("Konica Minolta", Kind.Printer, "konica", "minolta", "bizhub"),
            New Brand("OKI", Kind.Printer, "oki data", "oki electric", "=oki"),
            New Brand("Pantum", Kind.Printer, "pantum"),
            New Brand("Zebra", Kind.Printer, "zebra technologies"),
            New Brand("Sharp", Kind.Printer Or Kind.Tv, "=sharp"),
            New Brand("Toshiba", Kind.Printer Or Kind.Computer Or Kind.Tv, "toshiba"),
            New Brand("Samsung", Kind.Printer Or Kind.Phone Or Kind.Tv Or Kind.Home, "samsung"),
            New Brand("Dell", Kind.Computer, "dell inc", "dell technologies", "dell emc", "=dell"),
            New Brand("Synology", Kind.Nas, "synology", "diskstation", "rackstation"),
            New Brand("QNAP", Kind.Nas, "qnap", "=qts", "=quts"),
            New Brand("Western Digital", Kind.Nas, "western digital", "wdmycloud", "mycloud", "my cloud"),
            New Brand("Buffalo", Kind.Nas, "buffalo", "linkstation", "terastation"),
            New Brand("Asustor", Kind.Nas, "asustor"),
            New Brand("TerraMaster", Kind.Nas, "terramaster"),
            New Brand("TrueNAS", Kind.Nas, "truenas", "freenas", "ixsystems"),
            New Brand("Unraid", Kind.Nas, "unraid", "lime technology"),
            New Brand("OpenMediaVault", Kind.Nas, "openmediavault"),
            New Brand("Drobo", Kind.Nas, "drobo"),
            New Brand("Seagate", Kind.Nas, "seagate"),
            New Brand("Thecus", Kind.Nas, "thecus"),
            New Brand("Hikvision", Kind.Camera, "hikvision", "app-webs", "dnvrs-webs", "dvrdvs-webs", "hikvision-webs"),
            New Brand("Ezviz", Kind.Camera, "ezviz"),
            New Brand("Dahua", Kind.Camera, "dahua"),
            New Brand("Imou", Kind.Camera, "=imou"),
            New Brand("Axis", Kind.Camera, "axis communications", "=axis"),
            New Brand("Reolink", Kind.Camera, "reolink"),
            New Brand("Amcrest", Kind.Camera, "amcrest"),
            New Brand("Foscam", Kind.Camera, "foscam"),
            New Brand("Hanwha", Kind.Camera, "hanwha", "samsung techwin"),
            New Brand("Uniview", Kind.Camera, "uniview"),
            New Brand("Vivotek", Kind.Camera, "vivotek"),
            New Brand("Mobotix", Kind.Camera, "mobotix"),
            New Brand("Milesight", Kind.Camera, "milesight"),
            New Brand("Avigilon", Kind.Camera, "avigilon"),
            New Brand("Tiandy", Kind.Camera, "tiandy"),
            New Brand("Provision-ISR", Kind.Camera, "provision-isr"),
            New Brand("Arlo", Kind.Camera, "=arlo"),
            New Brand("Ring", Kind.Camera, "ring llc", "=ring"),
            New Brand("Wyze", Kind.Camera, "wyze"),
            New Brand("Eufy", Kind.Camera, "eufy", "anker"),
            New Brand("PlayStation", Kind.Console, "sony interactive", "playstation"),
            New Brand("Xbox", Kind.Console, "xbox"),
            New Brand("Nintendo", Kind.Console, "nintendo"),
            New Brand("Valve", Kind.Console, "valve corporation"),
            New Brand("LG", Kind.Tv Or Kind.Phone, "lg electronics", "lg innotek", "=lg", "webos"),
            New Brand("Sony", Kind.Tv Or Kind.Phone, "sony corporation", "sony visual", "sony mobile", "bravia", "=sony"),
            New Brand("Philips", Kind.Tv, "tp vision", "philips tv", "philips-tv"),
            New Brand("TCL", Kind.Tv Or Kind.Phone, "=tcl"),
            New Brand("Hisense", Kind.Tv, "hisense"),
            New Brand("Panasonic", Kind.Tv Or Kind.Voip Or Kind.Computer, "panasonic"),
            New Brand("Vizio", Kind.Tv, "vizio"),
            New Brand("Vestel", Kind.Tv, "vestel"),
            New Brand("Skyworth", Kind.Tv Or Kind.Network, "skyworth"),
            New Brand("Roku", Kind.Tv, "=roku"),
            New Brand("NVIDIA", Kind.Tv, "nvidia", "=shield"),
            New Brand("Apple", Kind.Phone Or Kind.Tv Or Kind.Computer Or Kind.Voice, "apple", "iphone", "ipad", "macbook", "homepod", "airtunes"),
            New Brand("Google", Kind.Phone Or Kind.Tv Or Kind.Voice Or Kind.Network, "google", "=nest", "=pixel", "chromecast"),
            New Brand("Amazon", Kind.Voice Or Kind.Tv, "amazon", "=echo", "alexa", "firetv", "kindle"),
            New Brand("Xiaomi", Kind.Phone Or Kind.Network Or Kind.Home, "xiaomi", "redmi", "=poco"),
            New Brand("Sonos", Kind.Voice, "sonos"),
            New Brand("Bose", Kind.Voice, "=bose"),
            New Brand("JBL", Kind.Voice, "harman", "=jbl"),
            New Brand("Bang & Olufsen", Kind.Voice, "bang & olufsen", "bang olufsen"),
            New Brand("Philips Hue", Kind.Home, "signify", "philips lighting", "philips hue", "philips-hue", "hue-bridge"),
            New Brand("Shelly", Kind.Home, "shelly", "allterco"),
            New Brand("Sonoff", Kind.Home, "sonoff", "itead"),
            New Brand("Tuya", Kind.Home, "tuya"),
            New Brand("Espressif", Kind.Home, "espressif", "esp32", "esp8266"),
            New Brand("Nanoleaf", Kind.Home, "nanoleaf"),
            New Brand("LIFX", Kind.Home, "lifx"),
            New Brand("Netatmo", Kind.Home, "netatmo"),
            New Brand("tado", Kind.Home, "=tado"),
            New Brand("ecobee", Kind.Home, "ecobee"),
            New Brand("Somfy", Kind.Home, "somfy"),
            New Brand("Fibaro", Kind.Home, "fibaro"),
            New Brand("Homey", Kind.Home, "athom", "homey"),
            New Brand("Aqara", Kind.Home, "aqara", "lumi united"),
            New Brand("Broadlink", Kind.Home, "broadlink"),
            New Brand("Meross", Kind.Home, "meross"),
            New Brand("Govee", Kind.Home, "govee"),
            New Brand("Yeelight", Kind.Home, "yeelight"),
            New Brand("WiZ", Kind.Home, "wiz connected"),
            New Brand("Nuki", Kind.Home, "=nuki"),
            New Brand("SwitchBot", Kind.Home, "switchbot", "wonderlabs"),
            New Brand("iRobot", Kind.Home, "irobot", "roomba"),
            New Brand("Roborock", Kind.Home, "roborock"),
            New Brand("Ecovacs", Kind.Home, "ecovacs"),
            New Brand("Dyson", Kind.Home, "dyson"),
            New Brand("Miele", Kind.Home, "miele"),
            New Brand("Bosch Siemens", Kind.Home, "bsh hausger", "home connect"),
            New Brand("Daikin", Kind.Home, "daikin", "prhttpd"),
            New Brand("Lutron", Kind.Home, "lutron"),
            New Brand("Vimar", Kind.Home, "vimar"),
            New Brand("BTicino", Kind.Home, "bticino", "legrand"),
            New Brand("Wemo", Kind.Home, "wemo"),
            New Brand("Belkin", Kind.Network, "belkin"),
            New Brand("APC", Kind.Ups, "american power conversion", "=apc", "powerchute"),
            New Brand("Eaton", Kind.Ups, "eaton"),
            New Brand("Riello", Kind.Ups, "riello"),
            New Brand("CyberPower", Kind.Ups, "cyber power", "cyberpower"),
            New Brand("Vertiv", Kind.Ups, "vertiv", "liebert"),
            New Brand("Socomec", Kind.Ups, "socomec"),
            New Brand("Salicru", Kind.Ups, "salicru"),
            New Brand("Tripp Lite", Kind.Ups, "tripp lite"),
            New Brand("OnePlus", Kind.Phone, "oneplus"),
            New Brand("Oppo", Kind.Phone, "=oppo", "guangdong oppo"),
            New Brand("Vivo", Kind.Phone, "vivo mobile", "=vivo"),
            New Brand("Realme", Kind.Phone, "realme"),
            New Brand("Motorola", Kind.Phone, "motorola mobility", "=moto", "motorola"),
            New Brand("Honor", Kind.Phone, "honor device", "=honor"),
            New Brand("Fairphone", Kind.Phone, "fairphone"),
            New Brand("Nothing", Kind.Phone, "nothing technology"),
            New Brand("Lenovo", Kind.Computer, "lenovo"),
            New Brand("Acer", Kind.Computer, "=acer"),
            New Brand("MSI", Kind.Computer, "micro-star", "=msi"),
            New Brand("Gigabyte", Kind.Computer, "giga-byte", "gigabyte"),
            New Brand("ASRock", Kind.Computer, "asrock"),
            New Brand("Intel", Kind.Computer, "intel corporate", "intel corporation", "=intel"),
            New Brand("Realtek", Kind.Computer, "realtek"),
            New Brand("Microsoft", Kind.Computer, "microsoft"),
            New Brand("Fujitsu", Kind.Computer, "fujitsu"),
            New Brand("Supermicro", Kind.Computer, "super micro", "supermicro"),
            New Brand("Lite-On", Kind.Computer, "liteon", "lite-on"),
            New Brand("AzureWave", Kind.Computer, "azurewave"),
            New Brand("Foxconn", Kind.Computer, "hon hai", "foxconn", "cloud network technology"),
            New Brand("Quanta", Kind.Computer, "quanta"),
            New Brand("Wistron", Kind.Computer, "wistron"),
            New Brand("Compal", Kind.Computer, "compal"),
            New Brand("Pegatron", Kind.Computer, "pegatron"),
            New Brand("Framework", Kind.Computer, "framework computer"),
            New Brand("Raspberry Pi", Kind.Computer Or Kind.Home, "raspberry")
        }

        Private Function FindBrand(text As String, kind As Kind) As Brand
            If String.IsNullOrEmpty(text) Then Return Nothing
            For Each brand In Brands
                If brand.Has(kind) AndAlso brand.MatchesText(text) Then Return brand
            Next
            Return Nothing
        End Function

        ''' <summary>Chiave nel testo (minuscolo): sottostringa, oppure parola intera se inizia con "=" (le cifre fanno da confine).</summary>
        Private Function Matches(text As String, key As String) As Boolean
            If String.IsNullOrEmpty(text) OrElse String.IsNullOrEmpty(key) Then Return False
            If key(0) <> "="c Then Return text.Contains(key, StringComparison.Ordinal)
            Dim word = key.Substring(1)
            Dim index = 0
            While True
                index = text.IndexOf(word, index, StringComparison.Ordinal)
                If index < 0 Then Return False
                Dim [end] = index + word.Length
                Dim startOk = index = 0 OrElse Not Char.IsLetter(text(index - 1))
                Dim endOk = [end] = text.Length OrElse Not Char.IsLetter(text([end]))
                If startOk AndAlso endOk Then Return True
                index += 1
            End While
            Return False
        End Function

#End Region

#Region "Segnali"

        ''' <summary>Segnali del dispositivo già pronti per le regole (testi in minuscolo, insieme delle porte, classe del TTL).</summary>
        Private NotInheritable Class Signals
            Public Sub New(host As DiscoveredHost)
                IsGateway = host.IsGateway
                Sadp = host.Sadp
                Ttl = If(host.Ttl.HasValue AndAlso host.Ttl.Value > 0, host.Ttl, Nothing)
                Ports = host.Ports.Select(Function(p) p.Port).ToHashSet()
                RandomMac = host.Vendor = MacVendorLookup.PrivateAddressLabel OrElse (host.Vendor Is Nothing AndAlso MacVendorLookup.IsLocallyAdministered(host.Mac))
                Vendor = If(RandomMac, "", If(host.Vendor, "").ToLowerInvariant())
                Dim names = {host.HostName, host.NetBiosName}.Where(Function(n) Not String.IsNullOrWhiteSpace(n)).ToArray()
                Me.Names = String.Join(" ", names).ToLowerInvariant()
                Labels = names.Select(Function(n) n.Split("."c)(0).Trim().ToLowerInvariant()).ToArray()
                Banners = String.Join(" | ", host.Ports.Select(Function(p) p.Banner).Where(Function(b) Not String.IsNullOrWhiteSpace(b))).ToLowerInvariant()
                VmKind = VirtualMachineKind(host.Mac)
            End Sub

            Public ReadOnly Property IsGateway As Boolean
            Public ReadOnly Property Sadp As SadpDeviceInfo
            Public ReadOnly Property Ttl As Integer?
            Public ReadOnly Property Ports As HashSet(Of Integer)
            Public ReadOnly Property RandomMac As Boolean
            Public ReadOnly Property Vendor As String
            Public ReadOnly Property Names As String
            Public ReadOnly Property Labels As String()
            Public ReadOnly Property Banners As String
            Public ReadOnly Property VmKind As String

            Public ReadOnly Property TtlUnix As Boolean
                Get
                    Return Ttl.HasValue AndAlso Ttl.Value <= 64
                End Get
            End Property
            Public ReadOnly Property TtlWindows As Boolean
                Get
                    Return Ttl.HasValue AndAlso Ttl.Value > 64 AndAlso Ttl.Value <= 128
                End Get
            End Property
            Public ReadOnly Property TtlNetwork As Boolean
                Get
                    Return Ttl.HasValue AndAlso Ttl.Value > 128
                End Get
            End Property
            ''' <summary>Windows dal TTL, oppure (senza TTL) dalle porte tipiche, oppure dai banner.</summary>
            Public ReadOnly Property WindowsLike As Boolean
                Get
                    Return TtlWindows OrElse (Not Ttl.HasValue AndAlso (Port(135) OrElse Port(5985) OrElse Port(3389))) OrElse
                           InBanners("microsoft-iis", "openssh_for_windows", "microsoft-httpapi")
                End Get
            End Property

            Public Function Port(number As Integer) As Boolean
                Return Ports.Contains(number)
            End Function
            Public Function AnyPort(ParamArray ports As Integer()) As Boolean
                Return ports.Any(AddressOf Me.Ports.Contains)
            End Function
            Public Function InVendor(ParamArray keys As String()) As Boolean
                Return keys.Any(Function(k) Matches(Vendor, k))
            End Function
            Public Function InNames(ParamArray keys As String()) As Boolean
                Return keys.Any(Function(k) Matches(Names, k))
            End Function
            Public Function InBanners(ParamArray keys As String()) As Boolean
                Return keys.Any(Function(k) Matches(Banners, k))
            End Function
            Public Function InNamesOrBanners(ParamArray keys As String()) As Boolean
                Return InNames(keys) OrElse InBanners(keys)
            End Function
            Public Function InAny(ParamArray keys As String()) As Boolean
                Return InVendor(keys) OrElse InNamesOrBanners(keys)
            End Function
            ''' <summary>Marchio del tipo richiesto: prima dal produttore del MAC, poi dai nomi, poi dai banner.</summary>
            Public Function FindBrand(kind As Kind) As Brand
                Return If(DeviceClassifier.FindBrand(Vendor, kind), If(DeviceClassifier.FindBrand(Names, kind), DeviceClassifier.FindBrand(Banners, kind)))
            End Function
            ''' <summary>Marchio del tipo richiesto dal solo produttore del MAC.</summary>
            Public Function VendorBrand(kind As Kind) As Brand
                Return DeviceClassifier.FindBrand(Vendor, kind)
            End Function
        End Class

#End Region

    End Module

End Namespace
