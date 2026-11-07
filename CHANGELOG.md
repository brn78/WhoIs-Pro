# Novità delle versioni

Setup e pacchetto portabile di ogni versione sono nella pagina [Releases](https://github.com/brn78/WhoIs-Pro/releases).
Il programma installato con il setup si aggiorna da solo: *Impostazioni → Aggiornamenti → Scarica e installa*
(fino alla 2.3: *Panoramica → Aggiornamenti*).
Su GitHub restano solo le ultime due release: le novità di tutte le versioni sono qui.

## 2.8.2 · 7 novembre 2026

- **Dispositivi: la lista resta dov'era** quando si apre la scheda di un dispositivo. Prima, mentre la scheda si
  caricava, la pagina veniva ridisegnata per intero e l'elenco tornava in cima: ora la scheda si sostituisce sul posto
  e, nei casi in cui la pagina si ridisegna, l'elenco riprende dalla stessa posizione.
- **Selezione multipla nell'archivio**: in *Dispositivi* il pulsante **Seleziona** accanto al conteggio fa spuntare più
  dispositivi (anche **Tutti** quelli visibili, per esempio dopo una ricerca o con «solo presenti»); la barra sopra
  l'elenco dà a tutti insieme la stessa fiducia (**Affidabile**, **Sospetto**, **Da classificare**) oppure li
  **dimentica**, con conferma. **Fine** chiude la selezione e si torna ad aprire le schede con un clic.
- **Selezione multipla anche nella pagina Rete**, in Elenco e in Schede: **Seleziona** accanto a «Svuota», spunta sulle
  righe, **Tutti** (gli host visibili con ricerca e filtri) e **Nessuno**; poi **Riscansiona** i selezionati (quattro
  alla volta, un solo avviso alla fine con quanti rispondono) oppure **Rimuovi dalla sessione**, con conferma.
- **Intervallo DHCP della rete**, facoltativo: in *Dispositivi*, accanto a «Rinomina», **DHCP** chiede il primo e
  l'ultimo indirizzo che il router assegna da solo (`192.168.1.100-192.168.1.199`, o in breve `192.168.1.100-199`), per
  chi lo conosce perché ha configurato lui il router. Da lì in poi, nell'archivio e nella pagina *Rete* (Elenco e
  Schede), accanto all'indirizzo compare **DHCP** se sta nell'intervallo e **fisso** se sta fuori, e nella pagina Rete
  si cerca anche scrivendo `dhcp` o `fisso`. Si toglie salvando il campo vuoto.
- Il pulsante del tema (luna / sole) della 2.8.1 passa **in alto a destra**, accanto a *Rete del PC* e *Impostazioni*,
  insieme agli altri comandi sempre a portata di mano.

## 2.8.1 · 7 novembre 2026

- **Righe alternate** in tutte le tabelle (Rete in Elenco, Porte, DNS, Traceroute, Connessioni, Pacchetti, scansioni
  dell'archivio) e nelle liste *Dispositivi* e *Cambiamenti*: una riga sì e una no ha un fondo appena più scuro, così
  l'occhio segue la riga nelle tabelle larghe. La tinta è più tenue di quella del passaggio del mouse e delle righe
  selezionate, nel tema chiaro e in quello scuro.
- **Tema con un clic**: in alto a sinistra, prima del titolo della pagina, un pulsante con la luna o il sole passa al
  tema scuro o chiaro e salva la scelta, la stessa di *Impostazioni → Generale → Tema*, dove resta anche «Automatico»
  che segue Windows.
- **Menu laterale chiaro con il tema chiaro**: la barra laterale non resta grafite ma prende una tinta chiara in
  armonia con il resto della finestra, con testi scuri e lo stesso verde acqua per la voce attiva e la versione; con
  il tema scuro resta com'era.
- **Un PC Windows con Hyper-V non è più «Host Hyper-V»**: la porta 2179 (VMConnect) è aperta anche su un Windows 10
  o 11 Pro con Hyper-V, WSL2 o Docker Desktop. Con un nome da postazione (DESKTOP-…, LAPTOP-…), o senza segni di
  server, il tipo è «PC Windows con Hyper-V» (categoria Postazione); questa macchina si riconosce dall'edizione di
  Windows installata. «Host Hyper-V» resta per i server (nome da server, WinRM, oppure RDP con SMB e RPC).

## 2.8.0 · 7 novembre 2026

- **Dispositivi Hikvision riconosciuti per modello**: durante la scansione, insieme a ping e ARP, parte la sonda **SADP**
  di Hikvision (multicast `239.255.255.250:37020`, senza credenziali), che riporta modello esatto, versione del firmware,
  numero di serie, porte web e SDK, canali e stato di attivazione. Per gli host di altre sottoreti o in VPN, dove il
  multicast non passa e il MAC non si vede, la stessa domanda va in unicast (UDP 37020); il modello si legge anche dal
  realm ISAPI della pagina web (`/ISAPI/System/deviceInfo`). Nel tipo si distinguono telecamere IP, PTZ, termiche e
  lettura targhe, NVR con il numero di canali, DVR, posti esterni e monitor videocitofonici, terminali e centraline di
  controllo accessi, centrali d'allarme AX PRO e switch PoE; il firmware compare in *Sistema stimato*, il riepilogo SDK
  sulla porta `8000`. Un Hikvision collegato al cavo ma con l'indirizzo fuori sottorete (quello di fabbrica, per esempio)
  è segnalato nell'avanzamento. Un nome come «nvr-sala» su un apparecchio di un'altra marca non basta a dirlo Hikvision.
- **Da verificare, anche per Hikvision**: nella scheda del dispositivo (*Dispositivi*) sono segnalati i dispositivi
  Hikvision **non attivati** (senza password) e quelli con **firmware datato** (build di cinque anni fa o più). Nel menu
  dell'host e in *Apri con…*, per telecamere e NVR Hikvision, *Copia indirizzo RTSP* del flusso principale (`101`) e di
  quello secondario (`102`).
- **Più sottoreti in una scansione**: in *Rete o intervallo* si scrivono più reti o intervalli separati da virgola
  (`192.168.1.0/24, 192.168.2.0/24`, fino a 4.096 indirizzi in tutto), anche nelle forme brevi `192.168.2` e
  `192.168.2.0`. Con host di più sottoreti, nelle pagine *Rete* e *Dispositivi* compare il selettore **Sottorete** per
  vederne una sola, e l'elenco si raggruppa per sottorete. Sulle reti raggiunte in VPN la porta TCP `53` «aperta» su
  ogni host, risposta del DNS del tunnel, viene scartata.
- **Banner HTTPS con il certificato** sulle porte `443` e `8443`: anche il soggetto del certificato TLS (nome e
  organizzazione), che fa riconoscere switch e controller Ubiquiti (EdgeSwitch, CloudKey, UniFi), inverter fotovoltaici
  (Fronius, SMA, SolarEdge), controller Vantage, interfacce clima Daikin e apparati Modbus TCP (porta `502`, ora tra le
  porte comuni). Le pagine di accesso vuote che rimandano via script (Hikvision e altri) mostrano il rinvio invece del
  titolo vuoto; una porta che non parla HTTP non lascia più byte casuali nel banner.
- **Un solo dispositivo con più IPv4**: più indirizzi sullo stesso MAC (telecamera con cavo e Wi-Fi, PC o router con più
  indirizzi sulla scheda) sono una riga sola, con l'etichetta `+1 IP` e gli altri indirizzi in *Altri IPv4* nel
  dettaglio, nella pagina *Rete* e nell'archivio *Dispositivi*; i ripetitori Wi-Fi in MAC-NAT e i proxy ARP, con molti
  indirizzi dietro un solo MAC, restano righe separate. Nell'archivio le vecchie righe degli indirizzi secondari passano
  nel principale con nome, note e storia, senza falsi «cambio di IP» o «assente».
- **Più veloce**: porte TCP chiuse riconosciute all'istante anche nella pagina *Porte* (lo stesso connettore e lo stesso
  lettore di banner della pagina *Rete*), produttore e MAC letti senza espressioni regolari (normalizzazione del MAC due
  volte più rapida), una sola lettura dell'ultima scansione per ogni registrazione in archivio invece di una per riga, meno
  scritture a metà scansione. Sulla stessa /24 di prova (52 host, porte e banner) la scansione dura come prima, tra 9 e
  12 secondi a seconda del momento, e legge più banner (35-38 invece di 28); la riscansione di un singolo host costa
  circa mezzo secondo in più per il tentativo HTTPS, che ha un tetto di mezzo secondo sulla stretta di mano TLS.
- Per chi vuole rifare le stesse cose in un altro programma: [docs/guida-rilevamento-hikvision.md](docs/guida-rilevamento-hikvision.md).

## 2.7.0 · 31 ottobre 2026

- **Un archivio per ogni rete**, per chi gira tra i clienti: la rete si riconosce dal MAC del router, non dalla sottorete
  (la stessa 192.168.1.0/24 esiste in mille sedi); senza un router raggiungibile vale la sottorete. Nella pagina
  *Dispositivi* un selettore mostra la rete in cui ti trovi («qui») e le altre visitate, più «Tutte le reti»; a ogni rete
  puoi dare un nome («Cliente Rossi», «Casa») e «Dimentica la rete» toglie una sede con i suoi dispositivi. I confronti
  valgono dentro la rete: il NAS del cliente non diventa «assente» quando sei a casa. La prima scansione automatica in
  una rete mai vista parte subito.
- **Sessione salvata da sola, una per rete**: pochi secondi dopo ogni modifica, in
  `Documenti\WhoIs Pro\Sessioni\<nome della rete>.whoispro`. All'avvio si riapre la sessione della rete in cui si trova
  il PC; quando ti colleghi a un'altra rete un avviso propone la sua sessione (o una nuova), e quella di prima resta
  salvata. Il riquadro in alto dice «Salvata da sola alle HH:MM»; «Salva con nome», «Apri» e la cartella delle sessioni
  restano nel menu. Si spegne in *Impostazioni → Generale → Sessione* (torna il salvataggio a mano di prima).
- L'archivio della 2.6.0 si aggiorna da solo: i dispositivi già conosciuti, compresi quelli importati dal vecchio
  registro, restano divisi per sottorete e passano alla rete giusta appena una scansione li ritrova.

## 2.6.0 · 31 ottobre 2026

- **Archivio della rete nel tempo** (nuova pagina *Dispositivi*): il registro dei dispositivi diventa un archivio
  SQLite (`archivio-rete.db`) che ogni scansione aggiorna, con chiave il MAC (un cambio di IP resta lo stesso
  dispositivo); nome, note, icona e colore dati ai dispositivi restano gli stessi di prima (`dispositivi.json` viene
  importato al primo avvio). Tre schede: *Dispositivi* (ricerca, «solo presenti», scheda con **alias**, **fiducia**
  da classificare / affidabile / sospetto, **note**, porte aperte, storia, «Dimentica»), *Cambiamenti* (nuovo,
  ricomparso, cambiato, assente, con «Segna tutto come visto») e *Scansioni* (storico con durata e differenze). Un
  contatore «da vedere» sulla voce del menu.
- **Confronto tra scansioni**: nuovo, ricomparso, cambiato (`IP a → b`, `nome x → y`, `porte aperte 22,80`, `porte
  chiuse 445`), assente solo dopo **due scansioni consecutive** senza risposta (i dispositivi Wi-Fi in risparmio
  energetico non oscillano). Si confrontano solo le porte provate in quella scansione: cambiare gruppo di porte non
  «chiude» le altre. La prima scansione crea l'archivio senza avvisi; alla fine di ogni scansione dalla pagina *Rete*
  l'esito riporta le differenze, con «Cambiamenti».
- **Scansioni automatiche e avvisi** (*Impostazioni → Archivio della rete*): intervallo in minuti, rete da
  scansionare, timeout per prova, avviso per i dispositivi nuovi. Le schedine in basso a destra (livello attenzione,
  45 s; al massimo quattro visibili; un solo avviso aggregato oltre tre dispositivi nuovi; lo stesso dispositivo non
  avvisa due volte in mezz'ora) hanno «Apri la scheda»; un dispositivo segnato sospetto che ricompare avvisa;
  cambiamenti e assenze restano nello storico.
- **Motore di scansione nuovo** anche per la pagina *Rete*: ping e ARP in parallelo (tabella dei vicini e SendARP
  dalla scheda LAN, così le VPN non deviano le richieste), host presenti solo in ARP, **nomi NetBIOS** (UDP 137, con
  gruppo di lavoro) oltre al DNS inverso; con *Porte degli host trovati* le porte TCP del gruppo scelto su ogni host,
  nella stessa scansione (non più una coda a parte), con porte chiuse istantanee (SIO_TCP_INITIAL_RTO) e **banner**
  HTTP (Server e titolo), SSH, FTP, posta e RTSP. Una /24 con 50 host, nomi, 20 porte e banner in meno di 10 secondi.
  Gli host compaiono subito e si arricchiscono fase per fase; riscansione del singolo host (ora anche con le porte),
  host «non risponde», *Ripeti* e IPv6 come prima.
- **Classificazione rifatta**: router, firewall, hypervisor, stampanti (anche 3D), NAS, telecamere e NVR, centrali
  d'allarme, automazione e PLC, console, TV e lettori, assistenti vocali, domotica, UPS, VoIP, Apple, Raspberry Pi,
  telefoni, ruoli dei server (controller di dominio, database, posta, Docker…), Windows e Linux, macchine virtuali dal
  MAC; stima del sistema dopo la classificazione (un modulo ESP con TTL 128 è «firmware embedded», non Windows).
  Categorie: Rete, Server, Virtualizzazione, Postazione, Storage, Stampante, Telecamera, Centrale d'allarme, Smart TV,
  Domotica, Mobile, Console, Altro.
- **Porte sensibili in evidenza** nella scheda del dispositivo: Telnet, FTP, SMB, RDP, VNC, WinRM, Docker e Redis
  esposti hanno il chip «da verificare».

## 2.5.4 · 17 ottobre 2026

- **Profilo GitHub nel piè di pagina**: accanto al copyright il logo di GitHub con *brn78*; un clic apre il profilo
  dell'autore nel browser.

## 2.5.3 · 17 ottobre 2026

- **Panoramica più ordinata sui PC con tante schede**: le schede non collegate (modem mobili «Cellulare», switch
  virtuali, Wi-Fi spento…) stanno dietro un solo pulsante «N non collegate» che le mostra o le nasconde; le schede
  collegate restano sempre a vista. Con una o due schede non collegate si vedono direttamente, come prima.

## 2.5.2 · 17 ottobre 2026

- **Database dei produttori aggiornato da solo**, come il programma: all'avvio, con Internet (se manca, appena torna),
  quando non viene dall'IEEE o ha più di un giorno, scarica i registri dell'IEEE (qualche MB) e riclassifica gli host
  della sessione; nella barra in alto compare «Aggiornamento in corso» e alla fine un avviso con quanti produttori e
  host riclassificati. Si spegne in *Impostazioni → Produttori*; *Aggiorna dall'IEEE* resta per farlo a mano.

## 2.5.1 · 17 ottobre 2026

- **Rete: filtro Tutti / Online / Non rispondono** accanto alla ricerca, con quanti sono per ciascuno (i numeri
  seguono la scansione): si vede subito chi risponde e chi no. Si combina con ricerca e ordinamento e resta scelto al
  prossimo avvio.
- **Porte: host come nella pagina Rete**: icona del dispositivo (quella scelta o quella del tipo), nome e indirizzo
  sotto; anche il menu *Host* mostra il nome e la ricerca trova le porte per nome del dispositivo.

## 2.5.0 · 17 ottobre 2026

- **Nome, note e icona dei dispositivi**: nel pannello di un dispositivo (pagina Rete) gli dai un nome («Router di
  casa»), delle note e un'icona con il suo colore. Restano legati al MAC nel registro dei dispositivi di questo PC:
  li ritrovi in ogni scansione e sessione, anche se il DHCP cambia l'indirizzo. Il nome compare in tabella e nelle
  schede, e la ricerca trova anche nomi e note.
- **7.188 icone Material Design Icons** incluse nel programma (funzionano senza Internet): la finestra di scelta cerca
  in italiano o in inglese («telecamera» trova la telecamera), ha le categorie e le raccolte per rete, sicurezza,
  energia, clima, edifici e attrezzi, icone piene o a contorno, le ultime usate e 16 colori più uno a scelta.
- **Un'icona per ogni tipo di dispositivo**: router, access point, telecamere, NAS, stampanti, TV, Chromecast,
  altoparlanti, Shelly, Raspberry Pi, PC, server… ognuno con la sua icona nel colore della categoria.
- **Riconosciuti dal produttore** i dispositivi Google (Nest, Chromecast, Google TV), Sonos, Roku, NVIDIA Shield e i
  telefoni Android di Huawei, Oppo, Vivo, OnePlus, Motorola, Realme e Nokia, che prima finivano in «Linux / Unix».
- **Scansione della rete all'avvio** (*Impostazioni → Generale*): con un ritardo a scelta, un avviso arancione nella
  barra in alto conta i secondi e permette di avviarla subito o di annullarla; poi si apre la pagina Rete.
- **Aggiornamento automatico**: il controllo delle versioni parte a ogni avvio (non più una volta al giorno) e, se ce
  n'è una nuova, il programma installato la scarica, ne verifica l'impronta SHA-256 e la installa chiudendosi e
  riaprendosi da solo. Si spegne in *Impostazioni → Aggiornamenti*; non scatta con dati non salvati o con una
  scansione in corso.
- **Panoramica**: compaiono tutte le schede di rete del PC, anche quelle non collegate (il Wi-Fi spento, il cavo
  staccato), in grigio; sotto il titolo della sessione c'è il primo appunto delle note (o il file); in alto il nome
  della sessione senza l'estensione e il riquadro Internet con la stessa forma di quello della sessione.
- **Menu dell'icona nell'area di notifica**: con la finestra aperta la prima voce è *Riduci nell'area di notifica*.
- **✕ nei campi di testo** per svuotarli con un clic; **frecce, PagSu/PagGiù, Inizio e Fine** scorrono gli elenchi
  (host, porte, connessioni…), che prima non rispondevano ai tasti.
- **WHOIS a tutta larghezza**: le ricerche della sessione sono una riga sotto il modulo, riepilogo e risposta
  completa affiancati. Anche le schede Aggiornamenti, Produttori e Dati delle Impostazioni usano tutta la larghezza.
- **Prestazioni**: durante il test latenza e download già misurati restano a vista mentre si misura l'upload.
- Rete: cambiare *Ripeti* mentre aspetta la prossima scansione vale subito, e *Una volta* la annulla.
- Il «?» delle spiegazioni è centrato sul testo; la barra dei messaggi si ritira verso destra, dalla parte dove
  stanno.

## 2.4.1 · 16 ottobre 2026

- **Riapre l'ultima sessione**: all'avvio il programma riapre l'ultima sessione salvata o aperta, se il file c'è
  ancora; altrimenti parte da una sessione nuova e lo dice. *Nuova sessione* fa ripartire da zero anche al prossimo
  avvio.
- **Profili di rete nell'icona dell'area di notifica**: il menu dell'icona elenca i profili, con la spunta su quello
  che la scheda ha adesso; un clic lo applica (Windows chiede il consenso) e l'esito arriva con l'avviso di Windows.
  C'è anche *Torna a DHCP* per le schede con un indirizzo statico, e *Crea un profilo…* se non ce n'è ancora nessuno.
- **Database dei produttori più chiaro** (*Impostazioni → Produttori*): origine (registri dell'IEEE o Wireshark,
  scaricato o incluso nel programma), data dell'ultimo aggiornamento e da quanti giorni, blocchi MA-L, MA-M e MA-S,
  file e dimensione; un avviso quando ha più di 30 giorni.
- **L'elenco resta dov'è**: riscansionando un host (o quando arrivano porte e aggiornamenti) le tabelle di Rete,
  Porte, Connessioni, DNS e Traceroute non tornano più in cima; la riga riscansionata si illumina per un attimo.
- **Pacchetti** (nuova pagina, sniffer): cattura con *pktmon*, lo strumento di Windows 10 e 11, senza driver da
  installare; Windows chiede il consenso una volta per cattura e il programma resta senza diritti di amministratore.
  Durata a scelta (fino a 10 minuti o «Ferma»), filtro facoltativo per indirizzo o rete, porta e protocollo. I
  pacchetti sono spiegati riga per riga: ARP, IPv4 e IPv6, TCP con i flag, UDP, ICMP e ICMPv6, domande e risposte DNS
  (anche mDNS e LLMNR), richieste HTTP e il nome del sito delle connessioni HTTPS (TLS), QUIC, DHCP, SSDP, LLDP. Con
  statistiche, protocolli, chi parla di più, ricerca e filtro; la cattura si salva in .pcapng e si apre in Wireshark.
- **Rete: Visualizza Elenco o Schede**, come in Esplora risorse. *Schede* descrive ogni dispositivo per intero su una
  riga senza colonne: nome e tipo, indirizzo, categoria e sistema, MAC e produttore, risposta e TTL, porte aperte con
  il servizio, IPv6, prima rilevazione e ultima volta visto, con le etichette Nuovo, Non risponde e IPv6. *Elenco* è
  la tabella di prima. Accanto alla ricerca *Ordina per* (indirizzo, nome, tipo, produttore, latenza, porte aperte,
  prima rilevazione, ultima volta visto) e il verso; vista e ordinamento restano al prossimo avvio.
- **Prestazioni della rete** (nuova pagina): un test di circa 20 secondi misura latenza, jitter e pacchetti persi verso
  il router e verso Internet, la risposta del DNS e la velocità di download e upload (server di prova di Cloudflare,
  gli stessi di speed.cloudflare.com, su più connessioni insieme). Ne esce un giudizio in parole semplici (ottima,
  buona, discreta, scarsa, con il punto debole) e cosa la linea regge: navigazione, streaming 4K, videochiamate,
  gioco online, lavoro da remoto e telecamere viste da fuori. I test restano nella sessione.
- **Servizi esterni solo con Internet**: WHOIS, aggiornamento del database dei produttori, controllo e scaricamento
  degli aggiornamenti e test delle prestazioni si fermano subito con un messaggio chiaro se Internet non c'è; il
  controllo automatico degli aggiornamenti aspetta che la connessione torni. La prova IPv6 di Internet parte solo
  se il PC ha un indirizzo IPv6 pubblico.
- **IPv6**: indirizzi IPv6 delle schede del PC (globali, temporanei, locali e link-local), gateway e DNS IPv6 nella
  Panoramica, in *Rete del PC* e nelle Impostazioni; nel riquadro Internet si vede se funziona anche IPv6. Alla fine
  di ogni scansione gli host ricevono i loro indirizzi IPv6 dalla tabella dei vicini di Windows (per MAC): etichetta
  *v6* nella tabella, elenco nel dettaglio, e la ricerca li trova.
- **Ping e nslookup nella Panoramica**: un campo con *Ping* (quattro richieste con tempo e TTL, minimo, media,
  massimo e perse) e *nslookup* (record A, AAAA, PTR, MX, NS e TXT, con il server DNS che ha risposto); Invio li fa
  insieme; scorciatoie per il gateway, 1.1.1.1 e google.com. Non finiscono nella sessione.
- Tabella degli host su una riga sola e con caratteri un po' più piccoli: il tipo di dispositivo non va più a capo,
  le porte aperte sono nella seconda riga del dispositivo, la latenza è il numero colorato.
- DNS: i record TXT non erano letti (nslookup li scrive sulla riga dopo); per un indirizzo IP non si cercano più MX,
  NS e TXT, che non esistono.
- Dopo *Aggiorna dall'IEEE* il riquadro «Aggiornamento in corso» nella barra superiore restava acceso: ora si spegne
  alla fine. La riclassificazione degli host dopo l'aggiornamento tiene conto delle porte aperte.

## 2.4.0 · 16 ottobre 2026

- **Grafica nuova, solo di WhoIs Pro**: barra laterale grafite anche nel tema chiaro, accento verde acqua, LED quadrati
  come quelli delle porte di rete, il carattere tecnico di Windows (Bahnschrift) per titoli e numeri, etichette in
  tondo invece che in maiuscolo, angoli più netti. Non è più la stessa grafica delle console web.
- **Panoramica rifatta**: il traffico dal vivo di ogni scheda collegata (ricezione e invio, traccia degli ultimi tre
  minuti, LED che lampeggia quando passano dati), indirizzo, gateway, DNS e MAC; host nella rete con i nuovi delle
  ultime 24 ore, porte aperte, risposta di Internet e ultima scansione; le ultime scansioni con esito, durata e
  «Ripeti»; processore, memoria e disco; dispositivi per tipo e ultimi arrivati.
- **Storico delle scansioni nella sessione**: rete, porte e traceroute, con ora, durata ed esito (finisce nel file).
- **Impostazioni in alto**: l'ingranaggio nella barra superiore apre le Impostazioni, divise in Generale, Rete del PC,
  Aggiornamenti, Produttori e Dati del programma. Aggiornamenti e database dei produttori sono passati qui dalla
  Panoramica.
- **Rete del PC**: l'icona accanto all'ingranaggio apre un pannello con tutte le schede (IP, maschera, gateway, DNS,
  MAC, DHCP o statico), i profili da applicare al volo e *Torna a DHCP*.
- **Dettagli della sessione** (titolo, note, file, impronta, date) in un pannello, dal menu della sessione o dalla
  Panoramica.
- **Colonne larghe quanto il contenuto**: nelle tabelle niente più nomi, produttori e DNS tagliati con i puntini; se la
  finestra è stretta la tabella scorre in orizzontale e la colonna delle azioni resta sempre in vista.
- **Riscansiona un host**: il pulsante su ogni riga della pagina Rete (e nel menu e nel dettaglio) rifà ARP, ping, nome
  e tipo di quel solo indirizzo, anche mentre gira la scansione della rete. Se non risponde più resta nella sessione
  segnato *non risponde*.
- **Porte degli host trovati**: un'opzione della scansione della rete manda ogni host, appena risponde, alla
  scansione delle porte (un gruppo a scelta), cinque host alla volta, mentre la rete continua. Le porte aperte
  aiutano anche a riconoscere il tipo di dispositivo.
- **Ripeti**: la scansione della rete riparte da sola ogni 1, 5, 15, 30 o 60 minuti; la riga di riepilogo dice
  quando parte la prossima e *Non ripetere* la ferma.
- **Data di rilevamento e dispositivi nuovi**: la colonna *Rilevato* nella tabella degli host, con *Nuovo* nelle
  prime 24 ore. Un registro dei dispositivi conosciuti (per MAC, in `%LOCALAPPDATA%\WhoIsPro\dispositivi.json`)
  tiene la prima rilevazione anche tra una sessione e l'altra e segnala chi si collega per la prima volta, anche con
  l'avviso di Windows quando la finestra è nell'area di notifica. Il registro si svuota da *Impostazioni → Dati del
  programma*.
- **Chi non risponde più** dopo una scansione completa della sua rete resta in elenco, segnato *non risponde*.
- Tabella degli host più compatta: MAC sotto il produttore, TTL sotto la latenza, nomi lunghi a capo; nuova colonna
  *Porte* con le porte aperte di ogni host.
- Una scansione della rete fermata a metà dice quanti host aveva già trovato, invece di zero.
- Immagini del README e banner rifatti con la nuova grafica.

## 2.3.0 · 16 ottobre 2026

- **Database dei produttori dall'IEEE**: *Aggiorna dall'IEEE* scarica i tre registri ufficiali (blocchi MA-L, MA-M e
  MA-S), aggiornati ogni giorno; se l'IEEE non risponde usa come prima il database di Wireshark. Il file si legge per
  colonne (tab), così nessun nome viene spezzato.
- **MAC privato**: telefoni, tablet e PC recenti in Wi-Fi usano un indirizzo casuale; invece di «Produttore non
  catalogato» la tabella scrive *MAC privato*, con la spiegazione, e il dispositivo è proposto come telefono o tablet.
  Le macchine virtuali QEMU/KVM e i contenitori Docker, che usano lo stesso tipo di indirizzo, restano riconosciuti.
- **Più marche riconosciute**: telecamere e videoregistratori (Hikvision, Dahua, Axis, Hanwha, Uniview, Reolink, EZVIZ…
  e la porta 37777), la nuova categoria **Centrale d'allarme** (Paradox, Inim, Ksenia, Ajax, Risco, Elkron,
  Tecnoalarm, Urmet…), altre stampanti (Lexmark, Kyocera, Ricoh, Xerox, Konica Minolta…), NAS (WD, Buffalo, Asustor,
  TerraMaster), apparati di rete (Ruckus, Fortinet, Tenda, Cambium) e automazione (Siemens, Schneider, ABB, KNX,
  Loxone, BTicino, Vimar e Modbus sulla porta 502). Le marche si cercano a parole intere: Minimax non diventa una
  centrale Inim.

## 2.2.1 · 16 ottobre 2026

- **Istanza singola**: aprire di nuovo il programma porta in primo piano la finestra già aperta (anche se era
  nell'area di notifica) invece di aprirne una seconda.
- Moduli di scansione e ricerca con una regola unica: il primo campo prende lo spazio che resta, gli interruttori
  quello del loro testo e il pulsante di avvio è ancorato al bordo destro, staccato dai campi.
- Campo di ricerca delle tabelle più largo.

## 2.2.0 · 16 ottobre 2026

- **Barra superiore** con icona e titolo della pagina al posto del percorso «WhoIs Pro › Pagina»; nelle pagine resta
  solo la riga del sottotitolo, con più spazio per i dati.
- **Rete e Porte**: a scansione finita il modulo si ripiega in una riga di riepilogo (esito, intervallo o host, thread,
  timeout, DNS o banner) con *Modifica* e *Riavvia*; durante la scansione la stessa riga mostra avanzamento, contatori
  e *Ferma*. Il modulo completo resta aperto finché non c'è stata nessuna scansione.
- **Tabella degli host**: produttore senza le forme giuridiche (GmbH, Co., Ltd., Inc., S.p.A.…) e troncato con i
  puntini, nome completo nel tooltip; sistema stimato nella seconda riga del dispositivo; TTL accanto alla latenza, che
  ha una barretta colorata (verde sotto i 10 ms, ambra sotto i 100, rossa oltre). Tutte le colonne stanno in una
  finestra da 1366 px, senza scorrimento orizzontale.
- **Panoramica**: le spiegazioni lunghe (database di Wireshark, controllo giornaliero su GitHub, salvataggio atomico)
  sono un'icona «?» con il testo al passaggio del mouse; metadati della sessione in griglia; *Salva con nome…* ed
  *Esporta* nel piede del riquadro; in *Aggiornamenti* versione in uso e ultima pubblicata su una riga, novità
  ripiegate dietro un collegamento.
- Le **notifiche** compaiono in basso a destra e non coprono più la sessione; *Salva* in alto compare solo quando ci
  sono modifiche da salvare.
- Nelle tabelle delle **schede di rete** (Panoramica, Computer, Impostazioni) le azioni stanno in un menu ⋯ in una
  colonna fissa a destra: niente più scorrimento orizzontale.
- Con la variabile d'ambiente `WHOISPRO_DATA_FOLDER` il programma usa un'altra cartella dei dati (per provare una
  copia di sviluppo accanto a quella installata).

## 2.1.2 · 16 ottobre 2026

- **Menu con il tasto destro** sulle righe di host, porte, connessioni e salti del traceroute: oltre a porte, WHOIS e
  traceroute, *Apri nel browser*, *Cartelle condivise* (Esplora risorse), *Desktop remoto*, *Terminale SSH*, *Ping
  continuo*; sulle porte 21, 22, 23, 139/445 e 3389 la voce giusta (FTP, SSH, Telnet, cartelle, RDP). Anche dal
  pulsante «Apri con…» nel dettaglio dell'host.
- Il riquadro della scansione nella barra superiore non sfarfalla più: si aggiorna sul posto.
- Modulo Porte: interruttori su una riga e pulsante Avvia allineato a destra.

## 2.1.1 · 16 ottobre 2026

- **Porte seriali (COM)** nella pagina *Computer*: quelle presenti ora, con il dispositivo e il collegamento (USB,
  Bluetooth, scheda madre).
- «Controlla ora» non resta più in attesa dopo il controllo.
- Le novità della versione nel riquadro *Aggiornamenti* sono un elenco leggibile, non più testo grezzo.

## 2.1.0 · 16 ottobre 2026

- **Impostazioni** (menu *Sistema*): avvio con Windows, **icona nell'area di notifica** (la X può nascondere la
  finestra invece di chiudere; all'avvio automatico resta solo l'icona), **tema** automatico, chiaro o scuro.
- **Profili di rete**: l'indirizzo IP di una scheda salvato con un nome (il sito, il cliente): DHCP oppure IP,
  maschera, gateway e DNS. «Applica» lo scrive nella scheda con netsh e Windows chiede il consenso (UAC), così il
  programma resta senza diritti di amministratore. «Salva come profilo» dalla configurazione attuale, «Torna a DHCP»,
  e il menu *Profilo* accanto a ogni scheda nella Panoramica.
- **Computer** (menu *Sistema*): processore, memoria e dischi con i **grafici a ciambella**, aggiornati ogni due
  secondi; Windows, nome del PC, utente, tempo di accensione, scheda video, schede di rete attive.
- **Barra superiore**: la scansione in corso si vede in alto, con l'avanzamento (clic per andare alla pagina), e
  accanto alla sessione c'è lo stato della **connessione a Internet** (controllo ogni 30 secondi, avviso quando cade
  o torna). In fondo alla finestra versione e diritti d'autore.
- Router Keenetic, Zyxel, Technicolor, Sagemcom e altri riconosciuti come apparati di rete.

## 2.0.1 · 16 ottobre 2026

- Prima versione distribuita con **Scarica e installa**: chi ha la 2.0.0 installata la riceve dalla *Panoramica*.
- Versione del programma senza l'hash del commit nelle proprietà del file.

## 2.0.0 · 16 ottobre 2026

Prima versione pubblica, con l'interfaccia rifatta da capo.

- **Nuova interfaccia**: una pagina web dentro la finestra (WebView2) con barra laterale, tabelle ordinabili, pannelli
  di dettaglio, menu e notifiche; tema chiaro o scuro come Windows.
- **Panoramica**: conteggi della sessione, file e salvataggio, database dei produttori, schede di rete con «Scansiona».
- **Rete**: host attivi di una rete o di un intervallo (ARP e ping) con MAC, produttore, nome DNS, latenza, TTL e tipo
  di dispositivo; dettaglio dell'host con le sue porte; dal menu della riga scansione delle porte, WHOIS e traceroute.
- **Porte**: TCP e UDP, gruppi di porte pronti o elenco libero, banner ripulito dai byte binari.
- **WHOIS**: riepilogo dei campi (registrar, organizzazione, date, name server, stato) e risposta completa; cronologia
  delle ricerche della sessione.
- **DNS**: record A, AAAA, PTR, MX, NS e TXT con filtro per tipo.
- **Traceroute**: tempi veri anche sui salti intermedi; più destinazioni nella stessa sessione.
- **Connessioni**: connessioni TCP e porte in ascolto con il processo, aggiornamento a intervalli, chiusura del processo.
- **Sessioni** `.whoispro` con salvataggio atomico (SHA-256 e copia .bak), esportazione HTML, CSV e testo; alla
  chiusura il programma chiede se salvare.
- **Aggiornamenti**: controllo giornaliero delle release su GitHub e «Scarica e installa» con verifica di dimensione,
  impronta SHA-256 e versione del setup.
- Setup per Windows (senza diritti di amministratore) e pacchetto portabile, entrambi con il runtime .NET incluso.
