// Panoramica: conteggi della sessione, file e salvataggio, database dei produttori, schede di rete.
import { call, on } from '../core/bridge.js';
import { $, esc, fmtDateTime, fmtNumber, plural, shortPath } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage, adopt, counts, bus } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { table } from '../core/table.js';
import { note, openMenu, pageHead, toast, toastError } from '../core/ui.js';
import { applyProfile } from './settings.js';

S.overview = { ouiMessage: '' };

function kpi(page, ic, tone, label, value, sub) {
  return `<button class="kpi is-link" data-action="go" data-page="${page}">
    <div class="kpi-top"><span class="kpi-label">${label}</span><span class="kpi-icon ${tone}">${icon(ic, { size: 16 })}</span></div>
    <div class="kpi-value">${fmtNumber(value)}</div><div class="kpi-sub">${sub}</div></button>`;
}

function sessionCard() {
  const s = S.st.session;
  const status = s.dirty
    ? '<span class="pill warning pill-dot">Modifiche non salvate</span>'
    : s.file ? '<span class="pill success pill-dot">Salvata</span>' : '<span class="pill">Vuota, mai salvata</span>';
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('archive')}<h2>Sessione</h2></div>${status}</div>
    <div class="card-body"><div class="fields">
      <div class="field"><label for="f-title">Titolo</label>
        <input class="input title-input" id="f-title" data-field="title" value="${esc(s.title)}" maxlength="120" autocomplete="off" placeholder="Es. Rete ufficio, ottobre 2026"></div>
      <dl class="kv compact">
        <dt>File</dt><dd>${s.file ? `<span class="mono" title="${esc(s.file)}">${esc(shortPath(s.file))}</span>` : '<span class="muted">Non ancora salvata</span>'}</dd>
        <dt>SHA-256</dt><dd>${s.checksum ? `<span class="mono" title="${esc(s.checksum)}">${esc(s.checksum.slice(0, 24))}…</span>` : '<span class="muted">—</span>'}</dd>
        <dt>Creata</dt><dd>${fmtDateTime(s.createdAt)}</dd>
        <dt>Modificata</dt><dd>${fmtDateTime(s.modifiedAt)}</dd>
      </dl>
      <div class="field"><label for="f-notes">Note</label>
        <textarea class="input" id="f-notes" data-field="notes" rows="3" placeholder="Appunti sulla sessione (finiscono nel file)">${esc(s.notes || '')}</textarea></div>
      <div class="card-actions">
        <button class="btn ${s.dirty ? 'btn-primary' : ''}" data-action="save">${icon('save')} Salva</button>
        <button class="btn" data-action="save-as">${icon('download')} Salva con nome…</button>
        <button class="btn" data-action="export-menu">${icon('fileText')} Esporta ${icon('chevronDown', { size: 14 })}</button>
      </div>
    </div></div>
    <div class="card-foot">${icon('shield', { size: 15 })} Salvataggio atomico: file temporaneo, SHA-256 e sostituzione in un colpo solo, con copia .bak della versione precedente.</div>
  </section>`;
}

function ouiCard() {
  const o = S.st.oui;
  const busy = S.st.running.oui;
  const msg = S.overview.ouiMessage;
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('database')}<h2>Database dei produttori</h2></div>
      <span class="pill primary">${plural(o.count, 'produttore', 'produttori')}</span></div>
    <div class="card-body"><div class="fields">
      <dl class="kv compact">
        <dt>Origine</dt><dd>${o.file ? `<span class="mono" title="${esc(o.file)}">${esc(shortPath(o.file))}</span>` : '<span class="muted">Solo i produttori incorporati nel programma</span>'}</dd>
        <dt>Serve a</dt><dd>dare il nome del produttore (e il tipo di dispositivo) a ogni MAC trovato nella scansione</dd>
      </dl>
      ${busy ? `<div class="form-note info" id="oui-status"><span class="spinner"></span><div class="note-text">${esc(msg || 'Aggiornamento in corso…')}</div></div>`
        : note('info', 'info', 'L\'elenco ufficiale di Wireshark/IEEE (circa 3 MB) viene salvato in <span class="mono">%LOCALAPPDATA%\\WhoIsPro</span> e gli host della sessione vengono riclassificati.')}
      <div class="card-actions"><button class="btn" data-action="oui-update" ${busy ? 'disabled' : ''}>${icon('refresh')} Aggiorna da Wireshark</button></div>
    </div></div></section>`;
}

/** Riquadro degli aggiornamenti: versione in uso, ultima pubblicata su GitHub, «Scarica e installa». */
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
  const notes = u.available && u.notes ? `<div><div class="form-section-head"><div class="form-section-title">${icon('fileText')}${esc(u.title || `Novità della ${u.version}`)}</div></div>
      <pre class="raw" style="max-height:260px">${esc(u.notes.replace(/\*\*/g, ''))}</pre></div>` : '';
  const install = !u.available ? ''
    : (u.installed && u.hasSetup)
      ? `<button class="btn btn-primary" data-action="install-update" ${busy ? 'disabled' : ''}>${icon('download')} Scarica e installa</button>`
      : `<button class="btn btn-primary" data-action="open-url" data-url="${esc(u.pageUrl)}">${icon('external')} Apri la pagina dei download</button>`;
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('rotate')}<h2>Aggiornamenti</h2></div>${pill}</div>
    <div class="card-body"><div class="fields">
      <dl class="kv compact">
        <dt>Versione in uso</dt><dd><b>${esc(u.current)}</b>${u.installed ? '' : ' <span class="tag">copia portabile</span>'}</dd>
        <dt>Ultima pubblicata</dt><dd>${u.version ? `<b>${esc(u.version)}</b>${u.publishedAt ? ` · ${fmtDateTime(u.publishedAt)}` : ''}` : '<span class="muted">—</span>'}</dd>
        <dt>Ultimo controllo</dt><dd>${u.checkedAt ? fmtDateTime(u.checkedAt) : '<span class="muted">mai</span>'}</dd>
      </dl>
      ${u.error ? note('danger', 'alert', esc(u.error)) : ''}
      ${notes}${progress}
      ${!u.available && !u.error ? note('info', 'info', 'Una volta al giorno il programma chiede a GitHub qual è l\'ultima versione pubblicata. Niente si installa da solo: quando c\'è una versione nuova compare qui «Scarica e installa».') : ''}
      ${u.available && !u.installed ? note('info', 'info', 'Questa copia non è stata installata con il setup: scarica il nuovo pacchetto dalla pagina delle release e sostituisci i file.') : ''}
      <div class="card-actions">
        ${install}
        <button class="btn ${u.checking ? 'is-loading' : ''}" data-action="check-update" ${busy || u.checking ? 'disabled' : ''}>${icon('refresh')} Controlla ora</button>
        <button class="btn btn-ghost" data-action="open-url" data-url="${esc(u.pageUrl || 'https://github.com/brn78/WhoIs-Pro/releases/latest')}">${icon('external')} Tutte le versioni</button>
      </div>
    </div></div></section>`;
}

function adaptersCard() {
  const rows = S.st.adapters || [];
  const columns = [
    { key: 'name', label: 'Scheda', cell: (a) => `<div class="cell-main"><span class="cat-icon primary">${icon('network')}</span><div class="cell-text"><b>${esc(a.name)}</b><small>${esc(a.description)}</small></div></div>` },
    { key: 'ip', label: 'Indirizzo IP', cls: 'mono' },
    { key: 'cidr', label: 'Rete', cls: 'mono' },
    { key: 'gateway', label: 'Gateway', cls: 'mono', cell: (a) => esc(a.gateway || '—') },
    { key: 'mac', label: 'MAC', cls: 'mono' },
    { key: 'actions', label: '', cls: 'actions', sortable: false, cell: (a) => `<div class="row-actions">
        <button class="btn btn-sm" data-action="scan-adapter" data-cidr="${esc(a.cidr)}">${icon('radar')} Scansiona</button>
        <button class="btn btn-sm" data-action="adapter-profiles" data-adapter="${esc(a.name)}" title="Profili di rete: cambia l'indirizzo IP della scheda">${icon('network')} Profilo ${icon('chevronDown', { size: 13 })}</button></div>` },
  ];
  return `<section class="card table-card"><div class="card-head"><div class="card-title">${icon('network')}<h2>Schede di rete</h2><span class="count">${rows.length}</span></div>
      <button class="btn btn-sm" data-action="detect-adapters">${icon('refresh')} Rileva di nuovo</button></div>
    ${table({ columns, rows, sort: null, empty: '<div class="empty"><h3>Nessuna scheda di rete attiva</h3><p>Nessuna interfaccia IPv4 collegata.</p></div>' })}
  </section>`;
}

function render() {
  const c = counts();
  const s = S.st.session;
  const sub = s.file ? `File <b>${esc(s.fileName)}</b>${s.dirty ? ' · modifiche non salvate' : ''}` : 'Sessione non ancora salvata: i dati raccolti si salvano in un file .whoispro.';
  return `${pageHead('dashboard', 'Panoramica', sub,
    `<button class="btn" data-action="new">${icon('filePlus')} Nuova</button>
     <button class="btn" data-action="open">${icon('folderOpen')} Apri…</button>
     <button class="btn ${s.dirty ? 'btn-primary' : ''}" data-action="save">${icon('save')} Salva</button>`)}
    <div class="kpis kpis-5">
      ${kpi('network', 'radar', '', 'Host trovati', c.hosts, 'scansione della rete')}
      ${kpi('ports', 'plug', 'success', 'Porte aperte', c.ports, 'scansione delle porte')}
      ${kpi('whois', 'globe', 'info', 'Ricerche WHOIS', c.whois, 'domini e indirizzi')}
      ${kpi('dns', 'dns', 'neutral', 'Record DNS', c.dns, 'A, AAAA, MX, NS, TXT, PTR')}
      ${kpi('trace', 'route', 'warning', 'Salti traceroute', c.trace, 'percorsi verso le destinazioni')}
    </div>
    <div class="grid grid-2">${sessionCard()}<div class="grid">${ouiCard()}${updateCard()}</div></div>
    <div class="grid section-gap">${adaptersCard()}</div>`;
}

/** Menu rapido dei profili di rete per una scheda: applica un profilo salvato, torna a DHCP o ne crea uno. */
async function adapterProfiles(anchor, adapter) {
  let settings;
  try { settings = await call('settings'); } catch (err) { toastError(err); return; }
  const mine = (settings.profiles || []).filter((p) => p.adapter === adapter);
  const items = [{ header: adapter }];
  for (const p of mine) items.push({ ic: 'zap', label: `Applica «${p.name}»`, hint: p.dhcp ? 'DHCP' : p.ip, run: () => applyProfile(p.id) });
  if (!mine.length) items.push({ html: '<div class="menu-path">Nessun profilo per questa scheda</div>' });
  items.push({ sep: true });
  items.push({ ic: 'rotate', label: 'Torna a DHCP', run: () => applyProfile(null, { id: '', name: 'DHCP', adapter, dhcp: true, ip: '', mask: '', gateway: '', dns1: '', dns2: '' }) });
  items.push({ ic: 'plus', label: 'Nuovo profilo per questa scheda…', run: () => navigate('settings', { newProfile: adapter }) });
  items.push({ ic: 'settings', label: 'Tutti i profili (Impostazioni)', run: () => navigate('settings') });
  openMenu(anchor, items);
}

const actions = {
  'scan-adapter': (el) => navigate('network', { target: el.dataset.cidr }),
  'adapter-profiles': (el) => adapterProfiles(el, el.dataset.adapter),
  'detect-adapters': async () => {
    try {
      S.st.adapters = await call('adapters');
      rerender();
      toast(plural(S.st.adapters.length, 'scheda di rete attiva', 'schede di rete attive'), { tone: 'success' });
    } catch (err) { toastError(err); }
  },
};

const saveInfo = async () => {
  const title = $('#f-title');
  const notes = $('#f-notes');
  if (!title || !notes) return;
  if (title.value.trim() === S.st.session.title && notes.value === (S.st.session.notes || '')) return;
  try {
    adopt(await call('sessionInfo', { title: title.value, notes: notes.value }));
    rerender();
  } catch (err) { toastError(err); }
};

function rerender() {
  if (S.page !== 'overview') return;
  const content = $('#content');
  if (content) content.innerHTML = render();
}

on('oui.progress', (d) => {
  S.overview.ouiMessage = d.message;
  const box = $('#oui-status .note-text');
  if (box) box.textContent = d.message;
});
bus.addEventListener('state', () => { if (S.page === 'overview' && !$('#f-title:focus') && !$('#f-notes:focus')) rerender(); });
bus.addEventListener('session', rerender);

definePage('overview', {
  title: 'Panoramica', icon: 'dashboard', section: '',
  render,
  actions,
  onChange: (el, field) => { if (field === 'title' || field === 'notes') saveInfo(); },
});
