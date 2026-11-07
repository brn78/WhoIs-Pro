# Guida: riconoscimento Hikvision (SADP), banner con certificato, più sottoreti e fusione per MAC

Questa guida descrive, con i dettagli di protocollo e le regole, quello che WhoIs Pro 2.8.0 fa durante la scansione
della rete per riconoscere i dispositivi Hikvision e per le altre migliorie arrivate con la stessa versione. Serve a
rifare le stesse cose in un altro programma (desktop o web, in qualunque linguaggio): ogni paragrafo dice cosa fare,
con quali valori e perché, e indica il file di WhoIs Pro dove la cosa è già scritta in VB.NET, da leggere come riferimento.

## In breve

| Funzione | Dove sta in WhoIs Pro | Cosa serve per portarla |
| --- | --- | --- |
| Sonda SADP multicast e unicast | `Core\Network\Scan\SadpDiscovery.vb` | un socket UDP, un parser XML |
| Uso dei risultati nella scansione | `Core\Network\Scan\DiscoveryScanner.vb` (`ApplySadpResults`, `PortsPhaseAsync`, `HikvisionWebFollowUpAsync`, `Describe`) | il proprio motore di scansione |
| Modello dal realm ISAPI e indizi web | `Core\Network\Scan\BannerGrabber.vb` (`GrabHikvisionRealmAsync`), `DiscoveryScanner.IsHikvisionWebBanner` | una GET HTTP/1.0 |
| Classificazione per famiglia | `Core\Utils\DeviceClassifier.vb` (`HikvisionDevice`, `ExtractHikvisionModel`, `InferHikvisionChannels`, `GuessOs`) | le regole e le regex di §3 |
| Avvisi: non attivato, firmware datato | `Core\Inventory\NetworkInventory.vb` (`NetHostRecord.SensitivePorts`) | due controlli di testo |
| Indirizzi RTSP | `ui\pages\network.js` (`launchItems`) | due stringhe |
| Banner HTTPS con certificato | `BannerGrabber.vb` (`GrabFromConnectedClientAsync`, `TryGetCertificateHint`) | TLS senza verifica del certificato |
| Più sottoreti in una scansione | `Core\Utils\NetworkUtils.vb` (`SplitTargets`, `ParseTargets`), `DiscoveryScanner.ResolveTarget` | il parser dei bersagli |
| Porta 53 falsa in VPN | `DiscoveryScanner.PortsPhaseAsync` | un conteggio |
| Fusione degli host con lo stesso MAC | `Core\Network\Scan\SameDeviceMerger.vb`, `NetworkInventory.RecordScan` / `AbsorbSecondaryRecords`, `AppBridge.MergeOtherIpv4` | le regole di §8 |
| Prestazioni | `NetInterop.vb` (`TcpConnector`, `Ipv4.TryParse`), `MacVendorLookup.vb` | vedi §9 |

Le prove che fissano il comportamento atteso sono in `Tests\UnitTest1.vb` (`SadpDiscovery_ParseHello…`,
`DeviceClassifier_ShouldClassifyAllHikvisionFamilies…`, `SensitivePorts_ShouldFlag…`, `MultiSubnetAndVpn…`) e in
`Tests\InventoryTests.vb` (`SameDeviceMerger_…`, `RecordScan_SameDeviceWithTwoIps…`): conviene portarle insieme al codice.

## 1. La sonda SADP

SADP (Search Active Devices Protocol) è il protocollo con cui lo strumento SADP di Hikvision trova i dispositivi in rete.
È un semplice scambio di XML su UDP e **non richiede credenziali**: anche un dispositivo appena uscito dalla scatola
risponde, e dice se è ancora da attivare.

### 1.1 Pacchetto e indirizzi

- Destinazione multicast: `239.255.255.250`, porta UDP `37020` (stessa porta anche in unicast).
- Pacchetto da inviare (XML_Probe), UTF-8, un UUID qualsiasi:

```xml
<?xml version="1.0" encoding="UTF-8"?><Probe><Uuid>8D2091BC-1DD2-11B2-807B-8CE748CF9334</Uuid><Types>inquiry</Types></Probe>
```

### 1.2 La risposta

Il dispositivo risponde in unicast al mittente (alcuni firmware anche in multicast) con una radice `<Hello>` oppure
`<ProbeMatch>`. I campi utili:

| Tag | Significato | Esempio |
| --- | --- | --- |
| `DeviceDescription` | codice modello esatto | `DS-2CD2143G2-I`, `DS-7608NI-K2/8P` |
| `DeviceSN` | numero di serie (spesso inizia con il modello) | `DS-7608NI-K2/8P0820210607CCRR000000001` |
| `SoftwareVersion` | firmware; a volte `build` è attaccato ai numeri | `V5.5.82build 190909` |
| `DSPVersion` | versione DSP | `V5.0 build 210601` |
| `DeviceType` | codice numerico del tipo | `139856` |
| `MAC` | con trattini | `44-19-b7-00-00-64` |
| `IPv4Address` | indirizzo che il dispositivo dichiara | `192.168.1.64` |
| `HttpPort`, `CommandPort` | porta web e porta SDK (predefinite 80 e 8000) | `80`, `8000` |
| `AnalogChannelNum`, `DigitalChannelNum` | canali (DVR/NVR) | `0`, `8` |
| `Activated` | `false` = mai attivato, senza password | `false` |
| `DHCP`, `BootTime`, `OEMInfo` | accessori | |

Esempio completo di risposta, buono come dato di prova:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<ProbeMatch><Uuid>FCA5F552-CD11-4EB8-8B07-F59B71B82E25</Uuid><Types>inquiry</Types>
<DeviceType>139856</DeviceType><DeviceDescription>DS-2CD2442FWD-IW</DeviceDescription>
<DeviceSN>DS-2CD2442FWD-IW20200101AAWR000000001</DeviceSN><CommandPort>8000</CommandPort><HttpPort>80</HttpPort>
<MAC>bc-ad-28-00-00-c9</MAC><IPv4Address>192.168.1.201</IPv4Address><DHCP>false</DHCP>
<AnalogChannelNum>0</AnalogChannelNum><DigitalChannelNum>1</DigitalChannelNum></ProbeMatch>
```

### 1.3 Multicast sulla rete locale: come farlo bene

Per ogni scheda di rete IPv4 (escluse quelle virtuali, se si sa riconoscerle):

1. Socket UDP con `ReuseAddress`. **Su Windows** disattivare `SIO_UDP_CONNRESET` (`IOControl(-1744830452, {0,0,0,0})`):
   altrimenti un ICMP «port unreachable» di un qualunque host fa fallire la ricezione con l'errore 10054 e si perdono
   le risposte successive.
2. `Bind` sull'indirizzo della scheda, porta `37020`; se è occupata (c'è il programma SADP aperto) va bene la porta 0.
   Sulla 37020 arrivano anche le risposte che i firmware mandano in multicast.
3. `MulticastInterface` = la scheda, `MulticastTimeToLive` = 2, `AddMembership(239.255.255.250)`. Se l'iscrizione al
   gruppo fallisce (schede particolari) si continua lo stesso: invio e risposte unicast funzionano.
4. Invio della sonda e **secondo invio dopo 220 ms** (il primo pacchetto UDP si perde spesso su Wi-Fi e switch).
5. Ascolto fino alla scadenza: **1,0-1,8 s** per una rete intera (nel motore: `max(1000, min(timeout·2, 1800))`),
   **0,3-0,6 s** per un singolo indirizzo (riscansione di un host). In LAN le risposte arrivano in pochi decimi.
6. La sonda parte **in parallelo al ping e all'ARP** e si legge il risultato dopo: così non allunga la scansione.

### 1.4 Unicast: VLAN instradate e VPN

Dove il multicast non passa (altra sottorete dietro un router, tunnel VPN) la stessa sonda si manda **in unicast alla
porta 37020 di ogni host** che ha risposto al ping e non è nella sottorete locale (oppure al singolo indirizzo chiesto).
Un solo socket legato a `0.0.0.0:0`, invio a tutti, secondo invio a 220 ms, ascolto 0,9-1,6 s. L'indirizzo valido è
**quello da cui arriva la risposta**, non quello che il dispositivo dichiara in `IPv4Address` (può essere interno o
diverso se il dispositivo ha più indirizzi). Questo è il modo in cui si leggono modello, firmware, seriale **e MAC** di
un dispositivo che in ARP non si vede.

### 1.5 Interpretazione

- Si accetta solo una radice `Hello` o `ProbeMatch`: l'**eco della propria sonda** (`<Probe>`) e tutto il resto si
  scarta per il nome della radice, **non** per `<Types>` (le telecamere rispondono `<ProbeMatch>` ripetendo
  `<Types>inquiry</Types>`).
- Indirizzo: `IPv4Address` se è un IPv4 valido e non `0.0.0.0`, altrimenti il mittente. MAC normalizzato
  (`aa:bb:cc:dd:ee:ff`). Senza né indirizzo né MAC la risposta non vale.
- Firmware: `(?<=\d)build` → ` build` (regex, ignora maiuscole), così `V5.5.82build 190909` diventa
  `V5.5.82 build 190909` e la data si legge.
- Porte fuori da 1-65535 → predefinite; canali negativi → 0; `Activated`/`DHCP`: `true`/`1`, `false`/`0`, altrimenti
  sconosciuto.
- Chiave per non contare due volte la stessa risposta: `MAC|IP` (un dispositivo con due indirizzi conta per entrambi).
- Banner da mostrare sulla porta SDK: `Hikvision SDK · <modello> · FW <firmware> · SN <seriale senza il modello
  davanti>` più ` · NON ATTIVATO` se `Activated=false`, al massimo 160 caratteri.

### 1.6 Uso dei risultati nella scansione

Dopo ping e ARP, per ogni risposta SADP:

1. Se l'indirizzo è tra quelli scansionati: si aggancia all'host (si crea se non c'era: «presente, non risponde al
   ping»), si completa il MAC se manca, e si avvisa l'avanzamento («192.168.1.64 → DS-7608NI-K2/8P (SADP)»). Gli
   stessi dati si copiano agli **altri host con lo stesso MAC** (telecamera con cavo e Wi-Fi: SADP risponde da una sola
   interfaccia).
2. Altrimenti si prova l'abbinamento per MAC.
3. Altrimenti, se si stava scansionando una rete (non un singolo IP), è un **Hikvision sul cavo ma con un indirizzo
   fuori sottorete** (tipico il `192.168.1.64` di fabbrica): si segnala nell'avanzamento, non si aggiunge alla lista.

Poi:

- Un host visto solo in ARP ma con risposta SADP **non** si scarta come «sparito».
- Nella fase porte, agli host con SADP si provano sempre anche `HttpPort`, `CommandPort` e `554`, qualunque gruppo di
  porte abbia scelto l'utente.
- In classificazione: produttore di ripiego «Hangzhou Hikvision Digital Technology Co.,Ltd.» se il MAC non lo dice (moduli
  Wi-Fi di terzi); sulla porta SDK aperta servizio `Hikvision-SDK` e banner SADP; se la porta SDK non è aperta il banner
  SADP va sulla porta web, così resta in archivio; la stima del sistema diventa `Firmware <SoftwareVersion>`.

## 2. Modello dal web: realm ISAPI e indizi Hikvision

Quando SADP non risponde (modulo Wi-Fi, firmware vecchio, filtro sul router) la pagina web tradisce comunque il marchio.
Indizi nel banner HTTP (intestazione `Server`, titolo, rinvio): `App-webs`, `DNVRS-Webs`, `DVRDVS-Webs`,
`Hikvision-Webs`, rinvio a `/doc/page/login.asp`, `Realm: IP Camera(`, la parola `Hikvision`. Molte pagine di login
Hikvision sono vuote e rimandano via script: il lettore dei banner deve continuare a leggere oltre un `<title></title>`
vuoto e cercare `location.href = "…"`, `location.replace(…)` o `meta http-equiv=refresh` (regex in
`BannerGrabber.ScriptRedirectPattern`).

Per avere il **modello** da un host riconosciuto così:

```
GET /ISAPI/System/deviceInfo HTTP/1.0
Host: 192.168.1.201
Connection: close
```

La risposta è `401 Unauthorized` con `WWW-Authenticate: Digest realm="DS-2CD2532F-I", …` (o `Basic`): il realm è il
codice modello. Regex: `realm\s*=\s*"?([^",]+)`. Si aggiunge al banner come ` · Realm: <modello>`. Nello stesso
passaggio si provano le porte `8000` e `554` (solo connessione TCP, più il banner RTSP sulla 554). Tutto con 16 host alla
volta e 0,8-2,5 s di tempo per host.

## 3. Classificazione per famiglia

### 3.1 Trovare il codice modello

Regex sul modello SADP, poi sui banner, poi sui nomi (DNS, NetBIOS), ignorando maiuscole:

```
\b((?:i?ds|hm|ipc|nvr|dvr)-[a-z0-9/]{2,16}(?:-[a-z0-9/]{1,12})*)\b
```

Il risultato si scrive in maiuscolo, tranne la «i» iniziale di `iDS-`.

**Prudenza.** Quando non c'è nessun altro indizio Hikvision (niente SADP, produttore del MAC diverso, banner muti), un
nome come `nvr-sala`, `dvr-garage` o `ds-server` **non** deve diventare un dispositivo Hikvision. In quel caso si accetta
solo un codice che usa soltanto Hikvision, e solo se il produttore del MAC non è un altro marchio conosciuto:

```
^(?:i?DS-(?:2[A-Z]{2}\d|[1-9]\d{2,3}[A-Z]|K[0-9A-Z]|P[WH]A|3E\d|19A|TC[GM])|HM-TD)
```

### 3.2 Dal prefisso del modello al tipo

Ordine di controllo (il primo che corrisponde vince), con la categoria usata da WhoIs Pro:

| Prefisso (maiuscolo) | Tipo | Categoria |
| --- | --- | --- |
| `DS-PWA`, `DS-PHA`, `DS-19A` | Centrale d'allarme AX PRO | Centrale d'allarme |
| `DS-K1T`, `DS-K1A` | Terminale controllo accessi (MinMoe, presenze) | Domotica |
| `DS-K26`, `DS-K28`, `DS-K2M` | Centralina controllo accessi | Domotica |
| `DS-KV`, `DS-KD`, `DS-KB` | Posto esterno videocitofono | Telecamera |
| `DS-KH` | Monitor videocitofono | Telecamera |
| `DS-3E` | Switch PoE | Rete |
| `DS-2DE`, `DS-2DF`, `IDS-2DE`, `IDS-2DF`, `DS-2PT` | Telecamera PTZ | Telecamera |
| `DS-2TD`, `IDS-2TD`, `HM-TD` | Telecamera termica | Telecamera |
| `IDS-2CD7A`, `DS-2CD7A`, `DS-2CD4A26`, `IDS-TCM`, `DS-TCG` | Telecamera ANPR (targhe) | Telecamera |
| `DS-69`, `DS-64` | Decoder video | Telecamera |
| `NVR-`, oppure `DS-71/76/77/86/96`, `IDS-77/96` con `NI`/`NXI` o senza `H` | NVR, con il numero di canali | Telecamera |
| `DVR-`, `DS-72/73/81/90`, `IDS-72/73`, `DS-71…H` | DVR Turbo HD | Telecamera |
| tutto il resto (`DS-2CD`, `IDS-2CD`, `DS-2CV`, `DS-2XM`, `IPC-`…) | Telecamera IP | Telecamera |

Senza codice modello ma con indizi Hikvision (e host non Windows): «Videoregistratore di rete (NVR) Hikvision» se nomi
o banner dicono `nvr`, `dvr`, `dvrdvs-webs`, `recorder`, `videoregistratore`, `rtsp/1.0 404`, oppure se SADP dichiara
più di un canale; altrimenti «Telecamera IP Hikvision».

**Canali dell'NVR**: `DigitalChannelNum` da SADP se > 0, altrimenti dal codice con la regex
`^(?:i?ds)-(?:71|76|77|86|96)(04|08|16|32|64|128)` (es. `DS-7608NI-K2/8P` → 8).

**Sistema stimato**: `Firmware <SoftwareVersion>` da SADP; in mancanza, dai banner con la regex
`(?:\bFW|\bFirmware)\s+(V\d[^\r\n·|]*)`.

Esempi attesi (dalle prove): `DS-7608NI-K2/8P` con 8 canali → «Videoregistratore di rete (NVR) Hikvision DS-7608NI-K2/8P
(8 canali)»; `DS-2DE4225IW-DE` → «Telecamera PTZ Hikvision …»; `DS-K1T341AMF` → «Terminale controllo accessi Hikvision
…» (Domotica); `DS-PWA96-M-WE` → «Centrale d'allarme Hikvision AX PRO (…)»; `DS-3E0508P-E` → «Switch PoE Hikvision …»
(Rete); `DS-K2602T` → «Centralina controllo accessi Hikvision …».

## 4. Avvisi di sicurezza («da verificare»)

- **Non attivato**: se un banner contiene `NON ATTIVATO` (cioè `Activated=false` da SADP) → «<porta> · Dispositivo
  Hikvision non attivato (senza password)». Un dispositivo così accetta la prima password che gli viene proposta.
- **Firmware datato**: per un host Hikvision (tipo, produttore o banner) si cerca in sistema stimato e banner la build
  con la regex `build\s*(\d{2})\d{4}\b`; anno = 2000 + le due cifre; se è tra il 2010 e **l'anno corrente meno cinque**
  → «Firmware datato (build 2019)».

## 5. Indirizzi RTSP

Solo per i dispositivi riconosciuti come Hikvision (tipo, oppure servizio/banner con «Hikvision»), perché il percorso è
quello di Hikvision:

- flusso principale: `rtsp://<host>:554/Streaming/Channels/101`
- flusso secondario: `rtsp://<host>:554/Streaming/Channels/102`
- porta `8554` se è aperta quella e non la 554.

Su un NVR il canale N è `<N>01` / `<N>02` (es. `201` per la seconda telecamera).

## 6. Banner HTTPS con il certificato

Sulle porte `443` e `8443` si apre una sessione TLS **senza verificare il certificato** (callback che accetta tutto;
`TargetHost` = l'indirizzo), si fa la stessa `GET / HTTP/1.0` dell'HTTP e in più si legge il **soggetto** del
certificato del server. La **stretta di mano ha un tetto di 500 ms** (`TlsHandshakeMs`): certi router tengono la 443
aperta ma non rispondono mai al ClientHello, e senza tetto la lettura consumerebbe tutto il tempo della fase (misurato:
1,4 s per ogni riscansione di un host). In LAN una stretta di mano vera finisce in pochi decimi, anche su una telecamera.
Dal certificato si prendono:

- `O` (organizzazione), scartando `Default Company Ltd` e `Internet Widgits Pty Ltd` (valori di default di OpenSSL);
- `CN`, scartando l'indirizzo IP stesso, `localhost` e `*`;
- risultato: `O (CN)` se diversi, altrimenti quello che c'è; nel banner come `Certificato: …` quando mancano titolo
  e realm, oppure da solo se la pagina non risponde.

Con il titolo o il certificato si riconoscono: switch e controller **Ubiquiti** (`EdgeSwitch`, `CloudKey` → «Controller
di rete Ubiquiti», `UniFi`), inverter **Fronius**, **SolarEdge**, **SMA** (`sma solar`, `sunny boy`, `sunny tripower`),
controller **Vantage** (`Vantage TPT`, `InFusion Controller`, titolo `IC-II`), interfacce **Daikin** (`PrHTTPD`),
apparati **Modbus TCP** (porta `502`, da aggiungere alle porte comuni → «Automazione / PLC (Modbus)»). Un dettaglio
importante: se la risposta su una porta non comincia con `HTTP/`, il banner è «niente», non la prima riga di byte
casuali (prima la porta `8000` dell'SDK Hikvision mostrava spazzatura).

## 7. Più sottoreti in una scansione

**Grammatica del bersaglio** (`NetworkUtils.SplitTargets` + `ParseTargets`, e `DiscoveryScanner.ResolveTarget` che
usa lo stesso spezzettamento):

- più parti separate da virgola, punto e virgola o spazio; gli spazi attorno a `/` e `-` si tolgono prima
  (`192.168.1.0 / 24` vale `192.168.1.0/24`);
- ogni parte: vuoto = rete principale del PC; `192.168.2`, `192.168.2.` o `192.168.2.*` = `/24`; `192.168.2.0` (ultimo
  ottetto zero) = `/24`; `a.b.c.d/n` con `n ≥ 22` (al massimo 1.024 indirizzi per rete); `a.b.c.d-e.f.g.h` o
  `a.b.c.d-n` (intervallo); un solo indirizzo;
- l'insieme non supera **4.096 indirizzi**; l'etichetta è `rete1, rete2`; la scheda di riferimento è quella della prima
  parte raggiungibile direttamente (on-link).

**Interfaccia**: quando gli host visti stanno in più sottoreti `/24` compare un selettore «Sottorete» (tutte, oppure
una) e, con «tutte», l'elenco si raggruppa per sottorete con un'intestazione e il conteggio (`subnetOf`, `countSubnets`,
`renderBySubnet` in `ui\core\dom.js`; `groupBy` in `ui\core\table.js`). La sottorete di un host partecipa anche alla
ricerca testuale.

**Porta 53 falsa in VPN**: alcune VPN con router di sottorete (Tailscale con MagicDNS, per esempio) rispondono loro
sulla TCP 53 di ogni indirizzo instradato, e tutti gli host remoti sembrano «server DNS». Regola: se gli host **fuori
dalla sottorete locale** con porte aperte sono almeno 3 e almeno il 70 % ha la 53, la 53 si toglie a tutti gli host
fuori sottorete.

## 8. Un solo dispositivo con più IPv4 (stesso MAC)

### 8.1 Quando unire (`SameDeviceMerger.IsSameDevice`)

Gli host con lo stesso MAC nella stessa scansione sono **un dispositivo** se, insieme:

- sono al massimo **4** indirizzi (oltre, è un ripetitore Wi-Fi in MAC-NAT o un proxy ARP: tanti dispositivi dietro un
  MAC solo);
- i nomi NetBIOS presenti sono tutti uguali (nomi diversi = macchine diverse);
- i TTL presenti sono tutti uguali (TTL diversi = sistemi diversi dietro lo stesso MAC);
- le categorie assegnate, tolta «Altro», sono al massimo una.

### 8.2 Chi resta come principale e cosa si unisce

Ordine: gateway → chi ha risposto a SADP con quel preciso indirizzo → questa macchina → chi risponde al ping → chi ha
più porte → l'indirizzo più basso. Gli altri indirizzi finiscono in `ExtraIps` (ordinati); si uniscono le porte (banner
compreso, se il principale non l'aveva), nome DNS e NetBIOS mancanti, dati SADP, TTL, la latenza migliore, gateway e
«questa macchina». La fusione si fa alla fine della scansione, dopo la classificazione, e il principale si riclassifica;
il conteggio «N host» della scansione conta i dispositivi, non gli indirizzi.

### 8.3 Archivio e sessione

- Archivio (SQLite): colonna `extra_ips` (testo, indirizzi separati da virgola; `NULL` se uno solo), aggiunta con
  `ALTER TABLE` alla prima apertura. Un indirizzo principale che cambia **tra quelli dello stesso dispositivo non è un
  «cambio di IP»**. Le vecchie righe degli indirizzi secondari (salvate quando ogni IP era una riga, chiave `ip:…`)
  vengono **assorbite** nel principale: alias, note e icona se il principale non li ha, la data di prima rilevazione più
  vecchia, gli eventi riassegnati, poi la riga si cancella: niente falsi «assente», niente dispositivi contati due volte.
  Un'osservazione intermedia (fase ARP) di un indirizzo secondario non sposta il principale.
- Sessione (file `.whoispro`): campo `OtherIpv4` sull'host; le righe degli indirizzi secondari spariscono e il
  principale eredita nome personalizzato, note e icona; le porte restano legate al loro indirizzo (pagina Porte) ma il
  conteggio e la classificazione le vedono tutte, senza doppioni per (porta, protocollo). Nell'interfaccia: etichetta
  `+1 IP` accanto all'indirizzo con gli altri nel suggerimento, voce «Altri IPv4» nel dettaglio, e nel flusso di
  avanzamento le righe secondarie si tolgono appena arriva il principale.

## 9. Prestazioni e accorgimenti tecnici

- **Porte TCP chiuse all'istante**: `SIO_TCP_INITIAL_RTO` (`0x98000011`) con `TCP_INITIAL_RTO_PARAMETERS` =
  `{FF, FF, FE, 00}` (Rtt non specificato, nessuna ritrasmissione del SYN): una porta chiusa o filtrata risponde subito
  invece di far ripetere il SYN per un secondo. Se Windows non supporta l'opzione si ricorda e non si riprova. Vale per
  la scansione della rete e per la pagina Porte, che usa lo stesso connettore e lo stesso lettore di banner.
- **UDP senza 10054**: `SIO_UDP_CONNRESET` spento sui socket in ascolto (§1.3).
- **Tempi con un tetto per ogni cosa che può non rispondere**: stretta di mano TLS 500 ms (§6), sonda SADP 0,3-1,8 s,
  banner 0,8-2,5 s, realm ISAPI 0,8-2,5 s. Su una rete intera i tempi si sovrappongono (64 banner alla volta); sulla
  riscansione di un singolo host sono in fila e si sentono tutti, quindi vanno tenuti corti.
- **Niente regex nei percorsi caldi**: normalizzazione del MAC, ricerca del produttore (MA-L/MA-M/MA-S con
  `TryGetValue`), bit «locally administered» e lettura del file `manuf` sono cicli sui caratteri; `Ipv4.TryParse` è un
  parser a mano che rifiuta `192.168.1` e gli ottetti con zero iniziale; il confronto tra indirizzi lo usa.
- **Archivio**: la mappa «ultima scansione per rete» si calcola una volta per registrazione e si passa a tutte le
  letture (prima una query per ogni riga letta); durante la scansione l'archivio si aggiorna nella fase ARP, a fine
  scansione e la prima volta che un host compare in una fase intermedia, non a ogni fase.
- **Avanzamento sicuro tra thread**: l'host passato all'avanzamento è una **copia** (`Snapshot`), perché la scansione
  continua ad arricchire l'originale (porte, banner, fusione) mentre l'interfaccia lo legge.

Misure fatte su questa macchina (Debug, stesso PC e stessa rete, versione 2.7.0 contro 2.8.0): vedi il CHANGELOG della
2.8.0 per i numeri.

## 10. Come portarla in un altro progetto

### 10.1 Programma o servizio .NET (desktop, servizio Windows, applicazione web ASP.NET Core)

Non ci sono dipendenze esterne: solo `System.Net.Sockets`, `System.Xml.Linq`, `System.Net.Security`. In un'applicazione
web .NET la sonda gira **nel processo del server** (un servizio registrato nella dependency injection, chiamato da un
endpoint o da un lavoro in background), con le avvertenze di §10.2.

1. Copiare `SadpDiscovery.vb` (o tradurlo in C#: è un modulo con `QueryAsync`, `QueryUnicastAsync`, `ParseHello`,
   `FormatBanner`). Dipende solo da `MacVendorLookup.Normalize` (normalizzazione del MAC) e `BannerGrabber.Clean`
   (taglio e pulizia di una stringa): si sostituiscono con due funzioni equivalenti.
2. Aggiungere al proprio modello di host un campo per i dati SADP e uno per gli altri IPv4.
3. Nel motore di scansione: avviare la sonda multicast insieme al ping, applicare i risultati come in §1.6, mandare
   l'unicast agli host fuori sottorete, aggiungere le porte extra, il banner SDK e il produttore di ripiego.
4. Classificazione: le regole di §3 (prefissi, regex, prudenza, canali, firmware). Se il programma ha già un
   classificatore, la regola Hikvision va **prima** di quella generica delle telecamere.
5. Avvisi (§4) e RTSP (§5) dove si mostrano i dettagli di un dispositivo.
6. Opzionali, ma pronti: banner HTTPS con certificato (§6), più sottoreti (§7), fusione per MAC (§8), connettore TCP
   con `SIO_TCP_INITIAL_RTO` (§9).
7. Portare le prove: XML di §1.2 e gli esempi di §3.2 come casi attesi.

Serve un firewall che lasci entrare UDP sulla porta scelta (il programma SADP ufficiale chiede la stessa cosa): con il
profilo «rete privata» di Windows di solito basta la regola che Windows propone al primo ascolto.

### 10.2 Applicazione web: cosa cambia

Dal browser non si aprono socket UDP: la sonda va **nel servizio lato server** (.NET, Node, Python…), esposta come
un'operazione («cerca dispositivi») che restituisce JSON con i campi di §1.2 più tipo riconosciuto, avvisi e indirizzi
RTSP. Accorgimenti:

- il server deve stare **nella stessa rete di livello 2** dei dispositivi per il multicast; se gira in un container,
  serve la rete dell'host (`network_mode: host`) o l'unicast verso indirizzi noti (§1.4), che funziona ovunque ci sia
  instradamento;
- la ricerca dura 1-2 s: va bene una richiesta normale con il risultato completo, oppure eventi lato server se si vuole
  mostrare i dispositivi man mano;
- su più schede del server si itera come in §1.3; con più sottoreti si accetta la grammatica di §7 e si manda l'unicast;
- per il modello senza SADP si usa la GET ISAPI di §2 dal server (è HTTP, la fa chiunque);
- l'eco e i pacchetti non XML vanno scartati in silenzio (§1.5); nessuna credenziale viene mai inviata.

### 10.3 Checklist sul campo

- Una telecamera appena collegata (non attivata, `192.168.1.64`) compare come «fuori sottorete» con il modello.
- Una telecamera con cavo e Wi-Fi attivi è **una riga**, `+1 IP`, con il banner SDK su entrambe.
- Un NVR mostra il numero di canali e `Firmware V…`; se la build ha più di cinque anni compare l'avviso.
- Dietro una VPN o su una VLAN instradata il modello arriva lo stesso (unicast); la porta 53 «aperta ovunque» sparisce.
- Un host chiamato `nvr-casa` di un'altra marca **non** diventa Hikvision.
