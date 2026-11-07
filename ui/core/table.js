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

/**
 * Ridisegna il contenuto di box (innerHTML) tenendo la posizione di scorrimento dell'elemento che scorre al suo interno
 * (la tabella, oppure un altro contenitore indicato con selector): senza, ogni aggiornamento (riscansione di un host,
 * porte che arrivano, scheda di un dispositivo) riporterebbe l'elenco all'inizio.
 */
export function replaceKeepingScroll(box, html, selector = '.table-wrap') {
  const old = box.querySelector(selector);
  const top = old ? old.scrollTop : 0;
  const left = old ? old.scrollLeft : 0;
  box.innerHTML = html;
  const wrap = box.querySelector(selector);
  if (wrap) { wrap.scrollTop = top; wrap.scrollLeft = left; }
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
export function table({ columns, rows, sort, rowAttrs, cls = '', empty = '', groupBy = null }) {
  if (!rows.length && empty) return empty;
  const head = columns.map((c) => {
    const sortable = c.sortable !== false;
    const sorted = sort && sort.key === c.key;
    const ind = sortable ? `<span class="sort-ind">${sorted && sort.dir < 0 ? '▼' : '▲'}</span>` : '';
    return `<th class="${sortable ? 'sortable' : ''} ${sorted ? 'is-sorted' : ''} ${c.cls || ''}" ${sortable ? `data-sort="${c.key}" tabindex="0"` : ''}>${esc(c.label)}${ind}</th>`;
  }).join('');
  const sortedRows = sortRows(rows, columns, sort);
  const renderRow = (r) => {
    const cells = columns.map((c) => `<td class="${c.cls || ''}">${c.cell ? c.cell(r) : esc(r[c.key])}</td>`).join('');
    return `<tr ${rowAttrs ? rowAttrs(r) : ''}>${cells}</tr>`;
  };
  let body;
  if (groupBy) {
    const groups = new Map();
    for (const r of sortedRows) {
      const g = groupBy(r) || '';
      if (!groups.has(g)) groups.set(g, []);
      groups.get(g).push(r);
    }
    if (groups.size > 1) {
      body = [...groups.entries()].map(([g, list]) => {
        const hdr = g ? `<tr class="subnet-row"><td colspan="${columns.length}"><span class="mono"><b>${esc(g)}</b></span> <span class="tag primary">${list.length} host</span></td></tr>` : '';
        return hdr + list.map(renderRow).join('');
      }).join('');
    } else {
      body = sortedRows.map(renderRow).join('');
    }
  } else {
    body = sortedRows.map(renderRow).join('');
  }
  return `<div class="table-wrap ${cls}"><table class="dt"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table></div>`;
}
