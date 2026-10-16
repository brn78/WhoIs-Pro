// Impostazioni (dalla barra superiore): avvio con Windows e area di notifica, tema, rete del PC con i profili di rete
// (IP della scheda al volo), aggiornamenti del programma, database dei produttori e cartella dei dati.
import { call, on } from '../core/bridge.js';
import { $, esc, fmtBytes, fmtDateTime, fmtNumber, plural, shortPath } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage, adopt, bus, emit } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { table } from '../core/table.js';
import { closeOverlays, confirmDialog, emptyState, help, note, openDrawer, openMenu, pageHead, toast, toastError } from '../core/ui.js';

S.settings = { data: null, adapters: null, busy: false, loading: false, tab: 'general', ouiMessage: '', notesOpen: false };

const TABS = [
  { key: 'general', ic: 'settings', label: 'Generale' },
  { key: 'network', ic: 'network', label: 'Rete del PC' },
  { key: 'updates', ic: 'rotate', label: 'Aggiornamenti' },
  { key: 'vendors', ic: 'database', label: 'Produttori' },
  { key: 'data', ic: 'folder', label: 'Dati del programma' },
];

const sw = (field, label, text, checked, disabled = false) => `<label class="switch"><input type="checkbox" data-field="${field}" ${checked ? 'checked' : ''} ${disabled ? 'disabled' : ''}><span class="track"></span>
    <span class="switch-text">${label}<small>${text}</small></span></label>`;

function startupCard(d) {
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('power')}<h2>Avvio e area di notifica</h2></div></div>
    <div class="card-body"><div class="switch-list">
      ${sw('autoStart', 'Avvia con Windows', 'All\'accesso dell\'utente, senza diritti di amministratore (voce in HKCU\\…\\Run).', d.autoStart)}
      ${sw('trayIcon', 'Icona nell\'area di notifica', 'Accanto all\'orologio: clic per aprire la finestra, tasto destro per uscire.', d.trayIcon)}
      ${sw('closeToTray', 'La X nasconde nell\'area di notifica', 'Il programma resta in esecuzione; si chiude da «Esci» nel menu dell\'icona.', d.closeToTray, !d.trayIcon)}
      ${sw('startMinimized', 'All\'avvio con Windows resta solo l\'icona', 'La finestra si apre con un clic sull\'icona.', d.startMinimized, !d.trayIcon || !d.autoStart)}
    </div></div></section>`;
}

function themeCard(d) {
  const opt = (value, ic, label, text) => `<button class="choice-option theme-option ${d.theme === value ? 'is-active' : ''}" data-action="theme" data-theme="${value}">
      <span class="choice-icon">${icon(ic)}</span><span class="choice-text"><b>${label}</b><small>${text}</small></span>${d.theme === value ? icon('check') : ''}</button>`;
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('sunMoon')}<h2>Tema</h2></div></div>
    <div class="card-body"><div class="theme-options">
      ${opt('auto', 'sunMoon', 'Automatico', 'Come il tema delle app di Windows (Impostazioni → Personalizzazione → Colori).')}
      ${opt('light', 'sun', 'Chiaro', 'Sempre chiaro.')}
      ${opt('dark', 'moon', 'Scuro', 'Sempre scuro.')}
    </div></div></section>`;
}

function profileText(p) {
  if (p.dhcp) return '<span class="pill info">DHCP</span>';
  return `<span class="profile-config">${esc(p.ip)} / ${esc(p.mask)}${p.gateway ? ` · gw ${esc(p.gateway)}` : ''}${p.dns1 ? ` · DNS ${esc(p.dns1)}${p.dns2 ? `, ${esc(p.dns2)}` : ''}` : ''}</span>`;
}

function profilesCard(d) {
  const columns = [
    { key: 'name', label: 'Profilo', cls: 'strong-cell' },
    { key: 'adapter', label: 'Scheda' },
    { key: 'config', label: 'Configurazione', sortable: false, cell: profileText },
    { key: 'actions', label: '', cls: 'actions', sortable: false, cell: (p) => `<div class="row-actions">
        <button class="btn btn-sm btn-primary" data-action="apply-profile" data-id="${esc(p.id)}">${icon('zap')} Applica</button>
        <button class="row-menu-btn" data-action="profile-menu" data-id="${esc(p.id)}" title="Altre azioni">${icon('more')}</button></div>` },
  ];
  return `<section class="card table-card"><div class="card-head"><div class="card-title">${icon('network')}<h2>Profili di rete</h2><span class="count">${d.profiles.length}</span></div>
      <button class="btn btn-sm btn-primary" data-action="new-profile">${icon('plus')} Nuovo profilo</button></div>
    <div class="card-body" style="padding-bottom:0">${note('info', 'info', 'Un profilo è l\'indirizzo IP di una scheda per un sito o un cliente: DHCP oppure IP, maschera, gateway e DNS. «Applica» lo scrive nella scheda con <span class="mono">netsh</span>: Windows chiede il consenso (UAC) ogni volta, il programma non ha bisogno di diritti di amministratore.')}</div>
    ${table({ columns, rows: d.profiles, sort: null, empty: emptyState('network', 'Nessun profilo', 'Salva la configurazione di una scheda con «Nuovo profilo» o da «Salva come profilo» qui sotto.') })}
  </section>`;
}

function adaptersCard() {
  const rows = S.settings.adapters;
  if (!rows) return `<section class="card"><div class="loading-block"><span class="spinner"></span> Lettura delle schede di rete…</div></section>`;
  const columns = [
    { key: 'name', label: 'Scheda', cell: (a) => `<div class="cell-main"><span class="cat-icon ${a.up ? 'primary' : ''}">${icon('network')}</span><div class="cell-text"><b class="trunc" title="${esc(a.name)}">${esc(a.name)}</b><small class="trunc" title="${esc(a.description)}">${esc(a.description)}</small></div></div>` },
    { key: 'up', label: 'Stato', cell: (a) => (a.up ? '<span class="pill success pill-dot">Collegata</span>' : '<span class="pill pill-dot">Scollegata</span>') },
    { key: 'dhcp', label: 'Indirizzo', cell: (a) => (a.dhcp ? '<span class="pill info">DHCP</span>' : '<span class="pill primary">Statico</span>') },
    { key: 'ip', label: 'IP / maschera', cls: 'mono', cell: (a) => (a.ip ? `${esc(a.ip)} / ${esc(a.mask)}` : '<span class="faint">—</span>') },
    { key: 'gateway', label: 'Gateway', cls: 'mono', cell: (a) => esc(a.gateway || '—') },
    { key: 'dns', label: 'DNS', cls: 'mono', cell: (a) => `<span class="trunc w-name" title="${esc((a.dns || []).join(', '))}">${esc((a.dns || []).join(', ') || '—')}</span>` },
    { key: 'actions', label: '', cls: 'actions', sortable: false, cell: (a) => `<button class="row-menu-btn" data-action="adapter-menu" data-adapter="${esc(a.name)}" title="Azioni">${icon('more')}</button>` },
  ];
  return `<section class="card table-card"><div class="card-head"><div class="card-title">${icon('hardDrive')}<h2>Schede di rete del PC</h2><span class="count">${rows.length}</span></div>
      <button class="btn btn-sm" data-action="reload-adapters">${icon('refresh')} Aggiorna</button></div>
    ${table({ columns, rows, sort: null, empty: '<div class="empty"><h3>Nessuna scheda di rete</h3></div>' })}</section>`;
}

function dataCard(d) {
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('folder')}<h2>Dati del programma</h2></div></div>
    <div class="card-body"><dl class="kv compact">
      <dt>Cartella dei dati</dt><dd><span class="mono" title="${esc(d.dataFolder)}">${esc(d.dataFolder)}</span></dd>
      <dt>Contenuto</dt><dd>impostazioni, database dei produttori aggiornato, esito del controllo versioni, posizione della finestra, dati di WebView2</dd>
      <dt>Programma</dt><dd><span class="mono" title="${esc(d.exePath)}">${esc(shortPath(d.exePath))}</span></dd>
    </dl></div></section>
    <section class="card"><div class="card-head"><div class="card-title">${icon('history')}<h2>Dispositivi conosciuti</h2></div>
      <span class="pill primary">${plural(d.devices || 0, 'dispositivo', 'dispositivi')}</span></div>
    <div class="card-body"><div class="fields">
      <p class="card-text">Ogni dispositivo trovato resta in un registro di questo PC, per MAC, con la data in cui è comparso la prima volta. Serve a segnalare chi si collega per la prima volta, anche tra una sessione e l'altra: con <b>Ripeti</b> nella pagina Rete diventa un controllo continuo.</p>
      <dl class="kv compact"><dt>File</dt><dd><span class="mono" title="${esc(d.devicesFile || '')}">${esc(shortPath(d.devicesFile || ''))}</span></dd></dl>
      <div class="card-actions" style="margin-top:0"><button class="btn btn-sm" data-action="clear-devices" ${d.devices ? '' : 'disabled'}>${icon('trash')} Svuota il registro</button></div>
    </div></div></section>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Aggiornamenti e database dei produttori (erano nella Panoramica)
// ---------------------------------------------------------------------------------------------------------------------
/**
 * Note della release (Markdown semplice del CHANGELOG) come HTML: elenco puntato con le righe spezzate riunite,
 * grassetto, corsivo e codice; tutto il resto come testo.
 */
function notesHtml(notes) {
  const inline = (t) => esc(t)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>')
    .replace(/\*([^*\n]+)\*/g, '<i>$1</i>');
  const blocks = [];
  let current = null;
  for (const raw of String(notes || '').split('\n')) {
    const line = raw.replace(/\s+$/, '');
    if (!line.trim()) { current = null; continue; }
    const bullet = /^\s*[-*]\s+(.*)$/.exec(line);
    if (bullet) { current = { type: 'li', text: bullet[1] }; blocks.push(current); continue; }
    const head = /^#+\s+(.*)$/.exec(line);
    if (head) { current = null; blocks.push({ type: 'h', text: head[1] }); continue; }
    if (current && /^\s/.test(raw)) { current.text += ` ${line.trim()}`; continue; }
    if (current && current.type === 'p') { current.text += ` ${line.trim()}`; continue; }
    current = { type: 'p', text: line.trim() };
    blocks.push(current);
  }
  let html = '';
  let inList = false;
  for (const b of blocks) {
    if (b.type === 'li') { if (!inList) { html += '<ul>'; inList = true; } html += `<li>${inline(b.text)}</li>`; continue; }
    if (inList) { html += '</ul>'; inList = false; }
    html += b.type === 'h' ? `<h4>${inline(b.text)}</h4>` : `<p>${inline(b.text)}</p>`;
  }
  if (inList) html += '</ul>';
  return html;
}

/** Versione in uso e ultima pubblicata, novità ripiegate, «Scarica e installa». */
function updateCard() {
  const u = S.st.update || {};
  const d = u.download;
  const busy = d && d.phase !== 'error';
  let pill;
  if (u.available) pill = `<span class="pill warning pill-dot">Nuova versione ${esc(u.version)}</span>`;
  else if (u.error) pill = '<span class="pill danger pill-dot">Controllo non riuscito</span>';
  else if (u.checkedAt) pill = '<span class="pill success pill-dot">Versione aggiornata</span>';
  else pill = '<span class="pill">Mai controllato</span>';

  let progress = '';
  if (d && d.phase === 'error') progress = note('danger', 'alert', esc(d.error));
  else if (d) {
    const mb = (n) => (n / 1048576).toFixed(1).replace('.', ',');
    const pct = d.total > 0 ? Math.min(100, Math.round(d.done * 100 / d.total)) : 0;
    const text = d.phase === 'download' ? `Scaricamento del setup ${esc(d.version)}: ${mb(d.done)} di ${mb(d.total)} MB`
      : d.phase === 'verify' ? 'Controllo di dimensione, impronta SHA-256 e versione del setup…'
        : 'Setup avviato: il programma si chiude e si riapre aggiornato.';
    progress = `<div class="stack" style="gap:8px"><div class="meter"><span style="width:${d.phase === 'download' ? pct : 100}%"></span></div>
      <div class="scan-stat"><span class="spinner"></span>${text}</div></div>`;
  }
  const open = S.settings.notesOpen;
  const notesLink = u.available && u.notes
    ? `<a href="#" class="notes-toggle" data-action="toggle-notes" aria-expanded="${open}">${esc(u.title || `Novità della ${u.version}`)} ${icon('chevronDown')}</a>` : '';
  const notes = u.available && u.notes && open ? `<div class="notes">${notesHtml(u.notes)}</div>` : '';
  const install = !u.available ? ''
    : (u.installed && u.hasSetup)
      ? `<button class="btn btn-sm btn-primary" data-action="install-update" ${busy ? 'disabled' : ''}>${icon('download')} Scarica e installa</button>`
      : `<button class="btn btn-sm btn-primary" data-action="open-url" data-url="${esc(u.pageUrl)}">${icon('external')} Apri la pagina dei download</button>`;
  const latest = u.version
    ? `<span class="muted">· ${esc(u.version)}${u.publishedAt ? `, ${fmtDateTime(u.publishedAt).slice(0, 10)}` : ''}</span>` : '<span class="muted">· —</span>';
  const checkHelp = help('Una volta al giorno il programma chiede a GitHub qual è l\'ultima versione pubblicata. Niente si installa da solo: quando c\'è una versione nuova compare qui «Scarica e installa».');
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('rotate')}<h2>Aggiornamenti</h2></div>${pill}</div>
    <div class="card-body"><div class="fields">
      <dl class="kv compact">
        <dt>In uso · ultima pubblicata</dt><dd><b>${esc(u.current)}</b>${u.installed ? '' : ' <span class="tag">copia portabile</span>'} ${latest}</dd>
        <dt>Ultimo controllo</dt><dd>${u.checkedAt ? fmtDateTime(u.checkedAt) : '<span class="muted">mai</span>'} ${checkHelp}</dd>
      </dl>
      ${u.error ? note('danger', 'alert', esc(u.error)) : ''}
      ${progress}
      ${u.available && !u.installed ? note('info', 'info', 'Questa copia non è stata installata con il setup: scarica il nuovo pacchetto dalla pagina delle release e sostituisci i file.') : ''}
      <div class="card-actions" style="margin-top:0">
        ${install}
        <button class="btn btn-sm ${u.checking ? 'is-loading' : ''}" data-action="check-update" ${busy || u.checking ? 'disabled' : ''}>${icon('refresh')} Controlla ora</button>
        <button class="btn btn-sm btn-ghost" data-action="open-url" data-url="${esc(u.pageUrl || 'https://github.com/brn78/WhoIs-Pro/releases/latest')}">${icon('external')} Tutte le versioni</button>
        ${notesLink}
      </div>
      ${notes}
    </div></div></section>`;
}

function ouiCard() {
  const o = S.st.oui;
  const info = o.info || {};
  const busy = S.st.running.oui;
  const msg = S.settings.ouiMessage;
  const days = info.updatedAt ? Math.floor((Date.now() - new Date(info.updatedAt).getTime()) / 86400000) : null;
  const age = days == null ? '' : days <= 0 ? 'oggi' : days === 1 ? 'ieri' : `${fmtNumber(days)} giorni fa`;
  const origin = !info.file ? '<span class="muted">Solo i produttori incorporati nel programma</span>'
    : info.source === 'IEEE' ? '<b>Registri ufficiali dell\'IEEE</b> <span class="muted">MA-L, MA-M e MA-S, scaricati da WhoIs Pro</span>'
      : info.downloaded ? '<b>Wireshark</b> <span class="muted">file manuf scaricato (l\'IEEE non aveva risposto)</span>'
        : '<b>Wireshark</b> <span class="muted">file manuf incluso nel programma</span>';
  const block = (label, n, bits) => `<span class="tag" title="Blocchi da ${bits} bit">${label} ${fmtNumber(n || 0)}</span>`;
  const old = days != null && days > 30;
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('database')}<h2>Database dei produttori</h2></div>
      <span class="pill ${old ? 'warning' : 'primary'}">${plural(info.prefixes || o.count, 'prefisso', 'prefissi')}</span></div>
    <div class="card-body"><div class="fields">
      <dl class="kv compact">
        <dt>Origine</dt><dd>${origin}</dd>
        <dt>Aggiornato</dt><dd>${info.updatedAt ? `${esc(fmtDateTime(info.updatedAt))} <span class="muted">· ${esc(age)}</span>` : '<span class="muted">—</span>'}</dd>
        <dt>Blocchi</dt><dd>${block('MA-L', info.maL, 24)} ${block('MA-M', info.maM, 28)} ${block('MA-S', info.maS, 36)} ${help('MA-L sono i blocchi grandi dati a un produttore (i primi 6 caratteri del MAC), MA-M e MA-S quelli medi e piccoli, più precisi: per ogni MAC si usa il blocco più preciso.')}</dd>
        <dt>File</dt><dd>${info.file ? `<span class="mono" title="${esc(info.file)}">${esc(shortPath(info.file))}</span> <span class="muted">· ${esc(fmtBytes(info.sizeBytes))}</span>` : '<span class="muted">—</span>'}</dd>
        <dt>Serve a</dt><dd>produttore e tipo di dispositivo ${help('I tre registri ufficiali dell\'IEEE (blocchi MA-L, MA-M e MA-S, aggiornati ogni giorno; se l\'IEEE non risponde, il database di Wireshark) vengono salvati in %LOCALAPPDATA%\\WhoIsPro e gli host della sessione vengono riclassificati. Per ogni MAC si usa il blocco più preciso.')}</dd>
      </dl>
      ${old && !busy ? note('warning', 'alert', `Il database ha ${fmtNumber(days)} giorni: i dispositivi usciti da poco potrebbero restare senza produttore. Aggiornalo dall'IEEE.`) : ''}
      ${busy ? `<div class="form-note info" id="oui-status"><span class="spinner"></span><div class="note-text">${esc(msg || 'Aggiornamento in corso…')}</div></div>` : ''}
      <div class="card-actions"><button class="btn btn-sm" data-action="oui-update" ${busy ? 'disabled' : ''}>${icon('refresh')} Aggiorna dall'IEEE</button></div>
    </div></div></section>`;
}

function tabsHtml() {
  const tab = S.settings.tab;
  return `<div class="tabs" role="tablist">${TABS.map((t) => `<button class="tab ${t.key === tab ? 'is-active' : ''}" role="tab" aria-selected="${t.key === tab}" data-action="settings-tab" data-tab="${t.key}">${icon(t.ic)}${t.label}${t.key === 'updates' && S.st.update && S.st.update.available ? '<span class="tab-dot"></span>' : ''}</button>`).join('')}</div>`;
}

function render() {
  const d = S.settings.data;
  const head = pageHead('Come si avvia il programma, il tema, la rete di questo PC e i profili di rete, gli aggiornamenti e il database dei produttori.');
  if (!d) return `${head}${tabsHtml()}<div class="card"><div class="loading-block"><span class="spinner"></span> Lettura delle impostazioni…</div></div>`;
  let body;
  switch (S.settings.tab) {
    case 'network': body = `<div class="grid">${adaptersCard()}${profilesCard(d)}</div>`; break;
    case 'updates': body = `<div class="grid settings-narrow">${updateCard()}</div>`; break;
    case 'vendors': body = `<div class="grid settings-narrow">${ouiCard()}</div>`; break;
    case 'data': body = `<div class="grid settings-narrow">${dataCard(d)}</div>`; break;
    default: body = `<div class="grid grid-2">${startupCard(d)}${themeCard(d)}</div>`;
  }
  return `${head}${tabsHtml()}${body}`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Rete del PC: pannello rapido dalla barra superiore
// ---------------------------------------------------------------------------------------------------------------------
function pcNetworkBody() {
  const rows = S.settings.adapters;
  if (!rows) return '<div class="loading-block"><span class="spinner"></span> Lettura delle schede di rete…</div>';
  if (!rows.length) return emptyState('network', 'Nessuna scheda di rete', 'Windows non vede schede Ethernet o Wi-Fi.');
  const profiles = ((S.settings.data || {}).profiles || []);
  return `<div class="nic-list">${rows.map((a) => {
    const mine = profiles.filter((p) => p.adapter === a.name);
    return `<article class="nic ${a.up ? '' : 'is-down'}">
      <header class="nic-head"><span class="led ${a.up ? 'is-link' : 'is-off'}"></span>
        <div class="nic-titles"><b>${esc(a.name)}</b><small>${esc(a.description)}</small></div>
        <span class="pill ${a.up ? (a.dhcp ? 'info' : 'primary') : ''}">${a.up ? (a.dhcp ? 'DHCP' : 'Statico') : 'Scollegata'}</span></header>
      ${a.ip ? `<dl class="nic-facts">
        <div><dt>IP</dt><dd class="mono">${esc(a.ip)}</dd></div><div><dt>Maschera</dt><dd class="mono">${esc(a.mask)}</dd></div>
        <div><dt>Gateway</dt><dd class="mono">${esc(a.gateway || '—')}</dd></div><div><dt>DNS</dt><dd class="mono">${esc((a.dns || []).join(', ') || '—')}</dd></div>
        <div><dt>MAC</dt><dd class="mono">${esc(a.mac)}</dd></div></dl>` : '<p class="card-text muted nic-none">Nessun indirizzo IPv4.</p>'}
      <footer class="nic-actions">
        ${mine.map((p) => `<button class="btn btn-sm" data-pcn="apply" data-id="${esc(p.id)}" title="${esc(p.dhcp ? 'DHCP' : `${p.ip} / ${p.mask}`)}">${icon('zap')} ${esc(p.name)}</button>`).join('')}
        ${a.dhcp ? '' : `<button class="btn btn-sm" data-pcn="dhcp" data-adapter="${esc(a.name)}">${icon('rotate')} Torna a DHCP</button>`}
        <button class="btn btn-sm btn-ghost" data-pcn="save" data-adapter="${esc(a.name)}">${icon('plus')} Salva come profilo</button>
      </footer></article>`;
  }).join('')}</div>`;
}

/** «Rete del PC»: schede con IP, gateway e DNS, profili da applicare al volo e ritorno a DHCP. */
export async function openPcNetwork() {
  const d = openDrawer({
    ic: 'network', title: 'Rete del PC', sub: 'Schede di rete di questo computer e profili da applicare', body: pcNetworkBody(), size: 'drawer-sm',
    foot: `<div class="left"><button class="btn" data-pcn="settings">${icon('settings')} Profili di rete…</button></div>
      <button class="btn btn-ghost" data-pcn="reload">${icon('refresh')} Aggiorna</button><button class="btn btn-primary" data-action="close-overlay">Chiudi</button>`,
  });
  const paint = () => { const b = $('.drawer-body', d.wrap); if (b) b.innerHTML = pcNetworkBody(); };
  d.wrap.addEventListener('click', (e) => {
    const el = e.target.closest('[data-pcn]');
    if (!el) return;
    const kind = el.dataset.pcn;
    if (kind === 'settings') { closeOverlays(); navigate('settings', { tab: 'network' }); return; }
    if (kind === 'reload') { call('adapterConfigs').then((r) => { S.settings.adapters = r; paint(); }, toastError); return; }
    if (kind === 'apply') { applyProfile(el.dataset.id).then(paint); return; }
    if (kind === 'dhcp') { applyProfile(null, { id: '', name: 'DHCP', adapter: el.dataset.adapter, dhcp: true, ip: '', mask: '', gateway: '', dns1: '', dns2: '' }).then(paint); return; }
    if (kind === 'save') { closeOverlays(); actions['save-adapter']({ dataset: { adapter: el.dataset.adapter } }); }
  });
  try {
    if (!S.settings.data) S.settings.data = await call('settings');
    S.settings.adapters = await call('adapterConfigs');
    paint();
  } catch (err) { toastError(err); }
}

function rerender() {
  if (S.page !== 'settings') return;
  const content = $('#content');
  if (content) content.innerHTML = render();
}

async function load() {
  const s = S.settings;
  if (s.loading) return;
  s.loading = true;
  try {
    s.data = await call('settings');
    rerender();
    s.adapters = await call('adapterConfigs');
    rerender();
  } catch (err) { toastError(err); }
  s.loading = false;
}

async function save(patch, done = 'Impostazione salvata.') {
  try {
    S.settings.data = await call('saveSettings', patch);
    if (S.st) S.st.theme = S.settings.data.theme;
    rerender();
    toast(done, { tone: 'success' });
  } catch (err) {
    toastError(err, 'Impostazione non salvata');
    rerender();
  }
}

/** Modulo del profilo nel pannello laterale; p = profilo da modificare, oppure valori iniziali (nuovo). */
function profileDrawer(p, { fromAdapter = false } = {}) {
  const adapters = S.settings.adapters || [];
  const options = adapters.map((a) => `<option value="${esc(a.name)}" ${a.name === p.adapter ? 'selected' : ''}>${esc(a.name)} · ${esc(a.description)}</option>`).join('')
    + (p.adapter && !adapters.some((a) => a.name === p.adapter) ? `<option value="${esc(p.adapter)}" selected>${esc(p.adapter)} (non presente ora)</option>` : '');
  const field = (id, label, value, placeholder) => `<div class="field"><label for="${id}">${label}</label><input class="input input-mono" id="${id}" value="${esc(value || '')}" placeholder="${placeholder}" autocomplete="off" spellcheck="false"></div>`;
  const body = `<form class="fields" data-modal-form id="profile-form">
      <div class="field"><label for="p-name">Nome del profilo<span class="req">*</span></label><input class="input" id="p-name" value="${esc(p.name || '')}" placeholder="Es. Sede cliente Rossi, Casa, Cantiere" autocomplete="off"></div>
      <div class="field"><label for="p-adapter">Scheda di rete</label><div class="field-row"><select class="select" id="p-adapter" style="flex:1 1 auto">${options}</select>
        <button type="button" class="btn" data-action="read-adapter" title="Compila con la configurazione attuale della scheda">${icon('download')} Leggi dalla scheda</button></div></div>
      <label class="switch"><input type="checkbox" id="p-dhcp" ${p.dhcp ? 'checked' : ''}><span class="track"></span><span class="switch-text">Indirizzo automatico (DHCP)<small>Il router assegna IP, gateway e DNS.</small></span></label>
      <div class="form-grid" id="p-static" ${p.dhcp ? 'style="display:none"' : ''}>
        ${field('p-ip', 'Indirizzo IP', p.ip, '192.168.1.50')}
        ${field('p-mask', 'Maschera', p.mask || '255.255.255.0', '255.255.255.0')}
        ${field('p-gateway', 'Gateway', p.gateway, '192.168.1.1 (vuoto: nessuno)')}
        ${field('p-dns1', 'DNS primario', p.dns1, '192.168.1.1 oppure 1.1.1.1')}
        ${field('p-dns2', 'DNS secondario', p.dns2, 'facoltativo')}
      </div>
      <button type="submit" hidden></button>
    </form>`;
  const d = openDrawer({
    ic: 'network', title: p.id ? `Profilo «${p.name}»` : 'Nuovo profilo di rete',
    sub: fromAdapter ? `Configurazione attuale di ${p.adapter}` : 'IP della scheda per un sito o un cliente',
    body, size: 'drawer-sm',
    foot: `<div class="left">${p.id ? `<button class="btn btn-primary" data-action="save-apply-profile">${icon('zap')} Salva e applica</button>` : ''}</div>
      <button class="btn btn-ghost" data-action="close-overlay">Annulla</button><button class="btn btn-primary" data-action="submit-profile">${icon('save')} Salva</button>`,
  });
  d.wrap.dataset.profileId = p.id || '';
  $('#p-dhcp', d.wrap).addEventListener('change', (e) => { $('#p-static', d.wrap).style.display = e.target.checked ? 'none' : ''; });
  $('#profile-form', d.wrap).addEventListener('submit', (e) => { e.preventDefault(); submitProfile(false); });
  $('#p-name', d.wrap).focus();
}

function readForm() {
  const wrap = $('#overlays [data-profile-id]');
  if (!wrap) return null;
  const v = (id) => ($(`#${id}`, wrap)?.value || '').trim();
  return {
    id: wrap.dataset.profileId, name: v('p-name'), adapter: v('p-adapter'), dhcp: $('#p-dhcp', wrap).checked,
    ip: v('p-ip'), mask: v('p-mask'), gateway: v('p-gateway'), dns1: v('p-dns1'), dns2: v('p-dns2'),
  };
}

async function submitProfile(applyAfter) {
  const p = readForm();
  if (!p) return;
  try {
    S.settings.data = await call('saveProfile', p);
    closeOverlays();
    rerender();
    toast(`Profilo «${p.name}» salvato.`, { tone: 'success' });
    if (applyAfter) {
      const saved = S.settings.data.profiles.find((x) => x.name === p.name && x.adapter === p.adapter);
      if (saved) applyProfile(saved.id);
    }
  } catch (err) { toastError(err, 'Profilo non salvato'); }
}

function fillFromAdapter() {
  const wrap = $('#overlays [data-profile-id]');
  const name = $('#p-adapter', wrap)?.value;
  const a = (S.settings.adapters || []).find((x) => x.name === name);
  if (!a) { toast('Scheda non trovata.', { tone: 'warning' }); return; }
  $('#p-dhcp', wrap).checked = a.dhcp;
  $('#p-static', wrap).style.display = a.dhcp ? 'none' : '';
  $('#p-ip', wrap).value = a.ip || '';
  $('#p-mask', wrap).value = a.mask || '255.255.255.0';
  $('#p-gateway', wrap).value = a.gateway || '';
  $('#p-dns1', wrap).value = (a.dns || [])[0] || '';
  $('#p-dns2', wrap).value = (a.dns || [])[1] || '';
  toast(`Valori attuali di ${a.name} nel modulo.`, { tone: 'success' });
}

export async function applyProfile(id, values = null) {
  const p = values || (S.settings.data || {}).profiles?.find((x) => x.id === id);
  if (!p) { toast('Profilo non trovato.', { tone: 'error' }); return; }
  const ok = await confirmDialog({
    title: `Applicare «${p.name}» a ${p.adapter}?`, ic: 'network', tone: '', confirmText: 'Applica',
    message: p.dhcp ? 'La scheda torna a prendere IP, gateway e DNS dal router (DHCP). Windows chiede il consenso per netsh.'
      : `La scheda prende l'indirizzo ${p.ip} / ${p.mask}${p.gateway ? `, gateway ${p.gateway}` : ''}${p.dns1 ? `, DNS ${p.dns1}${p.dns2 ? ` e ${p.dns2}` : ''}` : ''}. Windows chiede il consenso per netsh.`,
  });
  if (!ok) return;
  try {
    toast('Conferma il consenso nella finestra di Windows…', { tone: 'info', timeout: 4000 });
    const res = await call('applyProfile', values ? values : { id });
    adopt({ state: res.state });
    S.settings.adapters = res.adapters;
    rerender();
    emit('state');
    const a = (res.adapters || []).find((x) => x.name === p.adapter);
    toast(a && a.ip ? `${p.adapter}: ${a.dhcp ? 'DHCP' : 'statico'} · ${a.ip}` : `${p.adapter} configurata.`, { tone: 'success', title: `Profilo «${p.name}» applicato` });
  } catch (err) { toastError(err, 'Profilo non applicato'); }
}

const actions = {
  'settings-tab': (el) => { S.settings.tab = el.dataset.tab; rerender(); },
  'clear-devices': async () => {
    const n = (S.settings.data || {}).devices || 0;
    if (!(await confirmDialog({ title: 'Svuotare il registro dei dispositivi?', message: `${plural(n, 'dispositivo conosciuto', 'dispositivi conosciuti')} vengono dimenticati: alla prossima scansione tutti torneranno «nuovi». Le sessioni salvate non cambiano.`, confirmText: 'Svuota', tone: 'danger' }))) return;
    try { S.settings.data = await call('clearDevices'); rerender(); toast('Registro dei dispositivi svuotato.', { tone: 'success' }); } catch (err) { toastError(err); }
  },
  'toggle-notes': () => { S.settings.notesOpen = !S.settings.notesOpen; rerender(); },
  'theme': (el) => {
    const theme = el.dataset.theme;
    if (window.setTheme) window.setTheme(theme);
    save({ theme }, `Tema: ${theme === 'auto' ? 'automatico' : theme === 'light' ? 'chiaro' : 'scuro'}.`);
  },
  'new-profile': () => {
    const list = S.settings.adapters || [];
    const best = list.find((a) => a.up && a.gateway) || list.find((a) => a.up) || list[0] || {};
    profileDrawer({ dhcp: false, mask: '255.255.255.0', adapter: best.name || '' });
  },
  'edit-profile': (el) => { const p = S.settings.data.profiles.find((x) => x.id === el.dataset.id); if (p) profileDrawer(p); },
  'delete-profile': async (el) => {
    const p = S.settings.data.profiles.find((x) => x.id === el.dataset.id);
    if (!p || !(await confirmDialog({ title: `Eliminare il profilo «${p.name}»?`, message: 'La scheda di rete resta com\'è: si elimina solo il profilo salvato.', confirmText: 'Elimina', tone: 'danger' }))) return;
    try { S.settings.data = await call('deleteProfile', { id: p.id }); rerender(); toast('Profilo eliminato.', { tone: 'success' }); } catch (err) { toastError(err); }
  },
  'apply-profile': (el) => applyProfile(el.dataset.id),
  'save-apply-profile': () => submitProfile(true),
  'submit-profile': () => submitProfile(false),
  'read-adapter': () => fillFromAdapter(),
  'save-adapter': (el) => {
    const a = (S.settings.adapters || []).find((x) => x.name === el.dataset.adapter);
    if (!a) return;
    profileDrawer({ name: '', adapter: a.name, dhcp: a.dhcp, ip: a.ip, mask: a.mask || '255.255.255.0', gateway: a.gateway, dns1: (a.dns || [])[0] || '', dns2: (a.dns || [])[1] || '' }, { fromAdapter: true });
  },
  'adapter-dhcp': (el) => applyProfile(null, { id: '', name: 'DHCP', adapter: el.dataset.adapter, dhcp: true, ip: '', mask: '', gateway: '', dns1: '', dns2: '' }),
  'adapter-menu': (el) => {
    const a = (S.settings.adapters || []).find((x) => x.name === el.dataset.adapter);
    if (!a) return;
    const mine = ((S.settings.data || {}).profiles || []).filter((p) => p.adapter === a.name);
    openMenu(el, [
      { header: a.name },
      { ic: 'plus', label: 'Salva come profilo…', run: () => actions['save-adapter'](el) },
      { ic: 'rotate', label: 'Torna a DHCP', disabled: !!a.dhcp, run: () => actions['adapter-dhcp'](el) },
      ...(mine.length ? [{ sep: true }, ...mine.map((p) => ({ ic: 'zap', label: `Applica «${p.name}»`, hint: p.dhcp ? 'DHCP' : p.ip, run: () => applyProfile(p.id) }))] : []),
    ]);
  },
  'profile-menu': (el) => {
    const p = (S.settings.data.profiles || []).find((x) => x.id === el.dataset.id);
    if (!p) return;
    openMenu(el, [
      { header: `${p.name} · ${p.adapter}` },
      { ic: 'zap', label: 'Applica', run: () => applyProfile(p.id) },
      { ic: 'edit', label: 'Modifica…', run: () => profileDrawer(p) },
      { sep: true },
      { ic: 'trash', label: 'Elimina', danger: true, run: () => actions['delete-profile'](el) },
    ]);
  },
  'reload-adapters': async () => {
    try { S.settings.adapters = await call('adapterConfigs'); rerender(); } catch (err) { toastError(err); }
  },
};

// profilo applicato dal menu dell'icona nell'area di notifica: schede e stato aggiornati anche qui
on('profile.applied', (d) => {
  adopt({ state: d.state });
  S.settings.adapters = d.adapters;
  rerender();
  emit('state');
  const a = (d.adapters || []).find((x) => x.name === d.profile.adapter);
  toast(a && a.ip ? `${d.profile.adapter}: ${a.dhcp ? 'DHCP' : 'statico'} · ${a.ip}` : `${d.profile.adapter} configurata.`, { tone: 'success', title: `Profilo «${d.profile.name}» applicato` });
});

on('oui.progress', (d) => {
  S.settings.ouiMessage = d.message;
  const box = $('#oui-status .note-text');
  if (box) box.textContent = d.message;
});
// aggiornamenti e database cambiano lo stato: la scheda aperta si ridisegna (non mentre si scrive in un campo)
bus.addEventListener('state', () => { if (S.page === 'settings' && ['updates', 'vendors'].includes(S.settings.tab)) rerender(); });

/** Impostazioni non sta nel menu laterale: si apre dall'ingranaggio della barra superiore. */
definePage('settings', {
  title: 'Impostazioni', heading: 'Impostazioni', icon: 'settings', section: 'hidden',
  render,
  after: () => { if (!S.settings.data || !S.settings.adapters) load(); },
  enter: (params) => {
    if (params.tab && TABS.some((t) => t.key === params.tab)) { S.settings.tab = params.tab; rerender(); }
    if (params.newProfile) {
      S.settings.tab = 'network';
      rerender();
      setTimeout(() => actions['save-adapter']({ dataset: { adapter: params.newProfile } }), S.settings.adapters ? 0 : 1200);
    }
  },
  actions,
  onChange: (el, field) => {
    if (['autoStart', 'trayIcon', 'closeToTray', 'startMinimized'].includes(field)) {
      const labels = { autoStart: 'Avvio con Windows', trayIcon: 'Icona nell\'area di notifica', closeToTray: 'Chiusura nell\'area di notifica', startMinimized: 'Avvio ridotto a icona' };
      save({ [field]: el.checked }, `${labels[field]}: ${el.checked ? 'attivo' : 'disattivato'}.`);
    }
  },
});
