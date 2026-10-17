// Prestazioni: test di circa 20 secondi della rete locale (latenza e stabilità verso il gateway) e di Internet
// (latenza, jitter, perdita, DNS, download e upload sui server di prova di Cloudflare), con una valutazione in
// parole semplici e cosa la linea regge bene. I risultati restano nella sessione.
import { call, on } from '../core/bridge.js';
import { $, esc, fmtDateTime, fmtLinkSpeed, fmtNumber } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage, adopt, addScan, emit, markDirty } from '../core/store.js';
import { table } from '../core/table.js';
import { confirmDialog, emptyState, help, pageHead, toast, toastError } from '../core/ui.js';

S.perf = { live: null, last: null, error: '' };

const running = () => !!(S.st && S.st.running.perf);
const n1 = (v) => (v == null ? '—' : fmtNumber(Math.round(v * 10) / 10));
/** Millisecondi: sotto 1 ms (il ping di Windows arrotonda a 0) si scrive «< 1». */
const ms = (v) => (v == null ? '—' : v < 1 ? '< 1' : n1(v));

// ---------------------------------------------------------------------------------------------------------------------
// Valutazione: ogni misura da 0 (scarsa) a 3 (ottima), poi un giudizio complessivo e gli usi
// ---------------------------------------------------------------------------------------------------------------------
const step = (v, limits, lowerIsBetter = false) => {
  const [a, b, c] = limits;
  if (lowerIsBetter) return v <= a ? 3 : v <= b ? 2 : v <= c ? 1 : 0;
  return v >= a ? 3 : v >= b ? 2 : v >= c ? 1 : 0;
};
const TONES = ['danger', 'warning', 'success', 'success'];
const WORDS = ['scarsa', 'discreta', 'buona', 'ottima'];

export function grade(r) {
  if (!r || !r.internet) return null;
  const i = r.internet;
  const m = {
    down: step(r.downloadMbps, [100, 25, 10]),
    up: step(r.uploadMbps, [20, 5, 2]),
    lat: i.received ? step(i.avgMs, [20, 50, 100], true) : 0,
    jit: i.received ? step(i.jitterMs, [5, 15, 30], true) : 0,
    loss: step(i.lossPct, [0, 1, 3], true),
    local: r.local && r.local.received ? step(r.local.avgMs, [2, 10, 30], true) : null,
  };
  const parts = [[m.down, 2], [m.up, 1.5], [m.lat, 1.5], [m.jit, 1], [m.loss, 1.5]];
  if (m.local != null) parts.push([m.local, 0.5]);
  const score = parts.reduce((s, [v, w]) => s + v * w, 0) / parts.reduce((s, [, w]) => s + w, 0);
  const level = score >= 2.5 ? 3 : score >= 1.8 ? 2 : score >= 1 ? 1 : 0;
  const names = { down: 'il download', up: 'l\'upload', lat: 'la latenza', jit: 'la stabilità (jitter)', loss: 'i pacchetti persi', local: 'la rete locale' };
  const weakest = Object.entries(m).filter(([, v]) => v != null).sort((a, b) => a[1] - b[1])[0];
  const down = r.downloadMbps; const up = r.uploadMbps; const lat = i.avgMs; const jit = i.jitterMs; const loss = i.lossPct;
  const use = (label, ic, ok, warn, okText, warnText, badText) => ({ label, ic, tone: ok ? 'success' : warn ? 'warning' : 'danger', text: ok ? okText : warn ? warnText : badText });
  const cams = Math.floor(up / 2);
  return {
    level, tone: TONES[level], word: WORDS[level], m,
    weak: weakest && weakest[1] < 2 ? names[weakest[0]] : '',
    uses: [
      use('Navigazione e posta', 'globe', down >= 5 && lat <= 150, down >= 2, 'pagine veloci', 'funziona, un po\' lenta', 'troppo lenta'),
      use('Streaming video', 'tv', down >= 25, down >= 10, 'anche in 4K', 'in HD, non in 4K', 'a bassa qualità'),
      use('Videochiamate', 'monitor', down >= 5 && up >= 3 && jit <= 30 && loss <= 1, up >= 1.5 && loss <= 3, 'in HD, stabili', 'possibili scatti', 'audio e video a scatti'),
      use('Gioco online', 'gamepad', lat <= 50 && jit <= 15 && loss <= 1, lat <= 100 && loss <= 3, 'reattivo', 'giocabile, con ritardi', 'ritardi e scatti'),
      use('Lavoro da remoto e cloud', 'upload', up >= 10 && lat <= 80, up >= 3, 'invio di file e backup rapidi', 'invio di file lento', 'invio di file molto lento'),
      use('Telecamere viste da fuori', 'camera', cams >= 5, cams >= 2, cams > 20 ? 'più di 20 flussi HD insieme' : `circa ${fmtNumber(cams)} flussi HD insieme`, `${fmtNumber(cams)} flussi HD insieme`, 'una telecamera a bassa qualità'),
    ],
  };
}

// ---------------------------------------------------------------------------------------------------------------------
// Pagina
// ---------------------------------------------------------------------------------------------------------------------
function big(label, ic, value, unit, cls, live) {
  return `<div class="perf-big ${cls} ${live ? 'is-live' : ''}"><span class="perf-label">${icon(ic, { size: 15 })}${label}</span>
    <span class="perf-value"><b>${value}</b><span>${unit}</span></span></div>`;
}

function heroInner() {
  const L = S.perf.live;
  const r = S.perf.last || latest();
  const g = grade(r);
  if (running()) {
    const ph = (L && L.phase) || 'latency';
    const val = L ? L.value : 0;
    // le fasi già finite restano a vista (latenza e download misurati), quella in corso si muove
    const lat = L && L.latencyMs >= 0 ? n1(L.latencyMs) : ph === 'latency' ? '…' : '—';
    const down = ph === 'download' ? n1(val) : L && L.downloadMbps > 0 ? n1(L.downloadMbps) : '—';
    return `<div class="perf-bigs">
        ${big('Download', 'download', down, 'Mbit/s', 'rx', ph === 'download')}
        ${big('Upload', 'upload', ph === 'upload' ? n1(val) : '—', 'Mbit/s', 'tx', ph === 'upload')}
        ${big('Latenza', 'activity', lat, 'ms', '', ph === 'latency')}
      </div>
      <div class="perf-progress"><div class="meter"><span style="width:${L ? L.percent : 2}%"></span></div>
        <span class="scan-stat"><span class="spinner"></span>${esc((L && L.message) || 'Avvio del test…')}</span></div>`;
  }
  if (!r) {
    return `<div class="perf-empty">${emptyState('zap', 'Nessun test in questa sessione', 'Il test dura circa 20 secondi: latenza e stabilità verso il router e verso Internet, risposta del DNS, poi download e upload. Durante il test conviene non usare la rete.')}</div>`;
  }
  return `<div class="perf-bigs">
      ${big('Download', 'download', n1(r.downloadMbps), 'Mbit/s', 'rx')}
      ${big('Upload', 'upload', n1(r.uploadMbps), 'Mbit/s', 'tx')}
      ${big('Latenza', 'activity', r.internet && r.internet.received ? n1(r.internet.avgMs) : '—', 'ms', '')}
    </div>
    ${g ? `<div class="perf-grade ${g.tone}"><span class="perf-grade-word">Connessione ${esc(g.word)}</span>
      <span class="perf-grade-sub">${g.weak ? `da migliorare: ${esc(g.weak)}` : 'nessun punto debole'} · ${esc(fmtDateTime(r.at))}${r.server ? ` · ${esc(r.server)}` : ''}</span></div>` : ''}`;
}

const latest = () => { const l = S.data.perf || []; return l.length ? l[l.length - 1] : null; };

function tone(v) { return v >= 3 ? 'success' : v === 2 ? 'success' : v === 1 ? 'warning' : 'danger'; }

function latencyRows(s, scoreLat, scoreJit, scoreLoss) {
  if (!s) return '<dt>Latenza</dt><dd class="muted">non misurata</dd>';
  if (!s.received) return `<dt>Latenza</dt><dd><span class="pill danger pill-dot">nessuna risposta su ${fmtNumber(s.sent)}</span></dd>`;
  return `<dt>Latenza</dt><dd><b class="tone-${tone(scoreLat)}">${ms(s.avgMs)} ms</b> <span class="muted">media · min ${ms(s.minMs)} · max ${ms(s.maxMs)}</span></dd>
    <dt>Jitter ${help('Quanto cambia la latenza da un pacchetto all\'altro: alto vuol dire voce e video a scatti.')}</dt><dd><b class="tone-${tone(scoreJit)}">${ms(s.jitterMs)} ms</b></dd>
    <dt>Pacchetti persi</dt><dd><b class="tone-${tone(scoreLoss)}">${n1(s.lossPct)}%</b> <span class="muted">${fmtNumber(s.received)} risposte su ${fmtNumber(s.sent)}</span></dd>`;
}

function detailCards() {
  const r = S.perf.last || latest();
  if (!r || running()) return '';
  const g = grade(r);
  const m = g ? g.m : {};
  const loc = r.local;
  const locLat = loc && loc.received ? step(loc.avgMs, [2, 10, 30], true) : 0;
  return `<div class="grid grid-2 section-gap">
      <section class="card"><div class="card-head"><div class="card-title">${icon('router')}<h2>Rete locale</h2></div><span class="card-sub">verso il router ${loc ? `<span class="mono">${esc(loc.target)}</span>` : ''}</span></div>
        <div class="card-body"><dl class="kv compact">
          <dt>Scheda</dt><dd>${esc(r.adapter || '—')}${r.linkMbps ? ` <span class="tag">${esc(fmtLinkSpeed(r.linkMbps))}</span>` : ''}</dd>
          ${latencyRows(loc, locLat, loc && loc.received ? step(loc.jitterMs, [2, 5, 15], true) : 0, loc ? step(loc.lossPct, [0, 1, 3], true) : 0)}
        </dl>${loc && loc.received && loc.avgMs > 10 ? `<p class="card-text muted perf-hint">Più di 10 ms verso il router di casa di solito vuol dire Wi-Fi debole o affollato: il cavo o un punto di accesso più vicino aiutano.</p>` : ''}</div></section>
      <section class="card"><div class="card-head"><div class="card-title">${icon('globe')}<h2>Internet</h2></div><span class="card-sub">verso <span class="mono">${esc((r.internet && r.internet.target) || '1.1.1.1')}</span></span></div>
        <div class="card-body"><dl class="kv compact">
          ${latencyRows(r.internet, m.lat, m.jit, m.loss)}
          <dt>Risposta DNS ${help('Tempo perché il server DNS risponda su nomi mai chiesti prima (senza cache). Sopra i 100 ms ogni sito nuovo parte in ritardo.')}</dt><dd><b class="tone-${tone(step(r.dnsMs, [30, 80, 200], true))}">${n1(r.dnsMs)} ms</b></dd>
          <dt>Server di prova</dt><dd>${esc(r.server || 'Cloudflare')}</dd>
        </dl></div></section>
    </div>
    ${g ? `<section class="card section-gap"><div class="card-head"><div class="card-title">${icon('checkCircle')}<h2>Cosa regge la linea</h2></div></div>
      <div class="card-body"><ul class="perf-uses">${g.uses.map((u) => `<li class="${u.tone}"><span class="perf-use-icon">${icon(u.ic, { size: 16 })}</span><span class="perf-use-text"><b>${esc(u.label)}</b><small>${esc(u.text)}</small></span><span class="led ${u.tone === 'success' ? 'is-link' : u.tone === 'warning' ? 'is-act-still' : 'is-bad'}"></span></li>`).join('')}</ul></div></section>` : ''}`;
}

function historyCard() {
  const rows = [...(S.data.perf || [])].reverse();
  if (!rows.length) return '';
  const columns = [
    { key: 'at', label: 'Data', cell: (r) => esc(fmtDateTime(r.at)) },
    { key: 'downloadMbps', label: 'Download', cls: 'num', cell: (r) => `${n1(r.downloadMbps)} Mbit/s` },
    { key: 'uploadMbps', label: 'Upload', cls: 'num', cell: (r) => `${n1(r.uploadMbps)} Mbit/s` },
    { key: 'lat', label: 'Latenza', cls: 'num', value: (r) => (r.internet ? r.internet.avgMs : 0), cell: (r) => (r.internet && r.internet.received ? `${n1(r.internet.avgMs)} ms` : '—') },
    { key: 'jit', label: 'Jitter', cls: 'num', value: (r) => (r.internet ? r.internet.jitterMs : 0), cell: (r) => (r.internet ? `${n1(r.internet.jitterMs)} ms` : '—') },
    { key: 'loss', label: 'Persi', cls: 'num', value: (r) => (r.internet ? r.internet.lossPct : 0), cell: (r) => (r.internet ? `${n1(r.internet.lossPct)}%` : '—') },
    { key: 'local', label: 'Router', cls: 'num', value: (r) => (r.local ? r.local.avgMs : 0), cell: (r) => (r.local && r.local.received ? `${ms(r.local.avgMs)} ms` : '—') },
    { key: 'grade', label: 'Valutazione', sortable: false, cell: (r) => { const g = grade(r); return g ? `<span class="pill ${g.tone} pill-dot">${esc(g.word)}</span>` : '—'; } },
  ];
  return `<section class="card table-card section-gap"><div class="card-head"><div class="card-title">${icon('history')}<h2>Test della sessione</h2><span class="count">${rows.length}</span></div>
      <button class="btn btn-sm" data-action="perf-clear">${icon('trash')} Svuota</button></div>
    ${table({ columns, rows, sort: null })}</section>`;
}

function render() {
  const head = pageHead(`Velocità di Internet, latenza e stabilità verso il router e verso Internet, risposta del DNS. Circa 20 secondi; download e upload usano i server di prova di Cloudflare ${help('Gli stessi di speed.cloudflare.com. Il programma scarica e invia dati senza significato per qualche secondo: nessun dato del PC viene inviato.')}`,
    running() ? `<button class="btn btn-danger" data-action="perf-stop">${icon('stop')} Ferma</button>`
      : S.st.online === false ? `<button class="btn btn-primary" disabled title="Serve Internet: il pallino in alto è rosso">${icon('wifiOff')} Senza Internet</button>`
        : `<button class="btn btn-primary" data-action="perf-start">${icon('play')} Avvia il test</button>`);
  return `${head}<section class="card perf-hero" id="perf-hero">${heroInner()}</section>
    <div id="perf-details">${detailCards()}</div><div id="perf-history">${historyCard()}</div>`;
}

function rerender() {
  if (S.page !== 'perf') return;
  const c = $('#content');
  if (c) c.innerHTML = render();
}

async function start() {
  if (running()) return;
  try {
    const res = await call('perfStart');
    adopt({ state: res.state });
    S.perf.live = { phase: 'latency', message: res.gateway ? `Latenza verso il router ${res.gateway} e verso Internet…` : 'Latenza verso Internet…', value: 0, percent: 2 };
    S.perf.last = null;
    emit('state');
    rerender();
  } catch (err) { toastError(err, 'Test non avviato'); }
}

// Internet che va e viene: il pulsante del test segue lo stato
on('internet', () => { if (!running()) rerender(); });

on('perf.progress', (d) => {
  S.perf.live = d;
  if (S.page !== 'perf') return;
  const hero = $('#perf-hero');
  if (hero) hero.innerHTML = heroInner();
});

on('perf.done', (d) => {
  S.perf.live = null;
  adopt({ state: d.state });
  const r = d.result;
  if (d.scan) {
    S.data.perf = [...(S.data.perf || []), r];
    S.perf.last = r;
    addScan(d.scan);
    markDirty();
    emit('data', { list: 'perf' });
  }
  rerender();
  if (r.cancelled) toast('Test fermato prima della fine.', { tone: 'warning' });
  else if (r.error) toast(r.error, { tone: 'error', title: 'Test non riuscito' });
  else {
    const g = grade(r);
    toast(`${n1(r.downloadMbps)} Mbit/s in download · ${n1(r.uploadMbps)} in upload · ${r.internet && r.internet.received ? `${n1(r.internet.avgMs)} ms` : 'Internet non risponde al ping'}.`,
      { tone: g && g.level >= 2 ? 'success' : 'warning', title: `Connessione ${g ? g.word : 'misurata'}` });
  }
});

definePage('perf', {
  title: 'Prestazioni', heading: 'Prestazioni della rete', icon: 'zap', section: 'scan', order: 5,
  badge: () => (S.data.perf || []).length,
  render,
  actions: {
    'perf-start': () => start(),
    'perf-stop': () => call('perfStop').catch(toastError),
    'perf-clear': async () => {
      if (!(await confirmDialog({ title: 'Svuotare i test della sessione?', message: 'I risultati dei test delle prestazioni vengono tolti dalla sessione.', confirmText: 'Svuota', tone: 'danger' }))) return;
      try { adopt(await call('clearList', { list: 'perf' })); S.perf.last = null; rerender(); } catch (err) { toastError(err); }
    },
  },
});
