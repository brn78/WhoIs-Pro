// Panoramica: il collegamento di rete dal vivo (traffico delle schede con i LED delle porte), la rete trovata
// (host, nuovi arrivi, porte, Internet), le ultime scansioni e il sistema. Si aggiorna ogni due secondi.
import { call, on } from '../core/bridge.js';
import { $, $$, esc, fmtAgo, fmtBytes, fmtDateTime, fmtDuration, fmtLinkSpeed, fmtNumber, fmtRate, fmtSeconds, plural } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { catIcon, categoryOf } from '../core/categories.js';
import { S, definePage, addScan, bus } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { emptyState, pageHead, toastError } from '../core/ui.js';
import { shortVendor } from './network.js';
import { openSessionDrawer } from './session.js';
import { openPcNetwork } from './settings.js';

const SAMPLES = 90;          // 90 letture ogni 2 secondi: gli ultimi 3 minuti
const DAY = 86400000;

S.overview = { dash: null, error: '', timer: 0, busy: false, sel: '', hist: new Map(), quick: { target: '', ping: null, dns: null, pinging: false, looking: false, pingError: '', dnsError: '' } };

// ---------------------------------------------------------------------------------------------------------------------
// Dati dal vivo
// ---------------------------------------------------------------------------------------------------------------------
function remember(dash) {
  const o = S.overview;
  for (const a of dash.traffic || []) {
    const h = o.hist.get(a.id) || [];
    h.push({ rx: a.rxBps, tx: a.txBps });
    if (h.length > SAMPLES) h.splice(0, h.length - SAMPLES);
    o.hist.set(a.id, h);
  }
  const list = dash.traffic || [];
  if (!list.some((a) => a.id === o.sel)) o.sel = list.length ? list[0].id : '';
}

const selected = () => {
  const d = S.overview.dash;
  return d && (d.traffic || []).find((a) => a.id === S.overview.sel);
};

async function load() {
  const o = S.overview;
  if (o.busy) return;
  o.busy = true;
  try {
    o.dash = await call('dashboard');
    o.error = '';
    remember(o.dash);
  } catch (err) {
    o.error = err.message;
  }
  o.busy = false;
  if (S.page === 'overview') refreshLive();
}

function startTimer() {
  stopTimer();
  S.overview.timer = setInterval(() => { if (S.page === 'overview') load(); else stopTimer(); }, 2000);
}
function stopTimer() {
  clearInterval(S.overview.timer);
  S.overview.timer = 0;
}

// ---------------------------------------------------------------------------------------------------------------------
// Collegamento: porte (schede), velocità e traccia del traffico
// ---------------------------------------------------------------------------------------------------------------------
/** Scala del grafico: il primo valore «tondo» sopra il massimo (1, 2, 5 × 10^n bit/s). */
function niceMax(bytesPerSecond) {
  const bits = Math.max(8000, bytesPerSecond * 8 * 1.15);
  const p = 10 ** Math.floor(Math.log10(bits));
  const step = [1, 2, 5, 10].find((m) => m * p >= bits);
  return (step * p) / 8;
}

/** Traccia del traffico: ricezione come area blu, invio come linea arancio; il più recente a destra. */
function scope(hist) {
  const W = 900;
  const H = 150;
  const max = niceMax(Math.max(0, ...hist.map((s) => Math.max(s.rx, s.tx))));
  const x = (i) => W - (hist.length - 1 - i) * (W / (SAMPLES - 1));
  const y = (v) => H - Math.min(1, v / max) * (H - 6);
  const line = (key) => hist.map((s, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)} ${y(s[key]).toFixed(1)}`).join(' ');
  const area = (key) => (hist.length > 1 ? `${line(key)} L${x(hist.length - 1).toFixed(1)} ${H} L${x(0).toFixed(1)} ${H} Z` : '');
  const grid = [0.25, 0.5, 0.75].map((f) => `<line class="scope-grid" x1="0" x2="${W}" y1="${(H - f * (H - 6)).toFixed(1)}" y2="${(H - f * (H - 6)).toFixed(1)}"/>`).join('');
  const svg = hist.length < 2 ? '' : `<path class="scope-area rx" d="${area('rx')}"/><path class="scope-area tx" d="${area('tx')}"/>
    <path class="scope-line rx" d="${line('rx')}"/><path class="scope-line tx" d="${line('tx')}"/>`;
  return `<svg class="scope-svg" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" role="img" aria-label="Traffico degli ultimi tre minuti">${grid}${svg}</svg>
    <span class="scope-max">${esc(fmtRate(max).text)}</span><span class="scope-span">ultimi 3 min</span>`;
}

function ledClass(a) {
  return a.rxBps + a.txBps > 2048 ? 'led is-act' : 'led is-link';
}

function portsHtml() {
  const d = S.overview.dash;
  const list = (d && d.traffic) || [];
  if (!list.length) return '';
  return list.map((a) => `<button class="port ${a.id === S.overview.sel ? 'is-active' : ''}" data-action="link-select" data-id="${esc(a.id)}" title="${esc(a.description)}">
      <span class="${ledClass(a)}" data-led="${esc(a.id)}"></span>
      <span class="port-text"><b>${esc(a.name)}</b><small>${esc([a.type, fmtLinkSpeed(a.speedMbps)].filter(Boolean).join(', '))}</small></span></button>`).join('');
}

function readout(cls, ic, label, bps, total, attr) {
  const r = fmtRate(bps);
  return `<div class="readout ${cls}">
    <span class="readout-label">${icon(ic, { size: 15 })}${label}</span>
    <span class="readout-value"><b data-${attr}>${esc(r.value)}</b><span data-${attr}-unit>${esc(r.unit)}</span></span>
    <small data-${attr}-total>${esc(fmtBytes(total))} dall'avvio di Windows</small></div>`;
}

function prefix(mask) {
  const bits = String(mask || '').split('.').map((n) => (Number(n) >>> 0).toString(2).padStart(8, '0')).join('');
  return /^1*0*$/.test(bits) && bits.length === 32 ? `/${bits.indexOf('0') < 0 ? 32 : bits.indexOf('0')}` : '';
}

function factsHtml(a) {
  const f = (k, v) => `<div><dt>${k}</dt><dd>${v}</dd></div>`;
  return `${f('Indirizzo', `<span class="mono">${esc(a.ip)}${esc(prefix(a.mask))}</span>`)}
    ${f('Gateway', a.gateway ? `<span class="mono">${esc(a.gateway)}</span>` : '<span class="faint">nessuno</span>')}
    ${f('DNS', (a.dns || []).length ? `<span class="mono">${esc(a.dns.join(', '))}</span>` : '<span class="faint">—</span>')}
    ${f('Configurazione', a.dhcp ? 'automatica (DHCP)' : 'statica')}
    ${f('MAC', `<span class="mono">${esc(a.mac)}</span>`)}
    ${f('IPv6', v6Fact(a))}`;
}

/** IPv6 della scheda: il primo indirizzo utilizzabile su Internet, gli altri nel tooltip; il gateway IPv6. */
function v6Fact(a) {
  const list = a.ipv6 || [];
  if (!list.length) return '<span class="faint">spento</span>';
  const pub = list.find((x) => x.kind === 'globale' || x.kind === 'temporaneo');
  const all = list.map((x) => `${x.address}/${x.prefixLength} (${x.kind})`).join('\n');
  const main = pub ? `<span class="mono">${esc(pub.address)}</span>` : '<span class="muted">solo link-local</span>';
  return `<span title="${esc(all)}${a.gateway6 ? `\nGateway ${esc(a.gateway6)}` : ''}">${main}${list.length > 1 ? ` <span class="tag">+${list.length - 1}</span>` : ''}</span>`;
}

function linkPanel() {
  const o = S.overview;
  const a = selected();
  let inner;
  if (o.error && !o.dash) inner = `<div class="link-empty">${emptyState('alert', 'Traffico non letto', esc(o.error))}</div>`;
  else if (!o.dash) inner = '<div class="loading-block"><span class="spinner"></span> Lettura delle schede di rete…</div>';
  else if (!a) inner = `<div class="link-empty">${emptyState('wifiOff', 'Nessuna scheda collegata', 'Collega il cavo o la rete Wi-Fi: la scheda compare qui con il suo traffico.')}</div>`;
  else {
    inner = `<div class="link-body">
        <div class="readouts">
          ${readout('rx', 'download', 'Ricezione', a.rxBps, a.rxTotal, 'rx')}
          ${readout('tx', 'upload', 'Invio', a.txBps, a.txTotal, 'tx')}
        </div>
        <div class="scope" id="scope">${scope(o.hist.get(a.id) || [])}</div>
      </div>
      <dl class="link-facts" id="link-facts">${factsHtml(a)}</dl>`;
  }
  return `<section class="card link-panel" id="link-panel">
    <div class="link-head">
      <div class="ports" id="ports">${portsHtml()}</div>
      <button class="btn btn-sm" data-action="pc-network">${icon('settings')} Rete del PC</button>
    </div>
    ${inner}
  </section>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Rete trovata: host, nuovi arrivi, porte, Internet, ultima scansione
// ---------------------------------------------------------------------------------------------------------------------
const isNew = (h) => h.firstSeen && Date.now() - new Date(h.firstSeen).getTime() < DAY;

function lastScan() {
  const list = S.data.scans || [];
  return list.length ? list[list.length - 1] : null;
}

const KIND = {
  network: { ic: 'radar', label: 'Rete', unit: ['host attivo', 'host attivi'] },
  ports: { ic: 'plug', label: 'Porte', unit: ['porta aperta', 'porte aperte'] },
  trace: { ic: 'route', label: 'Traceroute', unit: ['salto', 'salti'] },
  host: { ic: 'refresh', label: 'Host', unit: ['risponde', 'risponde'] },
  autoports: { ic: 'plug', label: 'Porte di', unit: ['porta aperta', 'porte aperte'] },
  perf: { ic: 'zap', label: 'Prestazioni', unit: ['Mbit/s', 'Mbit/s'] },
  capture: { ic: 'list', label: 'Cattura', unit: ['pacchetto', 'pacchetti'] },
};

function stat(page, label, value, sub, tone = '') {
  return `<button class="stat ${tone}" data-action="go" data-page="${page}"><span class="stat-label">${label}</span>
    <span class="stat-value">${value}</span><span class="stat-sub">${sub}</span></button>`;
}

function statsHtml() {
  const hosts = S.data.hosts;
  const fresh = hosts.filter(isNew).length;
  const portHosts = new Set(S.data.ports.map((p) => p.host)).size;
  const net = S.overview.dash && S.overview.dash.internet;
  const online = net ? net.online : S.st.online;
  const ms = net && net.latencyMs;
  const last = lastScan();
  const k = last && (KIND[last.kind] || KIND.network);
  return `${stat('network', 'Host nella rete', fmtNumber(hosts.length), fresh ? `<b class="tone-new">${plural(fresh, 'nuovo', 'nuovi')}</b> nelle ultime 24 ore` : 'nessun nuovo nelle ultime 24 ore')}
    ${stat('ports', 'Porte aperte', fmtNumber(S.data.ports.length), portHosts ? `su ${plural(portHosts, 'host', 'host')}` : 'nessuna scansione delle porte')}
    ${stat('netstat', 'Internet', online === false ? 'Assente' : ms != null ? `${fmtNumber(ms)}<small>ms</small>` : '—',
      online === false ? 'WHOIS, DNS pubblici e aggiornamenti non funzionano'
        : `risposta della pagina di prova di Windows${net && net.ipv6 != null ? ` · ${net.ipv6 ? '<b class="tone-new">IPv6 attivo</b>' : 'solo IPv4'}` : ''}`, online === false ? 'is-bad' : '')}
    ${stat(last ? (last.kind === 'ports' ? 'ports' : last.kind === 'trace' ? 'trace' : 'network') : 'network', 'Ultima scansione', last ? esc(fmtAgo(last.at)) : 'mai',
      last ? `${esc(k.label)} ${esc(last.target)}` : 'avvia la prima dalla pagina Rete')}`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Ping e nslookup al volo (non finiscono nella sessione)
// ---------------------------------------------------------------------------------------------------------------------
const PING_STATUS = {
  TimedOut: 'nessuna risposta', DestinationHostUnreachable: 'host irraggiungibile', DestinationNetworkUnreachable: 'rete irraggiungibile',
  TtlExpired: 'TTL scaduto', DestinationUnreachable: 'irraggiungibile', Error: 'errore',
};

function pingInner() {
  const q = S.overview.quick;
  if (q.pinging) return '<div class="quick-wait"><span class="spinner"></span> Ping in corso…</div>';
  if (q.pingError) return `<div class="quick-msg tone-bad">${esc(q.pingError)}</div>`;
  const p = q.ping;
  if (!p) return '<div class="quick-msg muted">Quattro richieste ICMP, con tempo e TTL di ciascuna.</div>';
  const lost = p.sent - p.received;
  const max = Math.max(1, ...p.replies.map((r) => r.ms));
  const rows = p.replies.map((r) => (r.status === 'Success'
    ? `<li><span class="q-seq">${r.seq}</span><span class="q-bar"><i style="width:${Math.max(4, (r.ms / max) * 100).toFixed(0)}%"></i></span><b>${r.ms ? `${fmtNumber(r.ms)} ms` : '< 1 ms'}</b><span class="muted">TTL ${r.ttl}</span></li>`
    : `<li class="is-lost"><span class="q-seq">${r.seq}</span><span class="q-bar"></span><b>${esc(PING_STATUS[r.status] || r.status)}</b><span></span></li>`)).join('');
  return `<div class="quick-head"><b>${esc(p.target)}</b>${p.address !== p.target ? ` <span class="mono muted">${esc(p.address)}</span>` : ''}</div>
    <ul class="q-replies">${rows}</ul>
    <div class="quick-sum ${lost ? (p.received ? 'tone-warn' : 'tone-bad') : ''}">${fmtNumber(p.received)} di ${fmtNumber(p.sent)} risposte${lost ? ` · ${Math.round((lost * 100) / p.sent)}% perse` : ''}${p.received ? ` · min ${fmtNumber(p.min)} · media ${fmtNumber(p.avg)} · max ${fmtNumber(p.max)} ms` : ''}</div>`;
}

function dnsInner() {
  const q = S.overview.quick;
  if (q.looking) return '<div class="quick-wait"><span class="spinner"></span> nslookup in corso…</div>';
  if (q.dnsError) return `<div class="quick-msg tone-bad">${esc(q.dnsError)}</div>`;
  const d = q.dns;
  if (!d) return '<div class="quick-msg muted">Record A, AAAA, MX, NS e TXT di un nome, oppure il nome (PTR) di un indirizzo.</div>';
  const server = d.server && !/^unknown$/i.test(d.server) ? d.server : 'senza nome';
  const order = ['A', 'AAAA', 'PTR', 'MX', 'NS', 'TXT'];
  const recs = [...d.records].sort((a, b) => order.indexOf(a.recordType) - order.indexOf(b.recordType));
  return `<div class="quick-head"><b>${esc(d.target)}</b> <span class="muted">· server ${esc(server)}${d.serverAddress ? ` <span class="mono">${esc(d.serverAddress)}</span>` : ''} · ${fmtNumber(d.ms)} ms</span></div>
    ${recs.length ? `<ul class="q-records">${recs.map((r) => `<li><span class="tag q-type">${esc(r.recordType)}</span><span class="mono">${esc(r.value)}</span></li>`).join('')}</ul>`
    : '<div class="quick-msg tone-bad">Nessun record: il nome non esiste o il server non risponde.</div>'}`;
}

/** Scorciatoie: il gateway della scheda scelta, un DNS pubblico e un nome da risolvere. */
function picksHtml() {
  const a = selected();
  const picks = [...new Set([a && a.gateway, '1.1.1.1', 'google.com'].filter(Boolean))];
  return picks.map((t) => `<button type="button" class="chip quick-pick" data-action="quick-go" data-target="${esc(t)}" title="${t === (a && a.gateway) ? 'Gateway della scheda' : 'Prova con questo'}">${esc(t)}</button>`).join('');
}

function quickCard() {
  const q = S.overview.quick;
  return `<section class="card quick-card"><div class="card-head"><div class="card-title">${icon('activity')}<h2>Ping e nslookup</h2></div>
      <span class="card-sub">controllo al volo, non finisce nella sessione</span></div>
    <div class="card-body">
      <form class="quick-form" data-form="quick" autocomplete="off">
        <div class="search">${icon('search')}<input class="input input-mono" id="q-target" data-field="quickTarget" value="${esc(q.target)}" placeholder="Indirizzo IP o nome · Invio = ping e nslookup insieme" spellcheck="false"></div>
        <button type="button" class="btn btn-primary" data-action="quick-ping" ${q.pinging ? 'disabled' : ''}>${icon('activity')} Ping</button>
        <button type="button" class="btn" data-action="quick-lookup" ${q.looking ? 'disabled' : ''}>${icon('dns')} nslookup</button>
        <span class="quick-picks" id="quick-picks">${picksHtml()}</span>
      </form>
      <div class="quick-results"><div class="quick-box" id="quick-ping">${pingInner()}</div><div class="quick-box" id="quick-dns">${dnsInner()}</div></div>
    </div></section>`;
}

function paintQuick() {
  const p = $('#quick-ping'); if (p) p.innerHTML = pingInner();
  const d = $('#quick-dns'); if (d) d.innerHTML = dnsInner();
  $$('[data-action="quick-ping"]').forEach((b) => { b.disabled = S.overview.quick.pinging; });
  $$('[data-action="quick-lookup"]').forEach((b) => { b.disabled = S.overview.quick.looking; });
}

function quickTarget() {
  const q = S.overview.quick;
  const f = $('#q-target');
  if (f) q.target = f.value.trim();
  if (!q.target) { toastError(new Error('Scrivi un indirizzo IP o un nome.')); return ''; }
  return q.target;
}

async function quickPing() {
  const q = S.overview.quick;
  const target = quickTarget();
  if (!target || q.pinging) return;
  q.pinging = true; q.pingError = ''; paintQuick();
  try { q.ping = await call('quickPing', { target, count: 4 }); } catch (err) { q.ping = null; q.pingError = err.message; }
  q.pinging = false; paintQuick();
}

async function quickLookup() {
  const q = S.overview.quick;
  const target = quickTarget();
  if (!target || q.looking) return;
  q.looking = true; q.dnsError = ''; paintQuick();
  try { q.dns = await call('quickLookup', { target }); } catch (err) { q.dns = null; q.dnsError = err.message; }
  q.looking = false; paintQuick();
}

// ---------------------------------------------------------------------------------------------------------------------
// Ultime scansioni e sistema
// ---------------------------------------------------------------------------------------------------------------------
function scansCard() {
  const list = (S.data.scans || []).slice(-7).reverse();
  const items = list.map((s, i) => {
    const k = KIND[s.kind] || KIND.network;
    const result = s.error ? `<span class="tone-bad">errore: ${esc(s.error)}</span>`
      : s.kind === 'host' ? (s.found ? 'risponde' : '<span class="tone-bad">non risponde</span>')
        : s.kind === 'perf' ? `${fmtNumber(s.found)} Mbit/s in download`
        : `${plural(s.found, k.unit[0], k.unit[1])}${s.cancelled ? ' · fermata' : ''}`;
    return `<li class="scan-item">
      <span class="scan-kind">${icon(k.ic, { size: 16 })}</span>
      <span class="scan-text"><b>${esc(k.label)} <span class="mono">${esc(s.target)}</span></b><small>${result} in ${esc(fmtSeconds(s.seconds))}</small></span>
      <span class="scan-when" title="${esc(fmtDateTime(s.at))}">${esc(fmtAgo(s.at))}</span>
      <button class="btn btn-sm btn-ghost btn-icon" data-action="scan-again" data-index="${(S.data.scans.length - 1) - i}" title="Ripeti">${icon('refresh')}</button></li>`;
  }).join('');
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('history')}<h2>Ultime scansioni</h2>${S.data.scans.length ? `<span class="count">${S.data.scans.length}</span>` : ''}</div>
      <button class="btn btn-sm" data-action="go" data-page="network">${icon('radar')} Scansiona la rete</button></div>
    ${list.length ? `<ul class="scan-list">${items}</ul>` : emptyState('radar', 'Nessuna scansione in questa sessione', 'Le scansioni della rete, delle porte e i traceroute finiscono qui, con esito e durata.')}</section>`;
}

function bar(label, pct, value, sub) {
  const tone = pct >= 90 ? 'danger' : pct >= 75 ? 'warning' : '';
  return `<div class="gauge ${tone}"><div class="gauge-top"><span>${label}</span><b>${Math.round(pct)}%</b></div>
    <div class="gauge-track"><span style="width:${Math.min(100, Math.max(0, pct)).toFixed(1)}%"></span></div>
    <div class="gauge-sub"><span>${value}</span><span class="muted">${sub}</span></div></div>`;
}

function systemInner() {
  const d = S.overview.dash;
  if (!d || !d.system) return '<div class="loading-block"><span class="spinner"></span> Lettura del sistema…</div>';
  const { cpu, memory, disk, os } = d.system;
  return `${bar('Processore', cpu.usage, esc(cpu.name), `${fmtNumber(cpu.logical)} thread`)}
    ${bar('Memoria', memory.total ? (memory.used / memory.total) * 100 : 0, `${esc(fmtBytes(memory.used))} di ${esc(fmtBytes(memory.total))}`, 'in uso')}
    ${disk ? bar(`Disco ${esc(disk.name)}`, disk.total ? (disk.used / disk.total) * 100 : 0, `${esc(fmtBytes(disk.free))} liberi`, `su ${esc(fmtBytes(disk.total))}`) : ''}
    <dl class="sys-facts">
      <div><dt>Windows</dt><dd>${esc(os.name)}${os.version ? ` ${esc(os.version)}` : ''}</dd></div>
      <div><dt>Computer</dt><dd class="mono">${esc(os.machine)}</dd></div>
      <div><dt>Acceso da</dt><dd>${esc(fmtDuration(os.uptimeSeconds))}</dd></div>
    </dl>`;
}

function systemCard() {
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('laptop')}<h2>Questo PC</h2></div>
      <button class="btn btn-sm" data-action="go" data-page="computer">${icon('cpu')} Dettagli</button></div>
    <div class="card-body sys-body" id="sys-body">${systemInner()}</div></section>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Dispositivi per categoria e ultimi arrivati
// ---------------------------------------------------------------------------------------------------------------------
function categoriesCard() {
  const map = new Map();
  for (const h of S.data.hosts) {
    const key = h.deviceCategory || 'Altro';
    map.set(key, (map.get(key) || 0) + 1);
  }
  const rows = [...map.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0], 'it'));
  const max = rows.length ? rows[0][1] : 1;
  const items = rows.map(([cat, n]) => `<li><button class="cat-row" data-action="filter-hosts" data-q="${esc(cat)}" title="Mostra ${esc(cat)} nella pagina Rete">
      ${catIcon({ deviceCategory: cat })}<span class="cat-name">${esc(cat)}</span>
      <span class="cat-bar tone-${categoryOf({ deviceCategory: cat }).tone || 'neutral'}"><span style="width:${((n / max) * 100).toFixed(1)}%"></span></span>
      <b>${fmtNumber(n)}</b></button></li>`).join('');
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('dashboard')}<h2>Dispositivi per tipo</h2></div></div>
    ${rows.length ? `<ul class="cat-list">${items}</ul>` : emptyState('radar', 'Ancora nessun dispositivo', 'Scansiona la rete: telecamere, router, NAS, domotica e telefoni vengono riconosciuti e contati qui.')}</section>`;
}

function arrivalsCard() {
  const list = [...S.data.hosts].sort((a, b) => new Date(b.firstSeen) - new Date(a.firstSeen)).slice(0, 6);
  const items = list.map((h) => `<li><button class="arrival" data-action="filter-hosts" data-q="${esc(h.ipAddress)}">
      ${catIcon(h)}<span class="arrival-text"><b>${esc(h.hostname || h.deviceType)}</b><small><span class="mono">${esc(h.ipAddress)}</span> · ${esc(shortVendor(h.macVendor))}</small></span>
      <span class="arrival-when">${isNew(h) ? '<span class="tag new">Nuovo</span>' : ''}<small title="${esc(fmtDateTime(h.firstSeen))}">${esc(fmtAgo(h.firstSeen))}</small></span></button></li>`).join('');
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('clock')}<h2>Ultimi arrivati</h2></div>
      <span class="card-sub">per data di prima rilevazione</span></div>
    ${list.length ? `<ul class="arrivals">${items}</ul>` : emptyState('clock', 'Nessun host ancora', 'Ogni dispositivo trovato porta con sé l\'ora in cui è comparso per la prima volta.')}</section>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Pagina
// ---------------------------------------------------------------------------------------------------------------------
function headHtml() {
  const s = S.st.session;
  const sub = `<span class="session-line">${icon('archive', { size: 15 })}<b>${esc(s.title)}</b>${s.file ? ` <span class="muted">${esc(s.fileName)}</span>` : ' <span class="muted">non ancora salvata</span>'}${s.dirty ? ' <span class="pill warning pill-dot">da salvare</span>' : ''}</span>`;
  return pageHead(sub,
    `<button class="btn" data-action="session-details">${icon('edit')} Dettagli</button>
     <button class="btn" data-action="new">${icon('filePlus')} Nuova</button>
     <button class="btn" data-action="open">${icon('folderOpen')} Apri…</button>`);
}

function render() {
  return `${headHtml()}
    ${linkPanel()}
    <div class="stats" id="stats">${statsHtml()}</div>
    <div class="grid section-gap">${quickCard()}</div>
    <div class="grid grid-dash section-gap"><div id="scans-card">${scansCard()}</div>${systemCard()}</div>
    <div class="grid grid-2 section-gap" id="lists">${categoriesCard()}${arrivalsCard()}</div>`;
}

/** Ogni due secondi, sul posto: LED, velocità, traccia, configurazione e sistema (niente sfarfallio). */
function refreshLive() {
  const o = S.overview;
  const panel = $('#link-panel');
  if (!panel) return;
  const a = selected();
  const ids = ((o.dash && o.dash.traffic) || []).map((x) => x.id).join('|');
  if (!a || !$('#scope') || panel.dataset.ids !== ids) {
    panel.outerHTML = linkPanel();
    const fresh = $('#link-panel');
    if (fresh) fresh.dataset.ids = ids;
  } else {
    for (const x of o.dash.traffic) {
      const led = $(`[data-led="${CSS.escape(x.id)}"]`);
      if (led) led.className = ledClass(x);
    }
    const rx = fmtRate(a.rxBps);
    const tx = fmtRate(a.txBps);
    $('[data-rx]').textContent = rx.value; $('[data-rx-unit]').textContent = rx.unit;
    $('[data-tx]').textContent = tx.value; $('[data-tx-unit]').textContent = tx.unit;
    $('[data-rx-total]').textContent = `${fmtBytes(a.rxTotal)} dall'avvio di Windows`;
    $('[data-tx-total]').textContent = `${fmtBytes(a.txTotal)} dall'avvio di Windows`;
    $('#scope').innerHTML = scope(o.hist.get(a.id) || []);
    const facts = factsHtml(a);
    const box = $('#link-facts');
    if (box && box.dataset.html !== facts) { box.innerHTML = facts; box.dataset.html = facts; }
  }
  const sys = $('#sys-body');
  if (sys) sys.innerHTML = systemInner();
  const picks = $('#quick-picks');
  if (picks) { const html = picksHtml(); if (picks.dataset.html !== html) { picks.innerHTML = html; picks.dataset.html = html; } }
  const stats = $('#stats');
  if (stats) stats.innerHTML = statsHtml();
}

/** Host, porte o scansioni cambiati: contatori ed elenchi. */
function refreshData() {
  if (S.page !== 'overview') return;
  const stats = $('#stats');
  if (stats) stats.innerHTML = statsHtml();
  const scans = $('#scans-card');
  if (scans) scans.innerHTML = scansCard();
  const lists = $('#lists');
  if (lists) lists.innerHTML = categoriesCard() + arrivalsCard();
}

function rerender() {
  if (S.page !== 'overview') return;
  const content = $('#content');
  if (content) content.innerHTML = render();
}

function scanAgain(index) {
  const s = S.data.scans[index];
  if (!s) return;
  if (s.kind === 'ports') navigate('ports', { host: s.target, start: true });
  else if (s.kind === 'trace') navigate('trace', { target: s.target, start: true });
  else if (s.kind === 'host') navigate('network', { q: s.target });
  else if (s.kind === 'autoports') navigate('ports');
  else if (s.kind === 'perf') navigate('perf');
  else if (s.kind === 'capture') navigate('capture');
  else navigate('network', { target: s.target });
}

const actions = {
  'link-select': (el) => {
    S.overview.sel = el.dataset.id;
    $$('.port').forEach((p) => p.classList.toggle('is-active', p.dataset.id === el.dataset.id));
    const panel = $('#link-panel');
    if (panel) { panel.outerHTML = linkPanel(); }
  },
  'pc-network': () => openPcNetwork(),
  'session-details': () => openSessionDrawer(),
  'scan-again': (el) => scanAgain(Number(el.dataset.index)),
  'filter-hosts': (el) => navigate('network', { q: el.dataset.q }),
  'quick-ping': () => quickPing(),
  'quick-lookup': () => quickLookup(),
  'quick-go': (el) => {
    S.overview.quick.target = el.dataset.target;
    const f = $('#q-target');
    if (f) f.value = el.dataset.target;
    quickPing();
    quickLookup();
  },
};

for (const name of ['network.done', 'ports.done', 'trace.done']) {
  on(name, (d) => { addScan(d && d.scan); setTimeout(refreshData, 0); });
}
bus.addEventListener('data', refreshData);
bus.addEventListener('session', rerender);
bus.addEventListener('state', () => {
  // il titolo della sessione e lo stato di salvataggio stanno nella riga in cima
  if (S.page !== 'overview') return;
  const head = $('.page-head');
  if (!head) return;
  const fresh = document.createElement('div');
  fresh.innerHTML = headHtml();
  const next = fresh.firstElementChild;
  if (next && next.innerHTML !== head.innerHTML) head.innerHTML = next.innerHTML;
});

definePage('overview', {
  title: 'Panoramica', heading: 'Panoramica', icon: 'dashboard', section: '',
  render,
  after: () => { load().catch(toastError); startTimer(); },
  leave: stopTimer,
  actions,
  onInput: (el, field) => { if (field === 'quickTarget') S.overview.quick.target = el.value.trim(); },
  // Invio nel campo: ping e nslookup insieme
  onSubmit: (form, name) => { if (name === 'quick') { quickPing(); quickLookup(); } },
});
