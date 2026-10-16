// Dettagli della sessione in un pannello laterale (dal menu della sessione o dalla Panoramica): titolo, note,
// file, impronta SHA-256, date e contenuto, con «Salva con nome» ed «Esporta».
import { call } from '../core/bridge.js';
import { $, esc, fmtDateTime, plural, shortPath } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { S, adopt, counts } from '../core/store.js';
import { help, openDrawer, toastError } from '../core/ui.js';

function body() {
  const s = S.st.session;
  const c = counts();
  const status = s.dirty
    ? '<span class="pill warning pill-dot">Modifiche non salvate</span>'
    : s.file ? '<span class="pill success pill-dot">Salvata</span>' : '<span class="pill">Mai salvata</span>';
  return `<div class="fields">
    <div class="field"><label for="s-title">Titolo</label>
      <input class="input title-input" id="s-title" value="${esc(s.title)}" maxlength="120" autocomplete="off" placeholder="Es. Rete ufficio, ottobre 2026"></div>
    <div class="field"><label for="s-notes">Note</label>
      <textarea class="input" id="s-notes" rows="4" placeholder="Appunti sulla sessione (finiscono nel file)">${esc(s.notes || '')}</textarea></div>
    <dl class="kv compact">
      <dt>Stato</dt><dd>${status}</dd>
      <dt>File</dt><dd>${s.file ? `<span class="mono" title="${esc(s.file)}">${esc(shortPath(s.file))}</span>` : '<span class="muted">non ancora salvata</span>'}</dd>
      <dt>SHA-256</dt><dd>${s.checksum ? `<span class="mono" title="${esc(s.checksum)}">${esc(s.checksum.slice(0, 32))}…</span>` : '<span class="muted">—</span>'} ${help('Impronta del file all\'ultimo salvataggio: file temporaneo, SHA-256 e sostituzione in un colpo solo; la versione precedente resta in .bak.')}</dd>
      <dt>Creata</dt><dd>${fmtDateTime(s.createdAt) || '—'}</dd>
      <dt>Modificata</dt><dd>${fmtDateTime(s.modifiedAt) || '—'}</dd>
      <dt>Contenuto</dt><dd>${plural(c.hosts, 'host', 'host')}, ${plural(c.ports, 'porta', 'porte')}, ${plural(c.whois, 'WHOIS', 'WHOIS')}, ${plural(c.dns, 'record DNS', 'record DNS')}, ${plural(c.trace, 'salto', 'salti')}, ${plural(S.data.scans.length, 'scansione', 'scansioni')}</dd>
    </dl>
  </div>`;
}

export function openSessionDrawer() {
  const d = openDrawer({
    ic: 'archive', title: 'Dettagli della sessione', sub: S.st.session.fileName || 'Sessione non ancora salvata', body: body(), size: 'drawer-sm',
    foot: `<div class="left">
        <button class="btn" data-action="save-as">${icon('download')} Salva con nome…</button>
        <button class="btn" data-action="export-menu">${icon('fileText')} Esporta ${icon('chevronDown', { size: 13 })}</button></div>
      <button class="btn btn-ghost" data-action="close-overlay">Chiudi</button>`,
  });
  const save = async () => {
    const title = $('#s-title', d.wrap);
    const notes = $('#s-notes', d.wrap);
    if (!title || (title.value.trim() === S.st.session.title && notes.value === (S.st.session.notes || ''))) return;
    try { adopt(await call('sessionInfo', { title: title.value, notes: notes.value })); } catch (err) { toastError(err); }
  };
  $('#s-title', d.wrap).addEventListener('change', save);
  $('#s-notes', d.wrap).addEventListener('change', save);
  return d;
}
