// Grafici a ciambella (SVG inline), come nelle console web: parti in proporzione al totale, il resto è il fondo.
import { esc } from './dom.js';

/**
 * donut([{ value, cls }], { total, size, thickness, center, sub, label }): le parti partono dall'alto, in senso orario.
 * cls colora la parte (is-used, is-warn, is-danger, is-info…), definita in app.css.
 */
export function donut(parts, { total, size = 120, thickness = 16, center = '', sub = '', label = '' } = {}) {
  const r = (100 - thickness) / 2;
  const c = 2 * Math.PI * r;
  const sum = total || parts.reduce((s, p) => s + p.value, 0) || 1;
  const gap = parts.filter((p) => p.value > 0).length > 1 ? 0.7 : 0;
  let offset = 0;
  const arcs = [];
  for (const p of parts) {
    if (!(p.value > 0)) continue;
    const len = Math.min(c, (p.value / sum) * c);
    const drawn = Math.max(0, len - gap);
    if (drawn > 0) {
      arcs.push(`<circle class="donut-part ${p.cls || ''}" cx="50" cy="50" r="${r}" stroke-width="${thickness}" stroke-dasharray="${drawn.toFixed(3)} ${(c - drawn).toFixed(3)}" stroke-dashoffset="${(-offset).toFixed(3)}"></circle>`);
    }
    offset += len;
  }
  return `<svg class="donut" viewBox="0 0 100 100" width="${size}" height="${size}" role="img" aria-label="${esc(label)}">
      <circle class="donut-track" cx="50" cy="50" r="${r}" stroke-width="${thickness}"></circle>
      <g transform="rotate(-90 50 50)">${arcs.join('')}</g>
      ${center ? `<text class="donut-center" x="50" y="${sub ? 50 : 54}" text-anchor="middle">${esc(center)}</text>` : ''}
      ${sub ? `<text class="donut-sub" x="50" y="62" text-anchor="middle">${esc(sub)}</text>` : ''}
    </svg>`;
}

/** Percentuale leggibile: "< 0,1%", "7,5%", "42%". */
export function percent(value, total) {
  if (!total) return '0%';
  const p = (value / total) * 100;
  if (p > 0 && p < 0.1) return '< 0,1%';
  return `${p > 0 && p < 10 ? p.toFixed(1).replace('.', ',') : Math.round(p)}%`;
}

/** Colore della parte "usata" secondo quanto è piena (verde/blu fino al 70 %, ambra fino al 90 %, poi rosso). */
export function usageClass(value, total) {
  const p = total ? (value / total) * 100 : 0;
  return p >= 90 ? 'is-danger' : p >= 70 ? 'is-warn' : 'is-used';
}
