// Scansione della rete all'avvio (Impostazioni → Generale): nella barra superiore, al posto dell'attività in corso, un
// riquadro arancione con il conto alla rovescia, «Avvia ora» e «Annulla»; allo scadere apre la pagina Rete e la
// scansione parte con le opzioni di sempre (poi lì compare il riquadro «Scansione in corso»).
import { $, esc } from './dom.js';
import { icon } from './icons.js';
import { S } from './store.js';
import { navigate } from './nav.js';

let timer = 0;

function go() {
  close();
  navigate('network', { start: true });
}

function close() {
  clearInterval(timer);
  timer = 0;
  const el = $('#autoscan-chip');
  if (el) el.remove();
}

/** delay = secondi prima della scansione (0 = subito, senza avviso). */
export function offerAutoScan(delay) {
  if (delay == null || delay < 0) return;
  if (delay === 0) { go(); return; }
  const middle = $('#topbar-middle');
  if (!middle) { go(); return; }
  const target = ((S.st && S.st.adapters) || []).find((a) => a.cidr);
  const net = S.net && S.net.target ? S.net.target : target ? target.cidr : '';
  const el = document.createElement('div');
  el.id = 'autoscan-chip';
  el.className = 'activity-chip autoscan-chip';
  el.setAttribute('role', 'status');
  el.title = 'Scansione all\'avvio (Impostazioni → Generale): allo scadere si apre la pagina Rete';
  el.innerHTML = `<span class="pulse is-busy"></span>
    <span class="activity-text"><b>Scansione tra <span data-left>${delay}</span> s</b><small>${net ? `Rete ${esc(net)}` : 'Rete della scheda collegata'}</small></span>
    <span class="meter"><span style="animation-duration:${delay}s"></span></span>
    <button type="button" class="autoscan-btn is-go" data-autoscan="now" title="Avvia subito la scansione">${icon('play', { size: 13 })} Avvia ora</button>
    <button type="button" class="autoscan-btn" data-autoscan="cancel" title="Annulla la scansione all'avvio">${icon('close', { size: 14 })}</button>`;
  el.addEventListener('click', (e) => {
    const b = e.target.closest('[data-autoscan]');
    if (!b) return;
    if (b.dataset.autoscan === 'now') go(); else close();
  });
  middle.prepend(el);
  const end = Date.now() + delay * 1000;
  timer = setInterval(() => {
    const left = Math.ceil((end - Date.now()) / 1000);
    if (left <= 0) { go(); return; }
    // se la barra superiore è stata ridisegnata, il riquadro torna al suo posto
    if (!el.isConnected) { const m = $('#topbar-middle'); if (m) m.prepend(el); }
    const span = $('[data-left]', el);
    if (span) span.textContent = left;
  }, 250);
}
