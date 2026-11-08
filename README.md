<p align="center">
  <img src="docs/immagini/banner.png" alt="WhoIs Pro: chi c'è nella tua rete, quali porte sono aperte, a chi appartiene un dominio, quanto traffico passa" width="100%">
</p>

<p align="center">
  <a href="https://github.com/brn78/WhoIs-Pro/releases/latest"><img alt="Ultima versione" src="https://img.shields.io/github/v/release/brn78/WhoIs-Pro?label=versione&color=2563eb"></a>
  <img alt="Windows 10 e 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white">
  <img alt="Gratis" src="https://img.shields.io/badge/gratis-per%20tutti-16A34A">
  <img alt="Interfaccia in italiano" src="https://img.shields.io/badge/lingua-italiano-009246">
  <img alt="VB.NET e .NET 10" src="https://img.shields.io/badge/VB.NET-.NET%2010-512BD4">
</p>

<p align="center">
  <a href="https://github.com/brn78/WhoIs-Pro/releases/latest"><b>⬇️ Scarica il setup</b></a>
  &nbsp;·&nbsp;
  <a href="#-cosa-fa"><b>✨ Cosa fa</b></a>
  &nbsp;·&nbsp;
  <a href="#-aggiornamenti-al-volo"><b>🔄 Aggiornamenti</b></a>
  &nbsp;·&nbsp;
  <a href="#-domande-frequenti"><b>❓ Domande frequenti</b></a>
</p>

---

**WhoIs Pro** è il programma per Windows che risponde alle domande di chi gestisce una rete, piccola o grande:
*chi c'è nella mia rete?*, *quali porte sono aperte su quel dispositivo?*, *a chi appartiene questo dominio o questo
indirizzo?*, *che strada fanno i pacchetti?*, *quale programma ha aperto questa connessione?* Scansione della rete con
produttore e tipo di dispositivo, porte TCP e UDP con banner, WHOIS, DNS, traceroute e connessioni attive, tutto in
una finestra sola, con le sessioni salvate in un file e i report da esportare.

Gratis, senza account e senza cloud: i dati restano sul tuo PC.

<p align="center">
  <img src="docs/immagini/rete.png" alt="Scansione della rete: host trovati con produttore, nome e tipo di dispositivo" width="100%">
</p>

## ✨ Cosa fa

<table>
<tr>
<td width="50%" valign="top">

**📡 Scansione della rete**<br>
Una o più reti (`192.168.1.0/24, 192.168.30.0/24`), un intervallo (`192.168.1.1-50`) o un indirizzo: gli host rispondono
ad ARP, ping e sonda **SADP Hikvision** (multicast e unicast) e compaiono man mano, poi arrivano nomi **DNS e NetBIOS**,
le **porte** con i **banner** dei servizi (anche certificati TLS e ISAPI) e il **tipo di dispositivo** riconosciuto: router,
switch, NAS, Home Assistant, smart TV, telefoni (anche con il MAC privato), telecamere/NVR/videocitofoni/controllo accessi
Hikvision con modello e firmware, centrali d'allarme, inverter fotovoltaici, stampanti, moduli domotici, automazione, server
e macchine virtuali. Produttore dai registri IEEE (MA-L, MA-M e MA-S, oltre 58.000 prefissi, il più preciso per ogni MAC);
più IPv4 con lo stesso MAC vengono uniti in un solo dispositivo (`+1 IP`), e con più sottoreti sono disponibili filtro e
raggruppamento per VLAN. In **Elenco** (tabella) o in **Schede** che descrivono ogni dispositivo per intero, con *Ordina per*
accanto alla ricerca; un singolo host si **riscansiona** con un clic; con **Ripeti** la scansione riparte da sola. Una /24
intera, con porte e banner, in una decina di secondi.

**🗂️ Archivio della rete nel tempo, una rete per cliente**<br>
Ogni scansione aggiorna l'archivio dei **dispositivi conosciuti** della rete in cui ti trovi: la rete si riconosce dal
**MAC del router** (la stessa 192.168.1.0/24 esiste in mille sedi), i dispositivi dal MAC (un cambio di IP resta lo
stesso dispositivo). Nella pagina *Dispositivi* scegli la rete («qui» quella attuale, poi le altre visitate, o tutte),
le dai un **nome** («Cliente Rossi», «Casa») e vedi i **cambiamenti** di quella sede: nuovo, ricomparso, cambiato,
assente. A ogni dispositivo dai **nome**, **icona**, **fiducia** (da classificare, affidabile, sospetto) e **note**,
anche a più dispositivi insieme con **Seleziona**; la scheda, nel pannello laterale ridimensionabile, mostra porte,
storia, i servizi esposti **da verificare** e a cosa è **collegato**. La scheda **Topologia** disegna la rete ad albero
(Internet → router → switch → dispositivi) oppure, a scelta per ogni rete, la **componi a mano** su una tela vuota
trascinando router, dispositivi e apparati dall'elenco a lato: aggiungi la nuvola Internet (con l'ultima prova della
linea), gli switch e gli access point che la scansione non vede, collega i dispositivi trascinandoli o con la spunta nella
scheda di ogni nodo; fermo con il mouse su un'icona vedi la scheda breve. **Scansioni automatiche** a intervalli e
**avvisi** in basso a destra quando compare un dispositivo nuovo o ne ricompare uno sospetto. La **sessione si salva
da sola**, una per rete, e si riapre quando torni in quella rete.

**🔌 Porte TCP e UDP**<br>
Gruppi di porte pronti (le 20 o le 100 più comuni, web, database, posta, accesso remoto, domotica) o elenco libero
con intervalli; servizio riconosciuto e **banner** letto dal server. Da un host trovato si parte con un clic.

**🌐 WHOIS e DNS**<br>
Registrar, organizzazione, date e name server di un dominio o di un indirizzo, con i referral ai registrar e il
ripiego RDAP; record A, AAAA, PTR, MX, NS e TXT. Le ricerche restano nella sessione.

</td>
<td width="50%" valign="top">

**🛣️ Traceroute**<br>
Il percorso verso una destinazione, salto per salto, con tre tempi per salto e il nome di ogni router; più
destinazioni nella stessa sessione.

**📈 Connessioni attive**<br>
Connessioni TCP e porte in ascolto del PC con il **processo** che le ha aperte, filtri per stato, aggiornamento a
intervalli, scansione o WHOIS dell'indirizzo remoto e chiusura del processo.

**🔬 Pacchetti**<br>
Cattura dei pacchetti con *pktmon* di Windows (niente driver, un consenso per cattura): ogni pacchetto spiegato in
italiano (DNS, HTTP, nome del sito HTTPS, ARP, ping…), protocolli, chi parla di più, ricerca; salvataggio in
.pcapng per Wireshark.

**⚡ Prestazioni della rete**<br>
Un test di 20 secondi: latenza, jitter e pacchetti persi verso il router e verso Internet, risposta del DNS, velocità
di download e upload. Il giudizio è in parole semplici, con cosa regge la linea: streaming 4K, videochiamate, gioco
online, lavoro da remoto, telecamere viste da fuori.

**💾 Sessioni e report**<br>
Tutto quello che trovi finisce nella sessione, che **si salva da sola**: un file `.whoispro` per ogni rete in
`Documenti\WhoIs Pro\Sessioni` (salvataggio atomico, SHA-256 e copia `.bak`), riaperto quando torni in quella rete;
esportazione in **HTML**, **CSV** per Excel o testo.

**🔀 Profili di rete**<br>
L'indirizzo IP di una scheda salvato con un nome (il sito, il cliente): DHCP oppure IP, maschera, gateway e DNS.
**Applica** lo scrive nella scheda in un clic; Windows chiede il consenso, il programma non ha bisogno di diritti di
amministratore.

**💻 Computer**<br>
Processore, memoria e dischi con i **grafici a ciambella** aggiornati ogni due secondi; Windows, nome del PC,
utente, tempo di accensione, scheda video, schede di rete e **porte seriali (COM)** con il dispositivo collegato.

**📊 Panoramica dal vivo**<br>
Il **traffico** di ogni scheda collegata (ricezione e invio, gli ultimi tre minuti, il LED che lampeggia quando
passano dati), indirizzo, gateway e DNS; gli host trovati con i **nuovi** delle ultime 24 ore, le porte aperte, la
risposta di Internet (anche IPv6), le **ultime scansioni** da ripetere con un clic, processore, memoria e disco; un
campo per **ping e nslookup** al volo.

**🎨 Un'interfaccia da tecnico**<br>
Barra laterale grafite (chiara con il tema chiaro), LED quadrati come quelli delle porte di rete, numeri grandi e
leggibili; tabelle ordinabili con le colonne **larghe quanto il contenuto** e le righe alternate, pannelli di dettaglio,
menu e notifiche; **tema** automatico, chiaro o scuro, con il cambio al volo in alto a destra; **Impostazioni** e
**Rete del PC** sempre in alto; **icona nell'area di notifica** e avvio con Windows;
scorciatoie da tastiera. Si aggiorna da sola quando esce una versione nuova.

</td>
</tr>
</table>

## 🖼️ Uno sguardo al programma

<table>
<tr>
<td width="50%" align="center"><img src="docs/immagini/panoramica.png" alt="Panoramica con i conteggi della sessione"><br><b>Panoramica</b>: traffico dal vivo delle schede, rete trovata, ultime scansioni e sistema</td>
<td width="50%" align="center"><img src="docs/immagini/dettaglio-host.png" alt="Dettaglio di un host"><br><b>Dettaglio dell'host</b>: stato, porte, date di rilevamento e «Riscansiona»</td>
</tr>
<tr>
<td align="center"><img src="docs/immagini/porte.png" alt="Scansione delle porte"><br><b>Porte</b>: TCP e UDP, servizio e banner</td>
<td align="center"><img src="docs/immagini/whois.png" alt="WHOIS"><br><b>WHOIS</b>: riepilogo dei campi e risposta completa</td>
</tr>
<tr>
<td align="center"><img src="docs/immagini/traceroute.png" alt="Traceroute"><br><b>Traceroute</b>: salti e tempi di risposta</td>
<td align="center"><img src="docs/immagini/connessioni.png" alt="Connessioni attive"><br><b>Connessioni</b>: chi parla con chi, e con quale processo</td>
</tr>
<tr>
<td align="center"><img src="docs/immagini/computer.png" alt="Computer: processore, memoria e dischi"><br><b>Computer</b>: processore, memoria e dischi a colpo d'occhio</td>
<td align="center"><img src="docs/immagini/impostazioni.png" alt="Impostazioni: schede di rete del PC e profili di rete"><br><b>Impostazioni</b>: rete del PC e profili di rete, avvio, tema, aggiornamenti</td>
</tr>
<tr>
<td align="center"><img src="docs/immagini/dns.png" alt="DNS"><br><b>DNS</b>: record filtrati per tipo</td>
<td align="center"><img src="docs/immagini/panoramica-chiaro.png" alt="Panoramica con il tema chiaro"><br><b>Tema</b> automatico, chiaro o scuro</td>
</tr>
</table>

## ⬇️ Download e installazione

Dalla pagina [Releases](https://github.com/brn78/WhoIs-Pro/releases/latest):

- **`WhoIsPro-<versione>-Setup.exe`**: il setup. Installa per l'utente corrente, **senza diritti di amministratore**
  (oppure per tutti gli utenti, a scelta), crea il collegamento nel menu Start e si aggiorna da solo.
- **`WhoIsPro-<versione>-win-x64.zip`**: il pacchetto portabile. Si estrae dove si vuole e si avvia `WhoIsPro.exe`.

Entrambi contengono il runtime .NET: non serve installare altro. Serve **Windows 10 (1809) o 11 a 64 bit** con
Microsoft Edge WebView2 Runtime, già presente in Windows.

## 🔄 Aggiornamenti al volo

Il programma installato con il setup controlla **a ogni avvio** (o con *Controlla ora*), se c'è Internet, qual è
l'ultima versione pubblicata qui su GitHub. Con l'**aggiornamento automatico** (attivo di serie, *Impostazioni →
Aggiornamenti*) la versione nuova si scarica dalla release, se ne controllano **dimensione, impronta SHA-256 e
versione**, il programma si chiude, si aggiorna e si riapre da solo; non succede se ci sono dati non salvati o una
scansione in corso. Spento l'aggiornamento automatico, la versione nuova si annuncia con un avviso e un pallino
sull'ingranaggio in alto, e **Scarica e installa** fa lo stesso con un clic. La richiesta a GitHub non porta nessun
dato. Chi usa il pacchetto portabile riceve solo l'avviso con il collegamento alla pagina dei download.

## ⌨️ Scorciatoie

| Tasti | Cosa fanno |
| --- | --- |
| <kbd>Ctrl</kbd> + <kbd>N</kbd> · <kbd>Ctrl</kbd> + <kbd>O</kbd> · <kbd>Ctrl</kbd> + <kbd>S</kbd> | Nuova sessione, apri, salva (<kbd>Ctrl</kbd> + <kbd>Maiusc</kbd> + <kbd>S</kbd> salva con nome) |
| <kbd>Invio</kbd> | Avvia la scansione o la ricerca della pagina |
| <kbd>Esc</kbd> | Chiude pannelli, menu e finestre di dialogo |
| Clic sull'intestazione | Ordina la tabella (indirizzi IP e numeri in ordine vero) |

## ❓ Domande frequenti

<details>
<summary><b>Servono i diritti di amministratore?</b></summary>
<br>
No. Il setup installa per l'utente corrente e tutte le funzioni (scansioni, WHOIS, DNS, traceroute, connessioni)
lavorano con un utente normale. <i>Termina il processo</i> chiude solo i processi dell'utente.
</details>

<details>
<summary><b>Perché alcuni host non hanno il MAC o il produttore?</b></summary>
<br>
Il MAC si legge con ARP, che funziona solo nella rete locale: per gli host dietro un router (altre reti, Internet) il
MAC non è disponibile. Telefoni, tablet e PC recenti in Wi-Fi usano un <b>MAC privato</b> (casuale, diverso per ogni
rete): il produttore non si può sapere e la tabella lo dice. Il database dei produttori si aggiorna da solo all'avvio,
con Internet, quando ha più di un giorno; si può aggiornare anche a mano da
<i>Impostazioni → Produttori → Aggiorna dall'IEEE</i>.
</details>

<details>
<summary><b>Quanto è affidabile il tipo di dispositivo?</b></summary>
<br>
È una stima da produttore del MAC, nomi DNS e NetBIOS, TTL, porte aperte e banner: Home Assistant, Shelly, Samsung,
Apple, FRITZ!Box, NAS, stampanti, telecamere, controller di dominio e così via si riconoscono bene, un PC con una
scheda di rete generica resta "Computer". Con le <i>Porte comuni</i> attive la stima migliora molto. Nell'archivio puoi
dare un alias e correggere l'idea che il programma si è fatto.
</details>

<details>
<summary><b>Come funzionano l'archivio e gli avvisi?</b></summary>
<br>
Ogni scansione (dalla pagina <i>Rete</i>, da <i>Dispositivi → Scansiona adesso</i> o automatica) aggiorna l'archivio
in <code>%LOCALAPPDATA%\WhoIsPro\archivio-rete.db</code> e lo confronta con la precedente <b>della stessa rete</b>:
ogni rete (il router, riconosciuto dal MAC; senza router la sottorete) ha i suoi dispositivi, i suoi cambiamenti e le
sue scansioni, così il NAS di un cliente non diventa «assente» quando sei a casa. La prima scansione di una rete crea
il suo archivio senza avvisi. Un dispositivo è <b>assente</b> solo dopo due scansioni consecutive senza risposta, così i
telefoni e i sensori Wi-Fi in risparmio energetico non oscillano; si confrontano solo le porte provate in quella
scansione. In <i>Impostazioni → Archivio della rete</i> accendi le scansioni automatiche (intervallo, rete, timeout) e
l'avviso per i dispositivi nuovi: se sono più di tre arriva un solo avviso aggregato, e lo stesso dispositivo non avvisa
due volte in mezz'ora. <i>Dimentica</i> toglie un dispositivo dall'archivio (se ricompare torna «nuovo»);
<i>Dimentica la rete</i> toglie una sede intera. I dispositivi del vecchio registro, o di una scansione senza il MAC
del router, restano divisi per sottorete e passano alla rete giusta appena una scansione li ritrova.
</details>

<details>
<summary><b>Come cambio l'indirizzo IP della scheda per un sito?</b></summary>
<br>
In <i>Impostazioni → Rete del PC</i> crei un profilo (o lo salvi dalla configurazione attuale della scheda):
nome, scheda, DHCP oppure IP, maschera, gateway e DNS. Poi <b>Applica</b>, anche dal pannello <i>Rete del PC</i>
(l'icona delle schede in alto a destra): Windows chiede il consenso (UAC) perché l'indirizzo lo scrive <code>netsh</code> con i
diritti di amministratore, il programma no. <i>Torna a DHCP</i> rimette la scheda in automatico.
</details>

<details>
<summary><b>Posso lasciarlo sempre aperto?</b></summary>
<br>
Sì: in <i>Impostazioni</i> attivi l'icona nell'area di notifica e, se vuoi, l'avvio con Windows. La X nasconde la
finestra accanto all'orologio; dal menu dell'icona si riapre, si chiude davvero o si applica al volo un
<b>profilo di rete</b> (con la spunta su quello attivo). All'avvio il programma riapre la sessione della rete in cui si
trova il PC (quella salvata da sola); con il salvataggio automatico spento, l'ultima sessione salvata.
</details>

<details>
<summary><b>Dove finiscono i dati?</b></summary>
<br>
Le sessioni in <code>Documenti\WhoIs Pro\Sessioni</code>, un file <code>.whoispro</code> (JSON, con la copia
<code>.bak</code> accanto) per ogni rete, salvato da solo pochi secondi dopo ogni modifica; con <i>Salva con nome</i>
dove vuoi tu, e in <i>Impostazioni → Generale → Sessione</i> puoi tornare al salvataggio a mano. In
<code>%LOCALAPPDATA%\WhoIsPro</code> restano le impostazioni e i profili di rete, l'archivio della rete
(<code>archivio-rete.db</code>, SQLite: dispositivi conosciuti con nome, icona, fiducia e note, scansioni e
cambiamenti; si svuota da <i>Impostazioni → Archivio della rete</i>), il database dei produttori aggiornato, la
posizione della finestra e i dati di WebView2. Il programma non contatta nessun server, a parte la pagina di prova di
Windows (msftconnecttest.com) per sapere se Internet c'è, GitHub per gli aggiornamenti e, a richiesta, l'IEEE (o
Wireshark) per il database dei produttori, i server WHOIS, RDAP e DNS che interroghi e i server di prova di Cloudflare
per il test delle prestazioni. Senza Internet questi servizi non vengono nemmeno chiamati.
</details>

<details>
<summary><b>Posso scansionare una rete che non è la mia?</b></summary>
<br>
Scansiona solo reti e dispositivi che gestisci o per cui hai l'autorizzazione: una scansione delle porte su sistemi
altrui può essere vista come un tentativo di intrusione.
</details>

## 🛠️ Per chi vuole compilarlo

Programma in **VB.NET** (.NET 10, Windows Forms) con la finestra fatta da una pagina HTML dentro WebView2, con una
grafica sua: barra laterale grafite (chiara nel tema chiaro), accento verde acqua, LED delle porte e Bahnschrift per
titoli e numeri.

```
WhoIs Pro.vbproj        progetto (la cartella ui\ è incorporata nell'exe)
Program.vb              avvio
MainWindow.vb           finestra: WebView2, pagina servita da https://whoispro.invalid, tema, posizione, chiusura
AppBridge.vb            comandi della pagina: scansioni, WHOIS, DNS, traceroute, netstat, sessione, archivio, aggiornamenti
NetworkWatchService.vb  scansioni periodiche e a richiesta, una alla volta; registra nell'archivio e produce gli avvisi
Core\Models             HostInfo, PortInfo, WhoisResult, DnsRecordInfo, TracerouteHop, ConnectionInfo, ScanRecord, ScanSession
Core\Network\Scan       DiscoveryScanner (ping, ARP, NetBIOS, porte, banner, fasi), NetBiosResolver, BannerGrabber, interop
Core\Network            PortScanner, WhoisClient, DnsEngine, TracerouteEngine, NetstatInspector, Ipv6, PacketCapture, PerfTester
Core\Inventory          InventoryStore (SQLite) e NetworkInventory: dispositivi nel tempo, scansioni, cambiamenti
Core\Storage            AtomicFileManager (salvataggio atomico), SessionExporter (HTML, CSV, testo),
                        DeviceRegistry (nome, note e icona dei dispositivi: la stessa tabella dell'archivio)
Core\Utils              NetworkUtils, MacVendorLookup (registri IEEE / manuf di Wireshark), DeviceClassifier, PortDictionary, IconCatalog
Core\Update             UpdateService: release su GitHub, scaricamento e verifica del setup
Core\Config             AppSettings: impostazioni (impostazioni.json), avvio automatico, profili di rete
Core\Machine            NetworkConfig (IP delle schede, netsh elevato), ComputerInfo (CPU, memoria, dischi, sistema),
                        TrafficMonitor (traffico delle schede per la Panoramica)
ui\                     index.html, app.css, app.js, core\ (bridge, store, ui, table, chart…), pages\ (una per pagina)
Scripts\                Publish.ps1 (zip portabile), Build-Setup.ps1 (Inno Setup), Publish-Release.ps1 (release su GitHub)
Setup\WhoIsPro.iss      programma di installazione
Tests\                  prove NUnit dei motori in Core\
```

```
dotnet build "WhoIs Pro.vbproj" -c Release
dotnet test Tests\Tests.vbproj
.\Scripts\Build-Setup.ps1        # setup e zip in artifacts\ (serve Inno Setup)
.\dev\serve.ps1                  # anteprima dell'interfaccia nel browser, con dati finti (http://localhost:5517/)
```

La cartella `dev\` non finisce nel programma: `preview.html` carica la pagina vera di `ui\` con `mock.js`, che risponde
ai comandi al posto del programma con una rete dimostrativa. Serve per lavorare sulla grafica senza compilare.

La pagina chiede le operazioni al programma con `postMessage({ id, cmd, args })` e riceve `{ id, ok, result | error }`;
le scansioni in corso mandano eventi (`network.progress`, `ports.progress`, `trace.hop`, `…done`) accorpati ogni
120 ms. Con `WHOISPRO_DEVTOOLS=1` (o nella versione Debug) si apre DevTools con F12. Le novità di ogni versione sono nel
[CHANGELOG](CHANGELOG.md); i componenti e i dati di terze parti, con le loro licenze, in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), che il setup copia accanto al programma. Il riconoscimento dei
dispositivi Hikvision (sonda SADP, realm ISAPI, famiglie di modelli), i banner HTTPS con il certificato, la scansione di
più sottoreti e la fusione degli host con lo stesso MAC sono descritti, con i dettagli di protocollo e le regole, in
[docs/guida-rilevamento-hikvision.md](docs/guida-rilevamento-hikvision.md).

## 📬 Contatti

Segnalazioni e idee nelle [Issues](https://github.com/brn78/WhoIs-Pro/issues), oppure **b.leonardi78@gmail.com**.

---

<p align="center">
  <sub>© 2026 Bruno Leonardi · Tutti i diritti riservati.<br>
  Le immagini mostrano una rete dimostrativa.</sub>
</p>
