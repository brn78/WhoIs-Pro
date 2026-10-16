// Scansione delle porte di un host: TCP e UDP, servizio e banner.
import { call, on } from '../core/bridge.js';
import { $, esc, fmtMs, fmtNumber, fmtSeconds, matchesAll, plural } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage, adopt, markDirty, bus, emit } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { table, nextSort } from '../core/table.js';
import { confirmDialog, emptyState, openMenu, pageHead, toast, toastError } from '../core/ui.js';

S.ports = {
  host: '', preset: 'TOP20', ports: '', tcp: true, udp: false, banner: true, threads: 100, timeout: 1200,
  q: '', filterHost: '', sort: { key: 'portNumber', dir: 1 },
  progress: null, done: null, fresh: new Set(),
  editing: false,      // modulo riaperto con «Modifica» dopo una scansione
  last: null,          // parametri dell'ultima scansione avviata (per la riga di riepilogo)
};

const running = () => !!(S.st && S.st.running.ports);
const presets = () => (S.st && S.st.presets.ports) || [];

function ensurePorts() {
  if (!S.ports.ports) {
    const p = presets().find((x) => x.key === S.ports.preset);
    if (p) S.ports.ports = p.ports;
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Modulo di avvio: completo finché non c'è stata nessuna scansione, poi ripiegato in una riga di riepilogo
// ---------------------------------------------------------------------------------------------------------------------
function formCard() {
  const p = S.ports;
  if (running() || (p.done && !p.editing)) return `<section class="card scan-summary" id="scan-card">${summaryInner()}</section>`;
  ensurePorts();
  const options = presets().map((x) => `<option value="${esc(x.key)}" ${p.preset === x.key ? 'selected' : ''}>${esc(x.name)}</option>`).join('')
    + `<option value="CUSTOM" ${p.preset === 'CUSTOM' ? 'selected' : ''}>Personalizzato</option>`;
  const count = fmtNumber(countPorts(p.ports));
  return `<section class="card" id="scan-card"><div class="card-body">
    <form data-form="scan" class="scan-form ports-form" autocomplete="off">
      <div class="field"><label for="f-host">Host</label>
        <input class="input input-mono" id="f-host" data-field="host" value="${esc(p.host)}" placeholder="192.168.1.10 oppure nome.dominio"></div>
      <div class="field"><label for="f-preset">Gruppo di porte</label>
        <select class="select" id="f-preset" data-field="preset">${options}</select></div>
      <div class="field"><label for="f-threads">Thread</label><input class="input" id="f-threads" type="number" min="1" max="1000" data-field="threads" value="${p.threads}"></div>
      <div class="field"><label for="f-timeout">Timeout (ms)</label><input class="input" id="f-timeout" type="number" min="100" max="10000" step="100" data-field="timeout" value="${p.timeout}"></div>
      <div class="field field-wide"><div class="field-label-row"><label for="f-ports">Porte</label><span class="hint" id="ports-count">${count} porte</span></div>
        <textarea class="input ports" id="f-ports" data-field="ports" rows="2" placeholder="22, 80, 443, 8000-8100">${esc(p.ports)}</textarea></div>
      <div class="field field-switches-row"><span class="field-label">Protocolli e opzioni</span><div class="field-switches">
        <label class="switch"><input type="checkbox" data-field="tcp" ${p.tcp ? 'checked' : ''}><span class="track"></span><span>TCP</span></label>
        <label class="switch"><input type="checkbox" data-field="udp" ${p.udp ? 'checked' : ''}><span class="track"></span><span>UDP</span></label>
        <label class="switch"><input type="checkbox" data-field="banner" ${p.banner ? 'checked' : ''}><span class="track"></span><span>Leggi il banner</span></label></div></div>
      <div class="field field-actions-end"><span class="field-label">&nbsp;</span><div class="field-actions">
        ${p.done ? `<button type="button" class="btn" data-action="scan-collapse" title="Torna al riepilogo dell'ultima scansione">${icon('close')} Annulla</button>` : ''}
        <button type="submit" class="btn btn-primary">${icon('play')} Avvia</button>
      </div></div>
    </form>
  </div></section>`;
}

/** Conta le porte scritte nel campo (singole e intervalli), come fa il programma. */
function countPorts(text) {
  const seen = new Set();
  for (const tok of String(text || '').split(/[\s,;]+/).filter(Boolean)) {
    const m = /^(\d+)-(\d+)$/.exec(tok);
    if (m) {
      const a = Math.max(1, +m[1]); const b = Math.min(65535, +m[2]);
      if (b - a > 70000) continue;
      for (let i = a; i <= b; i++) seen.add(i);
    } else if (/^\d+$/.test(tok)) {
      const n = +tok;
      if (n >= 1 && n <= 65535) seen.add(n);
    }
  }
  return seen.size;
}

function runningStats() {
  const p = S.ports.progress;
  if (!p) return 'avvio…';
  return `${fmtNumber(p.scanned)} / ${fmtNumber(p.total)} porte · ${fmtSeconds(p.seconds)}${p.currentPort ? ` · porta ${esc(String(p.currentPort))}` : ''}`;
}

/** Contenuto della riga di riepilogo: avanzamento e «Ferma» durante la scansione, esito e parametri dopo. */
function summaryInner() {
  const s = S.ports;
  if (running()) {
    const pct = s.progress ? s.progress.percent : 0;
    return `<span class="scan-state is-busy"><span class="spinner"></span>In corso · <span data-pct>${pct}%</span></span><span class="vsep"></span>
      <span class="chip" title="Host"><b>${esc((s.last || s).host)}</b></span>
      <span class="meter"><span data-meter style="width:${pct}%"></span></span>
      <span class="scan-stat mono" data-stats>${runningStats()}</span>
      <span class="scan-actions"><button type="button" class="btn btn-danger" data-action="scan-stop">${icon('stop')} Ferma</button></span>`;
  }
  const d = s.done;
  const p = s.last || s;
  const text = d.error ? `Interrotta per un errore: ${esc(d.error)}`
    : d.cancelled ? `Fermata dopo ${fmtSeconds(d.seconds)} · ${plural(d.found, 'porta aperta', 'porte aperte')}`
      : `Completata in ${fmtSeconds(d.seconds)} · ${plural(d.found, 'porta aperta', 'porte aperte')}`;
  const tone = d.error ? 'danger' : d.cancelled ? 'warning' : 'success';
  const preset = presets().find((x) => x.key === p.preset);
  const group = preset ? preset.name : 'Personalizzato';
  const protos = [p.tcp ? 'TCP' : '', p.udp ? 'UDP' : ''].filter(Boolean).join(' + ') || 'nessun protocollo';
  return `<span class="scan-state ${tone}"><span class="dot"></span>${text}</span><span class="vsep"></span>
    <span class="chips">
      <span class="chip" title="Host"><b>${esc(d.host || p.host)}</b></span>
      <span class="chip" title="${esc(p.ports)}">${esc(group)} · ${fmtNumber(countPorts(p.ports))} porte · ${protos}</span>
      <span class="chip" title="Thread in parallelo e timeout di connessione">${fmtNumber(p.threads)} thread · ${fmtNumber(p.timeout)} ms</span>
      ${p.banner ? `<span class="chip" title="Banner letto dal server">${icon('check')} Banner</span>` : ''}
    </span>
    <span class="scan-actions">
      <button type="button" class="btn" data-action="scan-edit">${icon('edit')} Modifica</button>
      <button type="button" class="btn btn-primary" data-action="scan-restart">${icon('play')} Riavvia</button>
    </span>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Tabella delle porte
// ---------------------------------------------------------------------------------------------------------------------
function hosts() { return Array.from(new Set(S.data.ports.map((p) => p.host))); }

function filteredPorts() {
  const q = S.ports.q.trim();
  const fh = S.ports.filterHost;
  return S.data.ports.filter((p) => (!fh || p.host === fh) && matchesAll(q, [p.host, String(p.portNumber), p.protocol, p.serviceName, p.banner]));
}

const statusPill = (p) => (p.status === 'Open'
  ? '<span class="pill success pill-dot">Aperta</span>'
  : `<span class="pill warning pill-dot">${p.status === 'Open|Filtered' ? 'Aperta o filtrata' : esc(p.status)}</span>`);

const columns = [
  { key: 'host', label: 'Host', cls: 'mono' },
  { key: 'portNumber', label: 'Porta', cls: 'num strong-cell' },
  { key: 'protocol', label: 'Protocollo', cell: (p) => `<span class="tag ${p.protocol === 'TCP' ? 'primary' : 'info'}">${esc(p.protocol)}</span>` },
  { key: 'status', label: 'Stato', cell: statusPill },
  { key: 'serviceName', label: 'Servizio', cls: 'strong-cell' },
  { key: 'banner', label: 'Banner', cls: 'wrap-mono', cell: (p) => p.banner ? esc(p.banner) : '<span class="faint">—</span>' },
  { key: 'responseTimeMs', label: 'Latenza', cls: 'num', cell: (p) => fmtMs(p.responseTimeMs) },
  { key: 'actions', label: '', cls: 'actions', sortable: false, cell: (p) => `<button class="row-menu-btn" data-action="port-menu" data-key="${esc(key(p))}" title="Azioni">${icon('more')}</button>` },
];

const key = (p) => `${p.host}|${p.portNumber}|${p.protocol}`;

function tableHtml() {
  if (!S.data.ports.length) return emptyState('plug', 'Nessuna porta aperta nella sessione', 'Scegli un host e un gruppo di porte, poi avvia la scansione. Le porte aperte compaiono qui man mano.');
  const rows = filteredPorts();
  if (!rows.length) return emptyState('search', 'Nessuna porta corrisponde', 'Cambia la ricerca o il filtro per host.');
  return table({ columns, rows, sort: S.ports.sort, cls: 'scan-h', rowAttrs: (p) => `data-ctx="port" data-key="${esc(key(p))}" class="${S.ports.fresh.has(key(p)) ? 'is-new' : ''}"` });
}

function tableCard() {
  const hs = hosts();
  const hostFilter = hs.length > 1 ? `<div class="field"><label>Host</label><select class="select input-sm" data-field="filterHost">
      <option value="">Tutti (${hs.length})</option>${hs.map((h) => `<option value="${esc(h)}" ${S.ports.filterHost === h ? 'selected' : ''}>${esc(h)}</option>`).join('')}</select></div>` : '';
  return `<section class="card table-card">
    <div class="toolbar">
      <div class="search">${icon('search')}<input class="input" data-field="q" value="${esc(S.ports.q)}" placeholder="Cerca per porta, servizio o banner" autocomplete="off"></div>
      ${hostFilter}
      <div class="toolbar-actions"><span class="toolbar-info" id="ports-info"></span>
        <button class="btn btn-sm" data-action="clear-ports" ${S.data.ports.length ? '' : 'disabled'}>${icon('trash')} Svuota</button></div>
    </div>
    <div id="ports-table">${tableHtml()}</div>
  </section>`;
}

function render() {
  return `${pageHead('Porte TCP e UDP aperte di un host, con il servizio e il banner letto dal server.')}
    <div class="grid">${formCard()}${tableCard()}</div>`;
}

function updateInfo() {
  const info = $('#ports-info');
  if (!info) return;
  const total = S.data.ports.length;
  const shown = filteredPorts().length;
  info.textContent = total ? (shown === total ? plural(total, 'porta aperta', 'porte aperte') : `${shown} di ${total} porte`) : '';
}

function renderTable() {
  const box = $('#ports-table');
  if (!box) return;
  box.innerHTML = tableHtml();
  updateInfo();
  const clear = $('[data-action="clear-ports"]');
  if (clear) clear.disabled = !S.data.ports.length;
}

function renderTableCard() {
  const card = $('#ports-table');
  if (card) card.closest('section.card').outerHTML = tableCard();
  updateInfo();
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
  const pct = S.ports.progress ? S.ports.progress.percent : 0;
  meter.style.width = `${pct}%`;
  $('[data-pct]', card).textContent = `${pct}%`;
  $('[data-stats]', card).innerHTML = runningStats();
}

async function start() {
  const p = S.ports;
  if (running()) return;
  ensurePorts();
  try {
    const res = await call('scanPorts', { host: p.host, ports: p.ports, tcp: p.tcp, udp: p.udp, banner: p.banner, threads: p.threads, timeout: p.timeout });
    adopt({ state: res.state });
    p.last = { host: p.host, preset: p.preset, ports: p.ports, tcp: p.tcp, udp: p.udp, banner: p.banner, threads: p.threads, timeout: p.timeout };
    p.progress = { scanned: 0, total: res.total, percent: 0, seconds: 0, currentPort: '' };
    p.done = null;
    p.editing = false;
    p.fresh.clear();
    renderForm();
    emit('state');
  } catch (err) { toastError(err, 'Scansione non avviata'); }
}

function portMenu(anchor, k) {
  const p = S.data.ports.find((x) => key(x) === k);
  if (!p) return;
  const tcp = p.protocol === 'TCP';
  const open = (kind) => () => call('launch', { kind, host: p.host, port: p.portNumber }).catch((e) => toastError(e));
  const special = [];
  if (tcp && p.portNumber === 22) special.push({ ic: 'terminal', label: 'Terminale SSH su questa porta', run: open('ssh') });
  if (tcp && p.portNumber === 3389) special.push({ ic: 'monitor', label: 'Desktop remoto', run: open('rdp') });
  if (tcp && (p.portNumber === 445 || p.portNumber === 139)) special.push({ ic: 'folderOpen', label: 'Cartelle condivise (Esplora risorse)', run: open('explorer') });
  if (tcp && p.portNumber === 21) special.push({ ic: 'folderOpen', label: 'Apri FTP in Esplora risorse', run: open('ftp') });
  if (tcp && p.portNumber === 23) special.push({ ic: 'terminal', label: 'Telnet (prompt)', run: open('telnet') });
  openMenu(anchor, [
    { header: `${p.host}:${p.portNumber}/${p.protocol} · ${p.serviceName}` },
    { ic: 'external', label: 'Apri nel browser (http)', disabled: !tcp, run: open('http') },
    { ic: 'external', label: 'Apri nel browser (https)', disabled: !tcp, run: open('https') },
    ...special,
    { sep: true },
    { ic: 'copy', label: 'Copia il banner', disabled: !p.banner, run: () => copy(p.banner, 'Banner copiato.') },
    { ic: 'copy', label: 'Copia host:porta', run: () => copy(`${p.host}:${p.portNumber}`, 'Copiato.') },
    { sep: true },
    { ic: 'globe', label: 'WHOIS dell\'host', run: () => navigate('whois', { target: p.host, start: true }) },
  ]);
}

async function copy(text, done) {
  try { await call('setClipboard', { text }); toast(done, { tone: 'success' }); } catch (err) { toastError(err); }
}

/** Riapre il modulo completo (dopo una scansione) e porta il cursore sul campo dell'host. */
function edit() {
  S.ports.editing = true;
  renderForm();
  const f = $('#f-host');
  if (f) { f.focus(); f.select(); }
}

const actions = {
  'scan-stop': async () => { try { await call('stopPorts'); } catch (err) { toastError(err); } },
  'scan-edit': () => edit(),
  'scan-collapse': () => { S.ports.editing = false; renderForm(); },
  'scan-restart': () => start(),
  'clear-ports': async () => {
    if (!(await confirmDialog({ title: 'Svuotare l\'elenco delle porte?', message: `${plural(S.data.ports.length, 'porta', 'porte')} verranno tolte dalla sessione.`, confirmText: 'Svuota', tone: 'danger' }))) return;
    try { adopt(await call('clearList', { list: 'ports' })); S.ports.done = null; S.ports.editing = false; S.ports.filterHost = ''; renderTableCard(); renderForm(); } catch (err) { toastError(err); }
  },
  'port-menu': (el) => portMenu(el, el.dataset.key),
};

on('ports.progress', (d) => {
  S.ports.progress = d;
  if (d.ports && d.ports.length) {
    for (const p of d.ports) {
      const i = S.data.ports.findIndex((x) => key(x) === key(p));
      if (i >= 0) S.data.ports[i] = p; else S.data.ports.push(p);
      S.ports.fresh.add(key(p));
    }
    markDirty();
    emit('data', { list: 'ports' });
    if (S.page === 'ports') renderTable();
  }
  if (S.page === 'ports') updateBar();
});

on('ports.done', (d) => {
  S.ports.done = d;
  S.ports.progress = null;
  S.ports.editing = false;
  adopt({ state: d.state });
  if (S.page === 'ports') { renderForm(); renderTableCard(); }
  if (d.error) toast(d.error, { tone: 'error', title: 'Scansione interrotta' });
  else if (d.cancelled) toast(`${plural(d.found, 'porta aperta', 'porte aperte')} fino a quel momento.`, { tone: 'warning', title: `Scansione di ${d.host} fermata` });
  else toast(`${plural(d.found, 'porta aperta', 'porte aperte')} in ${fmtSeconds(d.seconds)}.`, { tone: 'success', title: `Porte di ${d.host}` });
  setTimeout(() => { S.ports.fresh.clear(); }, 2000);
});

bus.addEventListener('session', () => {
  S.ports.done = null; S.ports.editing = false; S.ports.filterHost = ''; S.ports.fresh.clear();
  if (S.page === 'ports') { renderTableCard(); renderForm(); }
});

definePage('ports', {
  title: 'Porte', heading: 'Scansione delle porte', icon: 'plug', section: 'scan', badge: () => S.data.ports.length,
  render, after: updateInfo, actions,
  enter: (params) => {
    if (params.host) {
      S.ports.host = params.host;
      if (params.start && !running()) start();
      else if (!running()) edit();
    }
  },
  onInput: (el, field) => {
    const p = S.ports;
    if (field === 'q') { p.q = el.value; renderTable(); return; }
    if (field === 'host') p.host = el.value;
    if (field === 'threads') p.threads = Number(el.value) || 100;
    if (field === 'timeout') p.timeout = Number(el.value) || 1200;
    if (field === 'ports') {
      p.ports = el.value;
      p.preset = 'CUSTOM';
      const sel = $('#f-preset'); if (sel) sel.value = 'CUSTOM';
      const c = $('#ports-count'); if (c) c.textContent = `${fmtNumber(countPorts(p.ports))} porte`;
    }
  },
  onChange: (el, field) => {
    const p = S.ports;
    if (field === 'tcp' || field === 'udp' || field === 'banner') p[field] = el.checked;
    if (field === 'preset') {
      p.preset = el.value;
      const preset = presets().find((x) => x.key === el.value);
      if (preset) { p.ports = preset.ports; const t = $('#f-ports'); if (t) t.value = p.ports; const c = $('#ports-count'); if (c) c.textContent = `${fmtNumber(countPorts(p.ports))} porte`; }
    }
    if (field === 'filterHost') { p.filterHost = el.value; renderTable(); }
  },
  onSubmit: () => start(),
  onSort: (k) => { S.ports.sort = nextSort(S.ports.sort, k); renderTable(); },
  onContext: (row, point) => portMenu(point, row.dataset.key),
});
