// WhoIs Pro: la pagina della finestra (WebView2). Scansioni, file e appunti li fa il programma (UI\AppBridge.vb);
// qui ci sono la struttura (barra laterale, barra superiore), la sessione e il passaggio tra le pagine (pages\).
import { call, on } from './core/bridge.js';
import { $, $$, esc, fmtNumber, fileName, plural } from './core/dom.js';
import { icon } from './core/icons.js';
import { S, PAGES, NAV, bus, setState, adopt, counts, isRunning } from './core/store.js';
import { setNavigator } from './core/nav.js';
import { askDialog, closeMenu, closeOverlays, closeTopOverlay, confirmDialog, hasOverlay, openMenu, toast, toastError } from './core/ui.js';
import './pages/overview.js';
import './pages/network.js';
import './pages/ports.js';
import './pages/whois.js';
import './pages/dns.js';
import './pages/trace.js';
import './pages/netstat.js';
import './pages/computer.js';
import './pages/settings.js';

const SECTIONS = [
  { key: 'scan', title: 'Scansione' },
  { key: 'lookup', title: 'Ricerca' },
  { key: 'system', title: 'Sistema' },
];

// ---------------------------------------------------------------------------------------------------------------------
// Avvio
// ---------------------------------------------------------------------------------------------------------------------
async function start() {
  try {
    const res = await call('start');
    adopt(res);
  } catch (err) {
    $('#app').innerHTML = `<div class="boot"><div class="empty"><div class="empty-icon">${icon('alert')}</div><h3>Pagina non disponibile</h3><p>${esc(err.message)}</p></div></div>`;
    return;
  }
  try { S.collapsed = localStorage.getItem('sidebar') === 'collapsed'; } catch { /* senza memoria locale */ }
  if (window.setTheme && S.st.theme && S.st.theme !== window.getTheme()) window.setTheme(S.st.theme);
  render();
  announceUpdate(S.st.update);
}

// ---------------------------------------------------------------------------------------------------------------------
// Struttura
// ---------------------------------------------------------------------------------------------------------------------
function navItem(p) {
  const badge = p.badge ? p.badge() : 0;
  return `<button class="nav-item ${p.key === S.page ? 'is-active' : ''}" data-action="go" data-page="${p.key}">${icon(p.icon)}<span>${esc(p.title)}</span>
    <span class="nav-badge is-neutral ${badge ? '' : 'hidden'}" data-badge="${p.key}">${fmtNumber(badge)}</span></button>`;
}

function shell() {
  const st = S.st;
  const top = NAV.filter((p) => !p.section).map(navItem).join('');
  const sections = SECTIONS.map((s) => {
    const items = NAV.filter((p) => p.section === s.key).sort((a, b) => (a.order || 0) - (b.order || 0));
    if (!items.length) return '';
    return `<div class="nav-section"><div class="nav-title"><span>${esc(s.title)}</span></div><div class="nav-items">${items.map(navItem).join('')}</div></div>`;
  }).join('');
  return `<div class="app ${S.collapsed ? 'is-collapsed' : ''}">
    <aside class="sidebar">
      <div class="brand"><span class="brand-mark">${icon('logo', { size: 20 })}</span><span class="brand-short">WP</span>
        <div class="brand-text"><span class="brand-name">WhoIs Pro</span><span class="brand-sub">Rete e domini <span class="brand-version">${esc(st.version)}</span></span></div></div>
      <nav class="nav">
        <div class="nav-section"><div class="nav-items">${top}</div></div>
        ${sections}
      </nav>
      <div class="side-foot">
        <button class="collapse-btn" data-action="collapse" title="Riduci o allarga il menu">${icon('panelLeft')}<span>Riduci il menu</span></button>
      </div>
    </aside>
    <main class="main">
      <header class="topbar">
        <div class="crumbs">WhoIs Pro ${icon('chevronRight')} <b id="crumb-page"></b></div>
        <div class="topbar-middle" id="topbar-middle">${activityChip()}</div>
        <div class="topbar-actions" id="topbar-actions">${topbarActions()}</div>
      </header>
      <div class="content" id="content"></div>
      <footer class="app-foot">WhoIs Pro ${esc(st.version)} · ${esc(st.copyright || '© 2026 Bruno Leonardi. Tutti i diritti riservati.')}</footer>
    </main>
  </div>`;
}

/** Attività in corso: cosa sta girando e a che punto è, oppure null. */
function activityData() {
  const r = (S.st && S.st.running) || {};
  if (!isRunning()) return null;
  let title = 'Scansione in corso';
  let sub = '';
  let pct = null;
  if (r.network) {
    const p = S.net && S.net.progress;
    sub = p ? `Rete · ${fmtNumber(p.scanned)} / ${fmtNumber(p.total)} indirizzi · ${fmtNumber(S.data.hosts.length)} host` : 'Rete · avvio…';
    pct = p ? p.percent : 0;
  } else if (r.ports) {
    const p = S.ports && S.ports.progress;
    sub = p ? `Porte di ${S.ports.host} · ${fmtNumber(p.scanned)} / ${fmtNumber(p.total)}` : 'Porte · avvio…';
    pct = p ? p.percent : 0;
  } else if (r.trace) {
    title = 'Traceroute in corso';
    sub = `Verso ${S.trace ? S.trace.current || S.trace.target : ''}`;
  } else if (r.oui) {
    title = 'Aggiornamento in corso';
    sub = 'Database dei produttori';
  }
  return { title: `${title}${pct != null ? ` · ${pct}%` : ''}`, sub, pct };
}

/** Riquadro dell'attività nella barra superiore; vuoto quando non c'è niente in corso. */
function activityChip() {
  const d = activityData();
  if (!d) return '';
  return `<button class="activity-chip" data-action="monitor" title="Vai alla pagina dell'attività in corso">
    <span class="pulse is-busy"></span>
    <span class="activity-text"><b>${esc(d.title)}</b><small>${esc(d.sub)}</small></span>
    ${d.pct != null ? `<span class="meter"><span style="width:${d.pct}%"></span></span>` : ''}</button>`;
}

/** Aggiorna il riquadro sul posto (testo e barra), senza ricrearlo: niente sfarfallio a ogni avanzamento. */
function updateActivity() {
  const middle = $('#topbar-middle');
  if (!middle) return;
  const d = activityData();
  const chip = $('.activity-chip', middle);
  if (!d) { if (chip) chip.remove(); return; }
  if (!chip || !!$('.meter', chip) !== (d.pct != null)) { middle.innerHTML = activityChip(); return; }
  const title = $('b', chip);
  const sub = $('small', chip);
  if (title.textContent !== d.title) title.textContent = d.title;
  if (sub.textContent !== d.sub) sub.textContent = d.sub;
  const bar = $('.meter > span', chip);
  if (bar) bar.style.width = `${d.pct}%`;
}

function internetPill() {
  const online = S.st ? S.st.online : null;
  if (online == null) return `<span class="net-pill" title="Controllo della connessione a Internet in corso">${icon('wifi')}<span>Internet</span></span>`;
  return online
    ? `<span class="net-pill is-on" title="Connesso a Internet (controllo ogni 30 secondi)">${icon('wifi')}<span>Internet</span></span>`
    : `<span class="net-pill is-off" title="Senza connessione a Internet: WHOIS, DNS pubblici e aggiornamenti non funzionano">${icon('wifiOff')}<span>Senza Internet</span></span>`;
}

function topbarActions() {
  const s = S.st.session;
  const dirty = s.dirty;
  const label = s.file ? fileName(s.file) : 'Sessione non salvata';
  return `${internetPill()}<button class="site-picker session-chip" data-action="session-menu" title="${esc(s.file || 'La sessione non è ancora stata salvata in un file')}">
      <span class="site-icon ${dirty ? 'is-dirty' : s.file ? 'is-saved' : ''}">${icon(dirty ? 'edit' : s.file ? 'check' : 'archive')}</span>
      <span class="site-label"><small>${dirty ? 'Modifiche non salvate' : s.file ? 'Sessione salvata' : 'Sessione'}</small><b>${esc(label)}</b></span>${icon('chevronDown')}</button>
    <button class="btn ${dirty ? 'btn-primary' : ''}" data-action="save" title="Salva la sessione (Ctrl+S)">${icon('save')} Salva</button>`;
}

function render() {
  $('#app').innerHTML = shell();
  renderPage();
}

function renderPage(scrollTop = false) {
  const content = $('#content');
  if (!content) return render();
  const page = PAGES[S.page] || PAGES.overview;
  content.innerHTML = page.render();
  content.classList.remove('page-enter');
  void content.offsetWidth;
  content.classList.add('page-enter');
  $$('.nav-item[data-page]').forEach((a) => a.classList.toggle('is-active', a.dataset.page === S.page));
  $('#crumb-page').textContent = page.title;
  if (scrollTop) window.scrollTo({ top: 0 });
  if (page.after) page.after();
}

/** Dopo un cambiamento di stato senza ridisegnare tutto: sessione nella barra, contatori, attività in corso. */
function updateChrome() {
  if (!S.st) return;
  const actions = $('#topbar-actions');
  if (actions) actions.innerHTML = topbarActions();
  updateActivity();
  for (const p of NAV) {
    const el = $(`[data-badge="${p.key}"]`);
    if (!el) continue;
    const n = p.badge ? p.badge() : 0;
    el.textContent = fmtNumber(n);
    el.classList.toggle('hidden', !n);
  }
  const overview = $('.nav-item[data-page="overview"]');
  if (overview) overview.classList.toggle('has-notice', !!(S.st.update && S.st.update.available));
}

function go(page, params = {}, scrollTop = true) {
  closeOverlays();
  closeMenu();
  if (S.page !== page) {
    const old = PAGES[S.page];
    if (old && old.leave) old.leave();
    S.page = page;
    renderPage(scrollTop);
  }
  const p = PAGES[page];
  if (p && p.enter && Object.keys(params).length) p.enter(params);
}
setNavigator(go);

// ---------------------------------------------------------------------------------------------------------------------
// Sessione
// ---------------------------------------------------------------------------------------------------------------------
/** Prima di perdere i dati: vero se si può procedere (salvato, o l'utente ha scelto di non salvare). */
async function confirmDiscard(what) {
  if (!S.st.session.dirty) return true;
  const total = Object.values(counts()).reduce((a, b) => a + b, 0);
  if (!total) return true;
  const answer = await askDialog({
    title: 'Salvare la sessione?', ic: 'save',
    message: `La sessione ha dati non salvati. Vuoi salvarla prima di ${what}?`,
    yesText: 'Salva', noText: 'Non salvare',
  });
  if (answer == null) return false;
  if (answer === 'yes') return !!(await saveSession(false));
  return true;
}

async function newSession() {
  if (!(await confirmDiscard('crearne una nuova'))) return;
  try {
    adopt(await call('newSession'));
    updateChrome();
    renderPage(true);
    toast('Nuova sessione vuota.', { tone: 'success' });
  } catch (err) { toastError(err); }
}

async function openSession() {
  if (!(await confirmDiscard('aprirne un\'altra'))) return;
  try {
    const res = await call('openSession');
    if (!res) return;
    const r = adopt(res);
    updateChrome();
    renderPage(true);
    const c = counts();
    toast(`${plural(c.hosts, 'host', 'host')}, ${plural(c.ports, 'porta', 'porte')}, ${plural(c.whois, 'ricerca WHOIS', 'ricerche WHOIS')}, ${plural(c.dns, 'record DNS', 'record DNS')}, ${plural(c.trace, 'salto', 'salti')}.`, { tone: 'success', title: fileName(r.file) });
    if (r.restoredFromBackup) toast('Il file principale era danneggiato: la sessione viene dalla copia .bak. Salvala per rigenerare il file.', { tone: 'warning', title: 'Ripristino dalla copia di sicurezza', timeout: 12000 });
  } catch (err) { toastError(err, 'Sessione non aperta'); }
}

async function saveSession(saveAs) {
  try {
    const res = await call('saveSession', { saveAs });
    if (!res) return null;
    const r = adopt(res);
    updateChrome();
    if (S.page === 'overview') renderPage();
    toast(`SHA-256 ${r.checksum.slice(0, 16)}…${r.backup ? ' · copia precedente in .bak' : ''}`, { tone: 'success', title: `Salvata: ${fileName(r.file)}` });
    return r;
  } catch (err) {
    toastError(err, 'Sessione non salvata');
    return null;
  }
}

async function exportSession(format) {
  try {
    const r = await call('export', { format });
    if (!r) return;
    toast(fileName(r.file), { tone: 'success', title: `Esportazione ${format.toUpperCase()} completata`, action: { label: 'Apri', run: () => call('openFile', { path: r.file }).catch((e) => toastError(e)) } });
  } catch (err) { toastError(err, 'Esportazione non riuscita'); }
}

function sessionMenu(anchor) {
  const s = S.st.session;
  openMenu(anchor, [
    { header: s.file ? fileName(s.file) : 'Sessione non salvata' },
    ...(s.file ? [{ html: `<div class="menu-path">${esc(s.file)}</div>` }] : []),
    { ic: 'filePlus', label: 'Nuova sessione', hint: 'Ctrl+N', run: newSession },
    { ic: 'folderOpen', label: 'Apri…', hint: 'Ctrl+O', run: openSession },
    { ic: 'save', label: 'Salva', hint: 'Ctrl+S', run: () => saveSession(false) },
    { ic: 'download', label: 'Salva con nome…', hint: 'Ctrl+Maiusc+S', run: () => saveSession(true) },
    { sep: true },
    { ic: 'fileCode', label: 'Esporta report HTML…', run: () => exportSession('html') },
    { ic: 'sheet', label: 'Esporta host in CSV…', run: () => exportSession('csv') },
    { ic: 'fileText', label: 'Esporta riepilogo di testo…', run: () => exportSession('txt') },
    { sep: true },
    { ic: 'dashboard', label: 'Panoramica', run: () => go('overview') },
  ]);
}

function exportMenu(anchor) {
  openMenu(anchor, [
    { ic: 'fileCode', label: 'Report HTML…', run: () => exportSession('html') },
    { ic: 'sheet', label: 'Host in CSV (Excel)…', run: () => exportSession('csv') },
    { ic: 'fileText', label: 'Riepilogo di testo…', run: () => exportSession('txt') },
  ]);
}

async function updateOui() {
  if (S.st.running.oui) return;
  S.st.running.oui = true;
  S.overview.ouiMessage = 'Connessione al server di Wireshark…';
  bus.dispatchEvent(new Event('state'));
  updateChrome();
  try {
    const r = adopt(await call('updateOui'));
    updateChrome();
    if (S.page === 'overview') renderPage();
    toast(`${plural(r.count, 'produttore', 'produttori')}${r.changedHosts ? ` · ${plural(r.changedHosts, 'host riclassificato', 'host riclassificati')}` : ''}.`, { tone: 'success', title: 'Database dei produttori aggiornato' });
  } catch (err) {
    S.st.running.oui = false;
    updateChrome();
    if (S.page === 'overview') renderPage();
    toastError(err, 'Database non aggiornato');
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Aggiornamenti
// ---------------------------------------------------------------------------------------------------------------------
let updateNotified = false;

function announceUpdate(u) {
  if (!u || !u.available || updateNotified) return;
  updateNotified = true;
  toast(`WhoIs Pro ${u.version} è pronta: aprila dalla Panoramica.`, { tone: 'info', title: 'Nuova versione disponibile', timeout: 12000, action: { label: 'Vedi', run: () => go('overview') } });
}

async function checkUpdate() {
  S.st.update.checking = true;
  bus.dispatchEvent(new Event('state'));
  try {
    const u = await call('checkUpdate');
    u.checking = false;
    S.st.update = u;
    bus.dispatchEvent(new Event('state'));
    if (u.error) toast(u.error, { tone: 'error', title: 'Controllo non riuscito' });
    else if (u.available) toast(`Versione ${u.version} pubblicata${u.publishedAt ? ` il ${u.publishedAt.slice(8, 10)}/${u.publishedAt.slice(5, 7)}/${u.publishedAt.slice(0, 4)}` : ''}.`, { tone: 'info', title: 'Nuova versione disponibile' });
    else toast(`La ${u.current} è l'ultima versione pubblicata.`, { tone: 'success', title: 'Nessun aggiornamento' });
  } catch (err) {
    S.st.update.checking = false;
    bus.dispatchEvent(new Event('state'));
    toastError(err, 'Controllo non riuscito');
  }
}

async function installUpdate() {
  const u = S.st.update;
  if (!u || !u.available) return;
  if (!(await confirmDiscard('aggiornare il programma'))) return;
  const ok = await confirmDialog({
    title: `Installare WhoIs Pro ${u.version}?`, ic: 'download', tone: '', confirmText: 'Scarica e installa',
    message: `Il setup (${(u.setupSize / 1048576).toFixed(0)} MB) viene scaricato da GitHub e controllato (dimensione, impronta SHA-256 e versione). Poi il programma si chiude, si aggiorna e si riapre da solo.`,
  });
  if (!ok) return;
  S.st.update.download = { phase: 'download', version: u.version, done: 0, total: u.setupSize, error: '' };
  bus.dispatchEvent(new Event('state'));
  try {
    await call('installUpdate');
  } catch (err) {
    S.st.update.download = { phase: 'error', version: u.version, done: 0, total: 0, error: err.message };
    bus.dispatchEvent(new Event('state'));
    toastError(err, 'Aggiornamento non riuscito');
  }
}

on('update.status', (u) => {
  if (!S.st) return;
  u.checking = false;
  S.st.update = u;
  bus.dispatchEvent(new Event('state'));
  announceUpdate(u);
});
on('update.progress', (d) => {
  if (!S.st || !S.st.update) return;
  S.st.update.download = d;
  bus.dispatchEvent(new Event('state'));
});
on('internet', (d) => {
  if (!S.st) return;
  const before = S.st.online;
  S.st.online = d.online;
  updateChrome();
  if (before != null && before !== d.online) {
    toast(d.online ? 'La connessione a Internet è tornata.' : 'WHOIS, DNS pubblici e aggiornamenti non funzionano finché la connessione non torna.',
      { tone: d.online ? 'success' : 'warning', title: d.online ? 'Connesso a Internet' : 'Senza Internet' });
  }
});

// ---------------------------------------------------------------------------------------------------------------------
// Eventi
// ---------------------------------------------------------------------------------------------------------------------
const ACTIONS = {
  'check-update': () => checkUpdate(),
  'install-update': () => installUpdate(),
  'go': (el) => go(el.dataset.page),
  'collapse': () => {
    S.collapsed = !S.collapsed;
    $('.app').classList.toggle('is-collapsed', S.collapsed);
    try { localStorage.setItem('sidebar', S.collapsed ? 'collapsed' : 'open'); } catch { /* senza memoria locale */ }
  },
  'monitor': () => {
    const r = S.st.running;
    go(r.network ? 'network' : r.ports ? 'ports' : r.trace ? 'trace' : 'overview');
  },
  'check-internet': () => call('checkInternet').catch(() => {}),
  'session-menu': (el) => sessionMenu(el),
  'export-menu': (el) => exportMenu(el),
  'new': () => newSession(),
  'open': () => openSession(),
  'save': () => saveSession(false),
  'save-as': () => saveSession(true),
  'export': (el) => exportSession(el.dataset.format),
  'oui-update': () => updateOui(),
  'open-url': (el) => call('openUrl', { url: el.dataset.url }),
  'copy-text': (el) => call('setClipboard', { text: el.dataset.text }).then(() => toast('Copiato negli appunti.', { tone: 'success' }), (e) => toastError(e)),
  'close-overlay': () => closeTopOverlay(),
};

document.addEventListener('click', (e) => {
  const sortHead = e.target.closest('[data-sort]');
  if (sortHead) {
    const page = PAGES[S.page];
    if (page && page.onSort) page.onSort(sortHead.dataset.sort);
    return;
  }
  const el = e.target.closest('[data-action]');
  if (!el || el.disabled) return;
  const page = PAGES[S.page];
  const fn = (page && page.actions && page.actions[el.dataset.action]) || ACTIONS[el.dataset.action];
  if (!fn) return;
  e.preventDefault();
  fn(el, e);
});

// Tasto destro su una riga con data-ctx: il menu delle operazioni della pagina, nel punto del clic
document.addEventListener('contextmenu', (e) => {
  const row = e.target.closest('[data-ctx]');
  if (!row) return;
  const page = PAGES[S.page];
  if (!page || !page.onContext) return;
  e.preventDefault();
  closeMenu();
  page.onContext(row, { x: e.clientX, y: e.clientY });
});

document.addEventListener('submit', (e) => {
  const form = e.target.closest('[data-form]');
  if (!form) return;
  e.preventDefault();
  if (form.hasAttribute('data-modal-form')) return;
  const page = PAGES[S.page];
  if (page && page.onSubmit) page.onSubmit(form, form.dataset.form);
});

document.addEventListener('input', (e) => {
  const field = e.target.dataset.field;
  if (!field) return;
  const page = PAGES[S.page];
  if (page && page.onInput) page.onInput(e.target, field);
});

document.addEventListener('change', (e) => {
  const field = e.target.dataset.field;
  if (!field) return;
  const page = PAGES[S.page];
  if (page && page.onChange) page.onChange(e.target, field);
});

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    if (closeMenu()) return;
    if (closeTopOverlay()) e.preventDefault();
    return;
  }
  if (e.key === 'Enter' && e.target.matches('th[data-sort]')) { e.target.click(); return; }
  if (!S.st || hasOverlay()) return;
  if (e.ctrlKey && !e.altKey) {
    const k = e.key.toLowerCase();
    if (k === 's') { e.preventDefault(); saveSession(e.shiftKey); }
    if (k === 'o') { e.preventDefault(); openSession(); }
    if (k === 'n') { e.preventDefault(); newSession(); }
  }
});

// file .whoispro trascinati sulla finestra: non si aprono da qui (serve la finestra di Windows), niente navigazione
document.addEventListener('dragover', (e) => e.preventDefault());
document.addEventListener('drop', (e) => e.preventDefault());

// stato e dati che cambiano (scansioni in corso, elementi nuovi): barra superiore, contatori e attività
bus.addEventListener('state', updateChrome);
bus.addEventListener('session', updateChrome);
bus.addEventListener('data', updateChrome);
on('network.progress', updateChrome);
on('ports.progress', updateChrome);
on('oui.progress', () => { S.st.running.oui = true; updateChrome(); });

start();
