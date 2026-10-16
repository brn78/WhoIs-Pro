// Computer: processore, memoria, dischi e sistema di questo PC, con i grafici a ciambella; si aggiorna ogni due secondi.
import { call } from '../core/bridge.js';
import { $, esc, fmtBytes, fmtDateTime, fmtDuration, fmtNumber } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, definePage } from '../core/store.js';
import { donut, percent, usageClass } from '../core/chart.js';
import { table } from '../core/table.js';
import { emptyState, pageHead, toastError } from '../core/ui.js';

S.computer = { info: null, timer: 0, busy: false, error: '' };

function kv(rows) {
  return `<dl class="kv compact">${rows.map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('')}</dl>`;
}

function cpuCard(c) {
  const cls = c.usage >= 90 ? 'is-danger' : c.usage >= 70 ? 'is-warn' : 'is-used';
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('cpu')}<h2>Processore</h2></div><span class="pill ${cls === 'is-used' ? 'success' : cls === 'is-warn' ? 'warning' : 'danger'} pill-dot">${c.usage.toFixed(0)}% in uso</span></div>
    <div class="card-body"><div class="chart-row">
      ${donut([{ value: c.usage, cls }], { total: 100, center: `${Math.round(c.usage)}%`, sub: 'in uso', label: `Processore al ${Math.round(c.usage)}%` })}
      <div class="chart-legend">${kv([
        ['Modello', `<b>${esc(c.name)}</b>`],
        ['Thread logici', fmtNumber(c.logical)],
        ['Frequenza base', c.mhz ? `${fmtNumber(c.mhz)} MHz` : '<span class="muted">—</span>'],
        ['Architettura', esc(c.arch)],
      ])}</div>
    </div></div></section>`;
}

function memoryCard(m) {
  const cls = usageClass(m.used, m.total);
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('memory')}<h2>Memoria</h2></div><span class="pill ${cls === 'is-used' ? 'success' : cls === 'is-warn' ? 'warning' : 'danger'} pill-dot">${m.percent}% in uso</span></div>
    <div class="card-body"><div class="chart-row">
      ${donut([{ value: m.used, cls }], { total: m.total, center: `${m.percent}%`, sub: 'in uso', label: `Memoria ${fmtBytes(m.used)} su ${fmtBytes(m.total)}` })}
      <div class="chart-legend"><ul>
        <li><span class="legend-dot ${cls}"></span><span class="legend-label">In uso</span><b>${fmtBytes(m.used)}</b><span class="muted">${percent(m.used, m.total)}</span></li>
        <li><span class="legend-dot is-free"></span><span class="legend-label">Disponibile</span><b>${fmtBytes(m.available)}</b><span class="muted">${percent(m.available, m.total)}</span></li>
        <li><span class="legend-dot is-total"></span><span class="legend-label">Installata</span><b>${fmtBytes(m.total)}</b><span class="muted"></span></li>
        <li><span class="legend-dot is-info"></span><span class="legend-label">File di paging</span><b>${fmtBytes(m.pageTotal - m.pageAvailable)}</b><span class="muted">su ${fmtBytes(m.pageTotal)}</span></li>
      </ul></div>
    </div></div></section>`;
}

function disksCard(disks) {
  const items = disks.map((d) => {
    const cls = usageClass(d.used, d.total);
    return `<div class="disk">
      ${donut([{ value: d.used, cls }], { total: d.total, size: 96, thickness: 15, center: percent(d.free, d.total), sub: 'libero', label: `Disco ${d.name}: ${fmtBytes(d.used)} usati su ${fmtBytes(d.total)}` })}
      <div class="chart-legend">
        <div class="legend-head">${icon('hardDrive', { size: 15 })}<b>${esc(d.name)}</b>${d.label ? `<span class="muted">${esc(d.label)}</span>` : ''}<span class="spacer"></span><span class="muted">${esc(d.type)}${d.format ? ` · ${esc(d.format)}` : ''}</span></div>
        <ul>
          <li><span class="legend-dot ${cls}"></span><span class="legend-label">Usato</span><b>${fmtBytes(d.used)}</b><span class="muted">${percent(d.used, d.total)}</span></li>
          <li><span class="legend-dot is-free"></span><span class="legend-label">Libero</span><b>${fmtBytes(d.free)}</b><span class="muted">${percent(d.free, d.total)}</span></li>
          <li><span class="legend-dot is-total"></span><span class="legend-label">Capacità</span><b>${fmtBytes(d.total)}</b><span class="muted"></span></li>
        </ul></div></div>`;
  }).join('');
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('hardDrive')}<h2>Dischi</h2><span class="count">${disks.length}</span></div></div>
    <div class="card-body">${disks.length ? `<div class="disks">${items}</div>` : '<p class="card-text muted">Nessun disco pronto.</p>'}</div></section>`;
}

function systemCard(o, gpu) {
  return `<section class="card"><div class="card-head"><div class="card-title">${icon('windows')}<h2>Sistema</h2></div></div>
    <div class="card-body">${kv([
      ['Windows', `<b>${esc(o.name)}</b>${o.version ? ` · ${esc(o.version)}` : ''} <span class="tag mono">build ${esc(o.build)}</span>`],
      ['Nome del computer', `<span class="mono">${esc(o.machine)}</span>`],
      ['Utente', `${esc(o.user)}${o.domain && o.domain !== o.machine ? ` <span class="muted">(${esc(o.domain)})</span>` : ''}`],
      ['Acceso da', `${fmtDuration(o.uptimeSeconds)} <span class="muted">· avviato il ${fmtDateTime(o.bootAt)}</span>`],
      ['Scheda video', gpu ? esc(gpu) : '<span class="muted">—</span>'],
      ['.NET', esc(o.dotnet)],
    ])}</div></section>`;
}

function adaptersCard() {
  const rows = S.st.adapters || [];
  const columns = [
    { key: 'name', label: 'Scheda', cell: (a) => `<div class="cell-main"><span class="cat-icon primary">${icon('network')}</span><div class="cell-text"><b>${esc(a.name)}</b><small>${esc(a.description)}</small></div></div>` },
    { key: 'ip', label: 'Indirizzo IP', cls: 'mono' },
    { key: 'mask', label: 'Maschera', cls: 'mono' },
    { key: 'gateway', label: 'Gateway', cls: 'mono', cell: (a) => esc(a.gateway || '—') },
    { key: 'mac', label: 'MAC', cls: 'mono' },
  ];
  return `<section class="card table-card"><div class="card-head"><div class="card-title">${icon('network')}<h2>Schede di rete attive</h2><span class="count">${rows.length}</span></div>
      <button class="btn btn-sm" data-action="go" data-page="settings">${icon('settings')} Profili di rete</button></div>
    ${table({ columns, rows, sort: null, empty: '<div class="empty"><h3>Nessuna scheda di rete attiva</h3></div>' })}</section>`;
}

function body() {
  const c = S.computer;
  if (c.error) return emptyState('alert', 'Informazioni non lette', esc(c.error));
  if (!c.info) return '<div class="loading-block"><span class="spinner"></span> Lettura di processore, memoria e dischi…</div>';
  const i = c.info;
  return `<div class="grid grid-2">${cpuCard(i.cpu)}${memoryCard(i.memory)}</div>
    <div class="grid section-gap">${disksCard(i.disks)}</div>
    <div class="grid grid-2 section-gap">${systemCard(i.os, i.gpu)}${adaptersCard()}</div>`;
}

function render() {
  return `${pageHead('laptop', 'Computer', 'Processore, memoria, dischi e sistema di questo PC, aggiornati ogni due secondi.',
    `<button class="btn" data-action="refresh-computer">${icon('refresh')} Aggiorna</button>`)}
    <div id="computer-body">${body()}</div>`;
}

async function load() {
  const c = S.computer;
  if (c.busy) return;
  c.busy = true;
  try {
    c.info = await call('computer');
    c.error = '';
  } catch (err) {
    c.error = err.message;
  }
  c.busy = false;
  const box = $('#computer-body');
  if (box && S.page === 'computer') box.innerHTML = body();
}

function startTimer() {
  stopTimer();
  S.computer.timer = setInterval(() => { if (S.page === 'computer') load(); else stopTimer(); }, 2000);
}
function stopTimer() {
  clearInterval(S.computer.timer);
  S.computer.timer = 0;
}

definePage('computer', {
  title: 'Computer', icon: 'laptop', section: 'system', order: 2,
  render,
  after: () => { load(); startTimer(); },
  leave: stopTimer,
  actions: { 'refresh-computer': () => load().catch(toastError) },
});
