// Tabelle dati ordinabili: la pagina tiene { key, dir } e ridisegna; il clic sull'intestazione arriva da app.js (onSort).
import { esc, ipToNumber } from './dom.js';

/** Confronto "intelligente": indirizzi IP per valore, numeri per valore, il resto come testo. */
export function compareValues(a, b) {
  if (a == null && b == null) return 0;
  if (a == null) return 1;
  if (b == null) return -1;
  const ia = ipToNumber(a); const ib = ipToNumber(b);
  if (ia != null && ib != null) return ia - ib;
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  const na = Number(a); const nb = Number(b);
  if (a !== '' && b !== '' && !Number.isNaN(na) && !Number.isNaN(nb)) return na - nb;
  return String(a).localeCompare(String(b), 'it', { sensitivity: 'base', numeric: true });
}

export function sortRows(rows, columns, sort) {
  if (!sort || !sort.key) return rows;
  const col = columns.find((c) => c.key === sort.key);
  if (!col) return rows;
  const value = col.value || ((r) => r[col.key]);
  const dir = sort.dir < 0 ? -1 : 1;
  return rows.map((r, i) => ({ r, i })).sort((x, y) => compareValues(value(x.r), value(y.r)) * dir || x.i - y.i).map((x) => x.r);
}

/** Prossimo ordinamento dopo un clic sull'intestazione. */
export function nextSort(sort, key) {
  if (sort && sort.key === key) return { key, dir: -sort.dir };
  return { key, dir: 1 };
}

/**
 * HTML della tabella. columns: [{ key, label, cls, sortable (predefinito sì), value(row), cell(row) }].
 * rowAttrs(row) aggiunge attributi alla riga (per esempio data-action e data-ip).
 */
export function table({ columns, rows, sort, rowAttrs, cls = '', empty = '' }) {
  if (!rows.length && empty) return empty;
  const head = columns.map((c) => {
    const sortable = c.sortable !== false;
    const sorted = sort && sort.key === c.key;
    const ind = sortable ? `<span class="sort-ind">${sorted && sort.dir < 0 ? '▼' : '▲'}</span>` : '';
    return `<th class="${sortable ? 'sortable' : ''} ${sorted ? 'is-sorted' : ''} ${c.cls || ''}" ${sortable ? `data-sort="${c.key}" tabindex="0"` : ''}>${esc(c.label)}${ind}</th>`;
  }).join('');
  const body = sortRows(rows, columns, sort).map((r) => {
    const cells = columns.map((c) => `<td class="${c.cls || ''}">${c.cell ? c.cell(r) : esc(r[c.key])}</td>`).join('');
    return `<tr ${rowAttrs ? rowAttrs(r) : ''}>${cells}</tr>`;
  }).join('');
  return `<div class="table-wrap ${cls}"><table class="dt"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table></div>`;
}
