// WHOIS: interrogazione dei registri (porta 43, con referral) o RDAP, riepilogo dei campi e risposta completa.
import { call } from '../core/bridge.js';
import { $, esc, fmtDateTime, fmtMs, fmtTime, plural } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage, adopt, bus } from '../core/store.js';
import { confirmDialog, emptyState, note, pageHead, toast, toastError } from '../core/ui.js';

S.whois = { target: '', server: '', busy: false, current: -1 };

function formCard() {
  const w = S.whois;
  const servers = (S.st.presets.whoisServers || []).map((s) => `<option value="${esc(s)}" ${w.server === s ? 'selected' : ''}>${esc(s)}</option>`).join('');
  return `<section class="card"><div class="card-body">
    <form data-form="query" class="scan-form" autocomplete="off">
      <div class="field"><label for="f-target">Dominio o indirizzo IP</label>
        <input class="input input-mono" id="f-target" data-field="target" value="${esc(w.target)}" placeholder="esempio.it · 8.8.8.8" ${w.busy ? 'disabled' : ''}></div>
      <div class="field field-2"><label for="f-server">Server</label>
        <select class="select" id="f-server" data-field="server" ${w.busy ? 'disabled' : ''}><option value="" ${w.server ? '' : 'selected'}>Automatico (TLD, IANA, referral, RDAP)</option>${servers}</select></div>
      <div class="field field-end"><span class="field-label">&nbsp;</span><div class="field-actions">
        <button type="submit" class="btn btn-primary ${w.busy ? 'is-loading' : ''}" ${w.busy ? 'disabled' : ''}>${icon('search')} Interroga</button></div></div>
    </form></div></section>`;
}

function current() { return S.data.whois[S.whois.current] || null; }

function resultCard() {
  const r = current();
  if (!r) {
    if (!S.data.whois.length) return `<section class="card">${emptyState('globe', 'Nessuna ricerca WHOIS', 'Scrivi un dominio o un indirizzo IP e premi Interroga: registrar, intestatario, date e name server compaiono qui.')}</section>`;
    return `<section class="card">${emptyState('history', 'Scegli una ricerca', 'Le ricerche della sessione sono nell\'elenco a destra.')}</section>`;
  }
  const kv = (label, value) => `<dt>${label}</dt><dd>${value && value !== 'N/D' ? esc(value) : '<span class="muted">—</span>'}</dd>`;
  const pills = (list, tone) => (list && list.length ? `<div class="pill-list">${list.map((x) => `<span class="pill ${tone}">${esc(x)}</span>`).join('')}</div>` : '<span class="muted">—</span>');
  return `<section class="card">
    <div class="card-head"><div class="card-title">${icon('globe')}<h2>${esc(r.queryTarget)}</h2><span class="count">${fmtMs(r.queryTimeMs)}</span></div>
      <div class="row"><span class="pill ${r.isSuccess ? 'success' : 'danger'} pill-dot">${r.isSuccess ? 'Risposta ricevuta' : 'Errore'}</span></div></div>
    <div class="card-body"><div class="fields">
      ${r.isSuccess ? '' : note('danger', 'alert', esc(r.errorMessage || 'Nessuna risposta.'))}
      <dl class="kv">
        <dt>Server</dt><dd class="mono">${esc(r.serverUsed || '—')}</dd>
        ${r.referralServer ? `<dt>Referral</dt><dd class="mono">${esc(r.referralServer)}</dd>` : ''}
        ${kv('Registrar', r.registrar)}
        ${kv('Organizzazione', r.registrantOrg)}
        ${kv('Nazione', r.country)}
        ${kv('Creazione', r.creationDate)}
        ${kv('Scadenza', r.expirationDate)}
        ${kv('Ultima modifica', r.updatedDate)}
        <dt>Name server</dt><dd>${pills(r.nameServers, 'primary')}</dd>
        <dt>Stato</dt><dd>${pills(r.domainStatus, '')}</dd>
        <dt>Interrogato</dt><dd>${fmtDateTime(r.queriedAt)}</dd>
      </dl>
      <div><div class="form-section-head"><div class="form-section-title">${icon('fileText')}Risposta completa</div>
          <button class="btn btn-sm" data-action="copy-raw">${icon('copy')} Copia</button></div>
        <pre class="raw">${esc(r.rawText || '')}</pre></div>
    </div></div></section>`;
}

function historyCard() {
  const list = S.data.whois;
  const items = list.map((r, i) => `<button class="history-item ${i === S.whois.current ? 'is-active' : ''}" data-action="show" data-index="${i}">
      ${icon(r.isSuccess ? 'globe' : 'xCircle')}<span class="history-text"><b>${esc(r.queryTarget)}</b><small>${esc(r.serverUsed || r.errorMessage || '')} · ${fmtTime(r.queriedAt)}</small></span>
      ${icon('chevronRight', { size: 14 })}</button>`).reverse().join('');
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('history')}<h2>Ricerche della sessione</h2><span class="count">${list.length}</span></div>
      <button class="btn btn-sm btn-ghost" data-action="clear-whois" ${list.length ? '' : 'disabled'} title="Svuota">${icon('trash')}</button></div>
    <div class="card-body">${list.length ? `<div class="history">${items}</div>` : '<p class="card-text muted">Le ricerche fatte in questa sessione restano qui e nel file salvato.</p>'}</div></section>`;
}

function render() {
  if (S.whois.current < 0 && S.data.whois.length) S.whois.current = S.data.whois.length - 1;
  return `${pageHead('Chi ha registrato un dominio o a chi è assegnato un indirizzo IP: registrar, organizzazione, date e name server.')}
    <div class="grid grid-side"><div class="grid">${formCard()}<div id="whois-result">${resultCard()}</div></div><div id="whois-history">${historyCard()}</div></div>`;
}

function rerender() {
  if (S.page !== 'whois') return;
  const form = $('[data-form="query"]');
  if (form) form.closest('section.card').outerHTML = formCard();
  const res = $('#whois-result'); if (res) res.innerHTML = resultCard();
  const hist = $('#whois-history'); if (hist) hist.innerHTML = historyCard();
}

async function query() {
  const w = S.whois;
  if (w.busy) return;
  if (!w.target.trim()) { toast('Scrivi un dominio o un indirizzo IP.', { tone: 'warning' }); return; }
  w.busy = true;
  rerender();
  try {
    const res = await call('whois', { target: w.target, server: w.server });
    adopt({ state: res.state });
    if (res.entry.isSuccess) {
      S.data.whois.push(res.entry);
      w.current = S.data.whois.length - 1;
    } else {
      w.current = -1;
      toast(res.entry.errorMessage || 'Nessuna risposta dal server WHOIS.', { tone: 'error', title: `WHOIS di ${res.entry.queryTarget}` });
    }
    w.busy = false;
    rerender();
    if (res.entry.isSuccess) {
      const r = $('#whois-result'); if (r) r.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  } catch (err) {
    w.busy = false;
    rerender();
    toastError(err, 'WHOIS non riuscito');
  }
}

const actions = {
  'show': (el) => { S.whois.current = Number(el.dataset.index); rerender(); },
  'copy-raw': async () => {
    const r = current();
    if (!r) return;
    try { await call('setClipboard', { text: r.rawText || '' }); toast('Risposta WHOIS copiata negli appunti.', { tone: 'success' }); } catch (err) { toastError(err); }
  },
  'clear-whois': async () => {
    if (!(await confirmDialog({ title: 'Svuotare le ricerche WHOIS?', message: `${plural(S.data.whois.length, 'ricerca', 'ricerche')} verranno tolte dalla sessione.`, confirmText: 'Svuota', tone: 'danger' }))) return;
    try { adopt(await call('clearList', { list: 'whois' })); S.whois.current = -1; rerender(); } catch (err) { toastError(err); }
  },
};

bus.addEventListener('session', () => { S.whois.current = S.data.whois.length ? S.data.whois.length - 1 : -1; rerender(); });

definePage('whois', {
  title: 'WHOIS', heading: 'WHOIS', icon: 'globe', section: 'lookup', badge: () => S.data.whois.length,
  render, actions,
  enter: (params) => {
    if (params.target) {
      S.whois.target = params.target;
      const f = $('#f-target'); if (f) f.value = params.target;
      if (params.start) query();
    }
  },
  onInput: (el, field) => { if (field === 'target') S.whois.target = el.value; },
  onChange: (el, field) => { if (field === 'server') S.whois.server = el.value; },
  onSubmit: () => query(),
});
