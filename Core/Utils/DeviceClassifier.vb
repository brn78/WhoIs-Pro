Imports System.Text.RegularExpressions

Namespace Utils
    Public Class DeviceClassification
        Public Property DeviceType As String = "Dispositivo Generico"
        Public Property Category As String = "Altro"
        Public Property Icon As String = "💻"
    End Class

    Public Module DeviceClassifier
        Public Function Classify(macVendor As String, hostname As String, openPorts As IEnumerable(Of Integer), ttl As Integer, mac As String) As DeviceClassification
            Dim res As New DeviceClassification()
            Dim v As String = If(macVendor, "").ToLowerInvariant()
            Dim h As String = If(hostname, "").ToLowerInvariant()
            Dim ports As HashSet(Of Integer) = If(openPorts IsNot Nothing, New HashSet(Of Integer)(openPorts), New HashSet(Of Integer)())

            ' 1. Segnali da Porte Aperte (i più affidabili)
            If ports.Contains(8123) OrElse h.Contains("homeassistant") OrElse h.Contains("hassio") OrElse h.Contains("hass") Then
                res.DeviceType = "Hub Domotico (Home Assistant)"
                res.Category = "Domotica / IoT"
                res.Icon = "🏠"
                Return res
            End If

            If ports.Contains(1883) OrElse ports.Contains(8883) Then
                res.DeviceType = "Broker MQTT / Gateway IoT"
                res.Category = "Domotica / IoT"
                res.Icon = "🏠"
                Return res
            End If

            If ports.Contains(8008) OrElse ports.Contains(8009) Then
                res.DeviceType = "Google Chromecast / Nest Cast"
                res.Category = "Smart TV"
                res.Icon = "📺"
                Return res
            End If

            If (ports.Contains(8001) OrElse ports.Contains(8002)) AndAlso (v.Contains("samsung") OrElse h.Contains("samsung")) Then
                res.DeviceType = "Smart TV Samsung (Tizen OS)"
                res.Category = "Smart TV"
                res.Icon = "📺"
                Return res
            End If

            If (ports.Contains(3000) OrElse ports.Contains(3001)) AndAlso (v.Contains("lg") OrElse h.Contains("lg")) Then
                res.DeviceType = "Smart TV LG (WebOS)"
                res.Category = "Smart TV"
                res.Icon = "📺"
                Return res
            End If

            If ports.Contains(554) OrElse ports.Contains(8554) OrElse h.Contains("camera") OrElse h.Contains("cam") OrElse h.Contains("nvr") Then
                res.DeviceType = "Telecamera IP / NVR (RTSP)"
                res.Category = "Telecamera IP"
                res.Icon = "📹"
                Return res
            End If

            If ports.Contains(9100) OrElse ports.Contains(515) OrElse ports.Contains(631) OrElse v.Contains("brother") OrElse v.Contains("epson") OrElse v.Contains("canon") OrElse h.Contains("printer") Then
                Dim prnVendor As String = If(v.Contains("brother"), "Brother", If(v.Contains("epson"), "Epson", If(v.Contains("canon"), "Canon", If(v.Contains("hp"), "HP", ""))))
                res.DeviceType = $"Stampante di rete {prnVendor}".Trim()
                res.Category = "Stampante"
                res.Icon = "🖨️"
                Return res
            End If

            If ports.Contains(5000) OrElse ports.Contains(5001) OrElse v.Contains("synology") OrElse h.Contains("synology") OrElse h.Contains("diskstation") Then
                res.DeviceType = "NAS Storage (Synology DiskStation)"
                res.Category = "NAS / Storage"
                res.Icon = "🗄️"
                Return res
            End If

            If v.Contains("qnap") OrElse h.Contains("qnap") Then
                res.DeviceType = "NAS Storage (QNAP Turbo NAS)"
                res.Category = "NAS / Storage"
                res.Icon = "🗄️"
                Return res
            End If

            ' 2. Segnali da Hostname
            If h.Contains("tv") OrElse h.Contains("tizen") OrElse h.Contains("bravia") OrElse h.Contains("smarttv") Then
                Dim brand As String = If(h.Contains("samsung") OrElse v.Contains("samsung"), "Samsung", If(h.Contains("lg") OrElse v.Contains("lg"), "LG", If(h.Contains("sony") OrElse v.Contains("sony"), "Sony", "Smart TV")))
                res.DeviceType = $"Smart TV ({brand})"
                res.Category = "Smart TV"
                res.Icon = "📺"
                Return res
            End If

            If h.Contains("iphone") OrElse (v.Contains("apple") AndAlso (h.Contains("phone") OrElse h.Contains("mobile"))) Then
                res.DeviceType = "Smartphone (Apple iPhone)"
                res.Category = "Smartphone / Tablet"
                res.Icon = "📱"
                Return res
            End If

            If h.Contains("ipad") Then
                res.DeviceType = "Tablet (Apple iPad)"
                res.Category = "Smartphone / Tablet"
                res.Icon = "📱"
                Return res
            End If

            If h.Contains("galaxy") OrElse h.Contains("sm-") OrElse (v.Contains("samsung") AndAlso (h.Contains("phone") OrElse h.Contains("mobile") OrElse h.Contains("android"))) Then
                res.DeviceType = "Smartphone (Samsung Galaxy)"
                res.Category = "Smartphone / Tablet"
                res.Icon = "📱"
                Return res
            End If

            If h.Contains("pixel") OrElse h.Contains("redmi") OrElse h.Contains("xiaomi") OrElse h.Contains("huawei") OrElse h.Contains("oneplus") Then
                res.DeviceType = "Smartphone Android"
                res.Category = "Smartphone / Tablet"
                res.Icon = "📱"
                Return res
            End If

            If h.Contains("echo") OrElse h.Contains("alexa") OrElse (v.Contains("amazon") AndAlso Not h.Contains("fire")) Then
                res.DeviceType = "Assistente Vocale (Amazon Echo / Alexa)"
                res.Category = "Assistente Vocale"
                res.Icon = "🔊"
                Return res
            End If

            If h.Contains("firetv") OrElse h.Contains("firestick") OrElse (v.Contains("amazon") AndAlso h.Contains("fire")) Then
                res.DeviceType = "Lettore Multimediale (Amazon Fire TV)"
                res.Category = "Smart TV"
                res.Icon = "📺"
                Return res
            End If

            If h.Contains("nest") OrElse h.Contains("googlehome") OrElse h.Contains("google-home") Then
                res.DeviceType = "Assistente Vocale (Google Nest / Home)"
                res.Category = "Assistente Vocale"
                res.Icon = "🔊"
                Return res
            End If

            If h.Contains("fritz.box") OrElse h.Contains("fritz") OrElse v.Contains("avm") Then
                res.DeviceType = "Modem / Router (AVM FRITZ!Box)"
                res.Category = "Router / Access Point"
                res.Icon = "🌐"
                Return res
            End If

            If h.Contains("unifi") OrElse h.Contains("ubnt") OrElse v.Contains("ubiquiti") Then
                res.DeviceType = "Access Point / Rete (Ubiquiti UniFi)"
                res.Category = "Router / Access Point"
                res.Icon = "🌐"
                Return res
            End If

            If h.Contains("tasmota") OrElse h.Contains("shelly") OrElse h.Contains("sonoff") OrElse h.Contains("esp32") OrElse h.Contains("esp8266") OrElse h.Contains("wled") OrElse h.Contains("zigbee") Then
                res.DeviceType = "Sensore / Modulo IoT Domotico"
                res.Category = "Domotica / IoT"
                res.Icon = "🏠"
                Return res
            End If

            ' 3. Segnali da MAC Vendor
            If v.Contains("espressif") Then
                res.DeviceType = "Modulo Domotico IoT (ESP32 / ESP8266)"
                res.Category = "Domotica / IoT"
                res.Icon = "🏠"
                Return res
            End If

            If v.Contains("shelly") OrElse v.Contains("allterco") Then
                res.DeviceType = "Interruttore / Relè Smart (Shelly)"
                res.Category = "Domotica / IoT"
                res.Icon = "🏠"
                Return res
            End If

            If v.Contains("tuya") Then
                res.DeviceType = "Dispositivo Smart IoT (Tuya)"
                res.Category = "Domotica / IoT"
                res.Icon = "🏠"
                Return res
            End If

            If v.Contains("philips hue") OrElse v.Contains("signify") Then
                res.DeviceType = "Bridge Illuminazione (Philips Hue)"
                res.Category = "Domotica / IoT"
                res.Icon = "🏠"
                Return res
            End If

            If v.Contains("raspberry pi") Then
                res.DeviceType = "Microcomputer Raspberry Pi (Server / Home Assistant)"
                res.Category = "Domotica / IoT"
                res.Icon = "🏠"
                Return res
            End If

            If v.Contains("samsung") Then
                ' Spesso TV se in LAN con porte multimediali o vendor TV
                If v.Contains("smart tv") Then
                    res.DeviceType = "Smart TV Samsung"
                    res.Category = "Smart TV"
                    res.Icon = "📺"
                Else
                    res.DeviceType = "Dispositivo Samsung (TV / Smartphone)"
                    res.Category = "Smart TV"
                    res.Icon = "📺"
                End If
                Return res
            End If

            If v.Contains("lg electronics") Then
                res.DeviceType = "Smart TV / Apparecchio LG"
                res.Category = "Smart TV"
                res.Icon = "📺"
                Return res
            End If

            If v.Contains("apple") Then
                res.DeviceType = "Dispositivo Apple (iPhone / iPad / Mac)"
                res.Category = "Smartphone / Tablet"
                res.Icon = "📱"
                Return res
            End If

            If v.Contains("playstation") OrElse v.Contains("sony interactive") Then
                res.DeviceType = "Console da Gioco (Sony PlayStation)"
                res.Category = "Console Gaming"
                res.Icon = "🎮"
                Return res
            End If

            If v.Contains("xbox") Then
                res.DeviceType = "Console da Gioco (Microsoft Xbox)"
                res.Category = "Console Gaming"
                res.Icon = "🎮"
                Return res
            End If

            If v.Contains("nintendo") Then
                res.DeviceType = "Console da Gioco (Nintendo)"
                res.Category = "Console Gaming"
                res.Icon = "🎮"
                Return res
            End If

            If v.Contains("tp-link") OrElse v.Contains("netgear") OrElse v.Contains("d-link") OrElse v.Contains("cisco") OrElse v.Contains("mikrotik") OrElse
               v.Contains("keenetic") OrElse v.Contains("zyxel") OrElse v.Contains("technicolor") OrElse v.Contains("sagemcom") OrElse v.Contains("huawei technologies") OrElse
               v.Contains("aruba") OrElse v.Contains("juniper") OrElse v.Contains("draytek") OrElse v.Contains("teltonika") Then
                res.DeviceType = "Dispositivo di Rete (Router / Switch / AP)"
                res.Category = "Router / Access Point"
                res.Icon = "🌐"
                Return res
            End If

            If v.Contains("vmware") OrElse v.Contains("hyper-v") OrElse v.Contains("virtualbox") OrElse v.Contains("qemu") Then
                res.DeviceType = "Macchina Virtuale / Hypervisor"
                res.Category = "PC / Server"
                res.Icon = "💻"
                Return res
            End If

            If v.Contains("dell") OrElse v.Contains("lenovo") OrElse v.Contains("hewlett packard") OrElse v.Contains("intel") OrElse v.Contains("realtek") OrElse v.Contains("asustek") OrElse v.Contains("msi") OrElse v.Contains("gigabyte") Then
                If ports.Contains(3389) OrElse ttl = 128 Then
                    res.DeviceType = "Computer Desktop / Laptop (Windows)"
                Else
                    res.DeviceType = "Computer / Workstation / Server"
                End If
                res.Category = "PC / Server"
                res.Icon = "💻"
                Return res
            End If

            ' 4. Inferenza da TTL
            If ttl > 0 Then
                If ttl <= 64 Then
                    res.DeviceType = "Dispositivo Linux / Unix / Mobile"
                    res.Category = "PC / Server"
                    res.Icon = "💻"
                ElseIf ttl <= 128 Then
                    res.DeviceType = "Computer Windows"
                    res.Category = "PC / Server"
                    res.Icon = "💻"
                Else
                    res.DeviceType = "Apparato di Rete / Gateway"
                    res.Category = "Router / Access Point"
                    res.Icon = "🌐"
                End If
            End If

            Return res
        End Function
    End Module
End Namespace