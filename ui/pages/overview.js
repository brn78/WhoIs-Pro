// Panoramica: conteggi della sessione, file e salvataggio, database dei produttori, aggiornamenti, schede di rete.
import { call, on } from '../core/bridge.js';
import { $, esc, fmtDateTime, fmtNumber, plural, shortPath } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage, adopt, counts, bus } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { table } from '../core/table.js';
import { help, note, openMenu, pageHead, toast, toastError } from '../core/ui.js';
import { applyProfile } from './settings.js';

S.overview = { ouiMessage: '', notesOpen: false };

function kpi(page, ic, tone, label, value, sub) {
  return `<button class="kpi is-link" data-action="go" data-page="${page}" title="Apri la pagina">
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
      <div class="field"><label for="f-notes">Note</label>
        <textarea class="input" id="f-notes" data-field="notes" rows="3" placeholder="Appunti sulla sessione (finiscono nel file)">${esc(s.notes || '')}</textarea></div>
      <dl class="meta">
        <div><dt>File</dt><dd>${s.file ? `<span class="mono" title="${esc(s.file)}">${esc(shortPath(s.file))}</span>` : '<span class="muted">Non ancora salvata</span>'}</dd></div>
        <div><dt>SHA-256</dt><dd>${s.checksum ? `<span class="mono" title="${esc(s.checksum)}">${esc(s.checksum.slice(0, 24))}…</span>` : '<span class="muted">—</span>'}</dd></div>
        <div><dt>Creata</dt><dd>${fmtDateTime(s.createdAt) || '<span class="muted">—</span>'}</dd></div>
        <div><dt>Modificata</dt><dd>${fmtDateTime(s.modifiedAt) || '<span class="muted">—</span>'}</dd></div>
      </dl>
    </div></div>
    <div class="card-foot">
      <button class="btn btn-sm" data-action="save-as">${icon('download')} Salva con nome…</button>
      <button class="btn btn-sm" data-action="export-menu">${icon('fileText')} Esporta ${icon('chevronDown', { size: 13 })}</button>
      <span class="foot-note">Salvataggio atomico, copia .bak ${help('File temporaneo, SHA-256 e sostituzione in un colpo solo; la versione precedente resta in .bak.')}</span>
    </div>
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
        <dt>Serve a</dt><dd>produttore e tipo di dispositivo ${help('L\'elenco ufficiale di Wireshark/IEEE (circa 3 MB) viene salvato in %LOCALAPPDATA%\\WhoIsPro e gli host della sessione vengono riclassificati.')}</dd>
      </dl>
      ${busy ? `<div class="form-note info" id="oui-status"><span class="spinner"></span><div class="note-text">${esc(msg || 'Aggiornamento in corso…')}</div></div>` : ''}
      <div class="card-actions"><button class="btn btn-sm" data-action="oui-update" ${busy ? 'disabled' : ''}>${icon('refresh')} Aggiorna da Wireshark</button></div>
    </div></div></section>`;
}

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

/** Riquadro degli aggiornamenti: versione in uso e ultima pubblicata su una riga, novità ripiegate, «Scarica e installa». */
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
  const open = S.overview.notesOpen;
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

function adaptersCard() {
  const rows = S.st.adapters || [];
  const columns = [
    { key: 'name', label: 'Scheda', cell: (a) => `<div class="cell-main"><span class="cat-icon primary">${icon('network')}</span><div class="cell-text"><b class="trunc" title="${esc(a.name)}">${esc(a.name)}</b><small class="trunc" title="${esc(a.description)}">${esc(a.description)}</small></div></div>` },
    { key: 'ip', label: 'Indirizzo IP', cls: 'mono' },
    { key: 'cidr', label: 'Rete', cls: 'mono' },
    { key: 'gateway', label: 'Gateway', cls: 'mono', cell: (a) => esc(a.gateway || '—') },
    { key: 'mac', label: 'MAC', cls: 'mono' },
    { key: 'actions', label: '', cls: 'actions', sortable: false, cell: (a) => `<div class="row-actions">
        <button class="btn btn-sm" data-action="scan-adapter" data-cidr="${esc(a.cidr)}">${icon('radar')} Scansiona</button>
        <button class="row-menu-btn" data-action="adapter-menu" data-adapter="${esc(a.name)}" data-cidr="${esc(a.cidr)}" title="Altre azioni">${icon('more')}</button></div>` },
  ];
  return `<section class="card table-card"><div class="card-head"><div class="card-title">${icon('network')}<h2>Schede di rete</h2><span class="count">${rows.length}</span></div>
      <button class="btn btn-sm" data-action="detect-adapters">${icon('refresh')} Rileva di nuovo</button></div>
    ${table({ columns, rows, sort: null, empty: '<div class="empty"><h3>Nessuna scheda di rete attiva</h3><p>Nessuna interfaccia IPv4 collegata.</p></div>' })}
  </section>`;
}

function render() {
  const c = counts();
  const s = S.st.session;
  const total = `${plural(c.hosts, 'host', 'host')}, ${plural(c.ports, 'porta', 'porte')}, ${plural(c.whois, 'WHOIS', 'WHOIS')}, ${plural(c.dns, 'record DNS', 'record DNS')}, ${plural(c.trace, 'salto', 'salti')}`;
  const sub = s.file ? `File <b>${esc(s.fileName)}</b>${s.dirty ? ' · modifiche non salvate' : ''} · ${total}` : `Sessione non ancora salvata (i dati raccolti si salvano in un file .whoispro) · ${total}`;
  return `${pageHead(sub,
    `<button class="btn" data-action="new">${icon('filePlus')} Nuova</button>
     <button class="btn" data-action="open">${icon('folderOpen')} Apri…</button>`)}
    <div class="kpis kpis-5">
      ${kpi('network', 'radar', '', 'Host trovati', c.hosts, 'scansione della rete')}
      ${kpi('ports', 'plug', 'success', 'Porte aperte', c.ports, 'scansione delle porte')}
      ${kpi('whois', 'globe', 'info', 'Ricerche WHOIS', c.whois, 'domini e indirizzi')}
      ${kpi('dns', 'dns', 'neutral', 'Record DNS', c.dns, 'A, AAAA, MX, NS, TXT, PTR')}
      ${kpi('trace', 'route', 'warning', 'Salti traceroute', c.trace, 'percorsi verso le destinazioni')}
    </div>
    <div class="grid grid-2">${sessionCard()}<div class="grid">${updateCard()}${ouiCard()}</div></div>
    <div class="grid section-gap">${adaptersCard()}</div>`;
}

/** Menu ⋯ di una scheda: profili di rete (applica, DHCP, nuovo) e copia dell'indirizzo. */
async function adapterMenu(anchor, adapter, cidr) {
  let settings;
  try { settings = await call('settings'); } catch (err) { toastError(err); return; }
  const a = (S.st.adapters || []).find((x) => x.name === adapter) || {};
  const mine = (settings.profiles || []).filter((p) => p.adapter === adapter);
  const items = [{ header: adapter }];
  items.push({ ic: 'radar', label: `Scansiona ${cidr}`, run: () => navigate('network', { target: cidr }) });
  items.push({ sep: true });
  for (const p of mine) items.push({ ic: 'zap', label: `Applica «${p.name}»`, hint: p.dhcp ? 'DHCP' : p.ip, run: () => applyProfile(p.id) });
  if (!mine.length) items.push({ html: '<div class="menu-path">Nessun profilo per questa scheda</div>' });
  items.push({ ic: 'rotate', label: 'Torna a DHCP', run: () => applyProfile(null, { id: '', name: 'DHCP', adapter, dhcp: true, ip: '', mask: '', gateway: '', dns1: '', dns2: '' }) });
  items.push({ ic: 'plus', label: 'Nuovo profilo per questa scheda…', run: () => navigate('settings', { newProfile: adapter }) });
  items.push({ ic: 'settings', label: 'Tutti i profili (Impostazioni)', run: () => navigate('settings') });
  items.push({ sep: true });
  items.push({ ic: 'copy', label: 'Copia l\'indirizzo IP', disabled: !a.ip, run: () => copy(a.ip) });
  items.push({ ic: 'copy', label: 'Copia il MAC', disabled: !a.mac, run: () => copy(a.mac) });
  openMenu(anchor, items);
}

async function copy(text) {
  try { await call('setClipboard', { text }); toast('Copiato negli appunti.', { tone: 'success' }); } catch (err) { toastError(err); }
}

const actions = {
  'scan-adapter': (el) => navigate('network', { target: el.dataset.cidr }),
  'adapter-menu': (el) => adapterMenu(el, el.dataset.adapter, el.dataset.cidr),
  'toggle-notes': () => { S.overview.notesOpen = !S.overview.notesOpen; rerender(); },
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
  title: 'Panoramica', heading: 'Panoramica', icon: 'dashboard', section: '',
  render,
  actions,
  onChange: (el, field) => { if (field === 'title' || field === 'notes') saveInfo(); },
});
