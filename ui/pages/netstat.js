// Connessioni attive del PC (TCP e UDP) con il processo che le ha aperte; aggiornamento manuale o a intervalli.
import { call } from '../core/bridge.js';
import { $, esc, fmtTime, matchesAll, plural, isRemoteAddress } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { table, nextSort } from '../core/table.js';
import { confirmDialog, emptyState, openMenu, pageHead, toast, toastError } from '../core/ui.js';

S.netstat = { list: [], q: '', state: 'all', auto: 0, busy: false, loadedAt: null, sort: { key: 'processName', dir: 1 }, timer: 0, error: '' };

const STATE_TONES = { ESTABLISHED: 'primary', LISTENING: 'success', TIME_WAIT: '', CLOSE_WAIT: 'warning', SYN_SENT: 'warning', SYN_RECEIVED: 'warning' };

function filtered() {
  const n = S.netstat;
  return n.list.filter((c) => {
    if (n.state === 'ESTABLISHED' && c.state !== 'ESTABLISHED') return false;
    if (n.state === 'LISTENING' && c.state !== 'LISTENING') return false;
    if (n.state === 'other' && (c.state === 'ESTABLISHED' || c.state === 'LISTENING')) return false;
    return matchesAll(n.q, [c.processName, c.localAddress, String(c.localPort), c.remoteAddress, String(c.remotePort), c.state, String(c.processId), c.protocol]);
  });
}

const rowKey = (c) => `${c.protocol}|${c.localAddress}|${c.localPort}|${c.remoteAddress}|${c.remotePort}|${c.processId}`;

const columns = [
  { key: 'protocol', label: 'Protocollo', cell: (c) => `<span class="tag ${c.protocol === 'TCP' ? 'primary' : 'info'}">${esc(c.protocol)}</span>` },
  { key: 'localAddress', label: 'Indirizzo locale', cls: 'mono', value: (c) => c.localAddress, cell: (c) => `${esc(c.localAddress)}<span class="muted">:${c.localPort}</span>` },
  { key: 'localPort', label: 'Porta', cls: 'num' },
  { key: 'remoteAddress', label: 'Indirizzo remoto', cls: 'mono', cell: (c) => c.remoteAddress === '*' || c.remotePort === 0 ? '<span class="faint">—</span>' : `${esc(c.remoteAddress)}<span class="muted">:${c.remotePort}</span>` },
  { key: 'state', label: 'Stato', cell: (c) => `<span class="pill ${STATE_TONES[c.state] ?? ''} pill-dot">${esc(c.state)}</span>` },
  { key: 'processId', label: 'PID', cls: 'num' },
  { key: 'processName', label: 'Processo', cls: 'strong-cell' },
  { key: 'actions', label: '', cls: 'actions', sortable: false, cell: (c) => `<button class="row-menu-btn" data-action="conn-menu" data-key="${esc(rowKey(c))}" title="Azioni">${icon('more')}</button>` },
];

function tableHtml() {
  const n = S.netstat;
  if (n.error) return emptyState('alert', 'Connessioni non lette', esc(n.error));
  if (n.busy && !n.list.length) return '<div class="loading-block"><span class="spinner"></span> Lettura delle connessioni…</div>';
  if (!n.list.length) return emptyState('activity', 'Nessuna connessione', 'Premi Aggiorna per leggere le connessioni attive.');
  const rows = filtered();
  if (!rows.length) return emptyState('search', 'Nessuna connessione corrisponde', 'Cambia la ricerca o il filtro sullo stato.');
  return table({ columns, rows, sort: n.sort, cls: 'list-h', rowAttrs: (c) => `data-ctx="conn" data-key="${esc(rowKey(c))}"` });
}

function pills() {
  const n = S.netstat;
  const count = (f) => n.list.filter(f).length;
  const pill = (key, label, tone, c) => `<button class="pill ${tone} ${n.state === key ? 'is-active' : ''}" data-action="state" data-state="${key}">${label}${c != null ? ` · ${c}` : ''}</button>`;
  return pill('all', 'Tutte', n.state === 'all' ? 'primary' : '', n.list.length)
    + pill('ESTABLISHED', 'Stabilite', 'primary', count((c) => c.state === 'ESTABLISHED'))
    + pill('LISTENING', 'In ascolto', 'success', count((c) => c.state === 'LISTENING'))
    + pill('other', 'Altre', 'warning', count((c) => c.state !== 'ESTABLISHED' && c.state !== 'LISTENING'));
}

function toolbar() {
  const n = S.netstat;
  const seg = (v, label) => `<button type="button" class="${n.auto === v ? 'is-active' : ''}" data-action="auto" data-seconds="${v}">${label}</button>`;
  return `<div class="toolbar">
    <button class="btn btn-sm ${n.busy ? 'is-loading' : ''}" data-action="refresh">${icon('refresh')} Aggiorna</button>
    <div class="segmented" role="group" title="Aggiornamento automatico">${seg(0, 'Manuale')}${seg(2, '2 s')}${seg(5, '5 s')}${seg(10, '10 s')}</div>
    <div class="search">${icon('search')}<input class="input" data-field="q" value="${esc(n.q)}" placeholder="Cerca per processo, indirizzo, porta o PID" autocomplete="off"></div>
    <div class="filter-pills" id="netstat-pills">${pills()}</div>
    <div class="toolbar-actions"><span class="toolbar-info" id="netstat-info"></span></div>
  </div>`;
}

function render() {
  return `${pageHead('activity', 'Connessioni attive', 'Connessioni TCP e porte in ascolto di questo PC, con il processo che le ha aperte.')}
    <div class="grid"><section class="card table-card">${toolbar()}<div id="netstat-table">${tableHtml()}</div>
      <div class="table-foot">${icon('info', { size: 14 })} Dal menu ⋯ scansione delle porte e WHOIS dell'indirizzo remoto, o chiusura del processo.</div></section></div>`;
}

function updateInfo() {
  const n = S.netstat;
  const info = $('#netstat-info');
  if (!info) return;
  const shown = filtered().length;
  info.textContent = n.list.length ? `${shown === n.list.length ? plural(n.list.length, 'connessione', 'connessioni') : `${shown} di ${n.list.length}`}${n.loadedAt ? ` · alle ${fmtTime(n.loadedAt)}` : ''}` : '';
}

function renderTable() {
  const box = $('#netstat-table');
  if (box) box.innerHTML = tableHtml();
  const p = $('#netstat-pills');
  if (p) p.innerHTML = pills();
  updateInfo();
}

function renderToolbar() {
  const t = $('.toolbar');
  if (t && S.page === 'netstat') t.outerHTML = toolbar();
  updateInfo();
}

async function refresh(silent = false) {
  const n = S.netstat;
  if (n.busy) return;
  n.busy = true;
  n.error = '';
  if (!silent) renderToolbar();
  try {
    const list = await call('netstat');
    n.list = list;
    n.loadedAt = new Date().toISOString();
  } catch (err) {
    n.error = err.message;
    if (!silent) toastError(err, 'Connessioni non lette');
  }
  n.busy = false;
  if (S.page === 'netstat') { renderToolbar(); renderTable(); }
}

function setAuto(seconds) {
  const n = S.netstat;
  n.auto = seconds;
  clearInterval(n.timer);
  n.timer = 0;
  if (seconds > 0) n.timer = setInterval(() => { if (S.page === 'netstat') refresh(true); }, seconds * 1000);
}

function connMenu(anchor, k) {
  const c = S.netstat.list.find((x) => rowKey(x) === k);
  if (!c) return;
  const remote = isRemoteAddress(c.remoteAddress);
  openMenu(anchor, [
    { header: `${c.processName} (PID ${c.processId})` },
    { ic: 'plug', label: 'Scansiona l\'indirizzo remoto', disabled: !remote, run: () => navigate('ports', { host: c.remoteAddress, start: true }) },
    { ic: 'globe', label: 'WHOIS dell\'indirizzo remoto', disabled: !remote, run: () => navigate('whois', { target: c.remoteAddress, start: true }) },
    { ic: 'copy', label: 'Copia l\'indirizzo remoto', disabled: !remote, run: () => call('setClipboard', { text: c.remoteAddress }).then(() => toast('Indirizzo copiato.', { tone: 'success' }), toastError) },
    { sep: true },
    { ic: 'external', label: 'Apri l\'indirizzo remoto nel browser', disabled: !remote, run: () => call('launch', { kind: c.remotePort === 443 ? 'https' : 'http', host: c.remoteAddress, port: c.remotePort }).catch(toastError) },
    { ic: 'activity', label: 'Ping continuo (prompt)', disabled: !remote, run: () => call('launch', { kind: 'ping', host: c.remoteAddress, port: 0 }).catch(toastError) },
    { sep: true },
    { ic: 'power', label: 'Termina il processo…', danger: true, disabled: c.processId <= 4, run: () => killProcess(c) },
  ]);
}

async function killProcess(c) {
  const ok = await confirmDialog({
    title: `Terminare ${c.processName}?`, tone: 'danger', confirmText: 'Termina il processo',
    message: `Il processo ${c.processName} (PID ${c.processId}) verrà chiuso subito, senza salvare quello che stava facendo.`,
  });
  if (!ok) return;
  try {
    const r = await call('killProcess', { pid: c.processId });
    toast(`${r.name} (PID ${r.pid}) terminato.`, { tone: 'success' });
    refresh(true);
  } catch (err) { toastError(err, 'Processo non terminato'); }
}

const actions = {
  'refresh': () => refresh(),
  'auto': (el) => { setAuto(Number(el.dataset.seconds)); renderToolbar(); },
  'state': (el) => { S.netstat.state = el.dataset.state; renderTable(); },
  'conn-menu': (el) => connMenu(el, el.dataset.key),
};

definePage('netstat', {
  title: 'Connessioni', icon: 'activity', section: 'system', order: 1,
  render, after: () => { updateInfo(); if (!S.netstat.list.length && !S.netstat.busy) refresh(); }, actions,
  leave: () => { /* il timer resta: refresh() non fa nulla fuori dalla pagina */ },
  onInput: (el, field) => { if (field === 'q') { S.netstat.q = el.value; renderTable(); } },
  onSort: (k) => { S.netstat.sort = nextSort(S.netstat.sort, k); renderTable(); },
  onContext: (row, point) => connMenu(point, row.dataset.key),
});
