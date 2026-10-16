// Pacchetti: cattura con pktmon (strumento di Windows), con un solo consenso di Windows per cattura; poi statistiche,
// protocolli, chi parla di più e l'elenco dei pacchetti spiegati, con ricerca e filtro per protocollo. La cattura si
// salva in .pcapng per Wireshark.
import { call, on } from '../core/bridge.js';
import { $, esc, fmtBytes, fmtNumber, fmtSeconds, matchesAll, plural } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage, adopt, addScan, emit } from '../core/store.js';
import { replaceKeepingScroll, table } from '../core/table.js';
import { emptyState, help, note, pageHead, toast, toastError } from '../core/ui.js';

const ROWS = 2000;   // righe disegnate alla volta: con la ricerca si trova il resto

S.capture = { seconds: 30, ip: '', port: '', protocol: '', progress: null, summary: null, q: '', proto: '', loaded: false };

const running = () => !!(S.st && S.st.running.capture);
const DURATIONS = [[10, '10 secondi'], [30, '30 secondi'], [60, '1 minuto'], [300, '5 minuti'], [0, 'Fino a «Ferma» (max 10 min)']];
const PROTOS = [['', 'Tutti'], ['TCP', 'TCP'], ['UDP', 'UDP'], ['ICMP', 'ICMP'], ['ICMPv6', 'ICMPv6']];

function formCard() {
  const c = S.capture;
  if (running()) {
    const p = c.progress || {};
    const text = !p.started ? 'In attesa del consenso di Windows…' : p.converting ? 'Lettura dei pacchetti catturati…'
      : `Cattura in corso · ${fmtSeconds(p.seconds || 0)}${p.limit ? ` di ${fmtSeconds(p.limit)}` : ''} · ${fmtBytes(p.bytes || 0)}`;
    const pct = p.limit && p.started ? Math.min(100, Math.round(((p.seconds || 0) * 100) / p.limit)) : null;
    return `<section class="card scan-summary" id="cap-card"><span class="scan-state is-busy"><span class="spinner"></span><span data-cap-text>${esc(text)}</span></span>
      ${pct != null ? `<span class="meter"><span data-cap-meter style="width:${pct}%"></span></span>` : '<span class="spacer"></span>'}
      <span class="scan-actions"><button type="button" class="btn btn-danger" data-action="cap-stop" ${p.started && !p.converting ? '' : 'disabled'}>${icon('stop')} Ferma</button></span></section>`;
  }
  return `<section class="card" id="cap-card"><div class="card-body">
    <form data-form="capture" class="scan-form" autocomplete="off">
      <div class="field field-select"><label for="c-sec">Durata</label>
        <select class="select" id="c-sec" data-field="seconds">${DURATIONS.map(([v, l]) => `<option value="${v}" ${c.seconds === v ? 'selected' : ''}>${l}</option>`).join('')}</select></div>
      <div class="field field-2"><label for="c-ip">Solo l'indirizzo o la rete <span class="muted">(facoltativo)</span></label>
        <input class="input input-mono" id="c-ip" data-field="ip" value="${esc(c.ip)}" placeholder="192.168.1.40 · 192.168.1.0/24 · fe80::1"></div>
      <div class="field"><label for="c-port">Porta</label><input class="input" id="c-port" type="number" min="1" max="65535" data-field="port" value="${esc(c.port)}" placeholder="tutte"></div>
      <div class="field field-select"><label for="c-proto">Protocollo</label>
        <select class="select" id="c-proto" data-field="protocol">${PROTOS.map(([v, l]) => `<option value="${v}" ${c.protocol === v ? 'selected' : ''}>${l}</option>`).join('')}</select></div>
      <div class="field field-end"><span class="field-label">&nbsp;</span><div class="field-actions">
        <button type="submit" class="btn btn-primary">${icon('play')} Avvia la cattura</button></div></div>
    </form>
    <div class="cap-note">${note('info', 'shield', 'La cattura usa <b>pktmon</b>, lo strumento di Windows: Windows chiede il consenso (UAC) una volta per ogni cattura, il programma resta senza diritti di amministratore. Si vedono i pacchetti che passano dalle schede di questo PC; il contenuto cifrato (HTTPS) resta cifrato, si vede a chi è diretto.')}</div>
  </div></section>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Risultati
// ---------------------------------------------------------------------------------------------------------------------
const PROTO_TONE = { DNS: 'info', mDNS: 'info', LLMNR: 'info', HTTPS: 'success', QUIC: 'success', HTTP: 'warning', ARP: 'neutral', ICMP: 'primary', ICMPv6: 'primary', RDP: 'danger', SMB: 'danger', SSH: 'primary', DHCP: 'info', SSDP: 'neutral' };

function filtered() {
  const c = S.capture;
  const all = (c.summary && c.summary.packets) || [];
  return all.filter((p) => (!c.proto || p.proto === c.proto) && matchesAll(c.q, [p.src, p.dst, p.proto, p.info, String(p.srcPort || ''), String(p.dstPort || '')]));
}

function statsHtml(s) {
  const rate = s.seconds > 0 ? s.total / s.seconds : 0;
  const cell = (label, value, sub) => `<div class="stat"><span class="stat-label">${label}</span><span class="stat-value">${value}</span><span class="stat-sub">${sub}</span></div>`;
  return `<div class="stats cap-stats">
    ${cell('Pacchetti', fmtNumber(s.total), s.truncated ? `elenco dei primi ${fmtNumber(s.packets.length)}` : 'tutti nell\'elenco')}
    ${cell('Dati', esc(fmtBytes(s.bytes)), `${esc(fmtBytes(s.seconds > 0 ? s.bytes / s.seconds : 0))} al secondo`)}
    ${cell('Durata', esc(fmtSeconds(s.seconds)), `${fmtNumber(Math.round(rate))} pacchetti al secondo`)}
    ${cell('Protocolli', fmtNumber(Object.keys(s.protocols || {}).length), 'riconosciuti')}
  </div>`;
}

function protocolsCard(s) {
  const rows = Object.entries(s.protocols || {}).sort((a, b) => b[1] - a[1]);
  const max = rows.length ? rows[0][1] : 1;
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('list')}<h2>Protocolli</h2></div><span class="card-sub">clic per filtrare l'elenco</span></div>
    <ul class="cat-list">${rows.map(([name, n]) => `<li><button class="cat-row cap-proto-row ${S.capture.proto === name ? 'is-active' : ''}" data-action="cap-proto" data-proto="${esc(name)}">
      <span class="tag ${PROTO_TONE[name] || ''}">${esc(name)}</span><span class="cat-name">${esc(protoHint(name))}</span>
      <span class="cat-bar tone-${PROTO_TONE[name] || 'neutral'}"><span style="width:${((n / max) * 100).toFixed(1)}%"></span></span><b>${fmtNumber(n)}</b></button></li>`).join('')}</ul></section>`;
}

const HINTS = {
  DNS: 'nomi di dominio', mDNS: 'nomi in rete locale', LLMNR: 'nomi in rete locale', HTTPS: 'web cifrato', QUIC: 'web cifrato (HTTP/3)', HTTP: 'web in chiaro',
  ARP: 'chi ha questo indirizzo', ICMP: 'ping e avvisi', ICMPv6: 'IPv6: vicini e ping', RDP: 'Desktop remoto', SMB: 'cartelle condivise', SSH: 'terminale remoto',
  DHCP: 'assegnazione degli indirizzi', SSDP: 'dispositivi UPnP', NTP: 'ora esatta', NetBIOS: 'nomi Windows', IGMP: 'gruppi multicast', LLDP: 'apparati di rete',
  TCP: 'altre connessioni', UDP: 'altri datagrammi', RTSP: 'flussi video', MQTT: 'domotica', Altro: 'altri protocolli',
};
const protoHint = (p) => HINTS[p] || '';

function talkersCard(s) {
  const rows = s.talkers || [];
  const max = rows.length ? rows[0].bytes : 1;
  const known = new Map((S.data.hosts || []).map((h) => [h.ipAddress, h]));
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('activity')}<h2>Chi parla di più</h2></div><span class="card-sub">per dati scambiati</span></div>
    <ul class="cat-list">${rows.map((t) => {
      const h = known.get(t.address);
      return `<li><button class="cat-row cap-talker" data-action="cap-filter" data-q="${esc(t.address)}" title="Mostra solo ${esc(t.address)}">
        <span class="cap-addr mono">${esc(t.address)}</span><span class="cat-name">${h ? esc(h.hostname || h.deviceType) : ''}</span>
        <span class="cat-bar tone-primary"><span style="width:${((t.bytes / max) * 100).toFixed(1)}%"></span></span><b class="cap-bytes">${esc(fmtBytes(t.bytes))}</b></button></li>`;
    }).join('')}</ul></section>`;
}

const columns = [
  { key: 'no', label: 'N.', cls: 'num muted' },
  { key: 't', label: 'Tempo', cls: 'num', cell: (p) => `${p.t.toFixed(3).replace('.', ',')} s` },
  { key: 'src', label: 'Origine', cls: 'mono', cell: (p) => `${esc(p.src)}${p.srcPort ? `<span class="muted">:${p.srcPort}</span>` : ''}` },
  { key: 'dst', label: 'Destinazione', cls: 'mono', cell: (p) => `${esc(p.dst)}${p.dstPort ? `<span class="muted">:${p.dstPort}</span>` : ''}` },
  { key: 'proto', label: 'Protocollo', cell: (p) => `<span class="tag ${PROTO_TONE[p.proto] || ''}">${esc(p.proto)}</span>` },
  { key: 'length', label: 'Byte', cls: 'num' },
  { key: 'info', label: 'Informazioni', cell: (p) => `<span class="cap-info" title="${esc(p.info)}">${esc(p.info)}</span>` },
];

function listHtml() {
  const rows = filtered();
  if (!rows.length) return emptyState('search', 'Nessun pacchetto corrisponde', 'Cambia la ricerca o togli il filtro del protocollo.');
  return table({ columns, rows: rows.slice(0, ROWS), sort: null, cls: 'list-h cap-list' });
}

function listInfo() {
  const n = filtered().length;
  const total = ((S.capture.summary && S.capture.summary.packets) || []).length;
  return n > ROWS ? `primi ${fmtNumber(ROWS)} di ${fmtNumber(n)}: restringi con la ricerca` : n === total ? plural(n, 'pacchetto', 'pacchetti') : `${fmtNumber(n)} di ${fmtNumber(total)}`;
}

function resultsHtml() {
  const s = S.capture.summary;
  if (running()) return '';
  if (!s) return `<section class="card section-gap">${emptyState('list', 'Nessuna cattura', 'Scegli durata e, se vuoi, un indirizzo, una porta o un protocollo, poi «Avvia la cattura». Per vedere il traffico di un dispositivo della rete, quello deve passare da questo PC.')}</section>`;
  if (!s.total) return `<section class="card section-gap">${emptyState('list', 'Nessun pacchetto catturato', 'Con i filtri scelti non è passato niente: allarga i filtri o allunga la durata.')}</section>`;
  const c = S.capture;
  return `${statsHtml(s)}
    <div class="grid grid-2 section-gap">${protocolsCard(s)}${talkersCard(s)}</div>
    <section class="card table-card section-gap">
      <div class="toolbar">
        <div class="search">${icon('search')}<input class="input" data-field="q" value="${esc(c.q)}" placeholder="Cerca indirizzo, porta, protocollo o testo…" autocomplete="off"></div>
        ${c.proto ? `<button class="chip quick-pick" data-action="cap-proto" data-proto="${esc(c.proto)}" title="Togli il filtro">${esc(c.proto)} ${icon('close', { size: 12 })}</button>` : ''}
        <div class="toolbar-actions"><span class="toolbar-info" id="cap-info">${esc(listInfo())}</span>
          <button class="btn btn-sm" data-action="cap-save">${icon('download')} Salva .pcapng</button>
          <button class="btn btn-sm" data-action="cap-open">${icon('external')} Apri in Wireshark</button></div>
      </div>
      <div id="cap-list">${listHtml()}</div>
    </section>`;
}

function render() {
  return `${pageHead(`Cattura dei pacchetti che passano dalle schede di rete di questo PC, spiegati riga per riga ${help('pktmon cattura sulle schede (Ethernet e Wi-Fi). Il .pcapng si apre con Wireshark per l\'analisi completa.')}`)}
    <div class="grid">${formCard()}</div><div id="cap-results">${resultsHtml()}</div>`;
}

function rerender() {
  if (S.page !== 'capture') return;
  const c = $('#content');
  if (c) c.innerHTML = render();
}

function renderList() {
  const box = $('#cap-list');
  if (box) replaceKeepingScroll(box, listHtml());
  const info = $('#cap-info');
  if (info) info.textContent = listInfo();
}

async function start() {
  if (running()) return;
  const c = S.capture;
  try {
    const res = await call('captureStart', { seconds: Number(c.seconds) || 0, ip: c.ip.trim(), port: Number(c.port) || 0, protocol: c.protocol });
    c.progress = { started: false };
    adopt({ state: res.state });
    emit('state');
    rerender();
  } catch (err) { toastError(err, 'Cattura non avviata'); }
}

on('capture.progress', (d) => {
  S.capture.progress = d;
  if (S.page !== 'capture') return;
  const card = $('#cap-card');
  if (!card || !$('[data-cap-text]', card) || (d.started && !$('[data-cap-meter]', card) && d.limit) || card.querySelector('[data-action=cap-stop]').disabled !== !(d.started && !d.converting)) {
    if (card) card.outerHTML = formCard();
    return;
  }
  $('[data-cap-text]', card).textContent = d.converting ? 'Lettura dei pacchetti catturati…' : `Cattura in corso · ${fmtSeconds(d.seconds || 0)}${d.limit ? ` di ${fmtSeconds(d.limit)}` : ''} · ${fmtBytes(d.bytes || 0)}`;
  const m = $('[data-cap-meter]', card);
  if (m && d.limit) m.style.width = `${Math.min(100, Math.round(((d.seconds || 0) * 100) / d.limit))}%`;
});

on('capture.done', (d) => {
  const c = S.capture;
  c.progress = null;
  adopt({ state: d.state });
  if (d.error) {
    toast(d.error, { tone: 'error', title: 'Cattura non riuscita' });
  } else {
    c.summary = d.summary;
    c.q = '';
    c.proto = '';
    addScan(d.scan);
    emit('data', { list: 'scans' });
    toast(`${plural(d.summary.total, 'pacchetto', 'pacchetti')} · ${fmtBytes(d.summary.bytes)} in ${fmtSeconds(d.summary.seconds)}.`, { tone: 'success', title: 'Cattura completata' });
  }
  emit('state');
  rerender();
});

definePage('capture', {
  title: 'Pacchetti', heading: 'Cattura dei pacchetti', icon: 'list', section: 'system', order: 1,
  render,
  after: async () => {
    // tornando sulla pagina dopo un riavvio della finestra: l'ultima cattura dal programma
    if (!S.capture.loaded && !S.capture.summary) {
      S.capture.loaded = true;
      try { const s = await call('captureResult'); if (s) { S.capture.summary = s; rerender(); } } catch { /* niente da mostrare */ }
    }
  },
  actions: {
    'cap-stop': () => call('captureStop').catch(toastError),
    'cap-proto': (el) => { S.capture.proto = S.capture.proto === el.dataset.proto ? '' : el.dataset.proto; rerender(); },
    'cap-filter': (el) => { S.capture.q = el.dataset.q; rerender(); },
    'cap-save': async () => {
      try { const r = await call('captureSave'); if (r) toast(r.file, { tone: 'success', title: 'Cattura salvata', action: { label: 'Apri', run: () => call('openFile', { path: r.file }).catch(toastError) } }); } catch (err) { toastError(err); }
    },
    'cap-open': () => call('captureOpen').catch(toastError),
  },
  onInput: (el, field) => {
    const c = S.capture;
    if (field === 'q') { c.q = el.value; renderList(); return; }
    if (field === 'ip') c.ip = el.value;
    if (field === 'port') c.port = el.value;
  },
  onChange: (el, field) => {
    const c = S.capture;
    if (field === 'seconds') c.seconds = Number(el.value) || 0;
    if (field === 'protocol') c.protocol = el.value;
  },
  onSubmit: () => start(),
});
