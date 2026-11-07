// Scansione della rete: host attivi con MAC, produttore, nomi (DNS e NetBIOS), porte e tipo di dispositivo. La scansione
// la fa il motore DiscoveryScanner (Core\Network\Scan) tramite il servizio di sorveglianza: alla fine aggiorna anche
// l'archivio della rete (pagina Dispositivi) e qui arrivano le differenze.
import { call, on } from '../core/bridge.js';
import { $, esc, fmtMs, fmtNumber, fmtSeconds, fmtDateTime, matchesAll, plural, isIp, subnetOf, countSubnets, renderBySubnet, shortVendor } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { catIcon } from '../core/categories.js';
import { S, definePage, adopt, addScan, markDirty, bus, emit } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { replaceKeepingScroll, sortRows, table, nextSort } from '../core/table.js';
import { closeOverlays, confirmDialog, emptyState, openDrawer, openMenu, pageHead, toast, toastError } from '../core/ui.js';
import { pickIcon } from '../core/iconpicker.js';

S.net = {
  target: '', threads: 128, timeout: 700, resolveDns: true,
  repeat: 0,           // minuti tra una scansione e la successiva (0 = una volta sola)
  autoPorts: true,     // porte TCP degli host trovati, nella stessa scansione (servono anche a riconoscere il dispositivo)
  autoPreset: 'TOP20', // gruppo di porte per autoPorts
  q: '', subnet: '', sort: { key: 'ipAddress', dir: 1 },
  progress: null, done: null, fresh: new Set(),
  rescanning: new Set(), // indirizzi riscansionati uno per uno, in corso
  nextAt: 0, repeatTimer: 0,
  editing: false,      // modulo riaperto con «Modifica» dopo una scansione
  last: null,          // parametri dell'ultima scansione avviata (per la riga di riepilogo)
};

// le opzioni della scansione restano al prossimo avvio (solo comodità: senza memoria locale si riparte dai predefiniti)
const OPTIONS = ['threads', 'timeout', 'resolveDns', 'repeat', 'autoPorts', 'autoPreset'];
try { Object.assign(S.net, JSON.parse(localStorage.getItem('net-options') || '{}')); } catch { /* senza memoria locale */ }
function saveOptions() {
  try { localStorage.setItem('net-options', JSON.stringify(Object.fromEntries(OPTIONS.map((k) => [k, S.net[k]])))); } catch { /* senza memoria locale */ }
}

const REPEATS = [[0, 'Una volta'], [1, 'Ogni minuto'], [5, 'Ogni 5 minuti'], [15, 'Ogni 15 minuti'], [30, 'Ogni 30 minuti'], [60, 'Ogni ora']];
const repeatLabel = (m) => (REPEATS.find((r) => r[0] === m) || [0, `Ogni ${m} minuti`])[1].toLowerCase();
// tutte le 65535 porte su ogni host della rete sarebbero troppe: per gli host trovati solo i gruppi
const portPresets = () => ((S.st && S.st.presets && S.st.presets.ports) || []).filter((p) => p.key !== 'ALL');
const autoPreset = () => portPresets().find((p) => p.key === S.net.autoPreset) || portPresets()[0] || { key: '', name: '', ports: '' };

const running = () => !!(S.st && S.st.running.network);
const hhmm = (t) => new Date(t).toLocaleTimeString('it-IT', { hour: '2-digit', minute: '2-digit' });

// ---------------------------------------------------------------------------------------------------------------------
// Modulo di avvio: completo finché non c'è stata nessuna scansione, poi ripiegato in una riga di riepilogo
// ---------------------------------------------------------------------------------------------------------------------
function formCard() {
  const n = S.net;
  if (running() || (n.done && !n.editing)) return `<section class="card scan-summary" id="scan-card">${summaryInner()}</section>`;
  const list = (S.st.adapters || []).filter((a) => a.cidr).map((a) => `<option value="${esc(a.cidr)}">${esc(a.name)} · ${esc(a.ip)}</option>`).join('');
  if (!n.target) n.target = (S.st.adapters.find((a) => a.cidr) || {}).cidr || '192.168.1.0/24';
  return `<section class="card" id="scan-card"><div class="card-body">
    <form data-form="scan" class="scan-form" autocomplete="off">
      <div class="field"><label for="f-target">Rete o intervallo</label>
        <div class="input-with-button">
          <input class="input input-mono" id="f-target" data-field="target" list="adapter-list" value="${esc(n.target)}" placeholder="192.168.1.0/24, 192.168.30.0/24 · 192.168.1.1-50 · 10.0.0.5" title="Una o più sottoreti o intervalli separati da virgola (es. 192.168.1.0/24, 192.168.2.0/24)">
          <datalist id="adapter-list">${list}</datalist>
          <button type="button" class="btn btn-icon" data-action="detect-adapters" title="Rileva di nuovo le schede di rete">${icon('refresh')}</button>
        </div></div>
      <div class="field"><label for="f-threads">Prove insieme</label><input class="input" id="f-threads" type="number" min="1" max="512" data-field="threads" value="${n.threads}" title="Ping, ARP e connessioni TCP contemporanee"></div>
      <div class="field"><label for="f-timeout">Timeout (ms)</label><input class="input" id="f-timeout" type="number" min="100" max="10000" step="100" data-field="timeout" value="${n.timeout}" title="Per ogni prova: più alto su Wi-Fi lenti o con dispositivi in risparmio energetico"></div>
      <div class="field field-select"><label for="f-repeat">Ripeti</label>
        <select class="select" id="f-repeat" data-field="repeat">${REPEATS.map(([m, label]) => `<option value="${m}" ${n.repeat === m ? 'selected' : ''}>${label}</option>`).join('')}</select></div>
      <div class="field field-auto"><span class="field-label">Opzioni</span><div class="field-switches">
        <label class="switch" title="DNS inverso e NetBIOS (UDP 137)"><input type="checkbox" data-field="resolveDns" ${n.resolveDns ? 'checked' : ''}><span class="track"></span><span>Nomi (DNS e NetBIOS)</span></label>
        <label class="switch" title="Porte TCP del gruppo scelto su ogni host trovato, con i banner dei servizi, nella stessa scansione: servono anche a riconoscere il dispositivo"><input type="checkbox" data-field="autoPorts" ${n.autoPorts ? 'checked' : ''}><span class="track"></span><span>Porte degli host trovati</span></label>
        <select class="select input-sm auto-preset" data-field="autoPreset" ${n.autoPorts ? '' : 'disabled'} title="Porte da controllare su ogni host trovato">${portPresets().map((p) => `<option value="${esc(p.key)}" ${p.key === autoPreset().key ? 'selected' : ''}>${esc(p.name)}</option>`).join('')}</select></div></div>
      <div class="field field-end"><span class="field-label">&nbsp;</span><div class="field-actions">
        ${n.done ? `<button type="button" class="btn" data-action="scan-collapse" title="Torna al riepilogo dell'ultima scansione">${icon('close')} Annulla</button>` : ''}
        <button type="submit" class="btn btn-primary">${icon('play')} Avvia</button>
      </div></div>
    </form>
  </div></section>`;
}

function runningStats() {
  const p = S.net.progress;
  if (!p) return 'avvio…';
  const total = p.total ? `${fmtNumber(p.scanned)} / ${fmtNumber(p.total)} · ` : '';
  return `${esc(p.phaseLabel || '')} · ${total}${plural(S.data.hosts.length, 'host', 'host')} · ${fmtSeconds(p.seconds)}${p.currentIp ? ` · ${esc(p.currentIp)}` : ''}`;
}

/** Ripetizione: ogni quanto e quando parte la prossima. */
function repeatChip() {
  const n = S.net;
  if (!n.repeat) return '';
  const next = n.nextAt && !running() ? ` · prossima alle ${hhmm(n.nextAt)}` : '';
  return `<span class="chip" title="Scansione ripetuta da sola: i dispositivi mai visti prima vengono segnalati">${icon('rotate')} ${esc(repeatLabel(n.repeat))}${next}</span>`;
}

/** Contenuto della riga di riepilogo: avanzamento e «Ferma» durante la scansione, esito e parametri dopo. */
function summaryInner() {
  const n = S.net;
  if (running()) {
    const pct = n.progress ? n.progress.percent : 0;
    return `<span class="scan-state is-busy"><span class="spinner"></span>In corso · <span data-pct>${pct}%</span></span><span class="vsep"></span>
      <span class="meter"><span data-meter style="width:${pct}%"></span></span>
      <span class="scan-stat mono" data-stats>${runningStats()}</span>
      <span class="scan-actions"><button type="button" class="btn btn-danger" data-action="scan-stop">${icon('stop')} Ferma</button></span>`;
  }
  const d = n.done;
  const p = n.last || n;
  const text = d.error ? `Interrotta per un errore: ${esc(d.error)}`
    : d.cancelled ? `Fermata dopo ${fmtSeconds(d.seconds)} · ${plural(d.found, 'host attivo', 'host attivi')}`
      : `Completata in ${fmtSeconds(d.seconds)} · ${plural(d.found, 'host attivo', 'host attivi')}`;
  const tone = d.error ? 'danger' : d.cancelled ? 'warning' : 'success';
  return `<span class="scan-state ${tone}"><span class="dot"></span>${text}</span><span class="vsep"></span>
    <span class="chips">
      <span class="chip" title="Rete o intervallo"><b>${esc(p.target)}</b></span>
      <span class="chip" title="Prove in parallelo e timeout per prova">${fmtNumber(p.threads)} insieme · ${fmtNumber(p.timeout)} ms</span>
      ${p.resolveDns ? `<span class="chip" title="DNS inverso e NetBIOS">${icon('check')} Nomi</span>` : '<span class="chip muted">Senza nomi</span>'}
      ${p.autoPorts ? `<span class="chip" title="Porte TCP e banner dei servizi sugli host trovati">${icon('check')} Porte</span>` : '<span class="chip muted">Senza porte</span>'}
      ${repeatChip()}
    </span>
    <span class="scan-actions">
      ${n.repeat && n.nextAt ? `<button type="button" class="btn" data-action="repeat-stop" title="La scansione non si ripete più da sola">${icon('close')} Non ripetere</button>` : ''}
      <button type="button" class="btn" data-action="scan-edit">${icon('edit')} Modifica</button>
      <button type="button" class="btn btn-primary" data-action="scan-restart">${icon('play')} Riavvia</button>
    </span>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Tabella degli host
// ---------------------------------------------------------------------------------------------------------------------
/** Filtro per stato: tutti, solo quelli che rispondono, solo quelli che non rispondono. */
const STATUS_FILTERS = [['all', 'Tutti', ''], ['online', 'Online', 'is-on'], ['offline', 'Non rispondono', 'is-off']];
const byStatus = (h, s) => s === 'online' ? !isOffline(h) : s === 'offline' ? isOffline(h) : true;

/** Elenco delle sottoreti /24 presenti nella sessione, ordinate per indirizzo con il conteggio degli host. */
function subnetsInSession() {
  return countSubnets(S.data.hosts, (h) => h.ipAddress);
}

function activeSubnetFilter() {
  const sub = S.net.subnet || '';
  if (!sub) return '';
  return S.data.hosts.some((h) => subnetOf(h.ipAddress) === sub) ? sub : '';
}

function filteredHosts() {
  const q = S.net.q.trim();
  const status = S.net.status || 'all';
  const sub = activeSubnetFilter();
  return S.data.hosts.filter((h) => (!sub || subnetOf(h.ipAddress) === sub)
    && byStatus(h, status)
    && matchesAll(q, [h.customName, h.notes, h.ipAddress, subnetOf(h.ipAddress), h.hostname, h.netBiosName, h.workgroup, h.macAddress, h.macVendor, h.deviceType, h.deviceCategory, h.osGuess, ...(h.ipv6 || []), ...(h.otherIpv4 || [])]));
}

/** Nome del dispositivo da mostrare: NetBIOS, poi DNS. */
const hostName = (h) => h.netBiosName || h.hostname || '';
/** Tutti i nomi conosciuti, per i tooltip. */
const allNames = (h) => [h.netBiosName, h.hostname, h.workgroup].filter(Boolean).join(' · ');

/** MAC privato (casuale): telefoni, tablet e PC recenti cambiano indirizzo per ogni rete Wi-Fi. */
const PRIVATE_MAC = 'MAC privato (casuale)';
const PRIVATE_MAC_TIP = 'Indirizzo casuale per la privacy: telefoni, tablet e PC recenti ne usano uno diverso per ogni rete Wi-Fi, quindi il produttore non si può sapere. Lo stesso dispositivo può comparire con MAC diversi.';

/** Il produttore nella tabella: forma breve, nome completo nel tooltip; il MAC privato spiegato. */
function vendorCell(h) {
  if (h.macVendor === PRIVATE_MAC) return `<span class="trunc w-vendor muted" title="${esc(PRIVATE_MAC_TIP)}">MAC privato</span>`;
  return `<span class="trunc w-vendor" title="${esc(h.macVendor)}">${esc(shortVendor(h.macVendor))}</span>`;
}

const OS_SHORT = { 'Linux / Android / iOS / macOS': 'Linux / Unix', 'Windows (PC / Server)': 'Windows', 'Cisco / Router / Network Device': 'Apparato di rete', 'Linux/Unix o dispositivo': 'Linux / Unix', 'Sconosciuto': '' };
const osShort = (g) => (g in OS_SHORT ? OS_SHORT[g] : g || '');
/** Latenza oppure «solo ARP» per chi non risponde al ping ma è nella tabella ARP. */
const latencyOrArp = (h) => (h.respondsToPing === false ? '<span class="faint" title="Non risponde al ping: trovato nella tabella ARP">solo ARP</span>' : latency(h.responseTimeMs));

/** Latenza con la barretta colorata: verde sotto i 10 ms, ambra sotto i 100, rossa oltre. */
function latency(ms) {
  const n = Number(ms) || 0;
  const tone = n < 10 ? '' : n < 100 ? 'warning' : 'danger';
  const w = Math.min(100, Math.round(Math.log10(n + 1) / 3 * 100 + 8));
  return `<span class="lat ${tone}"><i style="--w:${w}%"></i><span class="mono">${fmtMs(n)}</span></span>`;
}

const columns = [
  { key: 'deviceType', label: 'Dispositivo', cell: (h) => {
    const nPorts = openPorts(h);
    return `<div class="cell-main">${catIcon(h)}<div class="cell-text">
      <b class="trunc" title="${esc(h.customName ? `${h.customName} · ${h.deviceType}` : h.deviceType)}">${h.customName ? `<span class="custom-name">${esc(h.customName)}</span>` : esc(h.deviceType)}</b>
      <small class="trunc" title="${esc(h.deviceCategory || 'Altro')}${h.osGuess ? ` · ${esc(h.osGuess)}` : ''}">${isOffline(h) ? '<span class="offline-tag">non risponde</span> · ' : ''}${esc(h.deviceCategory || 'Altro')}${osShort(h.osGuess) ? ` · ${esc(osShort(h.osGuess))}` : ''}${h.isGateway ? ' · gateway' : ''}${h.isLocalMachine ? ' · questo PC' : ''}${nPorts ? ` · <b class="ports-count">${plural(nPorts, 'porta', 'porte')}</b>` : ''}</small></div></div>`;
  } },
  { key: 'ipAddress', label: 'Indirizzo IP', cls: 'mono strong-cell', cell: (h) => `${esc(h.ipAddress)}${otherIpsTag(h)}${(h.ipv6 || []).length ? ` <span class="tag v6" title="IPv6: ${esc(h.ipv6.join(', '))}">v6</span>` : ''}` },
  { key: 'hostname', label: 'Nome', value: hostName, cell: (h) => hostName(h) ? `<span class="trunc w-name" title="${esc(allNames(h))}">${esc(hostName(h))}</span>` : '<span class="faint">—</span>' },
  { key: 'macVendor', label: 'Produttore e MAC', cell: (h) => `<div class="cell-text">${vendorCell(h).replace('<span', '<b').replace(/<\/span>$/, '</b>')}<small class="mono">${h.macAddress ? esc(h.macAddress) : 'senza MAC'}</small></div>` },
  { key: 'responseTimeMs', label: 'Latenza', cls: 'num', cell: (h) => `<div class="cell-text cell-num">${latencyOrArp(h)}<small>${h.ttl ? `TTL ${h.ttl}` : 'TTL —'}</small></div>` },
  { key: 'firstSeen', label: 'Rilevato', value: (h) => new Date(h.firstSeen).getTime() || 0, cell: seenCell },
  { key: 'actions', label: '', cls: 'actions', sortable: false, cell: (h) => `<div class="row-actions">${rescanButton(h.ipAddress)}<button class="row-menu-btn" data-action="host-menu" data-ip="${esc(h.ipAddress)}" title="Azioni">${icon('more')}</button></div>` },
];

const isOffline = (h) => h.status === 'Offline';
/** Tipo di un indirizzo IPv6 dal prefisso: link-local (fe80::), locale (fc00::/7) oppure globale. */
const v6Kind = (a) => (/^fe[89ab]/i.test(a) ? 'link-local' : /^f[cd]/i.test(a) ? 'locale' : 'globale');
const openPorts = (h) => portsOf(h).length;
/** Tutti gli IPv4 del dispositivo: il principale e gli altri con lo stesso MAC. */
const ipsOf = (h) => [h.ipAddress, ...(h.otherIpv4 || [])];
/** Porte della sessione su tutti gli IPv4 del dispositivo, senza doppioni. */
function portsOf(h) {
  const ips = new Set(ipsOf(h));
  const seen = new Set();
  return S.data.ports.filter((p) => {
    if (!ips.has(p.host)) return false;
    const k = `${p.portNumber}/${p.protocol}`;
    if (seen.has(k)) return false;
    seen.add(k);
    return true;
  });
}
/** «+1 IP» accanto all'indirizzo, gli altri IPv4 dello stesso dispositivo nel tooltip. */
const otherIpsTag = (h) => ((h.otherIpv4 || []).length ? ` <span class="tag" title="Stesso dispositivo (stesso MAC) anche su: ${esc(h.otherIpv4.join(', '))}">+${h.otherIpv4.length} IP</span>` : '');
const isNewHost = (h) => h.firstSeen && Date.now() - new Date(h.firstSeen).getTime() < 86400000;

/** Data e ora brevi: «oggi 14:20», «ieri 09:05», «12/10 18:40», «03/05/2025». */
function shortWhen(iso) {
  const d = new Date(iso);
  if (!iso || Number.isNaN(d.getTime())) return '';
  const now = new Date();
  const day = (x) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const diff = Math.round((day(now) - day(d)) / 86400000);
  const p = (n) => String(n).padStart(2, '0');
  if (diff === 0) return hhmm(d);
  if (diff === 1) return `ieri ${hhmm(d)}`;
  return d.getFullYear() === now.getFullYear() ? `${p(d.getDate())}/${p(d.getMonth() + 1)} ${hhmm(d)}` : `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()}`;
}

/** Prima rilevazione (da questo PC, anche in sessioni precedenti) con «Nuovo» nelle prime 24 ore; l'ultima nel tooltip. */
function seenCell(h) {
  return `<span class="seen" title="Visto la prima volta il ${esc(fmtDateTime(h.firstSeen))} · l'ultima il ${esc(fmtDateTime(h.lastSeen))}">${isNewHost(h) ? '<span class="tag new">Nuovo</span>' : ''}${esc(shortWhen(h.firstSeen))}</span>`;
}

/** Pulsante «Riscansiona» di un host: gira su quel solo indirizzo, anche durante la scansione della rete. */
function rescanButton(ip, big = false) {
  const busy = S.net.rescanning.has(ip);
  if (big) return `<button class="btn ${busy ? 'is-loading' : ''}" data-action="host-rescan" data-ip="${esc(ip)}" ${busy ? 'disabled' : ''}>${icon('refresh')} Riscansiona</button>`;
  return `<button class="row-menu-btn rescan-btn ${busy ? 'is-busy' : ''}" data-action="host-rescan" data-ip="${esc(ip)}" title="Riscansiona questo host" ${busy ? 'disabled' : ''}>${busy ? '<span class="spinner"></span>' : icon('refresh')}</button>`;
}

/** Riscansione di un solo indirizzo: aggiorna la riga (e il pannello di dettaglio, se è aperto). */
async function rescan(ip) {
  if (S.net.rescanning.has(ip)) return;
  S.net.rescanning.add(ip);
  renderTable();
  refreshDrawer(ip);
  try {
    const res = await call('rescanHost', { ip, timeout: S.net.timeout, resolveDns: S.net.resolveDns });
    adopt({ state: res.state });
    if (res.host) {
      const i = S.data.hosts.findIndex((x) => x.ipAddress === ip);
      if (i >= 0) S.data.hosts[i] = res.host; else S.data.hosts.push(res.host);
    }
    addScan(res.scan);
    markDirty();
    emit('data', { list: 'hosts' });
    if (res.online) toast(`${ip}${res.host && res.host.hostname ? ` (${res.host.hostname})` : ''} risponde${res.host.responseTimeMs > 0 ? ` in ${fmtMs(res.host.responseTimeMs)}` : ' in meno di 1 ms'}.`, { tone: 'success', title: 'Host aggiornato' });
    else toast(res.host ? 'Non risponde né ad ARP né al ping: resta nella sessione segnato come «non risponde».' : 'Non risponde né ad ARP né al ping.', { tone: 'warning', title: `${ip} non risponde` });
  } catch (err) {
    toastError(err, 'Riscansione non riuscita');
  }
  S.net.rescanning.delete(ip);
  // la riga resta dov'era (lo scorrimento non torna in cima) e si illumina per un attimo
  S.net.fresh.add(ip);
  renderTable();
  setTimeout(() => { S.net.fresh.delete(ip); }, 2000);
  refreshDrawer(ip);
}

/** Il pannello di dettaglio aperto su quell'host si ridisegna con i dati nuovi. */
function refreshDrawer(ip) {
  const drawer = $('#overlays .drawer[data-host]');
  if (!drawer || drawer.dataset.host !== ip) return;
  closeOverlays();
  details(ip);
}

// ---------------------------------------------------------------------------------------------------------------------
// Vista a schede: un dispositivo per riga, senza colonne, descritto per intero; l'ordinamento è lo stesso della tabella
// ---------------------------------------------------------------------------------------------------------------------
const timeOf = (iso) => new Date(iso).getTime() || 0;
/** Ordinamenti possibili (anche dalla tabella): [chiave, etichetta, valore, verso predefinito]. */
const SORTS = [
  ['ipAddress', 'Indirizzo IP', (h) => h.ipAddress, 1],
  ['hostname', 'Nome', (h) => h.customName || hostName(h) || '￿', 1],
  ['deviceType', 'Tipo di dispositivo', (h) => `${h.deviceCategory} ${h.deviceType}`, 1],
  ['macVendor', 'Produttore', (h) => shortVendor(h.macVendor), 1],
  ['responseTimeMs', 'Latenza', (h) => Number(h.responseTimeMs) || 0, 1],
  ['ports', 'Porte aperte', (h) => openPorts(h), -1],
  ['firstSeen', 'Prima rilevazione', (h) => timeOf(h.firstSeen), -1],
  ['lastSeen', 'Ultima volta visto', (h) => timeOf(h.lastSeen), -1],
];
const SORT_COLUMNS = SORTS.map(([key, , value]) => ({ key, value }));

// vista e ordinamento restano al prossimo avvio (solo comodità)
try {
  const v = JSON.parse(localStorage.getItem('net-view') || '{}');
  if (v.view === 'cards' || v.view === 'table') S.net.view = v.view;
  if (v.sort && SORTS.some((s) => s[0] === v.sort.key)) S.net.sort = v.sort;
  if (['all', 'online', 'offline'].includes(v.status)) S.net.status = v.status;
} catch { /* senza memoria locale */ }
if (!S.net.view) S.net.view = 'cards';
function saveView() {
  try { localStorage.setItem('net-view', JSON.stringify({ view: S.net.view, sort: S.net.sort, status: S.net.status })); } catch { /* senza memoria locale */ }
}

/** Una voce della descrizione: etichetta piccola e valore. */
const fact = (label, value, cls = '', tip = '') => `<span class="hc-fact ${cls}"${tip ? ` title="${esc(tip)}"` : ''}><i>${label}</i><span class="hc-val">${value}</span></span>`;

function hostCard(h) {
  const ports = portsOf(h).sort((a, b) => a.portNumber - b.portNumber);
  const lat = Number(h.responseTimeMs) || 0;
  const latTone = lat < 10 ? 'success' : lat < 100 ? 'warning' : 'danger';
  const vendor = h.macVendor === PRIVATE_MAC ? `<span title="${esc(PRIVATE_MAC_TIP)}">MAC privato, produttore non noto</span>` : esc(h.macVendor);
  const name = h.customName || hostName(h) || h.deviceType;
  const sub = h.customName ? (hostName(h) || h.deviceType) : hostName(h) ? h.deviceType : '';
  const v6 = h.ipv6 || [];
  const tags = `${isNewHost(h) ? '<span class="tag new">Nuovo</span>' : ''}${h.isGateway ? '<span class="tag primary">gateway</span>' : ''}${h.isLocalMachine ? '<span class="tag">questo PC</span>' : ''}${isOffline(h) ? '<span class="tag danger">Non risponde</span>' : ''}${v6.length ? '<span class="tag v6">IPv6</span>' : ''}`;
  // al massimo 6 porte sulla riga (tutte nel tooltip e nel dettaglio): così ogni scheda ha la stessa altezza
  const portTip = ports.map((p) => `${p.portNumber}/${p.protocol} ${p.serviceName}`).join(', ');
  const portList = ports.slice(0, 6).map((p) => `<span class="hc-port">${p.portNumber}${p.protocol === 'UDP' ? '/udp' : ''} <em>${esc(p.serviceName)}</em></span>`).join('')
    + (ports.length > 6 ? `<span class="hc-port more">+${ports.length - 6}</span>` : '');
  const tipo = `${h.deviceCategory || 'Altro'}${h.osGuess ? ` · ${osShort(h.osGuess)}` : ''}`;
  const macText = h.macAddress ? `${h.macAddress} · ${h.macVendor === PRIVATE_MAC ? 'MAC privato, produttore non noto' : h.macVendor}` : 'non disponibile, host fuori dalla rete locale';
  const seen = `${shortWhen(h.firstSeen)}${h.lastSeen && shortWhen(h.lastSeen) !== shortWhen(h.firstSeen) ? ` · visto ${shortWhen(h.lastSeen)}` : ''}`;
  return `<article class="host-card ${S.net.fresh.has(h.ipAddress) ? 'is-new' : ''} ${isOffline(h) ? 'is-offline' : ''}" data-action="host-details" data-ctx="host" data-ip="${esc(h.ipAddress)}">
    ${catIcon(h, true)}
    <div class="hc-main">
      <div class="hc-line hc-title" title="${esc(name)}${sub ? ` · ${esc(sub)}` : ''}${h.notes ? `\n${esc(h.notes)}` : ''}">${h.customName ? icon('tag', { size: 14 }) : ''}<b>${esc(name)}</b>${sub ? `<span class="hc-type">${esc(sub)}</span>` : ''}${tags}</div>
      <div class="hc-line">
        <span class="hc-ip mono">${esc(h.ipAddress)}${otherIpsTag(h)}</span>
        ${fact('Tipo', esc(tipo), '', tipo)}
        ${fact('MAC', h.macAddress ? `<span class="mono">${esc(h.macAddress)}</span> · ${vendor}` : '<span class="muted">non disponibile</span>', 'grow', macText)}
        ${fact('Risposta', `${h.respondsToPing === false ? '<span class="muted" title="Non risponde al ping: trovato nella tabella ARP">solo ARP</span>' : `<b class="tone-${latTone}">${lat > 0 ? fmtMs(lat) : '< 1 ms'}</b>`}${h.ttl ? ` · TTL ${h.ttl}` : ''}`)}
      </div>
      <div class="hc-line">
        ${fact('Porte', ports.length ? `<span class="hc-ports">${portList}</span>` : '<span class="muted">nessuna scansione</span>', 'grow', portTip)}
        ${v6.length ? fact('IPv6', `<span class="mono">${esc(v6[0])}</span>${v6.length > 1 ? ` <span class="muted">+${v6.length - 1}</span>` : ''}`, '', v6.join(', ')) : ''}
        ${fact('Rilevato', esc(seen), '', `Prima volta ${fmtDateTime(h.firstSeen)} · ultima ${fmtDateTime(h.lastSeen)}`)}
      </div>
    </div>
    <div class="hc-actions">${rescanButton(h.ipAddress)}<button class="row-menu-btn" data-action="host-menu" data-ip="${esc(h.ipAddress)}" title="Azioni">${icon('more')}</button></div>
  </article>`;
}

function tableHtml() {
  const filtered = filteredHosts();
  if (!S.data.hosts.length) {
    return emptyState('radar', 'Nessun host nella sessione', 'Avvia una scansione della rete: gli host compaiono man mano che rispondono, poi arrivano nomi, porte e tipo di dispositivo. La scansione aggiorna anche l\'archivio della rete.');
  }
  if (!filtered.length) {
    const st = S.net.status || 'all';
    if (st !== 'all' && !S.net.q.trim()) {
      return emptyState(st === 'online' ? 'wifiOff' : 'check', st === 'online' ? 'Nessun host risponde' : 'Rispondono tutti',
        st === 'online' ? 'Nessun dispositivo della sessione ha risposto all\'ultima scansione.' : 'Nessun dispositivo della sessione è segnato come «non risponde».');
    }
    return emptyState('search', 'Nessun host corrisponde', st === 'all' ? 'Cambia le parole della ricerca.' : 'Cambia le parole della ricerca o il filtro Tutti / Online / Non rispondono.');
  }
  const rows = sortRows(filtered, SORT_COLUMNS, S.net.sort);
  const subs = subnetsInSession();
  const groupSubnets = subs.length > 1 && !activeSubnetFilter();
  if (S.net.view === 'cards') {
    const html = groupSubnets ? renderBySubnet(rows, (h) => h.ipAddress, hostCard, 'host', 'host') : rows.map(hostCard).join('');
    return `<div class="table-wrap scan-h host-cards">${html}</div>`;
  }
  return table({
    columns, rows, sort: S.net.sort, cls: 'scan-h',
    groupBy: groupSubnets ? (h) => subnetOf(h.ipAddress) : null,
    rowAttrs: (h) => `data-action="host-details" data-ctx="host" data-ip="${esc(h.ipAddress)}" class="${S.net.fresh.has(h.ipAddress) ? 'is-new' : ''} ${isOffline(h) ? 'is-offline' : ''}"`,
  }).replace('<table class="dt">', '<table class="dt clickable">');
}

/** Ordina per, verso e Schede / Tabella, accanto alla ricerca. */
function statusCount(key) {
  const sub = activeSubnetFilter();
  return S.data.hosts.filter((h) => (!sub || subnetOf(h.ipAddress) === sub) && byStatus(h, key)).length;
}

let lastSubnetSig = '';

function viewControls() {
  const s = S.net.sort || { key: 'ipAddress', dir: 1 };
  const st = S.net.status || 'all';
  const subs = subnetsInSession();
  const curSub = activeSubnetFilter();
  lastSubnetSig = subs.map(([c, n]) => `${c}:${n}`).join(',');
  const subnetBox = subs.length > 1
    ? `<div class="sort-box"><label for="f-subnet">Sottorete</label>
        <select class="select input-sm" id="f-subnet" data-field="subnet" title="Filtra gli host di una singola sottorete oppure mostrale tutte raggruppate">
          <option value="" ${!curSub ? 'selected' : ''}>Tutte (${subs.length} sottoreti · ${S.data.hosts.length})</option>
          ${subs.map(([cidr, cnt]) => `<option value="${esc(cidr)}" ${cidr === curSub ? 'selected' : ''}>${esc(cidr)} (${cnt})</option>`).join('')}
        </select></div>`
    : '';
  return `<div class="segmented status-filter" role="group" aria-label="Stato">${STATUS_FILTERS.map(([key, label, dot]) => `<button type="button" class="${st === key ? 'is-active' : ''}" data-action="net-status" data-status="${key}" title="${key === 'all' ? 'Tutti i dispositivi della sessione' : key === 'online' ? 'Solo quelli che hanno risposto all\'ultima scansione' : 'Solo quelli che non hanno risposto'}">${dot ? `<span class="status-dot ${dot}"></span>` : ''}${label}<span class="seg-count" data-status-count="${key}">${statusCount(key)}</span></button>`).join('')}</div>
    ${subnetBox}
    <div class="sort-box"><label for="f-sort">Ordina per</label>
      <select class="select input-sm" id="f-sort" data-field="sortKey">${SORTS.map(([k, label]) => `<option value="${k}" ${k === s.key ? 'selected' : ''}>${label}</option>`).join('')}</select>
      <button class="btn btn-sm btn-icon sort-dir ${s.dir < 0 ? 'is-desc' : ''}" data-action="sort-dir" title="${s.dir < 0 ? 'Decrescente: clic per crescente' : 'Crescente: clic per decrescente'}">${icon('chevronDown')}</button></div>
    <div class="view-box"><span class="view-label">Visualizza</span>
      <div class="segmented view-switch" role="group" aria-label="Visualizza">
        <button type="button" class="${S.net.view === 'table' ? 'is-active' : ''}" data-action="net-view" data-view="table" title="Elenco: una riga per dispositivo, con le colonne ordinabili">${icon('list', { size: 14 })} Elenco</button>
        <button type="button" class="${S.net.view === 'cards' ? 'is-active' : ''}" data-action="net-view" data-view="cards" title="Schede: ogni dispositivo descritto per intero">${icon('dashboard', { size: 14 })} Schede</button></div></div>`;
}

function refreshControls() {
  const box = $('#view-controls');
  if (box) box.innerHTML = viewControls();
}

function tableCard() {
  return `<section class="card table-card">
    <div class="toolbar">
      <div class="search">${icon('search')}<input class="input" data-field="q" value="${esc(S.net.q)}" placeholder="Cerca IP, nome, MAC, produttore, tipo…" title="Più parole: devono comparire tutte. Cerca anche negli indirizzi IPv6." autocomplete="off"></div>
      <div class="view-controls" id="view-controls">${viewControls()}</div>
      <div class="toolbar-actions"><span class="toolbar-info" id="hosts-info"></span>
        <button class="btn btn-sm" data-action="clear-hosts" ${S.data.hosts.length ? '' : 'disabled'}>${icon('trash')} Svuota</button></div>
    </div>
    <div id="hosts-table">${tableHtml()}</div>
    <div class="table-foot">${icon('info', { size: 14 })} Clic su un dispositivo per i dettagli; tasto destro (o ⋯) per porte, WHOIS, traceroute, browser, cartelle condivise, Desktop remoto, SSH e ping.</div>
  </section>`;
}

function render() {
  return `${pageHead('Host della rete (ping e ARP), con produttore dal MAC, nomi DNS e NetBIOS, porte aperte, latenza e tipo di dispositivo.')}
    <div class="grid">${formCard()}${tableCard()}</div>`;
}

function after() { updateInfo(); }

function updateInfo() {
  const info = $('#hosts-info');
  if (!info) return;
  const total = S.data.hosts.length;
  const shown = filteredHosts().length;
  info.textContent = total ? (shown === total ? plural(total, 'host', 'host') : `${shown} di ${total} host`) : '';
}

function renderTable() {
  const box = $('#hosts-table');
  if (!box) return;
  replaceKeepingScroll(box, tableHtml());
  updateInfo();
  const sig = subnetsInSession().map(([c, n]) => `${c}:${n}`).join(',');
  if (sig !== lastSubnetSig) refreshControls();
  // i numeri del filtro Tutti / Online / Non rispondono seguono la scansione
  for (const [key] of STATUS_FILTERS) {
    const c = $(`[data-status-count="${key}"]`);
    if (c) c.textContent = statusCount(key);
  }
  const clear = $('[data-action="clear-hosts"]');
  if (clear) clear.disabled = !S.data.hosts.length;
}

function renderForm() {
  const card = $('#scan-card');
  if (card) card.outerHTML = formCard();
}

/** Avanzamento sul posto (percentuale, barra, contatori) senza ricreare la riga: niente sfarfallio sui pulsanti. */
function updateBar() {
  const card = $('#scan-card');
  if (!card) return;
  const meter = $('[data-meter]', card);
  if (!running() || !meter) { renderForm(); return; }
  const pct = S.net.progress ? S.net.progress.percent : 0;
  meter.style.width = `${pct}%`;
  $('[data-pct]', card).textContent = `${pct}%`;
  $('[data-stats]', card).innerHTML = runningStats();
}

/** Prossima ripetizione, se è scelta: parte da sola all'ora indicata (se nel frattempo non ne gira già una). */
function scheduleRepeat() {
  const n = S.net;
  clearTimeout(n.repeatTimer);
  n.nextAt = 0;
  if (!n.repeat) return;
  n.nextAt = Date.now() + n.repeat * 60000;
  n.repeatTimer = setTimeout(() => { n.nextAt = 0; if (n.repeat && !running()) start(); }, n.repeat * 60000);
}

function stopRepeat() {
  clearTimeout(S.net.repeatTimer);
  S.net.nextAt = 0;
}

async function start() {
  const n = S.net;
  if (running()) return;
  stopRepeat();
  if (!n.target) n.target = (S.st.adapters.find((a) => a.cidr) || {}).cidr || '192.168.1.0/24';
  try {
    const preset = autoPreset();
    const res = await call('scanNetwork', {
      target: n.target, threads: n.threads, timeout: n.timeout, resolveDns: n.resolveDns,
      autoPorts: n.autoPorts && !!preset.ports, autoPortsList: preset.ports,
    });
    adopt({ state: res.state });
    n.last = { target: n.target, threads: n.threads, timeout: n.timeout, resolveDns: n.resolveDns, autoPorts: n.autoPorts && !!preset.ports };
    n.progress = { scanned: 0, total: res.total, percent: 0, seconds: 0, currentIp: '', phase: 'ping', phaseLabel: 'Avvio' };
    n.done = null;
    n.editing = false;
    n.fresh.clear();
    renderForm();
    emit('state');
  } catch (err) { toastError(err, 'Scansione non avviata'); }
}

async function stop() {
  try { await call('stopNetwork'); } catch (err) { toastError(err); }
}

function hostByIp(ip) { return S.data.hosts.find((h) => h.ipAddress === ip); }

/** Telecamera, NVR o altro apparato Hikvision, dal tipo riconosciuto: per gli indirizzi RTSP nel formato Hikvision. */
export const isHikvision = (h) => !!h && /hikvision/i.test(h.deviceType || '');

/**
 * Strumenti di Windows su un host (browser, Esplora risorse, Desktop remoto, SSH, ping): voci comuni ai menu.
 * Per un dispositivo Hikvision (opzione hikvision, o il banner SDK della 8000) anche gli indirizzi RTSP dei due flussi.
 */
export function launchItems(host, ports = [], { hikvision = false } = {}) {
  const open = (kind, port = 0) => () => call('launch', { kind, host, port }).catch((e) => toastError(e));
  const has = (n) => ports.some((p) => p.portNumber === n && p.protocol === 'TCP');
  const httpAlt = ports.find((p) => p.protocol === 'TCP' && [81, 8080, 8008].includes(p.portNumber));
  const httpPort = has(80) ? 80 : (httpAlt ? httpAlt.portNumber : 0);
  const rtspPort = has(8554) && !has(554) ? 8554 : 554;
  const isCam = hikvision || ports.some((p) => /hikvision/i.test(p.serviceName || p.banner || ''));
  const items = [
    { ic: 'external', label: 'Apri nel browser (http)', run: open('http', httpPort), hint: httpPort ? String(httpPort) : '' },
    { ic: 'external', label: 'Apri nel browser (https)', run: open('https'), hint: has(443) ? '443' : '' },
    { ic: 'folderOpen', label: 'Cartelle condivise (Esplora risorse)', run: open('explorer'), hint: has(445) ? 'SMB' : '' },
    { ic: 'monitor', label: 'Desktop remoto', run: open('rdp'), hint: has(3389) ? 'RDP' : '' },
    { ic: 'terminal', label: 'Terminale SSH', run: open('ssh'), hint: has(22) ? 'SSH' : '' },
    { ic: 'activity', label: 'Ping continuo (prompt)', run: open('ping') },
  ];
  if (isCam) {
    items.push(
      { ic: 'copy', label: 'Copia indirizzo RTSP (flusso 101)', hint: String(rtspPort), run: () => copy(`rtsp://${host}:${rtspPort}/Streaming/Channels/101`, 'Indirizzo RTSP (101) copiato.') },
      { ic: 'copy', label: 'Copia indirizzo RTSP (flusso 102)', hint: 'sub', run: () => copy(`rtsp://${host}:${rtspPort}/Streaming/Channels/102`, 'Indirizzo RTSP (102) copiato.') },
    );
  }
  return items;
}

function hostMenu(anchor, ip) {
  const h = hostByIp(ip);
  if (!h) return;
  openMenu(anchor, [
    { header: `${h.ipAddress}${h.hostname ? ` · ${h.hostname}` : ''}` },
    { ic: 'refresh', label: 'Riscansiona questo host', disabled: S.net.rescanning.has(ip), run: () => rescan(ip) },
    { ic: 'plug', label: 'Scansiona le porte', run: () => navigate('ports', { host: ip, start: true }) },
    { ic: 'globe', label: 'WHOIS', run: () => navigate('whois', { target: ip, start: true }) },
    { ic: 'route', label: 'Traceroute', run: () => navigate('trace', { target: ip, start: true }) },
    { sep: true },
    ...launchItems(ip, portsOf(h), { hikvision: isHikvision(h) }),
    { sep: true },
    { ic: 'copy', label: 'Copia l\'indirizzo IP', run: () => copy(h.ipAddress, 'Indirizzo IP copiato.') },
    { ic: 'copy', label: 'Copia il MAC', disabled: !h.macAddress, run: () => copy(h.macAddress, 'MAC copiato.') },
    { ic: 'clipboard', label: 'Copia la riga', run: () => copy([h.ipAddress, h.hostname, h.macAddress, h.macVendor, h.deviceType, `${h.responseTimeMs} ms`, h.ttl, h.osGuess].join('\t'), 'Riga copiata.') },
    { sep: true },
    { ic: 'trash', label: 'Togli dalla sessione', danger: true, run: () => removeHost(ip) },
  ]);
}

async function copy(text, done) {
  try { await call('setClipboard', { text }); toast(done, { tone: 'success' }); } catch (err) { toastError(err); }
}

async function removeHost(ip) {
  try {
    adopt(await call('removeHost', { ip }));
    closeOverlays();
    renderTable();
  } catch (err) { toastError(err); }
}

function details(ip) {
  const h = hostByIp(ip);
  if (!h) return;
  const ports = portsOf(h);
  const body = `<div class="fields">
    <form class="host-label" id="host-label" autocomplete="off">
      <div class="field"><label>Icona</label>
        <div class="host-icon-field">${catIcon(h, true)}
          <button type="button" class="btn btn-sm" data-host-icon>${icon('tag')} ${h.icon ? 'Cambia icona…' : 'Scegli un\'icona…'}</button>
          <span class="muted">${h.icon ? esc(h.icon) : 'quella del tipo di dispositivo'}</span></div></div>
      <div class="field"><label for="h-name">Nome del dispositivo</label>
        <input class="input" id="h-name" maxlength="80" value="${esc(h.customName || '')}" placeholder="${esc(h.hostname || h.deviceType)} · es. Router di casa"></div>
      <div class="field"><label for="h-notes">Note</label>
        <textarea class="input" id="h-notes" rows="2" maxlength="2000" placeholder="Dove si trova, a chi appartiene, credenziali di accesso…">${esc(h.notes || '')}</textarea></div>
      <div class="host-label-foot"><small class="muted">${h.macAddress ? 'Resta legato al MAC: lo ritrovi in ogni scansione e sessione su questo PC.' : 'Senza MAC resta legato all\'indirizzo IP.'}</small>
        <button type="submit" class="btn btn-sm btn-primary">${icon('save')} Salva</button></div>
    </form>
    <dl class="kv">
      <dt>Indirizzo IP</dt><dd class="mono">${esc(h.ipAddress)}${h.isGateway ? ' <span class="tag primary">gateway</span>' : ''}${h.isLocalMachine ? ' <span class="tag">questo PC</span>' : ''}</dd>
      ${(h.otherIpv4 || []).length ? `<dt>Altri IPv4</dt><dd><div class="v6-list">${h.otherIpv4.map((a) => `<span class="mono">${esc(a)}</span>`).join('<br>')}</div><small class="muted">Stesso dispositivo: stesso MAC, contato una volta sola.</small></dd>` : ''}
      <dt>Stato</dt><dd>${isOffline(h) ? '<span class="pill danger pill-dot">Non risponde</span>' : h.respondsToPing === false ? '<span class="pill warning pill-dot">Presente (solo ARP)</span>' : '<span class="pill success pill-dot">Risponde</span>'}</dd>
      <dt>IPv6</dt><dd>${(h.ipv6 || []).length ? `<div class="v6-list">${h.ipv6.map((a) => `<span class="mono">${esc(a)}</span> <span class="tag">${esc(v6Kind(a))}</span>`).join('<br>')}</div>` : '<span class="muted">nessuno visto (il dispositivo non usa IPv6, o non ha ancora risposto)</span>'}</dd>
      <dt>Nome DNS</dt><dd>${h.hostname ? esc(h.hostname) : '<span class="muted">non risolto</span>'}</dd>
      <dt>Nome NetBIOS</dt><dd>${h.netBiosName ? `${esc(h.netBiosName)}${h.workgroup ? ` <span class="tag">${esc(h.workgroup)}</span>` : ''}` : '<span class="muted">nessuna risposta (UDP 137)</span>'}</dd>
      <dt>MAC</dt><dd class="mono">${h.macAddress ? esc(h.macAddress) : '<span class="muted">non disponibile (host fuori dalla rete locale)</span>'}</dd>
      <dt>Produttore</dt><dd>${esc(h.macVendor)}${h.macVendor === PRIVATE_MAC ? `<br><small class="muted">${esc(PRIVATE_MAC_TIP)}</small>` : ''}</dd>
      <dt>Dispositivo</dt><dd>${esc(h.deviceType)} <span class="tag">${esc(h.deviceCategory || 'Altro')}</span></dd>
      <dt>Sistema stimato</dt><dd>${esc(h.osGuess)}</dd>
      <dt>Latenza</dt><dd>${h.respondsToPing === false ? '<span class="muted">non risponde al ping (presente nella tabella ARP)</span>' : latency(h.responseTimeMs)}</dd>
      <dt>TTL</dt><dd>${h.ttl || '—'}</dd>
      <dt>Visto la prima volta</dt><dd>${fmtDateTime(h.firstSeen)}${isNewHost(h) ? ' <span class="tag new">Nuovo</span>' : ''}</dd>
      <dt>Ultima volta</dt><dd>${fmtDateTime(h.lastSeen)}</dd>
    </dl>
    <div><div class="form-section-head"><div class="form-section-title">${icon('plug')}Porte aperte nella sessione</div></div>
      ${ports.length ? `<div class="pill-list">${ports.map((p) => `<span class="pill ${p.status === 'Open' ? 'success' : 'warning'}">${p.portNumber}/${esc(p.protocol)} · ${esc(p.serviceName)}</span>`).join('')}</div>`
        : '<p class="card-text muted">Nessuna scansione delle porte per questo host.</p>'}</div>
  </div>`;
  const d = openDrawer({
    ic: 'monitor', title: h.customName ? `${h.customName} · ${h.ipAddress}` : h.ipAddress, sub: `${h.deviceType}${hostName(h) ? ` · ${hostName(h)}` : ''}`, body,
    foot: `<div class="left">
        ${rescanButton(ip, true)}
        <button class="btn btn-primary" data-action="host-ports" data-ip="${esc(ip)}">${icon('plug')} Scansiona le porte</button>
        <button class="btn" data-action="host-whois" data-ip="${esc(ip)}">${icon('globe')} WHOIS</button>
        <button class="btn" data-action="host-trace" data-ip="${esc(ip)}">${icon('route')} Traceroute</button>
        <button class="btn" data-action="host-tools" data-ip="${esc(ip)}">${icon('external')} Apri con… ${icon('chevronDown', { size: 13 })}</button></div>
      <button class="btn btn-ghost" data-action="close-overlay">Chiudi</button>`,
  });
  const drawer = $('.drawer', d.wrap);
  if (drawer) drawer.dataset.host = ip;
  $('#host-label', d.wrap).addEventListener('submit', (e) => { e.preventDefault(); saveLabel(ip, d.wrap); });
  // l'icona si salva appena scelta, insieme a nome e note scritti nei campi
  $('[data-host-icon]', d.wrap).addEventListener('click', async () => {
    const choice = await pickIcon({ current: { icon: h.icon, color: h.iconColor }, fallback: catIcon({ deviceCategory: h.deviceCategory, deviceType: h.deviceType }, true).replace(/^<span[^>]*>|<\/span>$/g, '') });
    if (choice) saveLabel(ip, d.wrap, choice);
  });
}

/** Nome, note e (se scelta) icona del dispositivo: nella sessione e nel registro dei dispositivi di questo PC. */
async function saveLabel(ip, wrap, choice = null) {
  const name = $('#h-name', wrap).value.trim();
  const notes = $('#h-notes', wrap).value.trim();
  try {
    const res = await call('hostLabel', { ip, name, notes, ...(choice ? { icon: choice.icon, iconColor: choice.color } : {}) });
    adopt({ state: res.state });
    const i = S.data.hosts.findIndex((x) => x.ipAddress === ip);
    if (i >= 0) S.data.hosts[i] = res.host;
    markDirty();
    emit('data', { list: 'hosts' });
    renderTable();
    refreshDrawer(ip);
    const what = choice ? (choice.icon ? `Icona di ${ip}: ${choice.icon}.` : `${ip} torna all'icona del tipo.`)
      : name ? `${ip} ora si chiama «${name}».` : `Nome di ${ip} tolto.`;
    toast(what, { tone: 'success', title: 'Dispositivo salvato' });
  } catch (err) { toastError(err, 'Nome non salvato'); }
}

/** Riapre il modulo completo (dopo una scansione) e porta il cursore sul campo della rete. */
function edit() {
  S.net.editing = true;
  renderForm();
  const f = $('#f-target');
  if (f) { f.focus(); f.select(); }
}

const actions = {
  'scan-stop': () => { stopRepeat(); stop(); },
  'repeat-stop': () => { stopRepeat(); renderForm(); toast('La scansione non si ripete più da sola.', { tone: 'info' }); },
  'scan-edit': () => edit(),
  'scan-collapse': () => { S.net.editing = false; renderForm(); },
  'scan-restart': () => start(),
  'detect-adapters': async () => {
    try { S.st.adapters = await call('adapters'); renderForm(); toast(plural(S.st.adapters.length, 'scheda di rete attiva', 'schede di rete attive'), { tone: 'success' }); } catch (err) { toastError(err); }
  },
  'clear-hosts': async () => {
    if (!(await confirmDialog({ title: 'Svuotare l\'elenco degli host?', message: `${plural(S.data.hosts.length, 'host', 'host')} verranno tolti dalla sessione. Le porte trovate restano.`, confirmText: 'Svuota', tone: 'danger' }))) return;
    try { adopt(await call('clearList', { list: 'hosts' })); S.net.done = null; S.net.editing = false; renderTable(); renderForm(); } catch (err) { toastError(err); }
  },
  'host-menu': (el) => hostMenu(el, el.dataset.ip),
  'host-rescan': (el) => rescan(el.dataset.ip),
  'sort-dir': () => { const s = S.net.sort || { key: 'ipAddress', dir: 1 }; S.net.sort = { key: s.key, dir: -s.dir }; saveView(); renderTable(); refreshControls(); },
  'net-status': (el) => { S.net.status = el.dataset.status; saveView(); renderTable(); refreshControls(); },
  'net-view': (el) => { S.net.view = el.dataset.view; saveView(); renderTable(); refreshControls(); },
  'host-details': (el) => details(el.dataset.ip),
  'host-ports': (el) => navigate('ports', { host: el.dataset.ip, start: true }),
  'host-whois': (el) => navigate('whois', { target: el.dataset.ip, start: true }),
  'host-trace': (el) => navigate('trace', { target: el.dataset.ip, start: true }),
  'host-tools': (el) => {
    const h = hostByIp(el.dataset.ip);
    openMenu(el, [{ header: el.dataset.ip }, ...launchItems(el.dataset.ip, h ? portsOf(h) : [], { hikvision: isHikvision(h) })], { above: true });
  },
};

on('network.progress', (d) => {
  S.net.progress = d;
  // gli host arrivano più volte: trovati dal ping o dall'ARP, poi arricchiti con nomi, porte e tipo; si illuminano solo i nuovi
  if (d.hosts && d.hosts.length) {
    for (const h of d.hosts) {
      const i = S.data.hosts.findIndex((x) => x.ipAddress === h.ipAddress);
      if (i >= 0) S.data.hosts[i] = h; else { S.data.hosts.push(h); S.net.fresh.add(h.ipAddress); }
      // più IPv4 con lo stesso MAC: le righe degli indirizzi secondari confluiscono nel principale
      for (const ip of h.otherIpv4 || []) {
        const j = S.data.hosts.findIndex((x) => x.ipAddress === ip && x !== h && (!x.macAddress || x.macAddress === h.macAddress));
        if (j >= 0) { S.data.hosts.splice(j, 1); S.net.fresh.delete(ip); }
      }
    }
    markDirty();
    emit('data', { list: 'hosts' });
  }
  // porte aperte trovate dalla scansione della rete: nell'elenco delle porte della sessione, come quelle della pagina Porte
  if (d.ports && d.ports.length) {
    for (const p of d.ports) {
      const i = S.data.ports.findIndex((x) => x.host === p.host && x.portNumber === p.portNumber && x.protocol === p.protocol);
      if (i >= 0) S.data.ports[i] = p; else S.data.ports.push(p);
    }
    markDirty();
    emit('data', { list: 'ports' });
  }
  if (S.page === 'network') {
    if ((d.hosts && d.hosts.length) || (d.ports && d.ports.length)) renderTable();
    updateBar();
  }
});

on('network.done', (d) => {
  const n = S.net;
  n.done = d;
  n.progress = null;
  n.editing = false;
  adopt({ state: d.state });
  // chi questa volta non ha risposto resta nella tabella, segnato «non risponde»
  for (const ip of d.offline || []) {
    const h = S.data.hosts.find((x) => x.ipAddress === ip);
    if (h) h.status = 'Offline';
  }
  // IPv6 degli host, dalla tabella dei vicini letta alla fine della scansione
  for (const [ip, list] of Object.entries(d.ipv6 || {})) {
    const h = S.data.hosts.find((x) => x.ipAddress === ip);
    if (h) h.ipv6 = list;
  }
  if (!d.cancelled && !d.error) scheduleRepeat(); else stopRepeat();
  if (S.page === 'network') { renderForm(); renderTable(); }
  emit('data', { list: 'hosts' });
  const gone = (d.offline || []).length ? ` · ${plural(d.offline.length, 'non risponde più', 'non rispondono più')}` : '';
  // le differenze dell'archivio della rete (dispositivi nuovi, ricomparsi, cambiati, assenti): i nuovi arrivano anche come avviso
  const diff = d.diff;
  const changes = diff && !diff.isFirst ? diff.newCount + diff.goneCount + diff.changedCount + diff.returnedCount : 0;
  const archive = diff ? (diff.isFirst ? ' Archivio di questa rete creato: dalla prossima scansione arrivano confronti e avvisi.' : ` Archivio: ${diff.summary}`) : '';
  if (d.error) toast(d.error, { tone: 'error', title: 'Scansione interrotta' });
  else if (d.cancelled) toast(`${plural(d.found, 'host attivo', 'host attivi')} fino a quel momento (l'archivio non viene aggiornato).`, { tone: 'warning', title: 'Scansione fermata' });
  else toast(`${plural(d.found, 'host attivo', 'host attivi')} in ${fmtSeconds(d.seconds)}${gone}.${archive}`, {
    tone: 'success', title: 'Scansione della rete completata', timeout: changes || (diff && diff.isFirst) ? 10000 : 6000,
    action: changes ? { label: 'Cambiamenti', run: () => navigate('devices', { tab: 'events' }) } : diff && diff.isFirst ? { label: 'Archivio', run: () => navigate('devices') } : null,
  });
  setTimeout(() => { n.fresh.clear(); }, 2000);
});

// arrivate le icone scelte per i dispositivi (core/mdi.js): la tabella le mostra
bus.addEventListener('icons', () => { if (S.page === 'network') renderTable(); });

bus.addEventListener('session', () => {
  S.net.done = null; S.net.editing = false; S.net.fresh.clear();
  if (S.page === 'network') { renderTable(); renderForm(); }
});

definePage('network', {
  title: 'Rete', heading: 'Scansione della rete', icon: 'radar', section: 'scan', order: 0, badge: () => S.data.hosts.length,
  render, after, actions,
  enter: (params) => {
    if (params.target) { S.net.target = params.target; if (!running()) edit(); }
    // scansione all'avvio del programma (impostazione): parte con le opzioni di sempre
    if (params.start && !running()) start();
    // dalla Panoramica: un tipo di dispositivo o un indirizzo da cercare nella tabella
    if (params.q != null) {
      S.net.q = params.q;
      const f = $('[data-field="q"]');
      if (f) f.value = params.q;
      renderTable();
    }
  },
  onInput: (el, field) => {
    const n = S.net;
    if (field === 'q') { n.q = el.value; renderTable(); return; }
    if (field === 'target') n.target = el.value;
    if (field === 'threads') n.threads = Number(el.value) || 128;
    if (field === 'timeout') n.timeout = Number(el.value) || 700;
    if (field === 'threads' || field === 'timeout') saveOptions();
  },
  onChange: (el, field) => {
    const n = S.net;
    if (field === 'resolveDns') n.resolveDns = el.checked;
    if (field === 'autoPorts') {
      n.autoPorts = el.checked;
      const sel = $('.auto-preset');
      if (sel) sel.disabled = !el.checked;
    }
    if (field === 'autoPreset') n.autoPreset = el.value;
    if (field === 'repeat') {
      n.repeat = Number(el.value) || 0;
      // in attesa della prossima: il nuovo intervallo vale da subito («Una volta» la annulla)
      if (n.nextAt) { scheduleRepeat(); renderForm(); }
    }
    if (field === 'subnet') {
      n.subnet = el.value || '';
      renderTable();
      refreshControls();
      return;
    }
    if (field === 'sortKey') {
      const s = SORTS.find((x) => x[0] === el.value);
      n.sort = { key: el.value, dir: s ? s[3] : 1 };
      saveView();
      renderTable();
      refreshControls();
      return;
    }
    saveOptions();
  },
  onSubmit: () => start(),
  onSort: (key) => { S.net.sort = nextSort(S.net.sort, key); saveView(); renderTable(); refreshControls(); },
  onContext: (row, point) => hostMenu(point, row.dataset.ip),
});
