# Novità delle versioni

Setup e pacchetto portabile di ogni versione sono nella pagina [Releases](https://github.com/brn78/WhoIs-Pro/releases).
Il programma installato con il setup si aggiorna da solo: *Impostazioni → Aggiornamenti → Scarica e installa*
(fino alla 2.3: *Panoramica → Aggiornamenti*).
Su GitHub restano solo le ultime due release: le novità di tutte le versioni sono qui.

## 2.4.1 · in lavorazione

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
