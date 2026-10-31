// Archivio della rete nel tempo: dispositivi conosciuti con la scheda curata dall'utente (alias, fiducia, note),
// cambiamenti tra una scansione e l'altra (nuovo, ricomparso, cambiato, assente) e storico delle scansioni.
import { call, on } from '../core/bridge.js';
import { $, $$, esc, fmtDateTime, fmtNumber, fmtSeconds, plural } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { catIcon } from '../core/categories.js';
import { S, definePage, adopt, bus } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { table } from '../core/table.js';
import { confirmDialog, emptyState, note, openMenu, pageHead, toast, toastError } from '../core/ui.js';
import { launchItems, shortVendor } from './network.js';

S.devices = {
  data: null,          // risposta di «inventory»: hosts, events, scans, digest, watch
  loading: false,
  tab: 'hosts',
  q: '', onlyOnline: false,
  selectedId: null, detail: null, saved: false,
  pendingHost: null,   // scheda da aprire appena i dati arrivano (dall'avviso o da un evento)
  badgeSeen: false,    // il contatore «da vedere» si azzera quando si apre la pagina
};

const TRUST = { 0: { label: 'Da classificare', tone: '' }, 1: { label: 'Affidabile', tone: 'success' }, 2: { label: 'Sospetto', tone: 'danger' } };
const KIND = { nuovo: 'warning', ricomparso: 'info', cambiato: 'primary', assente: '' };

const watch = () => (S.devices.data && S.devices.data.watch) || (S.st && S.st.inventory) || {};
const scanning = () => !!watch().scanning;
const fmtShort = (iso) => fmtDateTime(iso).replace(/\/\d{4} /, ' ');
const upperMac = (m) => String(m || '').toUpperCase();

// ---------------------------------------------------------------------------------------------------------------------
// Intestazione: digest, «Scansiona adesso» con l'avanzamento per fase
// ---------------------------------------------------------------------------------------------------------------------
function scanControl() {
  const w = watch();
  if (w.scanning) {
    return `<span class="scan-live" id="inv-live">
      <span class="spinner"></span>
      <span class="scan-live-text"><b id="inv-status">${esc(w.status || 'Preparazione…')}</b><span class="meter"><span id="inv-meter" style="width:${w.percent || 0}%"></span></span></span>
      ${S.st && S.st.running && S.st.running.network ? '' : `<button class="btn btn-sm btn-danger" data-action="inv-stop">${icon('stop')} Ferma</button>`}</span>`;
  }
  return `<button class="btn btn-primary" data-action="inv-scan" title="Scansiona la rete principale (o quella delle Impostazioni → Archivio della rete) e aggiorna l'archivio">${icon('radar')} Scansiona adesso</button>`;
}

/** Aggiorna sul posto lo stato della scansione (testo e barra) senza ridisegnare la pagina. */
function updateLive() {
  const live = $('#inv-live');
  const w = watch();
  if (!live || !w.scanning) { const box = $('#inv-scan-box'); if (box) box.innerHTML = scanControl(); return; }
  $('#inv-status', live).textContent = w.status || 'Preparazione…';
  $('#inv-meter', live).style.width = `${w.percent || 0}%`;
}

function footerText() {
  const d = S.devices.data;
  const w = watch();
  const last = d && d.scans && d.scans[0];
  if (!last) return 'L\'archivio si costruisce scansione dopo scansione: anche le scansioni della pagina Rete lo aggiornano. Le scansioni automatiche si regolano in Impostazioni → Archivio della rete.';
  let next = '';
  if (w.enabled && w.nextScanAt) {
    const at = new Date(w.nextScanAt);
    next = at.getTime() <= Date.now() ? ' · Prossima automatica tra pochi istanti.' : ` · Prossima automatica alle ${fmtDateTime(w.nextScanAt).slice(11)}.`;
  } else next = ' · Scansioni automatiche spente (Impostazioni → Archivio della rete).';
  return `Ultima scansione ${fmtDateTime(last.startedAt)} su ${last.target}: ${plural(last.hostsCount, 'dispositivo', 'dispositivi')} in ${fmtSeconds(last.durationMs / 1000)}${next}`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Scheda Dispositivi: elenco a sinistra, scheda del dispositivo a destra
// ---------------------------------------------------------------------------------------------------------------------
function hostRow(h) {
  const t = TRUST[h.trust] || TRUST[0];
  const sub = [h.vendor ? shortVendor(h.vendor) : '', h.deviceType, h.isGateway ? 'gateway' : '', h.isLocal ? 'questa macchina' : ''].filter(Boolean).join(' · ');
  return `<button class="dev-row ${h.id === S.devices.selectedId ? 'is-active' : ''} ${h.isOnline ? '' : 'is-off'}" data-action="pick-host" data-id="${h.id}" data-ctx="device">
    ${catIcon(h)}
    <span class="dev-text">
      <span class="dev-name"><b class="trunc" title="${esc(h.displayName)}">${esc(h.displayName)}</b>
        ${h.trust ? `<span class="pill ${t.tone}">${t.label.toLowerCase()}</span>` : ''}${h.isNew ? '<span class="pill warning">nuovo</span>' : ''}</span>
      <small class="trunc" title="${esc(sub)}">${esc(sub)}</small></span>
    <span class="dev-state"><span class="state"><span class="dot ${h.isOnline ? 'on' : ''}"></span>${h.isOnline ? 'presente' : `ultima volta ${fmtShort(h.lastSeen)}`}</span><span class="mono">${esc(h.ip)}</span></span>
  </button>`;
}

function listCard() {
  const d = S.devices.data;
  const hosts = d.hosts;
  let body;
  if (!d.total) body = emptyState('server', 'Nessun dispositivo in archivio', 'Avvia una scansione: i dispositivi trovati restano qui anche quando si spengono, con la loro storia.');
  else if (!hosts.length) body = emptyState('search', 'Nessun dispositivo corrisponde', 'Cambia le parole della ricerca o togli «solo presenti».');
  else body = `<div class="dev-list" id="dev-list">${hosts.map(hostRow).join('')}</div>`;
  return `<section class="card table-card">
    <div class="toolbar">
      <div class="search">${icon('search')}<input class="input" data-field="q" value="${esc(S.devices.q)}" placeholder="Cerca per nome, IP, MAC, produttore, tipo, alias o note" autocomplete="off"></div>
      <label class="switch"><input type="checkbox" data-field="onlyOnline" ${S.devices.onlyOnline ? 'checked' : ''}><span class="track"></span><span>Solo presenti</span></label>
      <div class="toolbar-actions"><span class="toolbar-info">${hosts.length === d.total ? plural(d.total, 'dispositivo', 'dispositivi') : `${hosts.length} di ${d.total}`}</span></div>
    </div>
    ${body}
  </section>`;
}

function portPill(p, sensitive) {
  const bad = sensitive.has(p.port);
  const title = [p.banner, bad ? 'Servizio esposto da verificare' : ''].filter(Boolean).join(' · ');
  return `<span class="pill ${bad ? 'warning' : 'success'}" title="${esc(title)}">${p.port}${p.service ? ` · ${esc(p.service)}` : ''}${bad ? ` ${icon('alert', { size: 12 })}` : ''}</span>`;
}

function detailCard() {
  const det = S.devices.detail;
  if (!S.devices.selectedId) return `<section class="card"><div class="empty"><div class="empty-icon">${icon('monitor')}</div><h3>Scegli un dispositivo</h3><p>La scheda mostra identità, porte, storia e i campi che puoi compilare: alias, fiducia e note.</p></div></section>`;
  if (!det) return `<section class="card"><div class="loading-block"><span class="spinner"></span> Lettura della scheda…</div></section>`;
  const h = det.host;
  const t = TRUST[h.trust] || TRUST[0];
  const sensitive = new Set((h.sensitivePorts || []).map((s) => Number(String(s).split(' ')[0])));
  const chip = (ic, text, title = '') => `<span class="chip" title="${esc(title || text)}">${icon(ic, { size: 13 })}${esc(text)}</span>`;
  const chips = [
    chip('mapPin', h.ip),
    h.mac ? chip('tag', `${upperMac(h.mac)}${h.vendor ? ` · ${shortVendor(h.vendor)}` : ''}`, `${upperMac(h.mac)}${h.vendor ? ` · ${h.vendor}` : ''}`) : '',
    h.hostName ? chip('dns', h.hostName) : '',
    h.netBios ? chip('windows', `${h.netBios}${h.workgroup ? ` · ${h.workgroup}` : ''}`) : '',
    h.latencyMs != null ? chip('activity', `${fmtNumber(Math.round(h.latencyMs))} ms`) : '',
    h.isGateway ? chip('router', 'gateway') : '',
    h.isLocal ? chip('laptop', 'questa macchina') : '',
  ].filter(Boolean).join('');
  const seen = `${h.isOnline ? 'Presente nell\'ultima scansione' : `Non visto nell'ultima scansione: ultima volta ${fmtDateTime(h.lastSeen)}`} · conosciuto dal ${fmtDateTime(h.firstSeen)} · visto ${plural(h.seenCount, 'volta', 'volte')}.`;
  const ports = (h.ports || []).slice().sort((a, b) => a.port - b.port);
  const sens = h.sensitivePorts || [];
  const trustBtn = (v) => `<button type="button" class="${h.trust === v ? 'is-active' : ''}" data-action="trust" data-trust="${v}">${TRUST[v].label}</button>`;
  const history = det.history || [];
  return `<section class="card dev-detail" id="dev-detail">
    <div class="card-head"><div class="card-title">${catIcon(h, true)}<div class="dev-title"><h2 class="trunc" title="${esc(h.displayName)}">${esc(h.displayName)}</h2><span class="card-sub">${esc(h.deviceType)}${h.osGuess ? ` · ${esc(h.osGuess)}` : ''}</span></div></div>
      ${h.trust ? `<span class="pill ${t.tone} pill-dot">${t.label}</span>` : '<span class="pill">Da classificare</span>'}</div>
    <div class="card-body"><div class="fields">
      <div class="chips">${chips}</div>
      <p class="card-text muted">${seen}</p>
      ${sens.length ? note('warning', 'alert', `<b>Da verificare:</b> ${sens.map(esc).join(', ')}. Servizi esposti in rete senza cifratura o con storia di abusi: se non servono, spegnili o limitali.`) : ''}
      <div class="form-grid">
        <div class="field span-2"><label for="d-alias">Alias</label><input class="input" id="d-alias" data-field="alias" value="${esc(h.alias || '')}" placeholder="Es. NAS ufficio, TV soggiorno" autocomplete="off"></div>
        <div class="field span-2"><span class="field-label">Fiducia</span><div class="segmented trust-seg">${trustBtn(0)}${trustBtn(1)}${trustBtn(2)}</div></div>
        <div class="field span-2"><label for="d-notes">Note</label><textarea class="input" id="d-notes" data-field="notes" rows="2" placeholder="Dove sta, a chi appartiene, cosa fa">${esc(h.notes || '')}</textarea></div>
      </div>
      <div class="row"><button class="btn btn-primary btn-sm" data-action="save-host">${icon('save')} Salva</button><span class="field-status ok ${S.devices.saved ? '' : 'hidden'}" id="dev-saved">${icon('checkCircle')} Salvato</span></div>
      <div><div class="form-section-head"><div class="form-section-title">${icon('plug')}Porte aperte</div><span class="muted" style="font-size:12.5px">${ports.length ? plural(ports.length, 'porta', 'porte') : ''}</span></div>
        ${ports.length ? `<div class="pill-list">${ports.map((p) => portPill(p, sensitive)).join('')}</div>` : '<p class="card-text muted">Nessuna porta aperta tra quelle provate.</p>'}</div>
      <div><div class="form-section-head"><div class="form-section-title">${icon('history')}Storia</div></div>
        ${history.length ? `<div class="ev-list compact">${history.map(eventRow).join('')}</div>` : '<p class="card-text muted">Nessun evento registrato.</p>'}</div>
    </div></div>
    <div class="card-foot">
      <button class="btn btn-sm" data-action="host-ports">${icon('plug')} Scansiona le porte</button>
      <button class="btn btn-sm" data-action="host-tools">${icon('external')} Apri con… ${icon('chevronDown', { size: 13 })}</button>
      <span class="spacer"></span>
      <button class="btn btn-sm btn-danger-ghost" data-action="forget-host" title="Toglie il dispositivo e la sua storia dall'archivio: se ricompare torna «nuovo»">${icon('trash')} Dimentica</button>
    </div>
  </section>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Scheda Cambiamenti e scheda Scansioni
// ---------------------------------------------------------------------------------------------------------------------
function eventRow(e) {
  return `<button class="ev-row ${e.acknowledged ? '' : 'is-unseen'}" data-action="open-event" data-id="${e.id}" data-host="${e.hostId || ''}">
    <span class="pill ${KIND[e.kind] || ''}">${esc(e.kind)}</span>
    <span class="ev-text"><b class="trunc" title="${esc(e.title)}">${esc(e.title)}</b><small class="trunc" title="${esc(e.details)}">${esc(e.details)}</small></span>
    <span class="ev-time">${fmtShort(e.at)}</span>
  </button>`;
}

function eventsCard() {
  const events = S.devices.data.events || [];
  const unseen = events.filter((e) => !e.acknowledged).length;
  return `<section class="card table-card"><div class="card-head"><div class="card-title">${icon('history')}<h2>Cambiamenti</h2><span class="count">${events.length}</span>${unseen ? `<span class="pill warning">${plural(unseen, 'da vedere', 'da vedere')}</span>` : ''}</div>
      <button class="btn btn-sm" data-action="ack-all" ${unseen ? '' : 'disabled'}>${icon('check')} Segna tutto come visto</button></div>
    ${events.length ? `<div class="ev-list">${events.map(eventRow).join('')}</div>` : emptyState('history', 'Nessun cambiamento', 'Dalla seconda scansione in poi compaiono qui i dispositivi nuovi, ricomparsi, cambiati o assenti.')}
    <div class="table-foot">${icon('info', { size: 14 })} Nuovo = mai visto prima · Ricomparso = tornato dopo essere stato assente · Cambiato = IP, nome o porte diversi · Assente = non risponde da ${2} scansioni. Un clic apre la scheda del dispositivo.</div>
  </section>`;
}

function scansCard() {
  const scans = S.devices.data.scans || [];
  const columns = [
    { key: 'startedAt', label: 'Data', cell: (s) => fmtDateTime(s.startedAt) },
    { key: 'target', label: 'Rete', cls: 'mono' },
    { key: 'hostsCount', label: 'Host', cls: 'num' },
    { key: 'durationMs', label: 'Durata', cls: 'num', cell: (s) => fmtSeconds(s.durationMs / 1000) },
    { key: 'changes', label: 'Differenze', sortable: false, cell: (s) => `<span class="pill-list">${s.newCount ? `<span class="pill warning">+${s.newCount} nuovi</span>` : ''}${s.changedCount ? `<span class="pill primary">~${s.changedCount} cambiati</span>` : ''}${s.goneCount ? `<span class="pill">−${s.goneCount} assenti</span>` : ''}${!s.newCount && !s.changedCount && !s.goneCount ? '<span class="faint">nessuna</span>' : ''}</span>` },
    { key: 'summary', label: 'Riepilogo', sortable: false, cell: (s) => `<span class="trunc w-summary" title="${esc(s.summary)}">${esc(s.summary)}</span>` },
  ];
  return `<section class="card table-card"><div class="card-head"><div class="card-title">${icon('clock')}<h2>Scansioni</h2><span class="count">${scans.length}</span></div></div>
    ${table({ columns, rows: scans, sort: null, cls: 'list-h', empty: emptyState('clock', 'Nessuna scansione registrata', 'Premi «Scansiona adesso» oppure avvia una scansione dalla pagina Rete.') })}
  </section>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Pagina
// ---------------------------------------------------------------------------------------------------------------------
function render() {
  const d = S.devices.data;
  const head = pageHead(d ? esc(d.digest) : 'Dispositivi visti nel tempo, con confronto tra le scansioni e avvisi sui cambiamenti.', `<span id="inv-scan-box">${scanControl()}</span>`);
  if (!d) return `${head}<div class="card"><div class="loading-block"><span class="spinner"></span> Lettura dell'archivio…</div></div>`;
  const unseen = (d.events || []).filter((e) => !e.acknowledged).length;
  const tab = (key, ic, label, extra = '') => `<button class="tab ${S.devices.tab === key ? 'is-active' : ''}" data-action="tab" data-tab="${key}">${icon(ic)}${label}${extra}</button>`;
  const tabs = `<div class="tabs">${tab('hosts', 'server', 'Dispositivi', ` <span class="count">${d.total}</span>`)}${tab('events', 'history', 'Cambiamenti', unseen ? ` <span class="count is-warning">${unseen}</span>` : '')}${tab('scans', 'clock', 'Scansioni', ` <span class="count">${(d.scans || []).length}</span>`)}</div>`;
  let body;
  if (S.devices.tab === 'events') body = eventsCard();
  else if (S.devices.tab === 'scans') body = scansCard();
  else body = `<div class="grid grid-side devices-grid">${listCard()}${detailCard()}</div>`;
  return `${head}${tabs}${body}<p class="page-foot muted">${icon('info', { size: 14 })} ${esc(footerText())}</p>`;
}

function rerender() {
  if (S.page !== 'devices') return;
  const content = $('#content');
  if (content) content.innerHTML = render();
}

function renderList() {
  const box = $('#dev-list');
  if (box && S.devices.data) box.innerHTML = S.devices.data.hosts.map(hostRow).join(''); else rerender();
}

function renderDetail() {
  const old = $('#dev-detail');
  if (!old) { rerender(); return; }
  old.outerHTML = detailCard();
}

async function load() {
  const s = S.devices;
  if (s.loading) return;
  s.loading = true;
  try {
    s.data = await call('inventory', { query: s.q, onlyOnline: s.onlyOnline });
    if (S.st) S.st.inventory = s.data.watch;
    if (s.pendingHost) { s.selectedId = s.pendingHost; s.pendingHost = null; s.tab = 'hosts'; }
    const ids = new Set(s.data.hosts.map((h) => h.id));
    if (s.selectedId && !ids.has(s.selectedId) && !s.q && !s.onlyOnline) s.selectedId = null;
    if (!s.selectedId && s.data.hosts.length) s.selectedId = s.data.hosts[0].id;
    rerender();
    if (s.selectedId) await loadDetail(s.selectedId);
  } catch (err) { toastError(err, 'Archivio non letto'); }
  s.loading = false;
}

async function loadDetail(id) {
  try {
    const det = await call('inventoryHost', { id });
    if (S.devices.selectedId !== id) return;
    S.devices.detail = det;
    S.devices.saved = false;
    renderDetail();
  } catch (err) {
    S.devices.selectedId = null;
    S.devices.detail = null;
    toastError(err);
    load();
  }
}

function select(id) {
  if (!id) return;
  S.devices.selectedId = Number(id);
  S.devices.detail = null;
  $$('.dev-row').forEach((r) => r.classList.toggle('is-active', Number(r.dataset.id) === S.devices.selectedId));
  renderDetail();
  loadDetail(S.devices.selectedId);
}

async function save() {
  const det = S.devices.detail;
  if (!det) return;
  const alias = ($('#d-alias') || {}).value || '';
  const notes = ($('#d-notes') || {}).value || '';
  const trust = Number(($('.trust-seg .is-active') || { dataset: {} }).dataset.trust || 0);
  try {
    S.devices.detail = await call('inventorySave', { id: det.host.id, alias, notes, trust });
    S.devices.saved = true;
    renderDetail();
    await refreshList();
    toast(`Scheda di ${S.devices.detail.host.displayName} salvata.`, { tone: 'success' });
  } catch (err) { toastError(err, 'Scheda non salvata'); }
}

/** Ricarica elenco e digest senza perdere la scheda aperta. */
async function refreshList() {
  const s = S.devices;
  try {
    s.data = await call('inventory', { query: s.q, onlyOnline: s.onlyOnline });
    if (S.st) S.st.inventory = s.data.watch;
    renderList();
    const info = $('.toolbar-info');
    if (info) info.textContent = s.data.hosts.length === s.data.total ? plural(s.data.total, 'dispositivo', 'dispositivi') : `${s.data.hosts.length} di ${s.data.total}`;
    const sub = $('.page-sub');
    if (sub) sub.textContent = s.data.digest;
  } catch (err) { toastError(err); }
}

async function forget() {
  const det = S.devices.detail;
  if (!det) return;
  const ok = await confirmDialog({ title: `Dimenticare ${det.host.displayName}?`, message: 'Il dispositivo e la sua storia spariscono dall\'archivio. Se ricompare nella rete viene segnalato come nuovo.', confirmText: 'Dimentica', tone: 'danger' });
  if (!ok) return;
  try {
    await call('inventoryForget', { id: det.host.id });
    S.devices.selectedId = null;
    S.devices.detail = null;
    toast('Dispositivo dimenticato.', { tone: 'success' });
    load();
  } catch (err) { toastError(err); }
}

async function openEvent(id, hostId) {
  try {
    const r = await call('inventoryAck', { id });
    adopt({ state: r.state });
  } catch { /* l'evento si apre comunque */ }
  if (hostId) {
    S.devices.tab = 'hosts';
    S.devices.q = '';
    S.devices.onlyOnline = false;
    S.devices.pendingHost = Number(hostId);
  }
  load();
}

async function startScan() {
  try {
    adopt(await call('inventoryScan', {}));
    S.devices.data.watch = S.st.inventory;
    updateLive();
    toast('La scansione aggiorna l\'archivio: i dispositivi della sessione non cambiano.', { tone: 'info', title: 'Scansione dell\'archivio avviata', timeout: 5000 });
  } catch (err) { toastError(err, 'Scansione non avviata'); }
}

function hostMenu(anchor, id) {
  const h = (S.devices.data.hosts || []).find((x) => x.id === Number(id));
  if (!h) return;
  const ports = (h.ports || []).map((p) => ({ portNumber: p.port, protocol: 'TCP' }));
  openMenu(anchor, [
    { header: `${h.displayName} · ${h.ip}` },
    { ic: 'plug', label: 'Scansiona le porte', run: () => navigate('ports', { host: h.ip, start: true }) },
    { ic: 'radar', label: 'Scansiona la sua rete', run: () => navigate('network', { target: h.ip.replace(/\.\d+$/, '.0/24') }) },
    { sep: true },
    ...launchItems(h.ip, ports),
    { sep: true },
    { ic: 'copy', label: 'Copia l\'indirizzo IP', run: () => call('setClipboard', { text: h.ip }).then(() => toast('Indirizzo IP copiato.', { tone: 'success' })) },
    { ic: 'copy', label: 'Copia il MAC', disabled: !h.mac, run: () => call('setClipboard', { text: upperMac(h.mac) }).then(() => toast('MAC copiato.', { tone: 'success' })) },
  ]);
}

const actions = {
  'tab': (el) => { S.devices.tab = el.dataset.tab; rerender(); },
  'pick-host': (el) => select(el.dataset.id),
  'trust': (el) => { $$('.trust-seg button').forEach((b) => b.classList.toggle('is-active', b === el)); },
  'save-host': () => save(),
  'forget-host': () => forget(),
  'host-ports': () => { const h = S.devices.detail && S.devices.detail.host; if (h) navigate('ports', { host: h.ip, start: true }); },
  'host-tools': (el) => {
    const h = S.devices.detail && S.devices.detail.host;
    if (!h) return;
    openMenu(el, [{ header: h.ip }, ...launchItems(h.ip, (h.ports || []).map((p) => ({ portNumber: p.port, protocol: 'TCP' })))], { above: true });
  },
  'open-event': (el) => openEvent(Number(el.dataset.id), el.dataset.host),
  'ack-all': async () => {
    try { adopt({ state: (await call('inventoryAck', { all: true })).state }); load(); toast('Tutti i cambiamenti segnati come visti.', { tone: 'success' }); } catch (err) { toastError(err); }
  },
  'inv-scan': () => startScan(),
  'inv-stop': () => call('inventoryStop').catch((e) => toastError(e)),
};

let searchTimer = 0;

on('inventory.progress', () => { if (S.page === 'devices') updateLive(); });
on('inventory.state', (w) => { if (S.devices.data) S.devices.data.watch = w; if (S.page === 'devices') updateLive(); });
on('inventory.done', (d) => {
  S.devices.badgeSeen = false;
  if (d.diff && !d.diff.isFirst && !d.cancelled && !d.error && (S.page !== 'network' || d.automatic)) {
    const changes = d.diff.newCount + d.diff.returnedCount + d.diff.goneCount + d.diff.changedCount;
    toast(changes ? d.diff.summary : `${plural(d.found, 'dispositivo', 'dispositivi')}, nessun cambiamento.`, {
      tone: changes ? 'info' : 'success', title: d.automatic ? 'Scansione automatica completata' : 'Archivio aggiornato', timeout: 8000,
      action: changes ? { label: 'Cambiamenti', run: () => navigate('devices', { tab: 'events' }) } : null,
    });
  } else if (d.diff && d.diff.isFirst && !d.cancelled) {
    toast(`${plural(d.found, 'dispositivo', 'dispositivi')} nell'archivio: dalla prossima scansione arrivano i confronti e gli avvisi.`, { tone: 'success', title: 'Archivio della rete creato', timeout: 9000 });
  } else if (d.error && d.automatic) toast(d.error, { tone: 'error', title: 'Scansione automatica non riuscita' });
  if (S.page === 'devices') load();
});
bus.addEventListener('state', () => { if (S.page === 'devices') updateLive(); });

definePage('devices', {
  title: 'Dispositivi', heading: 'Archivio della rete', icon: 'server', section: 'scan', order: 1,
  badge: () => (S.devices.badgeSeen ? 0 : ((S.st && S.st.inventory && S.st.inventory.unseen) || 0)),
  render,
  after: () => { S.devices.badgeSeen = true; bus.dispatchEvent(new Event('state')); load(); },
  enter: (params) => {
    if (params.host) { S.devices.pendingHost = Number(params.host); S.devices.q = ''; S.devices.onlyOnline = false; S.devices.tab = 'hosts'; load(); }
    if (params.tab) { S.devices.tab = params.tab; rerender(); }
  },
  actions,
  onInput: (el, field) => {
    if (field === 'q') { S.devices.q = el.value; clearTimeout(searchTimer); searchTimer = setTimeout(refreshList, 180); }
  },
  onChange: (el, field) => {
    if (field === 'onlyOnline') { S.devices.onlyOnline = el.checked; refreshList(); }
  },
  onContext: (row, point) => hostMenu(point, row.dataset.id),
});
