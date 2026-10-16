// Sovrapposizioni della pagina: finestre di dialogo, pannelli laterali, menu e notifiche (toast).
import { $, $$, esc } from './dom.js';
import { icon } from './icons.js';

const overlays = [];

export function closeOverlays() {
  while (overlays.length) overlays.pop().close(true);
}

export function closeTopOverlay() {
  const top = overlays[overlays.length - 1];
  if (top) top.close();
  return !!top;
}

export const hasOverlay = () => overlays.length > 0;

function mountOverlay(html, onClose) {
  const host = $('#overlays');
  const wrap = document.createElement('div');
  wrap.innerHTML = `<div class="overlay"></div>${html}`;
  host.appendChild(wrap);
  const previous = document.activeElement;
  const entry = {
    wrap,
    close(silent) {
      const i = overlays.indexOf(entry);
      if (i >= 0) overlays.splice(i, 1);
      wrap.remove();
      if (!silent && onClose) onClose();
      if (previous && previous.focus && document.contains(previous)) previous.focus();
    },
  };
  overlays.push(entry);
  wrap.addEventListener('mousedown', (e) => {
    if (e.target.classList.contains('modal-host') || e.target.classList.contains('drawer-host') || e.target.classList.contains('overlay')) entry.close();
  });
  return entry;
}

export function openModal({ ic = 'info', tone = '', title, sub = '', body = '', foot = '', size = '', onClose }) {
  const entry = mountOverlay(`<div class="modal-host"><div class="modal ${size}" role="dialog" aria-modal="true">
      <div class="modal-head"><span class="modal-icon ${tone}">${icon(ic)}</span>
        <div class="modal-titles"><h2>${esc(title)}</h2>${sub ? `<p>${esc(sub)}</p>` : ''}</div>
        <button class="btn btn-ghost btn-icon btn-sm modal-close" data-action="close-overlay" title="Chiudi">${icon('close')}</button></div>
      <div class="modal-body">${body}</div>${foot ? `<div class="modal-foot">${foot}</div>` : ''}</div></div>`, onClose);
  const first = $('input, textarea, .modal-foot .btn-primary, .modal-foot .btn-danger', entry.wrap);
  if (first) first.focus();
  return entry;
}

export function openDrawer({ ic = 'info', title, sub = '', body, foot = '', size = 'drawer-lg' }) {
  return mountOverlay(`<div class="drawer-host"><div class="drawer ${size}" role="dialog" aria-modal="true">
      <div class="drawer-head"><span class="drawer-icon">${icon(ic)}</span>
        <div class="drawer-titles"><h2>${esc(title)}</h2>${sub ? `<p>${esc(sub)}</p>` : ''}</div>
        <button class="btn btn-ghost btn-icon btn-sm" data-action="close-overlay" title="Chiudi">${icon('close')}</button></div>
      <div class="drawer-body">${body}</div>${foot ? `<div class="drawer-foot">${foot}</div>` : ''}</div></div>`);
}

export function confirmDialog({ title, message, confirmText = 'Conferma', tone = 'warning', ic = 'alert' }) {
  return new Promise((resolve) => {
    let result = false;
    const m = openModal({
      ic, tone, title,
      body: `<p class="message">${esc(message)}</p>`,
      foot: `<button class="btn" data-action="close-overlay">Annulla</button><button class="btn ${tone === 'danger' ? 'btn-danger' : 'btn-primary'}" data-ok>${esc(confirmText)}</button>`,
      onClose: () => resolve(result),
    });
    $('[data-ok]', m.wrap).addEventListener('click', () => { result = true; m.close(); });
    $('[data-ok]', m.wrap).focus();
  });
}

/** Tre risposte: 'yes', 'no' oppure null (annullato). */
export function askDialog({ title, message, yesText = 'Sì', noText = 'No', ic = 'info', tone = '' }) {
  return new Promise((resolve) => {
    let result = null;
    const m = openModal({
      ic, tone, title,
      body: `<p class="message">${esc(message)}</p>`,
      foot: `<button class="btn" data-action="close-overlay">Annulla</button><button class="btn" data-no>${esc(noText)}</button><button class="btn btn-primary" data-yes>${esc(yesText)}</button>`,
      onClose: () => resolve(result),
    });
    $('[data-yes]', m.wrap).addEventListener('click', () => { result = 'yes'; m.close(); });
    $('[data-no]', m.wrap).addEventListener('click', () => { result = 'no'; m.close(); });
    $('[data-yes]', m.wrap).focus();
  });
}

/** Un campo di testo: il valore oppure null se annullato. */
export function promptDialog({ title, sub = '', label, value = '', placeholder = '', ic = 'edit', confirmText = 'OK', multiline = false }) {
  return new Promise((resolve) => {
    let result = null;
    const field = multiline
      ? `<textarea class="input" id="m-value" rows="5" placeholder="${esc(placeholder)}">${esc(value)}</textarea>`
      : `<input class="input" id="m-value" value="${esc(value)}" placeholder="${esc(placeholder)}" autocomplete="off">`;
    const m = openModal({
      ic, title, sub,
      body: `<form class="fields" data-modal-form><div class="field"><label for="m-value">${esc(label)}</label>${field}</div><button type="submit" hidden></button></form>`,
      foot: `<button class="btn" data-action="close-overlay">Annulla</button><button class="btn btn-primary" data-ok>${esc(confirmText)}</button>`,
      onClose: () => resolve(result),
    });
    const submit = () => { result = $('#m-value', m.wrap).value; m.close(); };
    $('[data-ok]', m.wrap).addEventListener('click', submit);
    $('[data-modal-form]', m.wrap).addEventListener('submit', (e) => { e.preventDefault(); submit(); });
    const input = $('#m-value', m.wrap);
    input.focus();
    if (!multiline) input.select();
  });
}

/** Menu a comparsa accanto a un elemento. items: { header } | { html } | { sep } | { ic, label, run, danger, disabled, hint }. */
export function openMenu(anchor, items, { above = false } = {}) {
  closeMenu();
  const menu = document.createElement('div');
  menu.className = 'menu';
  menu.setAttribute('role', 'menu');
  menu.innerHTML = items.map((it, i) => {
    if (it.header) return `<div class="menu-header">${esc(it.header)}</div>`;
    if (it.html) return it.html;
    if (it.sep) return '<div class="menu-sep"></div>';
    return `<button class="menu-item ${it.danger ? 'is-danger' : ''}" role="menuitem" data-index="${i}" ${it.disabled ? 'disabled' : ''}>${icon(it.ic || 'chevronRight')}<span>${esc(it.label)}</span>${it.hint ? `<span class="menu-hint">${esc(it.hint)}</span>` : ''}</button>`;
  }).join('');
  document.body.appendChild(menu);
  const r = anchor.getBoundingClientRect();
  const h = menu.offsetHeight;
  const w = menu.offsetWidth;
  let top = above ? r.top - h - 6 : r.bottom + 6;
  if (top + h > window.innerHeight - 8) top = Math.max(8, r.top - h - 6);
  let left = r.left;
  if (left + w > window.innerWidth - 8) left = Math.max(8, r.right - w);
  menu.style.left = `${Math.max(8, left)}px`;
  menu.style.top = `${Math.max(8, top)}px`;
  menu.addEventListener('click', (e) => {
    const b = e.target.closest('[data-index]');
    if (!b) return;
    closeMenu();
    items[Number(b.dataset.index)].run();
  });
  setTimeout(() => document.addEventListener('mousedown', outsideMenu), 0);
  const first = $('.menu-item:not(:disabled)', menu);
  if (first) first.focus();
}
function outsideMenu(e) { if (!e.target.closest('.menu')) closeMenu(); }
export function closeMenu() {
  const open = $$('.menu');
  open.forEach((m) => m.remove());
  document.removeEventListener('mousedown', outsideMenu);
  return open.length > 0;
}

const TOAST_ICONS = { success: 'checkCircle', error: 'xCircle', warning: 'alert', info: 'info' };
export function toast(text, { tone = 'info', title = '', timeout = 6000, action = null } = {}) {
  const el = document.createElement('div');
  el.className = `toast ${tone}`;
  el.innerHTML = `<span class="toast-icon">${icon(TOAST_ICONS[tone] || 'info')}</span>
    <div class="toast-text">${title ? `<b>${esc(title)}</b>` : ''}${esc(text)}${action ? `<div><button class="toast-action">${esc(action.label)}</button></div>` : ''}</div>
    <button class="toast-close" title="Chiudi">${icon('close', { size: 15 })}</button>
    <span class="toast-bar" style="animation-duration:${timeout}ms"></span>`;
  const close = () => { el.classList.add('is-closing'); setTimeout(() => el.remove(), 200); };
  $('.toast-close', el).addEventListener('click', close);
  if (action) $('.toast-action', el).addEventListener('click', () => { action.run(); close(); });
  $('#toasts').appendChild(el);
  let timer = setTimeout(close, timeout);
  el.addEventListener('mouseenter', () => clearTimeout(timer));
  el.addEventListener('mouseleave', () => { timer = setTimeout(close, 2500); });
}

export const toastError = (err, title = '') => toast(err && err.message ? err.message : String(err), { tone: 'error', title });

/** Riquadro informativo (form-note) con icona. */
export function note(tone, ic, html, actions = '') {
  return `<div class="form-note ${tone}${actions ? ' note-with-actions' : ''}">${icon(ic)}<div class="note-text">${html}</div>${actions ? `<div class="note-actions">${actions}</div>` : ''}</div>`;
}

export function emptyState(ic, title, text, actions = '') {
  return `<div class="empty"><div class="empty-icon">${icon(ic)}</div><h3>${esc(title)}</h3><p>${text}</p>${actions}</div>`;
}

export function pageHead(ic, title, sub, actions = '') {
  return `<div class="page-head"><div class="page-title"><h1><span class="page-icon">${icon(ic)}</span>${esc(title)}</h1>
    <div class="page-sub">${sub}</div></div>${actions ? `<div class="page-actions">${actions}</div>` : ''}</div>`;
}
