// DNS: record A, AAAA, PTR, MX, TXT e NS di un nome o di un indirizzo.
import { call } from '../core/bridge.js';
import { $, esc, matchesAll, plural } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage, adopt, bus } from '../core/store.js';
import { table, nextSort } from '../core/table.js';
import { confirmDialog, emptyState, pageHead, toast, toastError } from '../core/ui.js';

S.dns = { target: '', busy: false, q: '', type: '', sort: null, fresh: new Set() };

const TYPES = ['A', 'AAAA', 'PTR', 'MX', 'NS', 'TXT'];
const TONES = { A: 'primary', AAAA: 'info', PTR: '', MX: 'warning', NS: 'success', TXT: 'danger' };

function formCard() {
  const d = S.dns;
  return `<section class="card"><div class="card-body">
    <form data-form="query" class="scan-form" autocomplete="off">
      <div class="field" style="grid-column: span 3"><label for="f-target">Nome o indirizzo IP</label>
        <input class="input input-mono" id="f-target" data-field="target" value="${esc(d.target)}" placeholder="esempio.it · 192.168.1.1" ${d.busy ? 'disabled' : ''}></div>
      <div class="field"><span class="field-label">&nbsp;</span><div class="field-actions">
        <button type="submit" class="btn btn-primary ${d.busy ? 'is-loading' : ''}" ${d.busy ? 'disabled' : ''}>${icon('search')} Risolvi</button></div></div>
    </form>
    <div class="scan-bar" style="border-top:0;padding-top:0;margin-top:10px"><span class="hint">A e AAAA dal resolver di Windows; MX, TXT e NS con nslookup; PTR per gli indirizzi IP.</span></div>
  </div></section>`;
}

const rowKey = (r) => `${r.host}|${r.recordType}|${r.value}`;

function filtered() {
  const d = S.dns;
  return S.data.dns.filter((r) => (!d.type || r.recordType === d.type) && matchesAll(d.q, [r.host, r.recordType, r.value]));
}

const columns = [
  { key: 'recordType', label: 'Tipo', cell: (r) => `<span class="tag ${TONES[r.recordType] || ''}">${esc(r.recordType)}</span>` },
  { key: 'host', label: 'Nome', cls: 'mono strong-cell' },
  { key: 'value', label: 'Valore', cls: 'wrap-mono' },
  { key: 'actions', label: '', cls: 'actions', sortable: false, cell: (r) => `<button class="row-menu-btn" data-action="copy-value" data-text="${esc(r.value)}" title="Copia il valore">${icon('copy')}</button>` },
];

function tableHtml() {
  if (!S.data.dns.length) return emptyState('dns', 'Nessun record DNS nella sessione', 'Scrivi un nome di dominio o un indirizzo IP e premi Risolvi.');
  const rows = filtered();
  if (!rows.length) return emptyState('search', 'Nessun record corrisponde', 'Cambia la ricerca o il tipo.');
  return table({ columns, rows, sort: S.dns.sort, cls: 'list-h', rowAttrs: (r) => `class="${S.dns.fresh.has(rowKey(r)) ? 'is-new' : ''}"` });
}

function pills() {
  const counts = {};
  for (const r of S.data.dns) counts[r.recordType] = (counts[r.recordType] || 0) + 1;
  const all = `<button class="pill ${S.dns.type ? '' : 'primary is-active'}" data-action="type" data-type="">Tutti</button>`;
  return all + TYPES.filter((t) => counts[t]).map((t) => `<button class="pill ${TONES[t] || ''} ${S.dns.type === t ? 'is-active' : ''}" data-action="type" data-type="${t}">${t} · ${counts[t]}</button>`).join('');
}

function tableCard() {
  return `<section class="card table-card">
    <div class="toolbar">
      <div class="search">${icon('search')}<input class="input" data-field="q" value="${esc(S.dns.q)}" placeholder="Cerca per nome o valore" autocomplete="off"></div>
      <div class="filter-pills" id="dns-pills">${pills()}</div>
      <div class="toolbar-actions"><span class="toolbar-info" id="dns-info"></span>
        <button class="btn btn-sm" data-action="clear-dns" ${S.data.dns.length ? '' : 'disabled'}>${icon('trash')} Svuota</button></div>
    </div>
    <div id="dns-table">${tableHtml()}</div></section>`;
}

function render() {
  return `${pageHead('Record di un nome di dominio o di un indirizzo: A, AAAA, PTR, MX, NS e TXT.')}
    <div class="grid">${formCard()}${tableCard()}</div>`;
}

function updateInfo() {
  const info = $('#dns-info');
  if (!info) return;
  const total = S.data.dns.length;
  const shown = filtered().length;
  info.textContent = total ? (shown === total ? plural(total, 'record', 'record') : `${shown} di ${total} record`) : '';
}

function renderTable() {
  const box = $('#dns-table');
  if (box) box.innerHTML = tableHtml();
  const p = $('#dns-pills');
  if (p) p.innerHTML = pills();
  updateInfo();
  const clear = $('[data-action="clear-dns"]');
  if (clear) clear.disabled = !S.data.dns.length;
}

function renderForm() {
  const form = $('[data-form="query"]');
  if (form) form.closest('section.card').outerHTML = formCard();
}

async function query() {
  const d = S.dns;
  if (d.busy) return;
  if (!d.target.trim()) { toast('Scrivi un nome di dominio o un indirizzo IP.', { tone: 'warning' }); return; }
  d.busy = true;
  renderForm();
  try {
    const res = await call('dns', { target: d.target });
    adopt({ state: res.state });
    d.fresh.clear();
    for (const r of res.added || []) { S.data.dns.push(r); d.fresh.add(rowKey(r)); }
    d.busy = false;
    renderForm();
    renderTable();
    if (!res.records.length) toast(`Nessun record per ${d.target.trim()}.`, { tone: 'warning', title: 'DNS' });
    else toast(`${plural(res.records.length, 'record trovato', 'record trovati')}${res.added.length < res.records.length ? `, ${res.added.length} nuovi` : ''}.`, { tone: 'success', title: `DNS di ${d.target.trim()}` });
    setTimeout(() => { d.fresh.clear(); }, 2000);
  } catch (err) {
    d.busy = false;
    renderForm();
    toastError(err, 'DNS non riuscito');
  }
}

const actions = {
  'type': (el) => { S.dns.type = el.dataset.type; renderTable(); },
  'copy-value': async (el) => { try { await call('setClipboard', { text: el.dataset.text }); toast('Valore copiato.', { tone: 'success' }); } catch (err) { toastError(err); } },
  'clear-dns': async () => {
    if (!(await confirmDialog({ title: 'Svuotare i record DNS?', message: `${plural(S.data.dns.length, 'record', 'record')} verranno tolti dalla sessione.`, confirmText: 'Svuota', tone: 'danger' }))) return;
    try { adopt(await call('clearList', { list: 'dns' })); S.dns.type = ''; renderTable(); } catch (err) { toastError(err); }
  },
};

bus.addEventListener('session', () => { S.dns.type = ''; S.dns.fresh.clear(); if (S.page === 'dns') renderTable(); });

definePage('dns', {
  title: 'DNS', heading: 'DNS', icon: 'dns', section: 'lookup', badge: () => S.data.dns.length,
  render, after: updateInfo, actions,
  enter: (params) => {
    if (params.target) { S.dns.target = params.target; const f = $('#f-target'); if (f) f.value = params.target; if (params.start) query(); }
  },
  onInput: (el, field) => {
    if (field === 'target') S.dns.target = el.value;
    if (field === 'q') { S.dns.q = el.value; renderTable(); }
  },
  onSubmit: () => query(),
  onSort: (k) => { S.dns.sort = nextSort(S.dns.sort, k); renderTable(); },
});
