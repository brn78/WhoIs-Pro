// Traceroute: il percorso verso una destinazione, salto per salto, con tre tempi per salto.
import { call, on } from '../core/bridge.js';
import { $, esc, plural, isRemoteAddress } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage, adopt, markDirty, bus, emit } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { table } from '../core/table.js';
import { confirmDialog, emptyState, openMenu, pageHead, toast, toastError } from '../core/ui.js';

S.trace = { target: '', maxHops: 30, current: '', done: null };

const running = () => !!(S.st && S.st.running.trace);

function formCard() {
  const t = S.trace;
  const dis = running() ? 'disabled' : '';
  return `<section class="card"><div class="card-body">
    <form data-form="trace" class="scan-form" autocomplete="off">
      <div class="field" style="grid-column: span 2"><label for="f-target">Destinazione</label>
        <input class="input input-mono" id="f-target" data-field="target" value="${esc(t.target)}" placeholder="esempio.it · 1.1.1.1" ${dis}></div>
      <div class="field"><label for="f-hops">Salti al massimo</label><input class="input" id="f-hops" type="number" min="1" max="64" data-field="maxHops" value="${t.maxHops}" ${dis}></div>
      <div class="field"><span class="field-label">&nbsp;</span><div class="field-actions">
        ${running()
          ? `<button type="button" class="btn btn-danger" data-action="trace-stop">${icon('stop')} Ferma</button>`
          : `<button type="submit" class="btn btn-primary">${icon('play')} Avvia</button>`}
      </div></div>
    </form>
    <div id="trace-bar">${bar()}</div>
  </div></section>`;
}

function bar() {
  const d = S.trace.done;
  if (running()) return `<div class="scan-bar"><span class="scan-stat"><span class="spinner"></span> Traceroute verso <b>${esc(S.trace.current)}</b> in corso: ${plural(hopsOf(S.trace.current).length, 'salto', 'salti')} finora.</span></div>`;
  if (d) {
    const text = d.error ? `Traceroute interrotto per un errore: ${esc(d.error)}`
      : d.unresolved ? `Destinazione ${esc(d.target)} non risolta: controlla il nome.`
        : d.cancelled ? `Traceroute verso ${esc(d.target)} fermato dopo ${plural(d.hops, 'salto', 'salti')}.`
          : `Traceroute verso ${esc(d.target)} completato: ${plural(d.hops, 'salto', 'salti')}.`;
    const tone = d.error || d.unresolved ? 'danger' : d.cancelled ? 'warning' : 'success';
    return `<div class="scan-bar"><span class="scan-stat"><span class="pill ${tone} pill-dot">${text}</span></span></div>`;
  }
  return '';
}

function targets() { return Array.from(new Set(S.data.trace.map((h) => h.target || ''))); }
function hopsOf(target) { return S.data.trace.filter((h) => (h.target || '') === target).sort((a, b) => a.hopNumber - b.hopNumber); }

const rtt = (h, i) => {
  const v = h.roundTripTimes && h.roundTripTimes[i];
  return v == null || v < 0 ? '<span class="faint">*</span>' : `${v} ms`;
};
const rttCls = (h, i) => `rtt ${h.roundTripTimes && h.roundTripTimes[i] >= 0 ? '' : 'is-timeout'}`;

const columns = [
  { key: 'hopNumber', label: 'Salto', cls: 'num strong-cell', sortable: false },
  { key: 'ipAddress', label: 'Indirizzo IP', cls: 'mono', sortable: false, cell: (h) => h.status === 'OK' ? esc(h.ipAddress) : '<span class="faint">* * *</span>' },
  { key: 'hostname', label: 'Nome', sortable: false, cell: (h) => h.status === 'OK' ? (h.hostname ? esc(h.hostname) : '<span class="faint">—</span>') : '<span class="muted">Richiesta scaduta</span>' },
  { key: 'rtt1', label: 'Tempo 1', cls: 'rtt', sortable: false, cell: (h) => `<span class="${rttCls(h, 0)}">${rtt(h, 0)}</span>` },
  { key: 'rtt2', label: 'Tempo 2', cls: 'rtt', sortable: false, cell: (h) => `<span class="${rttCls(h, 1)}">${rtt(h, 1)}</span>` },
  { key: 'rtt3', label: 'Tempo 3', cls: 'rtt', sortable: false, cell: (h) => `<span class="${rttCls(h, 2)}">${rtt(h, 2)}</span>` },
  { key: 'status', label: 'Stato', sortable: false, cell: (h) => h.status === 'OK' ? '<span class="pill success pill-dot">Risposta</span>' : '<span class="pill warning pill-dot">Nessuna risposta</span>' },
  { key: 'actions', label: '', cls: 'actions', sortable: false, cell: (h) => h.status === 'OK' ? `<button class="row-menu-btn" data-action="hop-menu" data-ip="${esc(h.ipAddress)}" title="Azioni">${icon('more')}</button>` : '' },
];

function tableHtml() {
  if (!S.data.trace.length) return emptyState('route', 'Nessun traceroute nella sessione', 'Scrivi una destinazione e premi Avvia: i salti compaiono uno alla volta.');
  const rows = hopsOf(S.trace.current);
  if (!rows.length) return emptyState('search', 'Nessun salto per questa destinazione', 'Scegli un\'altra destinazione dall\'elenco.');
  return table({ columns, rows, sort: null, cls: 'list-h' });
}

function tableCard() {
  const ts = targets();
  if (!ts.includes(S.trace.current)) S.trace.current = ts[ts.length - 1] || '';
  const picker = ts.length > 1 ? `<div class="field"><label>Destinazione</label><select class="select input-sm" data-field="current">${ts.map((t) => `<option value="${esc(t)}" ${S.trace.current === t ? 'selected' : ''}>${esc(t || '(senza nome)')}</option>`).join('')}</select></div>`
    : (ts.length === 1 ? `<span class="toolbar-info" style="margin-left:0">Destinazione <b class="mono">${esc(ts[0])}</b></span>` : '');
  return `<section class="card table-card">
    <div class="toolbar">${picker}
      <div class="toolbar-actions"><span class="toolbar-info" id="trace-info"></span>
        <button class="btn btn-sm" data-action="clear-trace" ${S.data.trace.length ? '' : 'disabled'}>${icon('trash')} Svuota</button></div></div>
    <div id="trace-table">${tableHtml()}</div></section>`;
}

function render() {
  return `${pageHead('route', 'Traceroute', 'Il percorso dei pacchetti verso una destinazione: router attraversati e tempi di risposta.')}
    <div class="grid">${formCard()}${tableCard()}</div>`;
}

function updateInfo() {
  const info = $('#trace-info');
  if (info) info.textContent = S.data.trace.length ? plural(hopsOf(S.trace.current).length, 'salto', 'salti') : '';
}

function renderTable() {
  const box = $('#trace-table');
  if (box) box.innerHTML = tableHtml();
  updateInfo();
}

function renderTableCard() {
  const box = $('#trace-table');
  if (box) box.closest('section.card').outerHTML = tableCard();
  updateInfo();
}

function renderForm() {
  const form = $('[data-form="trace"]');
  if (form) form.closest('section.card').outerHTML = formCard();
}

function updateBar() {
  const b = $('#trace-bar');
  if (b) b.innerHTML = bar();
}

async function start() {
  const t = S.trace;
  if (running()) return;
  try {
    const res = await call('traceroute', { target: t.target, maxHops: t.maxHops });
    adopt({ state: res.state });
    S.data.trace = S.data.trace.filter((h) => (h.target || '').toLowerCase() !== res.target.toLowerCase());
    t.current = res.target;
    t.done = null;
    renderForm();
    renderTableCard();
    emit('state');
  } catch (err) { toastError(err, 'Traceroute non avviato'); }
}

function hopMenu(anchor, ip) {
  openMenu(anchor, [
    { header: ip },
    { ic: 'globe', label: 'WHOIS', run: () => navigate('whois', { target: ip, start: true }) },
    { ic: 'plug', label: 'Scansiona le porte', disabled: !isRemoteAddress(ip), run: () => navigate('ports', { host: ip, start: true }) },
    { ic: 'copy', label: 'Copia l\'indirizzo IP', run: () => call('setClipboard', { text: ip }).then(() => toast('Indirizzo IP copiato.', { tone: 'success' }), toastError) },
  ]);
}

const actions = {
  'trace-stop': async () => { try { await call('stopTrace'); } catch (err) { toastError(err); } },
  'hop-menu': (el) => hopMenu(el, el.dataset.ip),
  'clear-trace': async () => {
    if (!(await confirmDialog({ title: 'Svuotare i traceroute?', message: `${plural(S.data.trace.length, 'salto', 'salti')} verranno tolti dalla sessione.`, confirmText: 'Svuota', tone: 'danger' }))) return;
    try { adopt(await call('clearList', { list: 'trace' })); S.trace.done = null; renderTableCard(); updateBar(); } catch (err) { toastError(err); }
  },
};

on('trace.hop', (hop) => {
  S.data.trace.push(hop);
  markDirty();
  emit('data', { list: 'trace' });
  if (S.page === 'trace') {
    if (S.trace.current !== hop.target) { S.trace.current = hop.target; renderTableCard(); } else renderTable();
    updateBar();
  }
});

on('trace.done', (d) => {
  S.trace.done = d;
  adopt({ state: d.state });
  if (S.page === 'trace') { renderForm(); renderTableCard(); }
  if (d.error) toast(d.error, { tone: 'error', title: 'Traceroute interrotto' });
  else if (d.unresolved) toast(`Destinazione ${d.target} non risolta.`, { tone: 'error', title: 'Traceroute' });
  else if (d.cancelled) toast(`${plural(d.hops, 'salto', 'salti')} fino a quel momento.`, { tone: 'warning', title: `Traceroute verso ${d.target} fermato` });
  else toast(`${plural(d.hops, 'salto', 'salti')}.`, { tone: 'success', title: `Traceroute verso ${d.target} completato` });
});

bus.addEventListener('session', () => { S.trace.done = null; S.trace.current = ''; if (S.page === 'trace') renderTableCard(); });

definePage('trace', {
  title: 'Traceroute', icon: 'route', section: 'lookup', badge: () => targets().filter(Boolean).length,
  render, after: updateInfo, actions,
  enter: (params) => {
    if (params.target) {
      S.trace.target = params.target;
      renderForm();
      if (params.start && !running()) start();
    }
  },
  onInput: (el, field) => {
    if (field === 'target') S.trace.target = el.value;
    if (field === 'maxHops') S.trace.maxHops = Number(el.value) || 30;
  },
  onChange: (el, field) => { if (field === 'current') { S.trace.current = el.value; renderTable(); } },
  onSubmit: () => start(),
});
