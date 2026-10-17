// Pulizia veloce dei campi di testo: con qualcosa scritto compare una ✕ a destra del campo e un clic lo svuota.
// Vale per tutti i campi .input di testo della pagina, senza toccare le pagine: la ✕ è lo sfondo del campo (classe
// has-value, app.css) e il clic si riconosce dalla posizione, così non serve avvolgere i campi in altri elementi.
import { $$ } from './dom.js';

const ZONE = 30; // larghezza della zona della ✕, dal bordo destro del campo (px)

/** I campi che hanno la ✕: testo e ricerca, non in sola lettura né disattivati; esclusi quelli con un pulsante a destra. */
function clearable(el) {
  return el instanceof HTMLInputElement && el.classList.contains('input')
    && ['text', 'search', 'email', 'url', 'tel'].includes(el.type)
    && !el.readOnly && !el.disabled && !el.closest('.input-affix, .no-clear');
}

function sync(el) {
  if (clearable(el)) el.classList.toggle('has-value', el.value !== '');
}

const inZone = (el, e) => e.clientX >= el.getBoundingClientRect().right - ZONE;

document.addEventListener('input', (e) => sync(e.target), true);
document.addEventListener('focusin', (e) => sync(e.target));

document.addEventListener('mousemove', (e) => {
  const el = e.target;
  if (el instanceof HTMLInputElement && el.classList.contains('has-value')) el.classList.toggle('on-clear', inZone(el, e));
});

document.addEventListener('mouseout', (e) => {
  if (e.target instanceof HTMLInputElement) e.target.classList.remove('on-clear');
});

document.addEventListener('mousedown', (e) => {
  const el = e.target;
  if (e.button !== 0 || !(el instanceof HTMLInputElement) || !el.classList.contains('has-value') || !clearable(el) || !inZone(el, e)) return;
  e.preventDefault();
  el.value = '';
  el.classList.remove('has-value', 'on-clear');
  el.focus();
  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
});

// i campi che arrivano dalle pagine già riempiti (value="…")
new MutationObserver(() => $$('input.input').forEach(sync)).observe(document.body, { childList: true, subtree: true });
$$('input.input').forEach(sync);
