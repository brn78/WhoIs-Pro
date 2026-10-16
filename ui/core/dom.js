// Utilità per la pagina: selettori, testo sicuro nell'HTML, formati di numeri, date e indirizzi.
export const $ = (sel, root = document) => root.querySelector(sel);
export const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

const ESC = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
export const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ESC[c]);

export function debounce(fn, ms) {
  let t = 0;
  return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
}

const numberFormat = new Intl.NumberFormat('it-IT');
export const fmtNumber = (n) => numberFormat.format(Number(n) || 0);

/** "2026-10-16T09:30:12.123" → "16/10/2026 09:30" */
export function fmtDateTime(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return String(iso);
  const p = (n) => String(n).padStart(2, '0');
  return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

export function fmtTime(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const p = (n) => String(n).padStart(2, '0');
  return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

/** Secondi come "12,3 s" oppure "2 min 05 s". */
export function fmtSeconds(s) {
  const n = Number(s) || 0;
  if (n < 60) return `${n.toFixed(1).replace('.', ',')} s`;
  const m = Math.floor(n / 60);
  return `${m} min ${String(Math.round(n - m * 60)).padStart(2, '0')} s`;
}

export const fmtMs = (ms) => `${fmtNumber(ms)} ms`;

/** Byte in KB, MB, GB o TB con una cifra decimale ("15,8 GB"). */
export function fmtBytes(n) {
  const v = Number(n) || 0;
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let i = 0;
  let x = v;
  while (x >= 1024 && i < units.length - 1) { x /= 1024; i++; }
  return `${i === 0 ? x : x.toFixed(x >= 100 ? 0 : 1).replace('.', ',')} ${units[i]}`;
}

/** Secondi come "3 g 4 h 12 min" oppure "25 min". */
export function fmtDuration(seconds) {
  const s = Math.max(0, Math.floor(Number(seconds) || 0));
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  const parts = [];
  if (d) parts.push(`${d} g`);
  if (d || h) parts.push(`${h} h`);
  parts.push(`${m} min`);
  return parts.join(' ');
}

export function plural(n, one, many) { return `${fmtNumber(n)} ${n === 1 ? one : many}`; }

export const fileName = (p) => String(p || '').split('\\').pop();

/** Ultime due cartelle del percorso, es. "…\Documenti\Sessioni". */
export function shortPath(p) {
  const parts = String(p || '').split('\\').filter(Boolean);
  return parts.length > 3 ? `…\\${parts.slice(-2).join('\\')}` : p;
}

const IPV4 = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/;
export const isIp = (s) => IPV4.test(String(s || '').trim());

/** IPv4 come numero, per ordinare; null se non è un indirizzo. */
export function ipToNumber(s) {
  const m = IPV4.exec(String(s || '').trim());
  if (!m) return null;
  return ((+m[1]) * 16777216) + ((+m[2]) * 65536) + ((+m[3]) * 256) + (+m[4]);
}

/** Indirizzo utile per scansioni e WHOIS (niente 0.0.0.0, loopback o "*"). */
export function isRemoteAddress(s) {
  const v = String(s || '').trim();
  return isIp(v) && v !== '0.0.0.0' && !v.startsWith('127.') && v !== '*';
}

export function short(text, max) {
  const s = String(text ?? '');
  return s.length > max ? `${s.slice(0, max - 1)}…` : s;
}

/** Filtro per parole: tutte le parole devono comparire in almeno uno dei campi (MAC anche senza separatori). */
export function matchesAll(query, fields) {
  const tokens = String(query || '').toLowerCase().split(/[\s,;]+/).filter(Boolean);
  if (!tokens.length) return true;
  const hay = fields.map((f) => String(f ?? '').toLowerCase());
  const compact = hay.map((h) => h.replace(/[:\-.]/g, ''));
  return tokens.every((t) => hay.some((h) => h.includes(t)) || compact.some((h) => h.includes(t.replace(/[:\-.]/g, ''))));
}
