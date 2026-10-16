# Novità delle versioni

Setup e pacchetto portabile di ogni versione sono nella pagina [Releases](https://github.com/brn78/WhoIs-Pro/releases).
Il programma installato con il setup si aggiorna da solo: *Panoramica → Aggiornamenti → Scarica e installa*.

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
