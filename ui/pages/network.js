// Scansione della rete: host attivi con MAC, produttore, nome e tipo di dispositivo.
import { call, on } from '../core/bridge.js';
import { $, esc, fmtMs, fmtNumber, fmtSeconds, fmtDateTime, matchesAll, plural, isIp } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { catIcon } from '../core/categories.js';
import { S, definePage, adopt, markDirty, bus, emit } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { table, nextSort } from '../core/table.js';
import { closeOverlays, confirmDialog, emptyState, openDrawer, openMenu, pageHead, toast, toastError } from '../core/ui.js';

S.net = {
  target: '', threads: 80, timeout: 900, resolveDns: true,
  q: '', sort: { key: 'ipAddress', dir: 1 },
  progress: null, done: null, fresh: new Set(),
};

const running = () => !!(S.st && S.st.running.network);

function formCard() {
  const n = S.net;
  const list = (S.st.adapters || []).filter((a) => a.cidr).map((a) => `<option value="${esc(a.cidr)}">${esc(a.name)} · ${esc(a.ip)}</option>`).join('');
  if (!n.target) n.target = (S.st.adapters.find((a) => a.cidr) || {}).cidr || '192.168.1.0/24';
  return `<section class="card"><div class="card-body">
    <form data-form="scan" class="scan-form" autocomplete="off">
      <div class="field"><label for="f-target">Rete o intervallo</label>
        <div class="input-with-button">
          <input class="input input-mono" id="f-target" data-field="target" list="adapter-list" value="${esc(n.target)}" placeholder="192.168.1.0/24 · 192.168.1.1-50 · 10.0.0.5" ${running() ? 'disabled' : ''}>
          <datalist id="adapter-list">${list}</datalist>
          <button type="button" class="btn btn-icon" data-action="detect-adapters" title="Rileva di nuovo le schede di rete">${icon('refresh')}</button>
        </div></div>
      <div class="field"><label for="f-threads">Thread</label><input class="input" id="f-threads" type="number" min="1" max="500" data-field="threads" value="${n.threads}" ${running() ? 'disabled' : ''}></div>
      <div class="field"><label for="f-timeout">Timeout (ms)</label><input class="input" id="f-timeout" type="number" min="100" max="10000" step="100" data-field="timeout" value="${n.timeout}" ${running() ? 'disabled' : ''}></div>
      <div class="field"><span class="field-label">Opzioni</span><div class="field-switches">
        <label class="switch"><input type="checkbox" data-field="resolveDns" ${n.resolveDns ? 'checked' : ''} ${running() ? 'disabled' : ''}><span class="track"></span><span>Risolvi i nomi (DNS)</span></label></div></div>
      <div class="field"><span class="field-label">&nbsp;</span><div class="field-actions">
        ${running()
          ? `<button type="button" class="btn btn-danger" data-action="scan-stop">${icon('stop')} Ferma</button>`
          : `<button type="submit" class="btn btn-primary">${icon('play')} Avvia</button>`}
      </div></div>
    </form>
    <div id="scan-bar">${scanBar()}</div>
  </div></section>`;
}

function scanBar() {
  const p = S.net.progress;
  const d = S.net.done;
  if (running() && p) {
    return `<div class="scan-bar"><div class="meter"><span style="width:${p.percent}%"></span></div>
      <span class="scan-stat"><span class="spinner"></span><b>${p.percent}%</b></span>
      <span class="scan-stat">${fmtNumber(p.scanned)} / ${fmtNumber(p.total)} indirizzi</span>
      <span class="scan-stat">${fmtSeconds(p.seconds)}</span>
      <span class="scan-stat mono">${esc(p.currentIp || '')}</span></div>`;
  }
  if (running()) return `<div class="scan-bar"><span class="scan-stat"><span class="spinner"></span> Avvio della scansione…</span></div>`;
  if (d) {
    const text = d.error ? `Scansione interrotta per un errore: ${esc(d.error)}`
      : d.cancelled ? `Scansione fermata dopo ${fmtSeconds(d.seconds)}: ${plural(d.found, 'host attivo', 'host attivi')} fino a quel momento.`
        : `Scansione completata in ${fmtSeconds(d.seconds)}: ${plural(d.found, 'host attivo', 'host attivi')}.`;
    const tone = d.error ? 'danger' : d.cancelled ? 'warning' : 'success';
    return `<div class="scan-bar"><span class="scan-stat"><span class="pill ${tone} pill-dot">${text}</span></span></div>`;
  }
  return '';
}

function filteredHosts() {
  const q = S.net.q.trim();
  return S.data.hosts.filter((h) => matchesAll(q, [h.ipAddress, h.hostname, h.macAddress, h.macVendor, h.deviceType, h.deviceCategory, h.osGuess]));
}

const columns = [
  { key: 'deviceType', label: 'Dispositivo', cell: (h) => `<div class="cell-main">${catIcon(h)}<div class="cell-text"><b>${esc(h.deviceType)}</b><small>${esc(h.deviceCategory || 'Altro')}</small></div></div>` },
  { key: 'ipAddress', label: 'Indirizzo IP', cls: 'mono strong-cell' },
  { key: 'hostname', label: 'Nome', cell: (h) => h.hostname ? esc(h.hostname) : '<span class="faint">—</span>' },
  { key: 'macAddress', label: 'MAC', cls: 'mono', cell: (h) => h.macAddress ? esc(h.macAddress) : '<span class="faint">—</span>' },
  { key: 'macVendor', label: 'Produttore', cls: 'wrap', cell: (h) => esc(h.macVendor) },
  { key: 'responseTimeMs', label: 'Latenza', cls: 'num', cell: (h) => fmtMs(h.responseTimeMs) },
  { key: 'ttl', label: 'TTL', cls: 'num', cell: (h) => h.ttl || '<span class="faint">—</span>' },
  { key: 'osGuess', label: 'Sistema', cell: (h) => esc(h.osGuess) },
  { key: 'actions', label: '', cls: 'actions', sortable: false, cell: (h) => `<button class="row-menu-btn" data-action="host-menu" data-ip="${esc(h.ipAddress)}" title="Azioni">${icon('more')}</button>` },
];

function tableHtml() {
  const rows = filteredHosts();
  if (!S.data.hosts.length) {
    return emptyState('radar', 'Nessun host nella sessione', 'Avvia una scansione della rete: gli host attivi compaiono qui man mano che rispondono.');
  }
  if (!rows.length) return emptyState('search', 'Nessun host corrisponde', 'Cambia le parole della ricerca.');
  return table({
    columns, rows, sort: S.net.sort, cls: 'scan-h',
    rowAttrs: (h) => `data-action="host-details" data-ip="${esc(h.ipAddress)}" class="${S.net.fresh.has(h.ipAddress) ? 'is-new' : ''}"`,
  }).replace('<table class="dt">', '<table class="dt clickable">');
}

function tableCard() {
  return `<section class="card table-card">
    <div class="toolbar">
      <div class="search">${icon('search')}<input class="input" data-field="q" value="${esc(S.net.q)}" placeholder="Cerca per IP, nome, MAC, produttore o tipo (più parole = tutte)" autocomplete="off"></div>
      <div class="toolbar-actions"><span class="toolbar-info" id="hosts-info"></span>
        <button class="btn btn-sm" data-action="clear-hosts" ${S.data.hosts.length ? '' : 'disabled'}>${icon('trash')} Svuota</button></div>
    </div>
    <div id="hosts-table">${tableHtml()}</div>
    <div class="table-foot">${icon('info', { size: 14 })} Clic su una riga per i dettagli; dal menu ⋯ scansione delle porte, WHOIS e traceroute dell'host.</div>
  </section>`;
}

function render() {
  return `${pageHead('radar', 'Scansione della rete', 'Host attivi della rete (ARP e ping), con produttore dal MAC, nome DNS, latenza e tipo di dispositivo.')}
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
  box.innerHTML = tableHtml();
  updateInfo();
  const clear = $('[data-action="clear-hosts"]');
  if (clear) clear.disabled = !S.data.hosts.length;
}

function renderForm() {
  const card = $('[data-form="scan"]');
  if (!card) return;
  card.closest('section.card').outerHTML = formCard();
}

function updateBar() {
  const bar = $('#scan-bar');
  if (bar) bar.innerHTML = scanBar();
}

async function start() {
  const n = S.net;
  if (running()) return;
  try {
    const res = await call('scanNetwork', { target: n.target, threads: n.threads, timeout: n.timeout, resolveDns: n.resolveDns });
    adopt({ state: res.state });
    n.progress = { scanned: 0, total: res.total, percent: 0, seconds: 0, currentIp: '' };
    n.done = null;
    n.fresh.clear();
    renderForm();
    emit('state');
  } catch (err) { toastError(err, 'Scansione non avviata'); }
}

async function stop() {
  try { await call('stopNetwork'); } catch (err) { toastError(err); }
}

function hostByIp(ip) { return S.data.hosts.find((h) => h.ipAddress === ip); }

function hostMenu(anchor, ip) {
  const h = hostByIp(ip);
  if (!h) return;
  openMenu(anchor, [
    { header: h.ipAddress },
    { ic: 'plug', label: 'Scansiona le porte', run: () => navigate('ports', { host: ip, start: true }) },
    { ic: 'globe', label: 'WHOIS', run: () => navigate('whois', { target: ip, start: true }) },
    { ic: 'route', label: 'Traceroute', run: () => navigate('trace', { target: ip, start: true }) },
    { sep: true },
    { ic: 'external', label: 'Apri nel browser (http)', run: () => call('openUrl', { url: `http://${ip}/` }) },
    { ic: 'external', label: 'Apri nel browser (https)', run: () => call('openUrl', { url: `https://${ip}/` }) },
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
  const ports = S.data.ports.filter((p) => p.host === ip);
  const body = `<div class="fields">
    <dl class="kv">
      <dt>Indirizzo IP</dt><dd class="mono">${esc(h.ipAddress)}</dd>
      <dt>Nome</dt><dd>${h.hostname ? esc(h.hostname) : '<span class="muted">non risolto</span>'}</dd>
      <dt>MAC</dt><dd class="mono">${h.macAddress ? esc(h.macAddress) : '<span class="muted">non disponibile (host fuori dalla rete locale)</span>'}</dd>
      <dt>Produttore</dt><dd>${esc(h.macVendor)}</dd>
      <dt>Dispositivo</dt><dd>${esc(h.deviceType)} <span class="tag">${esc(h.deviceCategory || 'Altro')}</span></dd>
      <dt>Sistema stimato</dt><dd>${esc(h.osGuess)}</dd>
      <dt>Latenza</dt><dd>${fmtMs(h.responseTimeMs)}</dd>
      <dt>TTL</dt><dd>${h.ttl || '—'}</dd>
      <dt>Visto la prima volta</dt><dd>${fmtDateTime(h.firstSeen)}</dd>
      <dt>Ultima volta</dt><dd>${fmtDateTime(h.lastSeen)}</dd>
    </dl>
    <div><div class="form-section-head"><div class="form-section-title">${icon('plug')}Porte aperte nella sessione</div></div>
      ${ports.length ? `<div class="pill-list">${ports.map((p) => `<span class="pill ${p.status === 'Open' ? 'success' : 'warning'}">${p.portNumber}/${esc(p.protocol)} · ${esc(p.serviceName)}</span>`).join('')}</div>`
        : '<p class="card-text muted">Nessuna scansione delle porte per questo host.</p>'}</div>
  </div>`;
  openDrawer({
    ic: 'monitor', title: h.ipAddress, sub: `${h.deviceType}${h.hostname ? ` · ${h.hostname}` : ''}`, body,
    foot: `<div class="left">
        <button class="btn btn-primary" data-action="host-ports" data-ip="${esc(ip)}">${icon('plug')} Scansiona le porte</button>
        <button class="btn" data-action="host-whois" data-ip="${esc(ip)}">${icon('globe')} WHOIS</button>
        <button class="btn" data-action="host-trace" data-ip="${esc(ip)}">${icon('route')} Traceroute</button></div>
      <button class="btn btn-ghost" data-action="close-overlay">Chiudi</button>`,
  });
}

const actions = {
  'scan-stop': () => stop(),
  'detect-adapters': async () => {
    try { S.st.adapters = await call('adapters'); renderForm(); toast(plural(S.st.adapters.length, 'scheda di rete attiva', 'schede di rete attive'), { tone: 'success' }); } catch (err) { toastError(err); }
  },
  'clear-hosts': async () => {
    if (!(await confirmDialog({ title: 'Svuotare l\'elenco degli host?', message: `${plural(S.data.hosts.length, 'host', 'host')} verranno tolti dalla sessione. Le porte trovate restano.`, confirmText: 'Svuota', tone: 'danger' }))) return;
    try { adopt(await call('clearList', { list: 'hosts' })); S.net.done = null; renderTable(); updateBar(); } catch (err) { toastError(err); }
  },
  'host-menu': (el) => hostMenu(el, el.dataset.ip),
  'host-details': (el) => details(el.dataset.ip),
  'host-ports': (el) => navigate('ports', { host: el.dataset.ip, start: true }),
  'host-whois': (el) => navigate('whois', { target: el.dataset.ip, start: true }),
  'host-trace': (el) => navigate('trace', { target: el.dataset.ip, start: true }),
};

on('network.progress', (d) => {
  S.net.progress = d;
  if (d.hosts && d.hosts.length) {
    for (const h of d.hosts) {
      const i = S.data.hosts.findIndex((x) => x.ipAddress === h.ipAddress);
      if (i >= 0) S.data.hosts[i] = h; else S.data.hosts.push(h);
      S.net.fresh.add(h.ipAddress);
    }
    markDirty();
    emit('data', { list: 'hosts' });
    if (S.page === 'network') renderTable();
  }
  if (S.page === 'network') updateBar();
});

on('network.done', (d) => {
  S.net.done = d;
  S.net.progress = null;
  adopt({ state: d.state });
  if (S.page === 'network') { renderForm(); renderTable(); }
  if (d.error) toast(d.error, { tone: 'error', title: 'Scansione interrotta' });
  else if (d.cancelled) toast(`${plural(d.found, 'host attivo', 'host attivi')} fino a quel momento.`, { tone: 'warning', title: 'Scansione fermata' });
  else toast(`${plural(d.found, 'host attivo', 'host attivi')} in ${fmtSeconds(d.seconds)}.`, { tone: 'success', title: 'Scansione della rete completata' });
  setTimeout(() => { S.net.fresh.clear(); }, 2000);
});

bus.addEventListener('session', () => { S.net.done = null; S.net.fresh.clear(); if (S.page === 'network') renderTable(); });

definePage('network', {
  title: 'Rete', icon: 'radar', section: 'scan', badge: () => S.data.hosts.length,
  render, after, actions,
  enter: (params) => { if (params.target) { S.net.target = params.target; renderForm(); } },
  onInput: (el, field) => {
    const n = S.net;
    if (field === 'q') { n.q = el.value; renderTable(); return; }
    if (field === 'target') n.target = el.value;
    if (field === 'threads') n.threads = Number(el.value) || 80;
    if (field === 'timeout') n.timeout = Number(el.value) || 900;
  },
  onChange: (el, field) => { if (field === 'resolveDns') S.net.resolveDns = el.checked; },
  onSubmit: () => start(),
  onSort: (key) => { S.net.sort = nextSort(S.net.sort, key); renderTable(); },
});
