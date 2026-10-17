// WhoIs Pro: la pagina della finestra (WebView2). Scansioni, file e appunti li fa il programma (UI\AppBridge.vb);
// qui ci sono la struttura (barra laterale, barra superiore), la sessione e il passaggio tra le pagine (pages\).
import { call, on } from './core/bridge.js';
import { $, $$, esc, fmtNumber, fileName, plural } from './core/dom.js';
import { icon } from './core/icons.js';
import { S, PAGES, NAV, bus, setState, adopt, counts, isRunning } from './core/store.js';
import { setNavigator } from './core/nav.js';
import './core/clearable.js';
import { offerAutoScan } from './core/autoscan.js';
import { askDialog, closeMenu, closeOverlays, closeTopOverlay, confirmDialog, hasOverlay, openMenu, toast, toastError } from './core/ui.js';
import './pages/overview.js';
import './pages/network.js';
import './pages/ports.js';
import './pages/whois.js';
import './pages/dns.js';
import './pages/trace.js';
import './pages/netstat.js';
import './pages/computer.js';
import './pages/perf.js';
import './pages/capture.js';
import { openPcNetwork } from './pages/settings.js';
import { openSessionDrawer } from './pages/session.js';

const SECTIONS = [
  { key: 'scan', title: 'Scansione' },
  { key: 'lookup', title: 'Ricerca' },
  { key: 'system', title: 'Sistema' },
];

// ---------------------------------------------------------------------------------------------------------------------
// Avvio
// ---------------------------------------------------------------------------------------------------------------------
async function start() {
  let res;
  try {
    res = await call('start');
    adopt(res);
  } catch (err) {
    $('#app').innerHTML = `<div class="boot"><div class="empty"><div class="empty-icon">${icon('alert')}</div><h3>Pagina non disponibile</h3><p>${esc(err.message)}</p></div></div>`;
    return;
  }
  try { S.collapsed = localStorage.getItem('sidebar') === 'collapsed'; } catch { /* senza memoria locale */ }
  if (window.setTheme && S.st.theme && S.st.theme !== window.getTheme()) window.setTheme(S.st.theme);
  render();
  announceReopen(res && res.reopened);
  announceUpdate(S.st.update);
  offerAutoScan(res && res.autoScan);
}

/** Avvio: l'ultima sessione salvata è stata riaperta (o non c'è più, o non si legge). */
function announceReopen(r) {
  if (!r) return;
  const name = fileName(r.file);
  if (r.missing) toast(`Il file ${name} non c'è più (spostato o cancellato): si parte da una sessione nuova.`, { tone: 'warning', title: 'Ultima sessione non trovata', timeout: 9000 });
  else if (r.error) toast(`${name}: ${r.error}. Si parte da una sessione nuova; prova ad aprirla con «Apri…».`, { tone: 'error', title: 'Ultima sessione non riaperta', timeout: 12000 });
  else if (r.restoredFromBackup) toast('Il file principale era danneggiato: la sessione viene dalla copia .bak. Salvala per rigenerare il file.', { tone: 'warning', title: `Riaperta ${name} dalla copia di sicurezza`, timeout: 12000 });
  else {
    const c = counts();
    toast(`${plural(c.hosts, 'host', 'host')}, ${plural(c.ports, 'porta', 'porte')}, ${plural(S.data.scans.length, 'scansione', 'scansioni')}.`, { tone: 'success', title: `Riaperta l'ultima sessione: ${name}` });
  }
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
        <div class="topbar-title" id="topbar-title"></div>
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
  } else if (r.capture) {
    const p = S.capture && S.capture.progress;
    title = 'Cattura dei pacchetti';
    sub = !p || !p.started ? 'in attesa del consenso di Windows' : p.converting ? 'lettura dei pacchetti' : `${fmtNumber(Math.round(p.seconds || 0))} s · ${Math.round((p.bytes || 0) / 1024)} KB`;
    pct = p && p.limit && p.started ? Math.min(100, Math.round(((p.seconds || 0) * 100) / p.limit)) : null;
  } else if (r.perf) {
    const L = S.perf && S.perf.live;
    title = 'Test delle prestazioni';
    sub = L ? L.message : 'avvio…';
    pct = L ? L.percent : 0;
  } else if (r.autoPorts) {
    const a = (S.net && S.net.auto) || S.st.autoPorts || { done: 0, total: 0 };
    title = 'Porte degli host trovati';
    sub = `${fmtNumber(a.done)} di ${fmtNumber(a.total)} host · cinque alla volta`;
    pct = a.total ? Math.round((a.done * 100) / a.total) : 0;
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
  // il riquadro arancione della scansione all'avvio (core/autoscan.js) sta qui accanto e non va toccato
  const chip = $('.activity-chip:not(.autoscan-chip)', middle);
  if (!d) { if (chip) chip.remove(); return; }
  if (!chip || !!$('.meter', chip) !== (d.pct != null)) {
    if (chip) chip.remove();
    middle.insertAdjacentHTML('beforeend', activityChip());
    return;
  }
  const title = $('b', chip);
  const sub = $('small', chip);
  if (title.textContent !== d.title) title.textContent = d.title;
  if (sub.textContent !== d.sub) sub.textContent = d.sub;
  const bar = $('.meter > span', chip);
  if (bar) bar.style.width = `${d.pct}%`;
}

function internetPill() {
  // stessa forma della sessione accanto: riquadro con bordo, icona colorata, descrizione breve sopra e stato sotto
  const online = S.st ? S.st.online : null;
  const pill = (cls, ic, small, text, title) => `<span class="site-picker net-chip ${cls}" title="${title}">
      <span class="site-icon">${icon(ic)}</span><span class="site-label"><small>${small}</small><b>${text}</b></span></span>`;
  if (online == null) return pill('', 'wifi', 'Connessione a Internet', 'Controllo…', 'Controllo della connessione a Internet in corso');
  return online
    ? pill('is-on', 'wifi', 'Connessione a Internet', 'Attiva', 'Connesso a Internet (controllo ogni 30 secondi)')
    : pill('is-off', 'wifiOff', 'Connessione a Internet', 'Assente', 'Senza connessione a Internet: WHOIS, DNS pubblici e aggiornamenti non funzionano');
}

function topbarActions() {
  const st = S.st;
  const s = st.session;
  const dirty = s.dirty;
  const label = s.file ? fileName(s.file).replace(/\.whoispro$/i, '') : 'Sessione non salvata';
  return `${internetPill()}<button class="site-picker session-chip" data-action="session-menu" title="${esc(s.file || 'La sessione non è ancora stata salvata in un file')}">
      <span class="site-icon ${dirty ? 'is-dirty' : s.file ? 'is-saved' : ''}">${icon(dirty ? 'edit' : s.file ? 'check' : 'archive')}</span>
      <span class="site-label"><small>${dirty ? 'Modifiche non salvate' : s.file ? 'Sessione salvata' : 'Sessione'}</small><b>${esc(label)}</b></span>${icon('chevronDown')}</button>
    ${dirty ? `<button class="btn btn-primary" data-action="save" title="Salva la sessione (Ctrl+S)">${icon('save')} Salva</button>` : ''}
    <span class="topbar-sep"></span>
    <button class="icon-btn" data-action="pc-network" title="Rete del PC: schede, IP, gateway, DNS e profili">${icon('network')}</button>
    <button class="icon-btn ${S.page === 'settings' ? 'is-active' : ''}" data-action="go" data-page="settings" title="Impostazioni${st.update && st.update.available ? ' · nuova versione disponibile' : ''}">${icon('settings')}${st.update && st.update.available ? '<span class="dot-badge"></span>' : ''}</button>`;
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
  const gear = $('.topbar [data-page="settings"]');
  if (gear) gear.classList.toggle('is-active', S.page === 'settings');
  $('#topbar-title').innerHTML = `<span class="page-icon">${icon(page.icon)}</span><h1>${esc(page.heading || page.title)}</h1>`;
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
    { ic: 'edit', label: 'Dettagli della sessione…', run: () => openSessionDrawer() },
    { sep: true },
    { ic: 'filePlus', label: 'Nuova sessione', hint: 'Ctrl+N', run: newSession },
    { ic: 'folderOpen', label: 'Apri…', hint: 'Ctrl+O', run: openSession },
    { ic: 'save', label: 'Salva', hint: 'Ctrl+S', run: () => saveSession(false) },
    { ic: 'download', label: 'Salva con nome…', hint: 'Ctrl+Maiusc+S', run: () => saveSession(true) },
    { sep: true },
    { ic: 'fileCode', label: 'Esporta report HTML…', run: () => exportSession('html') },
    { ic: 'sheet', label: 'Esporta host in CSV…', run: () => exportSession('csv') },
    { ic: 'fileText', label: 'Esporta riepilogo di testo…', run: () => exportSession('txt') },
  ]);
}

function exportMenu(anchor) {
  openMenu(anchor, [
    { ic: 'fileCode', label: 'Report HTML…', run: () => exportSession('html') },
    { ic: 'sheet', label: 'Host in CSV (Excel)…', run: () => exportSession('csv') },
    { ic: 'fileText', label: 'Riepilogo di testo…', run: () => exportSession('txt') },
  ]);
}

let ouiActive = false;

async function updateOui() {
  if (S.st.running.oui) return;
  ouiActive = true;
  S.st.running.oui = true;
  S.settings.ouiMessage = 'Connessione ai registri dell\'IEEE…';
  bus.dispatchEvent(new Event('state'));
  updateChrome();
  try {
    const res = await call('updateOui');
    ouiActive = false;
    const r = adopt(res);
    S.st.running.oui = false;
    updateChrome();
    if (S.page === 'settings') renderPage();
    toast(`${plural(r.count, 'produttore', 'produttori')}${r.changedHosts ? ` · ${plural(r.changedHosts, 'host riclassificato', 'host riclassificati')}` : ''}.`, { tone: 'success', title: 'Database dei produttori aggiornato' });
  } catch (err) {
    ouiActive = false;
    S.st.running.oui = false;
    updateChrome();
    if (S.page === 'settings') renderPage();
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
  toast(`WhoIs Pro ${u.version} è pronta: la trovi in Impostazioni, Aggiornamenti.`, { tone: 'info', title: 'Nuova versione disponibile', timeout: 12000, action: { label: 'Vedi', run: () => go('settings', { tab: 'updates' }) } });
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
// il programma chiede di aprire una pagina (per esempio «Crea un profilo…» dal menu dell'icona)
on('navigate', (d) => { if (d && d.page && PAGES[d.page]) go(d.page, d.tab ? { tab: d.tab } : {}); });

// aggiornamento automatico all'avvio: avviso prima che il setup chiuda la finestra
on('update.auto', (d) => {
  updateNotified = true;
  toast(`Scaricamento e installazione della ${d.version}: il programma si chiude e si riapre aggiornato.`, { tone: 'info', title: 'Aggiornamento automatico', timeout: 30000, action: { label: 'Vedi', run: () => go('settings', { tab: 'updates' }) } });
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
    go(r.capture ? 'capture' : r.perf ? 'perf' : r.network || r.autoPorts ? 'network' : r.ports ? 'ports' : r.trace ? 'trace' : 'overview');
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
  'pc-network': () => openPcNetwork(),
  'session-details': () => openSessionDrawer(),
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
  scrollListByKey(e);
});

// ---------------------------------------------------------------------------------------------------------------------
// Frecce, PagSu/PagGiù, Inizio e Fine sugli elenchi: gli elenchi (host, porte, connessioni…) scorrono dentro il loro
// riquadro (.table-wrap), che non prende il focus; senza questo i tasti muoverebbero solo la pagina. Vale per l'elenco
// su cui si è cliccato l'ultima volta, altrimenti per il primo elenco della pagina che ha qualcosa da scorrere.
// ---------------------------------------------------------------------------------------------------------------------
const SCROLL_KEYS = new Set(['ArrowDown', 'ArrowUp', 'PageDown', 'PageUp', 'Home', 'End']);
let lastScroller = null;
const scrollable = (el) => el && el.isConnected && el.scrollHeight > el.clientHeight + 2;

document.addEventListener('mousedown', (e) => {
  const box = e.target.closest && e.target.closest('#content .table-wrap');
  if (box) lastScroller = box;
}, true);

function scrollListByKey(e) {
  if (!SCROLL_KEYS.has(e.key) || e.ctrlKey || e.altKey || e.metaKey) return;
  const t = e.target;
  if (t.closest && t.closest('input, textarea, select, [contenteditable="true"], .menu')) return;
  const box = scrollable(lastScroller) ? lastScroller : [...$$('#content .table-wrap')].find(scrollable);
  if (!box) return;
  const page = Math.max(40, box.clientHeight - 60);
  const delta = { ArrowDown: 48, ArrowUp: -48, PageDown: page, PageUp: -page }[e.key];
  const atEdge = delta > 0 ? box.scrollTop + box.clientHeight >= box.scrollHeight - 1 : box.scrollTop <= 0;
  // in fondo (o in cima) all'elenco le frecce tornano a muovere la pagina
  if (delta != null && atEdge && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) return;
  e.preventDefault();
  if (e.key === 'Home') box.scrollTo({ top: 0, behavior: 'smooth' });
  else if (e.key === 'End') box.scrollTo({ top: box.scrollHeight, behavior: 'smooth' });
  else box.scrollBy({ top: delta, behavior: e.repeat ? 'auto' : 'smooth' });
}

// file .whoispro trascinati sulla finestra: non si aprono da qui (serve la finestra di Windows), niente navigazione
document.addEventListener('dragover', (e) => e.preventDefault());
document.addEventListener('drop', (e) => e.preventDefault());

// stato e dati che cambiano (scansioni in corso, elementi nuovi): barra superiore, contatori e attività
bus.addEventListener('state', updateChrome);
bus.addEventListener('session', updateChrome);
bus.addEventListener('data', updateChrome);
on('network.progress', updateChrome);
on('ports.progress', updateChrome);
on('autoports.progress', updateChrome);
on('autoports.host', updateChrome);
on('perf.progress', updateChrome);
on('perf.done', updateChrome);
on('capture.progress', updateChrome);
on('capture.done', updateChrome);
// i messaggi di avanzamento passano da una coda: gli ultimi arrivano dopo la fine e non devono riaccendere l'attività
on('oui.progress', () => { if (!ouiActive) return; S.st.running.oui = true; updateChrome(); });

// aggiornamento automatico del database dei produttori all'avvio (Impostazioni → Produttori): stesso riquadro
// «Aggiornamento in corso» e, alla fine, un avviso con quanti produttori e host riclassificati
on('oui.auto', (d) => {
  if (!S.st) return;
  ouiActive = true;
  S.st.running.oui = true;
  S.settings.ouiMessage = d && d.message ? d.message : 'Aggiornamento in corso…';
  updateChrome();
  if (S.page === 'settings') renderPage();
});
on('oui.done', (res) => {
  ouiActive = false;
  const r = adopt(res);
  S.st.running.oui = false;
  updateChrome();
  if (S.page === 'settings') renderPage();
  toast(`${plural(r.count, 'produttore', 'produttori')}${r.changedHosts ? ` · ${plural(r.changedHosts, 'host riclassificato', 'host riclassificati')}` : ''}.`, { tone: 'success', title: 'Database dei produttori aggiornato da solo' });
});
on('oui.failed', (d) => {
  ouiActive = false;
  if (d && d.state) adopt({ state: d.state });
  if (S.st) S.st.running.oui = false;
  updateChrome();
  if (S.page === 'settings') renderPage();
});

start();
