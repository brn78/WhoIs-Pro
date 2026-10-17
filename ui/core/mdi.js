// Icone Material Design Icons scelte per i dispositivi (Core\Utils\IconCatalog.vb, incluse nel programma): i
// tracciati si chiedono al programma solo per le icone in uso e restano in memoria; quando arrivano, gli elenchi
// degli host si ridisegnano.
import { call } from './bridge.js';
import { esc } from './dom.js';
import { emit } from './store.js';

const paths = new Map();   // nome → tracciato SVG 24×24
const pending = new Set(); // nomi già chiesti
let timer = 0;

/** Ricorda tracciati già noti (dalla ricerca della finestra di scelta). */
export function rememberPaths(items) {
  for (const i of items || []) if (i && i.name && i.path) paths.set(i.name, i.path);
}

function request(name) {
  if (!name || paths.has(name) || pending.has(name)) return;
  pending.add(name);
  clearTimeout(timer);
  timer = setTimeout(async () => {
    const names = [...pending].filter((n) => !paths.has(n));
    if (!names.length) return;
    try {
      const res = await call('iconPaths', { names });
      for (const [n, p] of Object.entries(res || {})) paths.set(n, p);
      emit('icons');
      emit('data', { list: 'hosts' });
    } catch { /* senza tracciato resta l'icona del tipo */ }
  }, 30);
}

/** L'icona MDI come SVG pieno nel colore del testo; "" se il tracciato non è ancora arrivato (intanto lo chiede). */
export function mdiSvg(name, size = 18) {
  const p = paths.get(name);
  if (!p) { request(name); return ''; }
  return `<svg class="icon mdi" width="${size}" height="${size}" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" focusable="false"><path d="${esc(p)}"/></svg>`;
}

/** Stile del quadratino con l'icona nel colore scelto: icona nel colore, fondo tenue dello stesso colore. */
export function tintStyle(color) {
  return /^#[0-9a-f]{6}$/i.test(color || '') ? ` style="--tint:${color}"` : '';
}
