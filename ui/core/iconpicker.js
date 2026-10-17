// Scelta dell'icona e del colore di un dispositivo: le 7.188 icone di Material Design Icons incluse nel programma
// (Core\Utils\IconCatalog.vb), con ricerca in italiano, categorie (prima le raccolte per rete, sicurezza e impianti),
// piene o a contorno, le ultime usate e una tavolozza. Restituisce { icon, color } oppure null se annullata.
import { call } from './bridge.js';
import { $, $$, esc, fmtNumber } from './dom.js';
import { icon } from './icons.js';
import { mdiSvg, rememberPaths, tintStyle } from './mdi.js';
import { openModal, toastError } from './ui.js';

const PAGE = 240;
const RECENT_KEY = 'whoispro.icons.recent';
// tavolozza: i colori del programma e qualche tinta in più, ben leggibili sia col tema chiaro sia con lo scuro
const COLORS = ['#14b8a6', '#0ea5e9', '#3b82f6', '#6366f1', '#8b5cf6', '#d946ef', '#ec4899', '#ef4444',
  '#f97316', '#f59e0b', '#eab308', '#84cc16', '#22c55e', '#10b981', '#64748b', '#78716c'];

let meta = null;

function recent() {
  try { return JSON.parse(localStorage.getItem(RECENT_KEY) || '[]').filter((x) => typeof x === 'string').slice(0, 16); } catch { return []; }
}
function addRecent(name) {
  try { localStorage.setItem(RECENT_KEY, JSON.stringify([name, ...recent().filter((x) => x !== name)].slice(0, 16))); } catch { /* senza memoria locale */ }
}

/** current = { icon, color } attuali; fallback = HTML dell'icona del tipo (per l'anteprima senza icona scelta). */
export function pickIcon({ current = {}, fallback = '', title = 'Icona del dispositivo' } = {}) {
  return new Promise((resolve) => {
    let result = null;
    const st = { q: '', cat: '', variant: 'filled', offset: 0, total: 0, icon: current.icon || '', color: current.color || '', seq: 0 };
    const m = openModal({
      ic: 'tag', title, sub: 'Material Design Icons: cerca in italiano o in inglese (router, telecamera, stampante…)', size: 'modal-xl icon-picker',
      body: `<div class="ip-top">
          <span class="ip-preview" data-preview></span>
          <div class="search">${icon('search')}<input class="input" data-ip="q" placeholder="Cerca un'icona: router, telecamera, termostato, nas…" autocomplete="off" spellcheck="false"></div>
          <select class="select ip-cat" data-ip="cat"><option value="">Tutte le categorie</option></select>
          <div class="segmented ip-variant" role="group">
            <button type="button" data-variant="filled" class="is-active">Piene</button><button type="button" data-variant="outline">Contorno</button><button type="button" data-variant="all">Tutte</button></div>
        </div>
        <div class="ip-recent" data-recent></div>
        <div class="ip-grid" data-grid><div class="loading-block"><span class="spinner"></span> Lettura delle icone…</div></div>
        <div class="ip-more" data-more></div>
        <div class="ip-colors"><span class="ip-colors-label">Colore</span>
          <button type="button" class="ip-swatch is-auto" data-color="" title="Il colore del programma">${icon('close', { size: 13 })}</button>
          ${COLORS.map((c) => `<button type="button" class="ip-swatch" data-color="${c}" style="--sw:${c}" title="${c}"></button>`).join('')}
          <label class="ip-custom" title="Un altro colore"><input type="color" data-ip="custom" value="${esc(st.color || '#14b8a6')}">${icon('edit', { size: 13 })}</label>
        </div>`,
      foot: `<div class="left"><button class="btn btn-ghost" data-ip-clear>${icon('rotate')} Icona del tipo</button></div>
        <button class="btn" data-action="close-overlay">Annulla</button><button class="btn btn-primary" data-ip-ok>${icon('check')} Usa questa icona</button>`,
      onClose: () => resolve(result),
    });
    const w = m.wrap;

    const preview = () => {
      const svg = st.icon ? mdiSvg(st.icon, 26) : '';
      $('[data-preview]', w).innerHTML = `<span class="cat-icon cat-lg ${st.icon ? (st.color ? 'is-tinted' : 'primary') : ''}"${tintStyle(st.color)}>${svg || fallback}</span>
        <span class="ip-preview-text"><b>${st.icon ? esc(st.icon) : 'Icona del tipo'}</b><small>${st.icon ? (st.color || 'colore del programma') : 'nessuna scelta'}</small></span>`;
      $$('.ip-swatch', w).forEach((b) => b.classList.toggle('is-active', (b.dataset.color || '') === st.color));
      $('[data-ip-ok]', w).disabled = !st.icon;
    };
    const tile = (i) => `<button type="button" class="ip-tile ${i.name === st.icon ? 'is-active' : ''}" data-name="${esc(i.name)}" title="${esc(i.name)}">
        <svg viewBox="0 0 24 24" width="24" height="24" fill="currentColor" aria-hidden="true"><path d="${esc(i.path)}"/></svg></button>`;
    const select = (name) => {
      st.icon = name;
      $$('.ip-tile', w).forEach((t) => t.classList.toggle('is-active', t.dataset.name === name));
      preview();
    };

    async function search(append = false) {
      const seq = ++st.seq;
      if (!append) st.offset = 0;
      try {
        const res = await call('iconsSearch', { q: st.q, cat: st.cat, variant: st.variant, offset: st.offset, limit: PAGE });
        if (seq !== st.seq) return;
        rememberPaths(res.items);
        st.total = res.total;
        const grid = $('[data-grid]', w);
        const html = res.items.map(tile).join('');
        if (append) grid.insertAdjacentHTML('beforeend', html);
        else grid.innerHTML = html || '<p class="card-text muted ip-none">Nessuna icona: prova con un\'altra parola, anche in inglese.</p>';
        st.offset += res.items.length;
        $('[data-more]', w).innerHTML = st.offset < st.total
          ? `<button type="button" class="btn btn-sm btn-ghost" data-ip-more>Altre icone (${fmtNumber(st.total - st.offset)})</button>` : '';
        if (!append) grid.scrollTop = 0;
      } catch (err) { toastError(err, 'Icone non lette'); }
    }

    async function loadRecent() {
      const names = recent();
      const box = $('[data-recent]', w);
      if (!names.length) { box.innerHTML = ''; return; }
      try {
        const res = await call('iconPaths', { names });
        const items = names.filter((n) => res[n]).map((n) => ({ name: n, path: res[n] }));
        rememberPaths(items);
        box.innerHTML = items.length ? `<span class="ip-colors-label">Usate di recente</span>${items.map(tile).join('')}` : '';
      } catch { box.innerHTML = ''; }
    }

    let typing = 0;
    $('[data-ip="q"]', w).addEventListener('input', (e) => { st.q = e.target.value; clearTimeout(typing); typing = setTimeout(() => search(), 180); });
    $('[data-ip="cat"]', w).addEventListener('change', (e) => { st.cat = e.target.value; search(); });
    $('[data-ip="custom"]', w).addEventListener('input', (e) => { st.color = e.target.value.toLowerCase(); preview(); });
    w.addEventListener('click', (e) => {
      const t = e.target.closest('.ip-tile');
      if (t) { select(t.dataset.name); return; }
      const v = e.target.closest('[data-variant]');
      if (v) { st.variant = v.dataset.variant; $$('[data-variant]', w).forEach((b) => b.classList.toggle('is-active', b === v)); search(); return; }
      const c = e.target.closest('.ip-swatch');
      if (c) { st.color = c.dataset.color || ''; preview(); return; }
      if (e.target.closest('[data-ip-more]')) { search(true); return; }
      if (e.target.closest('[data-ip-clear]')) { result = { icon: '', color: '' }; m.close(); return; }
      if (e.target.closest('[data-ip-ok]') && st.icon) { addRecent(st.icon); result = { icon: st.icon, color: st.color }; m.close(); }
    });
    // doppio clic su un'icona: la sceglie subito
    w.addEventListener('dblclick', (e) => {
      const t = e.target.closest('.ip-tile');
      if (t) { st.icon = t.dataset.name; addRecent(st.icon); result = { icon: st.icon, color: st.color }; m.close(); }
    });

    (async () => {
      try {
        meta = meta || await call('iconsMeta');
        const sel = $('[data-ip="cat"]', w);
        const curated = meta.categories.filter((c) => c.curated);
        const others = meta.categories.filter((c) => !c.curated);
        sel.insertAdjacentHTML('beforeend', `<optgroup label="Raccolte">${curated.map((c) => `<option value="${esc(c.key)}">${esc(c.label.replace(/^★\s*/, ''))} (${c.count})</option>`).join('')}</optgroup>
          <optgroup label="Categorie">${others.map((c) => `<option value="${esc(c.key)}">${esc(c.label)} (${c.count})</option>`).join('')}</optgroup>`);
      } catch { /* senza categorie resta la ricerca */ }
    })();
    preview();
    loadRecent();
    search();
    $('[data-ip="q"]', w).focus();
  });
}
