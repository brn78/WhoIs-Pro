# Novità delle versioni

Setup e pacchetto portabile di ogni versione sono nella pagina [Releases](https://github.com/brn78/WhoIs-Pro/releases).
Il programma installato con il setup si aggiorna da solo: *Panoramica → Aggiornamenti → Scarica e installa*.

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
