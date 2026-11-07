// Disegno della topologia: i nodi ad albero da sinistra a destra (Internet → router → switch → dispositivi) come li
// collega «Collegato a». Ogni collegamento è una linea curva del colore dello stato del nodo a valle (tratteggiata se
// non risponde); sotto l'icona il pallino dello stato e il nome, a destra il pulsante tondo per chiudere il ramo (chiuso
// mostra quanti nodi ci sono dietro). Un nodo si trascina dove si vuole e il suo ramo lo segue (con Maiusc si sposta
// solo lui); lasciato sopra un altro nodo si collega lì. Chi non è mai stato spostato sta al suo posto nell'albero,
// accanto al suo nodo a monte. La rotellina ingrandisce attorno al puntatore, trascinando il fondo si scorre.
// Chi usa il modulo passa i nodi già pronti: { key, uplink, name, sub, state ('on' | 'off' | 'node'), tile (HTML
// dell'icona), x, y (posizione salvata, oppure null) } e riceve le azioni (apri, menu, collega, sposta, chiudi ramo).
import { esc } from './dom.js';

const COL = 224; // distanza tra le colonne (un livello dell'albero)
const ROW = 104; // distanza tra le righe
const TILE = 52; // il riquadro dell'icona
const PAD = 36;
const BELOW = 40; // sotto l'icona: nome e indirizzo
const MARGIN = 360; // spazio libero attorno ai nodi: ci si trascina dentro senza che il disegno salti
const GRID_FROM = 6; // da quanti dispositivi senza rami in poi, sotto lo stesso nodo, si va a griglia su più colonne
const TRUNK = 118; // la linea verticale della griglia, a destra del nodo a monte
const collator = new Intl.Collator('it', { numeric: true, sensitivity: 'base' });
/** Colonne della griglia dei dispositivi senza rami sotto uno stesso nodo (1 = in colonna come i rami). */
export const gridColumns = (n) => (n < GRID_FROM ? 1 : Math.min(5, Math.ceil(Math.sqrt(n / 2.5))));
export const ZOOM_MIN = 0.25;
export const ZOOM_MAX = 2;
export const clampZoom = (z) => Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, Number(z) || 1));

/** I collegamenti come albero: radici (senza nodo a monte) e figli in ordine di nome. */
export function topologyTree(nodes) {
  const byKey = new Map(nodes.map((n) => [n.key, n]));
  const kids = new Map();
  const roots = [];
  nodes.forEach((n) => {
    if (n.uplink && byKey.has(n.uplink) && n.uplink !== n.key) {
      if (!kids.has(n.uplink)) kids.set(n.uplink, []);
      kids.get(n.uplink).push(n);
    } else roots.push(n);
  });
  const byName = (a, b) => collator.compare(a.name, b.name);
  roots.sort(byName);
  kids.forEach((list) => list.sort(byName));
  // un giro chiuso (il programma lo rifiuta, ma per sicurezza) non resta fuori dall'albero
  const reached = new Set();
  const walk = (n) => { if (reached.has(n.key)) return; reached.add(n.key); (kids.get(n.key) || []).forEach(walk); };
  roots.forEach(walk);
  nodes.filter((n) => !reached.has(n.key)).forEach((n) => { roots.push(n); walk(n); });
  return { roots, kids, byKey };
}

/** Quanti nodi stanno dietro a uno (tutto il ramo) e lo stato peggiore del ramo. */
function branchInfo(key, kids, seen = new Set()) {
  let count = 0;
  let off = false;
  let on = false;
  (kids.get(key) || []).forEach((k) => {
    if (seen.has(k.key)) return;
    seen.add(k.key);
    count += 1;
    if (k.state === 'off') off = true;
    if (k.state === 'on') on = true;
    const sub = branchInfo(k.key, kids, seen);
    count += sub.count;
    off = off || sub.off;
    on = on || sub.on;
  });
  return { count, off, on, tone: off ? 'off' : on ? 'on' : 'muted' };
}

const saved = (n) => (n.x !== null && n.x !== undefined && n.y !== null && n.y !== undefined ? { x: n.x, y: n.y } : null);

const slotX = (col) => PAD + col * COL + TILE / 2 + 60;

/**
 * Dove sta ogni nodo (il centro dell'icona, in punti del disegno): la posizione salvata, altrimenti il posto nell'albero
 * accanto al suo nodo a monte (lo segue se lo si sposta). Sotto uno stesso nodo prima i rami (switch, access point…),
 * poi i dispositivi senza rami: pochi in colonna, tanti in una griglia su più colonne (rail: collegati con una linea
 * verticale e una orizzontale per riga, non con una curva ciascuno). Anche i rami chiusi hanno un posto: serve a chi li apre.
 * @returns {Map<string, {x:number, y:number, auto:boolean, rail:boolean}>}
 */
export function topologyPositions(nodes, collapsed = new Set()) {
  const { roots, kids } = topologyTree(nodes);
  const auto = new Map();
  const rail = new Set();
  let row = 0;
  const place = (n, depth, seen) => {
    if (seen.has(n.key)) return null;
    seen.add(n.key);
    // i rami chiusi occupano una riga sola: i loro nodi si mettono come se fossero aperti, ma non spostano gli altri
    const closed = collapsed.has(n.key);
    const children = closed ? [] : (kids.get(n.key) || []).filter((k) => !seen.has(k.key));
    const ys = [];
    children.filter((k) => kids.has(k.key)).forEach((k) => { const r = place(k, depth + 1, seen); if (r) ys.push(r.y); });
    const leaves = children.filter((k) => !kids.has(k.key) && !seen.has(k.key));
    const cols = gridColumns(leaves.length);
    leaves.forEach((k, i) => {
      seen.add(k.key);
      const ky = (row + Math.floor(i / cols)) * ROW;
      auto.set(k.key, { x: slotX(depth + 1 + (i % cols)), y: PAD + ky + TILE / 2 });
      if (cols > 1) rail.add(k.key);
      ys.push(ky);
    });
    if (leaves.length) row += Math.ceil(leaves.length / cols);
    const y = ys.length ? (Math.min(...ys) + Math.max(...ys)) / 2 : (row++) * ROW;
    const p = { x: slotX(depth), y: PAD + y + TILE / 2 };
    auto.set(n.key, p);
    if (closed) {
      let r = 0;
      const hidden = (pk, d) => (kids.get(pk) || []).forEach((k) => {
        if (seen.has(k.key)) return;
        seen.add(k.key);
        auto.set(k.key, { x: slotX(d), y: p.y + (r++) * ROW });
        hidden(k.key, d + 1);
      });
      hidden(n.key, depth + 1);
    }
    return { y };
  };
  const seen = new Set();
  roots.forEach((r) => { place(r, 0, seen); row += 0.35; }); // un po' d'aria tra un albero e l'altro
  const pos = new Map();
  const walk = (n, parent) => {
    const own = saved(n);
    const a = auto.get(n.key);
    let p;
    if (own) p = { ...own, auto: false, rail: false };
    else if (parent) {
      const pa = auto.get(parent.key);
      const pp = pos.get(parent.key);
      p = { x: pp.x + (a.x - pa.x), y: pp.y + (a.y - pa.y), auto: true, rail: rail.has(n.key) };
    } else p = { x: a.x, y: a.y, auto: true, rail: false };
    pos.set(n.key, p);
    (kids.get(n.key) || []).forEach((k) => { if (!pos.has(k.key)) walk(k, n); });
  };
  roots.forEach((r) => walk(r, null));
  return pos;
}

/** I nodi del ramo sotto key (tutti, anche dietro i rami chiusi). */
export function branchOf(key, kids) {
  const out = new Set();
  const walk = (pk) => (kids.get(pk) || []).forEach((k) => { if (!out.has(k.key)) { out.add(k.key); walk(k.key); } });
  walk(key);
  return out;
}

/** Il collegamento tra un nodo a monte (p) e uno a valle (c), dai bordi che si guardano. */
function linkGeometry(p, c, parentHasKids, childHasKids) {
  const dx = c.x - p.x;
  const dy = c.y - p.y;
  const gap = TILE + 30;
  if (dx > gap || dx < -gap) {
    const right = dx > 0;
    const x1 = right ? p.x + TILE / 2 + (parentHasKids ? 17 : 4) : p.x - TILE / 2 - 4;
    const x2 = right ? c.x - TILE / 2 - 4 : c.x + TILE / 2 + (childHasKids ? 17 : 4);
    const k = Math.max(30, Math.abs(x2 - x1) / 2) * (right ? 1 : -1);
    return `M${x1} ${p.y} C${x1 + k} ${p.y}, ${x2 - k} ${c.y}, ${x2} ${c.y}`;
  }
  const down = dy >= 0;
  const y1 = down ? p.y + TILE / 2 + BELOW : p.y - TILE / 2 - 4;
  const y2 = down ? c.y - TILE / 2 - 4 : c.y + TILE / 2 + BELOW;
  const k = Math.max(24, Math.abs(y2 - y1) / 2) * (down ? 1 : -1);
  return `M${p.x} ${y1} C${p.x} ${y1 + k}, ${c.x} ${y2 - k}, ${c.x} ${y2}`;
}

/** Il collegamento di un nodo della griglia: dal nodo a monte una linea verticale, poi orizzontale fino alla sua riga. */
function railGeometry(p, c) {
  const x1 = p.x + TILE / 2 + 17;
  const xt = p.x + TRUNK;
  const x2 = c.x - TILE / 2 - 4;
  if (x2 <= xt + 10 || c.x < p.x) return linkGeometry(p, c, true, false);
  return `M${x1} ${p.y} H${xt} V${c.y} H${x2}`;
}

/**
 * Le linee dei collegamenti tra i nodi mostrati (pos: le posizioni da usare, anche durante un trascinamento). Le linee
 * della griglia si sovrappongono (stessa verticale, stessa riga): si disegnano prima le più lontane, così ogni tratto
 * prende il colore del nodo più vicino che ci passa.
 */
function linksSvg(scene, pos) {
  const links = [];
  scene.shown.forEach((n) => {
    if (!n.uplink || !scene.shown.has(n.uplink)) return;
    const p = pos.get(n.uplink);
    const c = pos.get(n.key);
    if (!p || !c) return;
    links.push({ n, p, c, rail: !!c.rail && !!p });
  });
  links.sort((a, b) => (a.rail !== b.rail ? (a.rail ? -1 : 1) : a.rail ? (b.c.x - a.c.x) || (b.c.y - a.c.y) : 0));
  return links.map(({ n, p, c, rail }) => {
    const d = rail ? railGeometry(p, c) : linkGeometry(p, c, scene.kids.has(n.uplink), scene.kids.has(n.key));
    const tone = n.state === 'off' ? 'off' : n.state === 'on' ? 'on' : 'muted';
    return `<path class="topo-link is-${tone}" d="${d}"></path>`;
  }).join('');
}

/** Il riquadro dei nodi mostrati (con il posto per nomi e indirizzi). */
function contentOf(shown, pos) {
  let x0 = Infinity; let y0 = Infinity; let x1 = -Infinity; let y1 = -Infinity;
  shown.forEach((n) => {
    const p = pos.get(n.key);
    if (!p) return;
    x0 = Math.min(x0, p.x - 80); x1 = Math.max(x1, p.x + 80);
    y0 = Math.min(y0, p.y - TILE / 2 - 30); y1 = Math.max(y1, p.y + TILE / 2 + BELOW + 10);
  });
  if (!Number.isFinite(x0)) return { x: 0, y: 0, w: 400, h: 300 };
  return { x: x0 - PAD, y: y0 - PAD / 2, w: x1 - x0 + 2 * PAD, h: y1 - y0 + 1.5 * PAD };
}

/**
 * Disegna la topologia in host. opts: nodes, collapsed (Set delle chiavi chiuse), zoom, moved (Map chiave → {x, y}:
 * posizioni appena cambiate, non ancora rilette dal programma), keep: il riquadro di prima (il disegno non si restringe
 * mentre lo si usa: i nodi non cambiano posto sullo schermo). Restituisce la scena: { byKey, kids, pos, shown, content, frame, zoom }.
 */
export function renderTopology(host, { nodes, collapsed = new Set(), zoom = 1, moved = null, keep = null }) {
  const { kids, byKey } = topologyTree(nodes);
  const list = moved && moved.size ? nodes.map((n) => (moved.has(n.key) ? { ...n, x: moved.get(n.key).x, y: moved.get(n.key).y } : n)) : nodes;
  const pos = topologyPositions(list, collapsed);
  const shown = new Map();
  const hiddenKeys = new Set();
  collapsed.forEach((key) => { if (byKey.has(key)) branchOf(key, kids).forEach((x) => hiddenKeys.add(x)); });
  nodes.forEach((n) => { if (!hiddenKeys.has(n.key)) shown.set(n.key, n); });
  const content = contentOf(shown, pos);
  let frame = { x: content.x - MARGIN, y: content.y - MARGIN, w: content.w + 2 * MARGIN, h: content.h + 2 * MARGIN };
  if (keep) {
    const x0 = Math.min(frame.x, keep.x);
    const y0 = Math.min(frame.y, keep.y);
    frame = { x: x0, y: y0, w: Math.max(frame.x + frame.w, keep.x + keep.w) - x0, h: Math.max(frame.y + frame.h, keep.y + keep.h) - y0 };
  }
  const scene = { byKey, kids, pos, shown, content, frame, zoom };

  const nodeHtml = (n) => {
    const p = pos.get(n.key);
    const hasKids = kids.has(n.key);
    const closed = collapsed.has(n.key);
    const info = hasKids ? branchInfo(n.key, kids) : null;
    const up = n.uplink && byKey.get(n.uplink);
    return `<div class="topo-node is-${n.state}" data-topo-key="${esc(n.key)}" style="left:${p.x - frame.x - 80}px;top:${p.y - frame.y - TILE / 2}px">
      <button type="button" class="topo-tile" data-topo-open title="${esc(n.name)}${n.sub ? ` · ${esc(n.sub)}` : ''}${up ? ` · collegato a ${esc(up.name)}` : ''}">${n.tile}</button>
      ${hasKids ? `<button type="button" class="topo-fold is-${info.tone} ${closed ? 'is-closed' : ''}" data-topo-fold title="${closed ? `Apri il ramo: ${info.count} ${info.count === 1 ? 'nodo' : 'nodi'} dietro` : 'Chiudi il ramo'}" aria-label="${closed ? 'Apri il ramo' : 'Chiudi il ramo'}">${closed ? info.count : '−'}</button>` : ''}
      <div class="topo-label"><span class="topo-dot is-${n.state}"></span><b title="${esc(n.name)}">${esc(n.name)}</b></div>
      ${n.sub ? `<small class="topo-sub mono" title="${esc(n.sub)}">${esc(n.sub)}</small>` : ''}
    </div>`;
  };

  host.innerHTML = `<div class="topo-sizer" style="width:${Math.round(frame.w * zoom)}px;height:${Math.round(frame.h * zoom)}px">
    <div class="topo-canvas" style="width:${frame.w}px;height:${frame.h}px;transform:scale(${zoom})">
      <svg class="topo-links" width="${frame.w}" height="${frame.h}" viewBox="${frame.x} ${frame.y} ${frame.w} ${frame.h}" aria-hidden="true">${linksSvg(scene, pos)}</svg>
      ${[...shown.values()].map(nodeHtml).join('')}
    </div></div>`;
  return scene;
}

/** Ingrandimento senza ridisegnare: il punto sotto anchor ({x, y} nello schermo, di solito il puntatore) resta lì. */
export function setTopologyZoom(host, scene, zoom, anchor = null) {
  const sizer = host.querySelector('.topo-sizer');
  const canvas = host.querySelector('.topo-canvas');
  if (!sizer || !canvas || !scene) return zoom;
  const z = clampZoom(zoom);
  const r = host.getBoundingClientRect();
  const ax = anchor ? anchor.x - r.left : r.width / 2;
  const ay = anchor ? anchor.y - r.top : r.height / 2;
  const wx = (host.scrollLeft + ax) / scene.zoom;
  const wy = (host.scrollTop + ay) / scene.zoom;
  sizer.style.width = `${Math.round(scene.frame.w * z)}px`;
  sizer.style.height = `${Math.round(scene.frame.h * z)}px`;
  canvas.style.transform = `scale(${z})`;
  host.scrollLeft = wx * z - ax;
  host.scrollTop = wy * z - ay;
  scene.zoom = z;
  return z;
}

/** L'ingrandimento con cui tutti i nodi entrano nella vista (al massimo 100%). */
export function fitZoom(host, scene) {
  if (!scene || !scene.content) return 1;
  const r = host.getBoundingClientRect();
  return clampZoom(Math.min(1, (r.width - 16) / scene.content.w, (r.height - 16) / scene.content.h));
}

/** Porta i nodi al centro della vista (o in alto a sinistra, se non ci stanno). */
export function centerTopology(host, scene) {
  if (!scene || !scene.content) return;
  const r = host.getBoundingClientRect();
  const z = scene.zoom;
  host.scrollLeft = (scene.content.x - scene.frame.x) * z - Math.max(8, (r.width - scene.content.w * z) / 2);
  host.scrollTop = (scene.content.y - scene.frame.y) * z - Math.max(8, (r.height - scene.content.h * z) / 2);
}

/**
 * Collega gli eventi della topologia (una volta sola sul contenitore): clic, tasto destro, rami, trascinamento dei nodi
 * e del fondo, rotellina. get() restituisce la scena attuale. Azioni: onOpen(n), onMenu(n, punto), onToggle(key),
 * onLink(n, uplinkKey), onMove([{key, x, y}]), onZoom(zoom), onIdle() (finito un trascinamento: si può ridisegnare).
 * Restituisce { busy() }: vero mentre si trascina.
 */
export function bindTopology(host, get, { onOpen, onMenu, onLink, onToggle, onMove, onZoom, onIdle }) {
  let press = null;     // pulsante premuto: { kind: 'node'|'pan', ... }
  let dragging = null;  // trascinamento di un nodo in corso
  let swallowClick = false;

  const nodeEl = (key) => host.querySelector(`[data-topo-key="${CSS.escape(key)}"]`);
  const nodeOf = (el) => {
    const n = el && el.closest ? el.closest('[data-topo-key]') : null;
    return n ? get().byKey.get(n.dataset.topoKey) || null : null;
  };
  const canLink = (n, target) => target && target.key !== n.key && target.key !== n.uplink && !branchOf(n.key, get().kids).has(target.key);
  const clear = () => host.querySelectorAll('.is-drop-target').forEach((x) => x.classList.remove('is-drop-target'));
  /** Il nodo sotto il puntatore (escluso chi si sta spostando), dalla sua icona. */
  const targetAt = (x, y, moving) => {
    const el = document.elementsFromPoint(x, y).find((e) => e.closest && e.closest('.topo-tile') && host.contains(e));
    const n = el ? nodeOf(el) : null;
    return n && !moving.has(n.key) ? n : null;
  };

  host.addEventListener('click', (e) => {
    if (swallowClick) { swallowClick = false; e.stopPropagation(); return; }
    const n = nodeOf(e.target);
    if (!n) return;
    if (e.target.closest('[data-topo-fold]')) { onToggle(n.key); return; }
    if (e.target.closest('[data-topo-open]')) onOpen(n);
  }, true);
  host.addEventListener('contextmenu', (e) => {
    const n = nodeOf(e.target);
    if (!n) return;
    e.preventDefault();
    e.stopPropagation();
    onMenu(n, { x: e.clientX, y: e.clientY });
  });

  // rotellina: ingrandisce attorno al puntatore (anche il pizzico del touchpad, che arriva con Ctrl); uno scorrimento
  // di lato del touchpad sposta la vista
  host.addEventListener('wheel', (e) => {
    const scene = get();
    if (!scene || !scene.frame) return;
    if (!e.ctrlKey && Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;
    e.preventDefault();
    const unit = e.deltaMode === 1 ? 33 : e.deltaMode === 2 ? 400 : 1;
    const k = Math.exp(-e.deltaY * unit * (e.ctrlKey ? 0.01 : 0.0015));
    const z = setTopologyZoom(host, scene, scene.zoom * k, { x: e.clientX, y: e.clientY });
    onZoom(z);
  }, { passive: false });

  host.addEventListener('pointerdown', (e) => {
    if (e.button !== 0 || press) return;
    if (e.target.closest('[data-topo-fold]')) return;
    const n = nodeOf(e.target);
    if (n) {
      press = { kind: 'node', n, x: e.clientX, y: e.clientY, id: e.pointerId, only: e.shiftKey };
    } else if (e.target.closest('.topo-sizer') || e.target === host) {
      press = { kind: 'pan', x: e.clientX, y: e.clientY, left: host.scrollLeft, top: host.scrollTop, id: e.pointerId };
      try { host.setPointerCapture(e.pointerId); } catch { /* puntatore già rilasciato */ }
      host.classList.add('is-panning');
      e.preventDefault();
    }
  });

  const startDrag = () => {
    const scene = get();
    const n = press.n;
    // si sposta il nodo con il suo ramo (quello mostrato; con Maiusc solo lui)
    const branch = branchOf(n.key, scene.kids);
    const moving = new Set([n.key, ...(press.only ? [] : [...branch].filter((key) => scene.shown.has(key)))]);
    const start = new Map([...moving].map((key) => [key, { ...scene.pos.get(key) }]));
    dragging = { n, moving, start, branch, only: press.only, dx: 0, dy: 0, target: null };
    try { host.setPointerCapture(press.id); } catch { /* puntatore già rilasciato */ }
    host.classList.add('is-moving');
    moving.forEach((key) => { const el = nodeEl(key); if (el) el.classList.add('is-dragging'); });
  };

  host.addEventListener('pointermove', (e) => {
    if (!press || e.pointerId !== press.id) return;
    if (press.kind === 'pan') {
      host.scrollLeft = press.left - (e.clientX - press.x);
      host.scrollTop = press.top - (e.clientY - press.y);
      return;
    }
    if (!dragging) {
      if (Math.hypot(e.clientX - press.x, e.clientY - press.y) < 5) return;
      startDrag();
    }
    const scene = get();
    const d = dragging;
    d.dx = (e.clientX - press.x) / scene.zoom;
    d.dy = (e.clientY - press.y) / scene.zoom;
    const pos = new Map(scene.pos);
    d.moving.forEach((key) => {
      const s = d.start.get(key);
      pos.set(key, { ...s, x: s.x + d.dx, y: s.y + d.dy });
      const el = nodeEl(key);
      if (el) { el.style.left = `${s.x + d.dx - scene.frame.x - 80}px`; el.style.top = `${s.y + d.dy - scene.frame.y - TILE / 2}px`; }
    });
    d.pos = pos;
    const svg = host.querySelector('.topo-links');
    if (svg) svg.innerHTML = linksSvg(scene, pos);
    // sopra un altro nodo: si collega lì
    const target = targetAt(e.clientX, e.clientY, d.moving);
    clear();
    d.target = target && canLink(d.n, target) ? target : null;
    if (d.target) { const el = nodeEl(d.target.key); if (el) el.classList.add('is-drop-target'); }
    // vicino al bordo la vista scorre da sola
    const r = host.getBoundingClientRect();
    const edge = 28;
    if (e.clientX < r.left + edge) host.scrollLeft -= 12; else if (e.clientX > r.right - edge) host.scrollLeft += 12;
    if (e.clientY < r.top + edge) host.scrollTop -= 12; else if (e.clientY > r.bottom - edge) host.scrollTop += 12;
  });

  const finish = (e) => {
    if (!press || e.pointerId !== press.id) return;
    const was = press;
    press = null;
    host.classList.remove('is-panning', 'is-moving');
    if (was.kind === 'pan') { onIdle(); return; }
    const d = dragging;
    dragging = null;
    clear();
    if (!d) return; // un clic: lo gestisce click
    swallowClick = true;
    setTimeout(() => { swallowClick = false; }, 0);
    host.querySelectorAll('.is-dragging').forEach((x) => x.classList.remove('is-dragging'));
    if (e.type === 'pointercancel') { onIdle(); return; }
    if (d.target) {
      onLink(d.n, d.target.key);
      return;
    }
    const scene = get();
    const moves = [...d.moving].map((key) => {
      const s = d.start.get(key);
      return { key, x: Math.round(s.x + d.dx), y: Math.round(s.y + d.dy) };
    });
    // spostato di poco e senza ramo: niente da salvare
    if (Math.abs(d.dx) < 2 && Math.abs(d.dy) < 2) { onIdle(); return; }
    onMove(moves, scene);
  };
  host.addEventListener('pointerup', finish);
  host.addEventListener('pointercancel', finish);

  return { busy: () => !!dragging };
}
