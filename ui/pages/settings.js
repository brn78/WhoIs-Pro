// Impostazioni: avvio con Windows e icona nell'area di notifica, tema, profili di rete (IP della scheda al volo).
import { call } from '../core/bridge.js';
import { $, esc, shortPath } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage, adopt, emit } from '../core/store.js';
import { table } from '../core/table.js';
import { closeOverlays, confirmDialog, emptyState, note, openDrawer, openMenu, pageHead, toast, toastError } from '../core/ui.js';

S.settings = { data: null, adapters: null, busy: false, loading: false };

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
    </dl></div></section>`;
}

function render() {
  const d = S.settings.data;
  const head = pageHead('Avvio con Windows, icona nell\'area di notifica, tema e profili di rete.');
  if (!d) return `${head}<div class="card"><div class="loading-block"><span class="spinner"></span> Lettura delle impostazioni…</div></div>`;
  return `${head}<div class="grid grid-2">${startupCard(d)}${themeCard(d)}</div>
    <div class="grid section-gap">${profilesCard(d)}${adaptersCard()}${dataCard(d)}</div>`;
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

definePage('settings', {
  title: 'Impostazioni', heading: 'Impostazioni', icon: 'settings', section: 'system', order: 3,
  render,
  after: () => { if (!S.settings.data || !S.settings.adapters) load(); },
  enter: (params) => { if (params.newProfile) setTimeout(() => actions['save-adapter']({ dataset: { adapter: params.newProfile } }), S.settings.adapters ? 0 : 1200); },
  actions,
  onChange: (el, field) => {
    if (['autoStart', 'trayIcon', 'closeToTray', 'startMinimized'].includes(field)) {
      const labels = { autoStart: 'Avvio con Windows', trayIcon: 'Icona nell\'area di notifica', closeToTray: 'Chiusura nell\'area di notifica', startMinimized: 'Avvio ridotto a icona' };
      save({ [field]: el.checked }, `${labels[field]}: ${el.checked ? 'attivo' : 'disattivato'}.`);
    }
  },
});
