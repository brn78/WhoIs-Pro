// Archivio della rete nel tempo, una rete (sede, cliente) per volta: dispositivi conosciuti con la scheda curata dall'utente
// (alias, fiducia, note), cambiamenti tra una scansione e l'altra (nuovo, ricomparso, cambiato, assente) e storico delle
// scansioni. La rete è il router (MAC) o, in mancanza, la sottorete; si apre su quella in cui si trova il PC.
import { call, on } from '../core/bridge.js';
import { $, $$, esc, fmtDateTime, fmtNumber, fmtSeconds, plural, subnetOf, countSubnets, renderBySubnet, shortVendor, dhcpKind, dhcpTag } from '../core/dom.js';
import { icon } from '../core/icons.js';
import { catIcon } from '../core/categories.js';
import { S, definePage, adopt, bus } from '../core/store.js';
import { navigate } from '../core/nav.js';
import { replaceKeepingScroll, table } from '../core/table.js';
import { renderTopology, bindTopology, setTopologyZoom, fitZoom, centerTopology, clampZoom, branchOf, topologyTree } from '../core/topology.js';
import { closeOverlays, confirmDialog, emptyState, note, openDrawer, openMenu, pageHead, promptDialog, toast, toastError, updateDrawer } from '../core/ui.js';
import { isHikvision, launchItems } from './network.js';

S.devices = {
  data: null,          // risposta di «inventory»: network, current, networks, hosts, events, scans, digest, watch
  loading: false,
  tab: 'hosts',
  network: '',         // rete mostrata: '' = quella in cui si trova il PC, '*' = tutte, altrimenti la chiave
  networkPinned: false, // vero se l'utente ha scelto una rete: non si segue più quella del PC finché non cambia pagina
  q: '', subnet: '', onlyOnline: false,
  // pannello laterale: il dispositivo aperto (id e scheda letta) oppure il nodo virtuale aperto { key, network }
  selectedId: null, detail: null, saved: false, node: null, seed: null,
  topoList: false, topoFilter: '', // sezione Topologia della scheda: elenco «collegati qui» aperto e sua ricerca
  selectMode: false,   // selezione multipla: le righe si spuntano e la barra sopra l'elenco agisce su tutte insieme
  selected: new Set(), // id dei dispositivi spuntati
  // scheda Topologia: risposta di «topology» (hosts, nodes), ingrandimento, rami chiusi, scena disegnata, posizioni appena
  // trascinate; byNet: la stessa risposta per rete, per la sezione Topologia della scheda (anche di un'altra rete)
  topo: { data: null, zoom: 1, collapsed: new Set(), scene: null, moved: new Map(), loading: false, ctl: null, byNet: new Map(), pending: new Set() },
  pendingHost: null,   // scheda da aprire appena i dati arrivano (dall'avviso o da un evento)
  badgeSeen: false,    // il contatore «da vedere» si azzera quando si apre la pagina
};

const TRUST = { 0: { label: 'Da classificare', tone: '' }, 1: { label: 'Affidabile', tone: 'success' }, 2: { label: 'Sospetto', tone: 'danger' } };
const KIND = { nuovo: 'warning', ricomparso: 'info', cambiato: 'primary', assente: '' };

const watch = () => (S.devices.data && S.devices.data.watch) || (S.st && S.st.inventory) || {};
const fmtShort = (iso) => fmtDateTime(iso).replace(/\/\d{4} /, ' ');
const upperMac = (m) => String(m || '').toUpperCase();
const allNetworks = () => S.devices.data && S.devices.data.network === '*';
/** La rete mostrata, con i suoi numeri (null con «Tutte le reti» o se non è ancora in archivio). */
const shownNetwork = () => { const d = S.devices.data; return d ? (d.networks || []).find((n) => n.key === d.network) || null : null; };
/** Nome della rete mostrata: alias dell'utente, altrimenti l'etichetta della scansione. */
function shownNetworkName() {
  const d = S.devices.data;
  if (!d) return '';
  if (d.network === '*') return 'Tutte le reti';
  const n = shownNetwork();
  if (n) return n.name;
  return d.current && d.current.key === d.network ? d.current.name : d.network.replace(/^(gw|net):/, '');
}

// ---------------------------------------------------------------------------------------------------------------------
// Intestazione: rete mostrata (selettore, rinomina, dimentica), digest, «Scansiona adesso» con l'avanzamento per fase
// ---------------------------------------------------------------------------------------------------------------------
function scanControl() {
  const w = watch();
  if (w.scanning) {
    return `<span class="scan-live" id="inv-live">
      <span class="spinner"></span>
      <span class="scan-live-text"><b id="inv-status">${esc(w.status || 'Preparazione…')}</b><span class="meter"><span id="inv-meter" style="width:${w.percent || 0}%"></span></span></span>
      ${S.st && S.st.running && S.st.running.network ? '' : `<button class="btn btn-sm btn-danger" data-action="inv-stop">${icon('stop')} Ferma</button>`}</span>`;
  }
  const cur = w.network;
  return `<button class="btn btn-primary" data-action="inv-scan" title="${esc(cur ? `Scansiona la rete in cui sei adesso (${cur.name}) e aggiorna il suo archivio` : 'Scansiona la rete principale e aggiorna l\'archivio')}">${icon('radar')} Scansiona adesso</button>`;
}

/** Aggiorna sul posto lo stato della scansione (testo e barra) senza ridisegnare la pagina. */
function updateLive() {
  const live = $('#inv-live');
  const w = watch();
  if (!live || !w.scanning) { const box = $('#inv-scan-box'); if (box) box.innerHTML = scanControl(); return; }
  $('#inv-status', live).textContent = w.status || 'Preparazione…';
  $('#inv-meter', live).style.width = `${w.percent || 0}%`;
}

/** Selettore della rete: quella in cui si trova il PC è segnata «qui»; le altre sono le sedi visitate; in fondo «Tutte le reti». */
function networkBar() {
  const d = S.devices.data;
  const nets = (d.networks || []).slice();
  const cur = d.current;
  if (cur && !nets.some((n) => n.key === cur.key)) nets.unshift({ key: cur.key, name: cur.name, label: cur.label, hostsCount: 0, scansCount: 0, isCurrent: true, hasGateway: cur.hasGateway });
  const sel = d.network;
  const selected = nets.find((n) => n.key === sel);
  const opt = (n) => `<option value="${esc(n.key)}" ${n.key === sel ? 'selected' : ''}>${esc(n.name)}${n.isCurrent ? ' · qui' : ''}${n.hostsCount ? ` · ${plural(n.hostsCount, 'dispositivo', 'dispositivi')}` : ''}</option>`;
  const total = nets.reduce((a, n) => a + (n.hostsCount || 0), 0);
  let where = '';
  if (!cur) where = '<span class="pill" title="Nessuna scheda di rete collegata a un router">Nessuna rete locale</span>';
  else if (sel === '*') where = `<span class="pill" title="${esc(cur.name)}">Adesso sei in «${esc(cur.name)}»</span>`;
  else if (selected && selected.isCurrent) where = '<span class="pill success pill-dot" title="L\'archivio di questa rete si aggiorna con le scansioni di adesso">Sei in questa rete</span>';
  else where = `<span class="pill warning pill-dot" title="Adesso sei in «${esc(cur.name)}»: questo è l'archivio di un'altra sede">Non sei in questa rete</span>`;
  const info = selected
    ? `${selected.hasGateway ? `router ${esc(selected.label.split(' · ').slice(1).join(' · ') || selected.cidr || '')}` : 'riconosciuta dalla sottorete'}${selected.scansCount ? ` · ${plural(selected.scansCount, 'scansione', 'scansioni')}` : ''}${selected.lastScanAt ? ` · ultima ${fmtShort(selected.lastScanAt)}` : ''}${selected.dhcpRange ? ` · DHCP <span class="mono">${esc(selected.dhcpRange)}</span>` : ''}`
    : sel === '*' ? `${plural(nets.length, 'rete', 'reti')} in archivio` : '';
  return `<div class="net-bar">
    <span class="net-bar-label">${icon('network')} Rete</span>
    <select class="select" data-field="network" title="L'archivio è diviso per rete: a casa vedi i dispositivi di casa, dal cliente quelli del cliente. La rete si riconosce dal MAC del router.">
      ${nets.map(opt).join('')}<option value="*" ${sel === '*' ? 'selected' : ''}>Tutte le reti · ${plural(total, 'dispositivo', 'dispositivi')}</option></select>
    ${where}
    ${sel !== '*' ? `<button class="btn btn-sm" data-action="net-rename" title="Dai un nome alla rete: il cliente, la sede. Vale anche per il file della sessione.">${icon('edit')} Rinomina</button>` : ''}
    ${sel !== '*' ? `<button class="btn btn-sm" data-action="net-dhcp" title="Il primo e l'ultimo indirizzo che il router assegna da solo (DHCP), se li conosci: nelle liste compare «DHCP» o «fisso» accanto a ogni indirizzo">${icon('mapPin')} DHCP</button>` : ''}
    ${sel !== '*' && selected && !selected.isCurrent ? `<button class="btn btn-sm btn-danger-ghost" data-action="net-forget" title="Toglie dall'archivio la rete con i suoi dispositivi, la loro storia e le sue scansioni">${icon('trash')} Dimentica la rete</button>` : ''}
    <span class="net-bar-info muted">${info}</span>
  </div>`;
}

function footerText() {
  const d = S.devices.data;
  const w = watch();
  const last = d && d.scans && d.scans[0];
  if (!last) return 'L\'archivio si costruisce scansione dopo scansione, rete per rete: anche le scansioni della pagina Rete lo aggiornano. Le scansioni automatiche si regolano in Impostazioni → Archivio della rete.';
  let next = '';
  if (w.enabled && w.nextScanAt) {
    const at = new Date(w.nextScanAt);
    next = at.getTime() <= Date.now() ? ' · Prossima automatica tra pochi istanti.' : ` · Prossima automatica alle ${fmtDateTime(w.nextScanAt).slice(11)}.`;
  } else next = ' · Scansioni automatiche spente (Impostazioni → Archivio della rete).';
  return `Ultima scansione ${fmtDateTime(last.startedAt)} su ${last.target}: ${plural(last.hostsCount, 'dispositivo', 'dispositivi')} in ${fmtSeconds(last.durationMs / 1000)}${next}`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Scheda Dispositivi: elenco a sinistra, scheda del dispositivo a destra
// ---------------------------------------------------------------------------------------------------------------------
function hostRow(h) {
  const t = TRUST[h.trust] || TRUST[0];
  const sub = [h.vendor ? shortVendor(h.vendor) : '', h.deviceType, h.isGateway ? 'gateway' : '', h.isLocal ? 'questa macchina' : ''].filter(Boolean).join(' · ');
  const checked = S.devices.selectMode && S.devices.selected.has(h.id);
  return `<button class="dev-row ${h.id === S.devices.selectedId && !S.devices.selectMode ? 'is-active' : ''} ${checked ? 'is-checked' : ''} ${h.isOnline ? '' : 'is-off'}" data-action="pick-host" data-id="${h.id}" data-ctx="device">
    ${S.devices.selectMode ? `<span class="dev-check ${checked ? 'is-on' : ''}" aria-hidden="true">${icon('check', { size: 12 })}</span>` : ''}${catIcon(h)}
    <span class="dev-text">
      <span class="dev-name"><b class="trunc" title="${esc(h.displayName)}">${esc(h.displayName)}</b>
        ${h.trust ? `<span class="pill ${t.tone}">${t.label.toLowerCase()}</span>` : ''}${h.isNew ? '<span class="pill warning">nuovo</span>' : ''}${allNetworks() && h.networkName ? `<span class="tag" title="Rete">${esc(h.networkName)}</span>` : ''}</span>
      <small class="trunc" title="${esc(sub)}">${esc(sub)}</small></span>
    <span class="dev-state"><span class="state"><span class="dot ${h.isOnline ? 'on' : ''}"></span>${h.isOnline ? 'presente' : `ultima volta ${fmtShort(h.lastSeen)}`}</span><span class="mono">${esc(h.ip)}</span>${(h.extraIps || []).length ? ` <span class="tag" title="Stesso dispositivo (stesso MAC) anche su: ${esc(h.extraIps.join(', '))}">+${h.extraIps.length} IP</span>` : ''}${dhcpTag(h.ip, networkOf(h))}</span>
  </button>`;
}

/** La rete di un dispositivo dell'archivio (per l'intervallo DHCP): tra quelle conosciute, oppure quella in cui si trova il PC. */
function networkOf(h) {
  const d = S.devices.data;
  if (!d) return null;
  return (d.networks || []).find((n) => n.key === h.network) || (d.current && d.current.key === h.network ? d.current : null);
}

function deviceSubnets() {
  return countSubnets(S.devices.data && S.devices.data.hosts, (h) => h.ip);
}

function activeDeviceSubnet() {
  const sub = S.devices.subnet || '';
  if (!sub || !S.devices.data) return '';
  return (S.devices.data.hosts || []).some((h) => subnetOf(h.ip) === sub) ? sub : '';
}

function visibleDeviceHosts() {
  const hosts = (S.devices.data && S.devices.data.hosts) || [];
  const sub = activeDeviceSubnet();
  return sub ? hosts.filter((h) => subnetOf(h.ip) === sub) : hosts;
}

function renderDeviceRows(hosts) {
  const subs = deviceSubnets();
  if (subs.length <= 1 || activeDeviceSubnet()) return hosts.map(hostRow).join('');
  return renderBySubnet(hosts, (h) => h.ip, hostRow, 'dispositivo', 'dispositivi');
}

function listCard() {
  const d = S.devices.data;
  const hosts = visibleDeviceHosts();
  const subs = deviceSubnets();
  const curSub = activeDeviceSubnet();
  const others = (d.networks || []).filter((n) => n.key !== d.network && n.hostsCount > 0).length;
  // la stessa sottorete conosciuta solo per sottorete (vecchio registro, versioni precedenti): i suoi dispositivi passano qui appena ricompaiono
  const shown = shownNetwork() || d.current;
  const legacy = shown && shown.cidr ? (d.networks || []).find((n) => n.key !== d.network && !n.hasGateway && n.cidr === shown.cidr && n.hostsCount > 0) : null;
  let body;
  if (!d.total) {
    body = allNetworks()
      ? emptyState('server', 'Nessun dispositivo in archivio', 'Avvia una scansione: i dispositivi trovati restano qui anche quando si spengono, con la loro storia.')
      : emptyState('server', `Nessun dispositivo in «${shownNetworkName()}»`, `Avvia una scansione da qui o dalla pagina Rete: i dispositivi di questa rete restano in archivio con la loro storia.${legacy ? ` In archivio ci sono già ${plural(legacy.hostsCount, 'dispositivo', 'dispositivi')} della sottorete ${legacy.cidr} (versioni precedenti): passano qui appena la scansione li ritrova.` : others ? ` Le altre ${plural(others, 'rete conosciuta è', 'reti conosciute sono')} nel menu «Rete» qui sopra.` : ''}`);
  } else if (!hosts.length) body = emptyState('search', 'Nessun dispositivo corrisponde', 'Cambia le parole della ricerca o togli «solo presenti».');
  else body = `<div class="dev-list" id="dev-list">${renderDeviceRows(hosts)}</div>`;
  const subnetBox = subs.length > 1
    ? `<div class="sort-box"><label for="d-subnet">Sottorete</label>
        <select class="select input-sm" id="d-subnet" data-field="subnet" title="Filtra i dispositivi di una singola sottorete">
          <option value="" ${!curSub ? 'selected' : ''}>Tutte (${subs.length})</option>
          ${subs.map(([cidr, cnt]) => `<option value="${esc(cidr)}" ${cidr === curSub ? 'selected' : ''}>${esc(cidr)} (${cnt})</option>`).join('')}
        </select></div>`
    : '';
  return `<section class="card table-card">
    <div class="toolbar">
      <div class="search">${icon('search')}<input class="input" data-field="q" value="${esc(S.devices.q)}" placeholder="Cerca per nome, IP, MAC, produttore, tipo, alias o note" autocomplete="off"></div>
      ${subnetBox}
      <label class="switch"><input type="checkbox" data-field="onlyOnline" ${S.devices.onlyOnline ? 'checked' : ''}><span class="track"></span><span>Solo presenti</span></label>
      <div class="toolbar-actions"><span class="toolbar-info">${hosts.length === d.total ? plural(d.total, 'dispositivo', 'dispositivi') : `${hosts.length} di ${d.total}`}</span>
        ${d.total ? `<button class="btn btn-sm ${S.devices.selectMode ? 'is-active' : ''}" data-action="select-mode" title="${S.devices.selectMode ? 'Chiude la selezione multipla' : 'Spunta più dispositivi e cambia la fiducia o dimenticali tutti insieme'}">${icon(S.devices.selectMode ? 'close' : 'check')} ${S.devices.selectMode ? 'Fine' : 'Seleziona'}</button>` : ''}</div>
    </div>
    ${S.devices.selectMode ? bulkBar() : ''}
    ${body}
  </section>`;
}

/** Barra della selezione multipla: quanti sono spuntati, Tutti / Nessuno e le azioni su tutti insieme (fiducia, dimentica). */
function bulkBar() {
  const n = S.devices.selected.size;
  const dis = n ? '' : 'disabled';
  return `<div class="bulk-bar" id="bulk-bar">
    <span class="bulk-count">${n ? `<b>${n}</b> ${n === 1 ? 'selezionato' : 'selezionati'}` : 'Spunta i dispositivi nell\'elenco'}</span>
    <button class="btn btn-sm btn-ghost" data-action="sel-all" title="Spunta tutti i dispositivi visibili">Tutti</button>
    <button class="btn btn-sm btn-ghost" data-action="sel-none" ${dis}>Nessuno</button>
    <span class="spacer"></span>
    <span class="bulk-label muted">Fiducia</span>
    <button class="btn btn-sm" data-action="bulk-trust" data-trust="1" ${dis} title="Segna i selezionati come affidabili">${icon('shield', { size: 14 })} Affidabile</button>
    <button class="btn btn-sm" data-action="bulk-trust" data-trust="2" ${dis} title="Segna i selezionati come sospetti">${icon('alert', { size: 14 })} Sospetto</button>
    <button class="btn btn-sm" data-action="bulk-trust" data-trust="0" ${dis} title="Riporta i selezionati a «da classificare»">Da classificare</button>
    <button class="btn btn-sm btn-danger-ghost" data-action="bulk-forget" ${dis} title="Toglie i selezionati e la loro storia dall'archivio">${icon('trash', { size: 14 })} Dimentica</button>
  </div>`;
}

/** Spunta o toglie la spunta a una riga senza ridisegnare l'elenco; poi aggiorna la barra. */
function toggleSelected(id) {
  const sel = S.devices.selected;
  if (sel.has(id)) sel.delete(id); else sel.add(id);
  const row = $(`.dev-row[data-id="${id}"]`);
  if (row) {
    row.classList.toggle('is-checked', sel.has(id));
    const mark = $('.dev-check', row);
    if (mark) mark.classList.toggle('is-on', sel.has(id));
  }
  refreshBulkBar();
}

function refreshBulkBar() {
  const bar = $('#bulk-bar');
  if (bar) bar.outerHTML = bulkBar();
}

async function bulkTrust(trust) {
  const ids = [...S.devices.selected];
  if (!ids.length) return;
  try {
    S.devices.data = await call('inventoryBulk', { ids, action: 'trust', trust, network: S.devices.network });
    if (S.st) S.st.inventory = S.devices.data.watch;
    rerender();
    if (S.devices.selectedId && ids.includes(S.devices.selectedId)) loadDetail(S.devices.selectedId);
    toast(`${plural(ids.length, 'dispositivo segnato', 'dispositivi segnati')} «${TRUST[trust].label.toLowerCase()}».`, { tone: 'success' });
  } catch (err) { toastError(err, 'Fiducia non salvata'); }
}

async function bulkForget() {
  const ids = [...S.devices.selected];
  if (!ids.length) return;
  const ok = await confirmDialog({ title: `Dimenticare ${plural(ids.length, 'dispositivo', 'dispositivi')}?`, message: 'I dispositivi selezionati e la loro storia spariscono dall\'archivio. Se ricompaiono nella rete vengono segnalati come nuovi.', confirmText: 'Dimentica', tone: 'danger' });
  if (!ok) return;
  try {
    S.devices.data = await call('inventoryBulk', { ids, action: 'forget', network: S.devices.network });
    if (S.st) S.st.inventory = S.devices.data.watch;
    S.devices.selected.clear();
    if (ids.includes(S.devices.selectedId)) { S.devices.selectedId = null; S.devices.detail = null; closeOverlays(); }
    rerender();
    toast(`${plural(ids.length, 'dispositivo dimenticato', 'dispositivi dimenticati')}.`, { tone: 'success' });
    bus.dispatchEvent(new Event('state'));
  } catch (err) { toastError(err); }
}

function portPill(p, sensitive) {
  const bad = sensitive.has(p.port);
  const title = [p.banner, bad ? 'Servizio esposto da verificare' : ''].filter(Boolean).join(' · ');
  return `<span class="pill ${bad ? 'warning' : 'success'}" title="${esc(title)}">${p.port}${p.service ? ` · ${esc(p.service)}` : ''}${bad ? ` ${icon('alert', { size: 12 })}` : ''}</span>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Pannello laterale (come in Rete, ridimensionabile): la scheda del dispositivo oppure quella di un nodo virtuale della
// topologia; si apre dall'elenco, dalla topologia, da un cambiamento o dall'avviso. Un solo pannello alla volta: se è
// già aperto si aggiorna sul posto (titolo, corpo, piede), così la lista dietro non si ridisegna.
// ---------------------------------------------------------------------------------------------------------------------
const drawerEl = () => $('#overlays .drawer[data-dev-drawer]');
/** Il dispositivo nell'elenco caricato (per titolo e icona mentre la scheda arriva). */
const listHost = (id) => ((S.devices.data && S.devices.data.hosts) || []).find((h) => h.id === Number(id)) || null;

function highlightRow() {
  $$('.dev-row').forEach((r) => r.classList.toggle('is-active', !S.devices.selectMode && Number(r.dataset.id) === S.devices.selectedId));
}

/** Apre (o aggiorna) il pannello con quello che c'è nello stato: nodo virtuale, dispositivo in lettura o scheda completa. */
function showDrawer() {
  const parts = S.devices.node ? nodeDrawerParts() : hostDrawerParts();
  if (!parts) { closeOverlays(); return; }
  const open = drawerEl();
  if (open) { updateDrawer(open, parts); return; }
  const d = openDrawer({
    ...parts, resize: 'devices',
    onClose: () => { S.devices.selectedId = null; S.devices.detail = null; S.devices.node = null; highlightRow(); },
  });
  const drawer = $('.drawer', d.wrap);
  if (drawer) { drawer.dataset.devDrawer = '1'; drawer.classList.add('dev-drawer'); }
}

/** La scheda di un dispositivo dell'archivio; seed = i dati già noti (dall'elenco o dalla topologia) per il titolo immediato. */
function openDetail(id, seed = null) {
  S.devices.node = null;
  S.devices.selectedId = Number(id);
  S.devices.detail = null;
  S.devices.saved = false;
  S.devices.topoList = false;
  S.devices.topoFilter = '';
  S.devices.seed = seed;
  highlightRow();
  showDrawer();
  loadDetail(S.devices.selectedId);
}

/** La scheda di un nodo virtuale (nuvola Internet, switch, access point, apparato): nome, tipo, collegamenti. */
function openTopoNode(node, network = topoNetworkKey()) {
  S.devices.selectedId = null;
  S.devices.detail = null;
  S.devices.node = { key: node.key, network };
  S.devices.topoList = true;
  S.devices.topoFilter = '';
  highlightRow();
  showDrawer();
}

function hostDrawerParts() {
  const det = S.devices.detail;
  const id = S.devices.selectedId;
  if (!id) return null;
  const known = (det && det.host) || listHost(id) || S.devices.seed;
  const foot = `<div class="left">
      <button class="btn btn-sm" data-action="host-ports" ${det ? '' : 'disabled'}>${icon('plug')} Scansiona le porte</button>
      <button class="btn btn-sm" data-action="host-tools" ${det ? '' : 'disabled'}>${icon('external')} Apri con… ${icon('chevronDown', { size: 13 })}</button></div>
    <button class="btn btn-sm btn-danger-ghost" data-action="forget-host" ${det ? '' : 'disabled'} title="Toglie il dispositivo e la sua storia dall'archivio: se ricompare torna «nuovo»">${icon('trash')} Dimentica</button>
    <button class="btn btn-sm btn-ghost" data-action="close-overlay">Chiudi</button>`;
  if (!det) {
    return {
      iconHtml: known ? catIcon(known, true) : '', ic: 'monitor', title: known ? known.displayName : 'Dispositivo', sub: known ? known.deviceType : '',
      body: '<div class="loading-block"><span class="spinner"></span> Lettura della scheda…</div>', foot,
    };
  }
  const h = det.host;
  const t = TRUST[h.trust] || TRUST[0];
  const sensitive = new Set((h.sensitivePorts || []).map((s) => Number(String(s).split(' ')[0])));
  const chip = (ic, text, title = '') => `<span class="chip" title="${esc(title || text)}">${icon(ic, { size: 13 })}${esc(text)}</span>`;
  const addrKind = dhcpKind(h.ip, networkOf(h));
  const chips = [
    `<span class="pill ${t.tone} pill-dot" title="Fiducia">${t.label}</span>`,
    chip('mapPin', h.ip),
    ...(addrKind ? [chip('mapPin', addrKind === 'dhcp' ? 'assegnato dal router (DHCP)' : 'indirizzo fisso', `Intervallo DHCP della rete: ${networkOf(h).dhcpRange}`)] : []),
    ...(h.extraIps || []).map((ip) => chip('mapPin', ip, `Altro IPv4 dello stesso dispositivo (stesso MAC): ${ip}`)),
    h.mac ? chip('tag', `${upperMac(h.mac)}${h.vendor ? ` · ${shortVendor(h.vendor)}` : ''}`, `${upperMac(h.mac)}${h.vendor ? ` · ${h.vendor}` : ''}`) : '',
    h.hostName ? chip('dns', h.hostName) : '',
    h.netBios ? chip('windows', `${h.netBios}${h.workgroup ? ` · ${h.workgroup}` : ''}`) : '',
    h.latencyMs != null ? chip('activity', `${fmtNumber(Math.round(h.latencyMs))} ms`) : '',
    h.isGateway ? chip('router', 'gateway') : '',
    h.isLocal ? chip('laptop', 'questa macchina') : '',
    h.networkName ? chip('network', h.networkName, `Rete: ${h.networkName}`) : '',
  ].filter(Boolean).join('');
  const seen = `${h.isOnline ? 'Presente nell\'ultima scansione della sua rete' : `Non visto nell'ultima scansione: ultima volta ${fmtDateTime(h.lastSeen)}`} · conosciuto dal ${fmtDateTime(h.firstSeen)} · visto ${plural(h.seenCount, 'volta', 'volte')}.`;
  const ports = (h.ports || []).slice().sort((a, b) => a.port - b.port);
  const sens = h.sensitivePorts || [];
  const trustBtn = (v) => `<button type="button" class="${h.trust === v ? 'is-active' : ''}" data-action="trust" data-trust="${v}">${TRUST[v].label}</button>`;
  const history = det.history || [];
  const body = `<div class="fields">
      <div class="chips">${chips}</div>
      <p class="card-text muted">${seen}</p>
      ${sens.length ? note('warning', 'alert', `<b>Da verificare:</b> ${sens.map(esc).join(', ')}. Servizi esposti in rete senza cifratura o con storia di abusi: se non servono, spegnili o limitali.`) : ''}
      <div class="form-grid">
        <div class="field span-2"><label for="d-alias">Alias</label><input class="input" id="d-alias" data-field="alias" value="${esc(h.alias || '')}" placeholder="Es. NAS ufficio, TV soggiorno" autocomplete="off"></div>
        <div class="field span-2"><span class="field-label">Fiducia</span><div class="segmented trust-seg">${trustBtn(0)}${trustBtn(1)}${trustBtn(2)}</div></div>
        <div class="field span-2"><label for="d-notes">Note</label><textarea class="input" id="d-notes" data-field="notes" rows="2" placeholder="Dove sta, a chi appartiene, cosa fa">${esc(h.notes || '')}</textarea></div>
      </div>
      <div class="row"><button class="btn btn-primary btn-sm" data-action="save-host">${icon('save')} Salva</button><span class="field-status ok ${S.devices.saved ? '' : 'hidden'}" id="dev-saved">${icon('checkCircle')} Salvato</span></div>
      ${topoSection(h.key, h.network)}
      <div><div class="form-section-head"><div class="form-section-title">${icon('plug')}Porte aperte</div><span class="muted" style="font-size:12.5px">${ports.length ? plural(ports.length, 'porta', 'porte') : ''}</span></div>
        ${ports.length ? `<div class="pill-list">${ports.map((p) => portPill(p, sensitive)).join('')}</div>` : '<p class="card-text muted">Nessuna porta aperta tra quelle provate.</p>'}</div>
      <div><div class="form-section-head"><div class="form-section-title">${icon('history')}Storia</div></div>
        ${history.length ? `<div class="ev-list compact">${history.map(eventRow).join('')}</div>` : '<p class="card-text muted">Nessun evento registrato.</p>'}</div>
    </div>`;
  return { iconHtml: catIcon(h, true), title: h.displayName, sub: `${h.deviceType}${h.osGuess ? ` · ${h.osGuess}` : ''}`, body, foot };
}

/** Il nodo virtuale aperto nel pannello, dai dati della topologia della sua rete (null se non c'è più). */
function openNode() {
  const n = S.devices.node;
  if (!n) return null;
  const data = S.devices.topo.byNet.get(n.network);
  return data ? (data.nodes || []).find((x) => x.key === n.key) || null : null;
}

function nodeDrawerParts() {
  const n = S.devices.node;
  const data = S.devices.topo.byNet.get(n.network);
  if (!data) { ensureTopo(n.network); return { ic: 'network', title: 'Nodo', sub: '', body: '<div class="loading-block"><span class="spinner"></span> Lettura della topologia…</div>', foot: '<button class="btn btn-sm btn-ghost" data-action="close-overlay">Chiudi</button>' }; }
  const node = openNode();
  if (!node) return null;
  const k = TOPO_KINDS[node.kind] || TOPO_KINDS.other;
  const kindOpt = (kind) => `<option value="${kind}" ${kind === node.kind ? 'selected' : ''}>${esc(kind === 'internet' ? 'Nuvola Internet' : TOPO_KINDS[kind].label)}</option>`;
  const body = `<div class="fields">
      <p class="card-text muted">${node.kind === 'internet' ? 'La nuvola Internet: ci sta sotto il router (o i router). Non la vede la scansione: è un segnaposto del disegno.' : 'Un apparato che la scansione non vede (non ha un indirizzo, o non risponde): serve a disegnare la rete com\'è davvero. Spunta qui sotto i dispositivi collegati alle sue porte.'}</p>
      <div class="form-grid">
        <div class="field ${node.kind === 'internet' ? 'span-2' : ''}"><label for="n-name">Nome</label><input class="input" id="n-name" maxlength="80" value="${esc(node.name)}" placeholder="${esc(k.label)}" autocomplete="off"></div>
        ${node.kind === 'internet' ? '' : `<div class="field"><label for="n-kind">Tipo</label><select class="select" id="n-kind">${['switch', 'ap', 'other'].map(kindOpt).join('')}</select></div>`}
      </div>
      <div class="row"><button class="btn btn-primary btn-sm" data-action="node-save">${icon('save')} Salva</button><span class="field-status ok ${S.devices.saved ? '' : 'hidden'}" id="dev-saved">${icon('checkCircle')} Salvato</span></div>
      ${topoSection(node.key, n.network)}
    </div>`;
  const foot = `<div class="left">${node.topoX != null ? `<button class="btn btn-sm" data-action="node-unpin" title="Torna al posto automatico nel disegno">${icon('refresh')} Posto automatico</button>` : ''}</div>
    <button class="btn btn-sm btn-danger-ghost" data-action="node-delete" title="Toglie il nodo dal disegno: chi ci stava sotto passa al nodo sopra di lui">${icon('trash')} Elimina</button>
    <button class="btn btn-sm btn-ghost" data-action="close-overlay">Chiudi</button>`;
  return { ic: k.icon, iconHtml: '', title: node.name, sub: node.kind === 'internet' ? 'Nuvola Internet' : k.label, body, foot };
}

// ---------------------------------------------------------------------------------------------------------------------
// Sezione «Topologia» della scheda (dispositivo o nodo virtuale): «Collegato a» con tutti i nodi possibili (o un nuovo
// switch / access point da creare lì), e l'elenco di chi sta collegato sotto, con la spunta per attaccare o staccare
// ---------------------------------------------------------------------------------------------------------------------
const topoCollator = new Intl.Collator('it', { numeric: true, sensitivity: 'base' });
const storedUplinkOf = (c) => (c.host ? c.host.uplink : c.node.uplink) || '';
const topoRank = (c) => (c.state === 'node' ? (c.node.kind === 'internet' ? 0 : 1) : c.host && c.host.isGateway ? 2 : 3);
/** La rete della scheda Topologia (la chiave risolta dal programma, se i dati sono arrivati). */
const topoNetworkKey = () => (S.devices.topo.data && S.devices.topo.data.network) || S.devices.network;

/** Legge la topologia di una rete per la scheda, una volta sola; poi la sezione si ridisegna. */
async function ensureTopo(network) {
  const t = S.devices.topo;
  if (!network || t.byNet.has(network) || t.pending.has(network)) return;
  t.pending.add(network);
  try {
    const data = await call('topology', { network });
    t.byNet.set(network, data);
    if (data.network !== network) t.byNet.set(data.network, data);
    if (S.devices.node) showDrawer(); else refreshTopoSection();
  } catch (err) {
    const box = $('#topo-section');
    if (box) box.innerHTML = `<div class="form-section-head"><div class="form-section-title">${icon('network')}Topologia</div></div><p class="card-text muted">${esc(err.message)}</p>`;
  }
  t.pending.delete(network);
}

function refreshTopoSection() {
  const box = $('#topo-section');
  if (!box) return;
  const self = S.devices.node ? S.devices.node : S.devices.detail ? { key: S.devices.detail.host.key, network: S.devices.detail.host.network } : null;
  if (self) box.outerHTML = topoSection(self.key, self.network);
}

function topoSection(selfKey, network) {
  const head = (extra = '') => `<div class="form-section-head"><div class="form-section-title">${icon('network')}Topologia</div>${extra}</div>`;
  const data = S.devices.topo.byNet.get(network);
  if (!data) { ensureTopo(network); return `<div id="topo-section">${head()}<p class="card-text muted">Lettura…</p></div>`; }
  const nodes = topoNodes(data);
  const { kids, byKey } = topologyTree(nodes);
  const me = byKey.get(selfKey);
  if (!me) return `<div id="topo-section">${head()}<p class="card-text muted">Questo dispositivo non è nel disegno della sua rete.</p></div>`;
  const branch = branchOf(selfKey, kids);
  const ancestors = new Set();
  for (let c = me.uplink; c && byKey.has(c) && !ancestors.has(c); c = byKey.get(c).uplink) ancestors.add(c);
  const stored = storedUplinkOf(me);
  const storedValid = stored && byKey.has(stored) ? stored : '';
  const byName = (a, b) => topoRank(a) - topoRank(b) || topoCollator.compare(a.name, b.name);
  const label = (c) => `${c.name}${c.sub && c.sub !== c.name ? ` · ${c.sub}` : ''}`;

  // «Collegato a»: tutti tranne se stesso e il suo ramo; la nuvola Internet non ha nulla sopra
  let uplinkField = '';
  if (!(me.node && me.node.kind === 'internet')) {
    const candidates = [...byKey.values()].filter((c) => c.key !== selfKey && !branch.has(c.key)).sort(byName);
    const opt = (c) => `<option value="${esc(c.key)}" ${c.key === storedValid ? 'selected' : ''}>${esc(label(c))}</option>`;
    const virtual = candidates.filter((c) => c.state === 'node');
    const devices = candidates.filter((c) => c.state !== 'node');
    const autoLabel = me.uplink && !storedValid && byKey.has(me.uplink) ? `Automatico (sotto ${byKey.get(me.uplink).name})` : 'Automatico';
    uplinkField = `<div class="field"><label for="d-uplink">Collegato a</label>
      <select class="select" id="d-uplink" data-field="topo-uplink" title="Il nodo a monte nel disegno della rete: lo switch, l'access point o il dispositivo a cui è attaccato. Si salva subito.">
        <option value="" ${storedValid ? '' : 'selected'}>${esc(autoLabel)}</option>
        ${virtual.length ? `<optgroup label="Apparati">${virtual.map(opt).join('')}</optgroup>` : ''}
        ${devices.length ? `<optgroup label="Dispositivi">${devices.map(opt).join('')}</optgroup>` : ''}
        <optgroup label="Nuovo apparato, qui in mezzo"><option value="new:switch">Nuovo switch…</option><option value="new:ap">Nuovo access point…</option><option value="new:other">Nuovo apparato…</option></optgroup>
      </select></div>`;
  }

  // «Collegati qui»: chi può stare sotto (non se stesso, non chi gli sta sopra); già sotto = spuntato
  const under = [...byKey.values()].filter((c) => c.key !== selfKey && !ancestors.has(c.key));
  const direct = under.filter((c) => c.uplink === selfKey);
  const q = S.devices.topoFilter.trim().toLowerCase();
  const rows = under
    .filter((c) => !q || label(c).toLowerCase().includes(q))
    .sort((a, b) => (b.uplink === selfKey) - (a.uplink === selfKey) || byName(a, b));
  const row = (c) => {
    const on = c.uplink === selfKey;
    const auto = on && storedUplinkOf(c) !== selfKey;
    const where = !on && c.uplink && byKey.has(c.uplink) ? ` · sotto ${byKey.get(c.uplink).name}` : '';
    const tile = c.host ? catIcon(c.host) : icon((TOPO_KINDS[c.node.kind] || TOPO_KINDS.other).icon);
    return `<label class="topo-pick ${on ? 'is-on' : ''} ${auto ? 'is-auto' : ''}" title="${auto ? 'Collegato qui in automatico (nessuna scelta salvata): per spostarlo spuntalo nella scheda di un altro nodo o cambia il suo «Collegato a»' : on ? 'Togli la spunta per staccarlo (torna al posto automatico)' : `Spunta per collegarlo qui${where ? ` (adesso è${where})` : ''}`}">
      <input type="checkbox" data-field="topo-child" value="${esc(c.key)}" ${on ? 'checked' : ''} ${auto ? 'disabled' : ''}>${tile}
      <span class="topo-pick-text"><b class="trunc">${esc(c.name)}</b><small class="trunc">${esc(c.sub || '')}${auto ? ' · automatico' : ''}${esc(where)}</small></span></label>`;
  };
  const open = S.devices.topoList;
  const count = direct.length ? `<b>${direct.length}</b> ${direct.length === 1 ? 'collegato qui' : 'collegati qui'}` : 'Nessun dispositivo collegato qui';
  const list = open
    ? `<div class="topo-pick-head">${under.length > 8 ? `<div class="search">${icon('search')}<input class="input input-sm" data-field="topo-filter" value="${esc(S.devices.topoFilter)}" placeholder="Cerca tra i dispositivi" autocomplete="off"></div>` : `<span>${count}</span>`}
        <button class="btn btn-sm btn-ghost" data-action="topo-list" title="Chiude l'elenco">${icon('chevronDown', { size: 14 })} Chiudi</button></div>
      <div class="topo-pick-list" id="topo-pick-list">${rows.length ? rows.map(row).join('') : '<p class="card-text muted" style="padding:10px 12px">Nessun dispositivo corrisponde.</p>'}</div>`
    : `<div class="topo-pick-head"><span>${count}</span><button class="btn btn-sm" data-action="topo-list" title="Elenco di tutti i dispositivi della rete: spunta quelli collegati a questo nodo">${icon('link', { size: 14 })} Scegli i collegati…</button></div>`;
  return `<div id="topo-section">${head(`<span class="muted" style="font-size:12.5px">${esc(data.name || '')}</span>`)}
    <div class="fields" style="gap:12px">${uplinkField}<div class="field"><span class="field-label">Collegati qui</span>${list}</div></div></div>`;
}

/** «Collegato a» scelto nella scheda: un nodo esistente, nessuno (automatico) oppure un nuovo apparato da mettere in mezzo. */
async function pickUplink(selfKey, network, value) {
  const data = S.devices.topo.byNet.get(network);
  const nodes = data ? topoNodes(data) : [];
  const me = nodes.find((n) => n.key === selfKey);
  const name = me ? me.name : 'Il nodo';
  if (value.startsWith('new:')) {
    const kind = value.slice(4);
    const k = TOPO_KINDS[kind] || TOPO_KINDS.other;
    const v = await promptDialog({ title: `Nuovo ${k.label.toLowerCase()} sopra ${name}`, sub: 'Prende il posto che aveva il dispositivo nel disegno, e il dispositivo si attacca a lui', label: 'Nome', value: k.label, placeholder: 'Es. Switch armadio, AP piano terra', ic: k.icon, confirmText: 'Crea e collega' });
    if (v == null) { refreshTopoSection(); return; }
    const title = v.trim() || k.label;
    await topoCall('topologyNodeSave', { kind, name: title, uplink: me ? storedUplinkOf(me) : '', attach: selfKey }, `«${title}» creato: ${name} è collegato lì, e «${title}» sta dove stava ${name}.`, network);
    return;
  }
  const target = nodes.find((n) => n.key === value);
  await topoCall('topologyLink', { key: selfKey, uplink: value }, value ? `${name} collegato a ${target ? target.name : value}.` : `${name}: torna al collegamento automatico.`, network);
}

/** Spunta tolta o messa nell'elenco «Collegati qui». */
async function pickChild(selfKey, network, childKey, on) {
  const data = S.devices.topo.byNet.get(network);
  const nodes = data ? topoNodes(data) : [];
  const me = nodes.find((n) => n.key === selfKey);
  const child = nodes.find((n) => n.key === childKey);
  const cn = child ? child.name : childKey;
  await topoCall('topologyLink', { key: childKey, uplink: on ? selfKey : '' }, on ? `${cn} collegato a ${me ? me.name : selfKey}.` : `${cn} staccato: torna al collegamento automatico.`, network);
}

/** Nome e tipo del nodo virtuale aperto. */
async function saveNode() {
  const node = openNode();
  if (!node) return;
  const name = ($('#n-name') || {}).value || '';
  const kind = ($('#n-kind') || {}).value || node.kind;
  const data = await topoCall('topologyNodeSave', { id: node.id, kind, name: name.trim() }, null, S.devices.node.network);
  if (data) { S.devices.saved = true; showDrawer(); toast(`Nodo «${name.trim() || node.name}» salvato.`, { tone: 'success' }); }
}

// ---------------------------------------------------------------------------------------------------------------------
// Scheda Cambiamenti e scheda Scansioni
// ---------------------------------------------------------------------------------------------------------------------
// ---------------------------------------------------------------------------------------------------------------------
// Scheda Topologia: i dispositivi dell'archivio ad albero (ui/core/topology.js) con i nodi virtuali (nuvola Internet,
// switch, access point, altro apparato), i collegamenti «Collegato a» e le posizioni, salvati nell'archivio per rete
// ---------------------------------------------------------------------------------------------------------------------
const TOPO_KINDS = {
  internet: { label: 'Internet', icon: 'cloud' },
  switch: { label: 'Switch', icon: 'network' },
  ap: { label: 'Access point', icon: 'wifi' },
  other: { label: 'Apparato', icon: 'server' },
};

function topologyCard() {
  const t = S.devices.topo;
  return `<section class="card topo-card">
    <div class="topo-bar">
      <button class="btn btn-sm btn-primary" data-action="topo-add">${icon('plus', { size: 14 })} Aggiungi ${icon('chevronDown', { size: 13 })}</button>
      <button class="btn btn-sm" data-action="topo-reset" title="Rimette ogni dispositivo al suo posto nell'albero; i collegamenti restano">${icon('refresh', { size: 14 })} Disposizione automatica</button>
      <span class="muted" style="font-size:12.5px">Trascina un dispositivo per spostarlo (con Maiusc solo lui), lascialo sopra un altro per collegarlo lì; tasto destro per le altre azioni.</span>
      <span class="topo-zoom" role="group" aria-label="Ingrandimento">
        <button class="icon-btn" type="button" data-action="topo-zoom" data-zoom="-1" title="Rimpicciolisci (o la rotellina)">${icon('minus', { size: 16 })}</button>
        <button class="btn btn-sm btn-ghost" type="button" data-action="topo-zoom" data-zoom="0" title="Dimensione normale" data-zoom-label>${Math.round(t.zoom * 100)}%</button>
        <button class="icon-btn" type="button" data-action="topo-zoom" data-zoom="1" title="Ingrandisci (o la rotellina)">${icon('plus', { size: 16 })}</button>
        <button class="icon-btn" type="button" data-action="topo-zoom" data-zoom="fit" title="Adatta: tutta la rete nella vista">${icon('maximize', { size: 16 })}</button>
      </span>
    </div>
    <div class="topo" id="topo-host"></div>
    <div class="topo-foot"><span class="topo-legend"><span><i class="topo-swatch is-on"></i>Risponde</span><span><i class="topo-swatch is-off"></i>Non risponde</span><span><i class="topo-swatch"></i>Nodo virtuale</span></span></div>
  </section>`;
}

/** I nodi del disegno dai dati del programma: dispositivi e nodi virtuali, con il nodo a monte effettivo. */
function topoNodes(data) {
  const hosts = data.hosts || [];
  const virtual = data.nodes || [];
  const keys = new Set([...hosts.map((h) => h.key), ...virtual.map((n) => n.key)]);
  const gw = hosts.find((h) => h.isGateway);
  const inet = virtual.find((n) => n.kind === 'internet');
  const valid = (k) => (k && keys.has(k) ? k : null);
  // senza un collegamento scelto: i dispositivi e gli apparati sotto il router, il router sotto la nuvola Internet (se c'è)
  const fallback = (self) => {
    if (gw && self !== gw.key) return gw.key;
    return inet && self !== inet.key ? inet.key : null;
  };
  const out = hosts.map((h) => ({
    key: h.key, uplink: valid(h.uplink) || (h.isGateway ? (inet ? inet.key : null) : fallback(h.key)),
    name: h.displayName, sub: h.ip, state: h.isOnline ? 'on' : 'off', tile: catIcon(h, true), x: h.topoX, y: h.topoY, host: h,
  }));
  virtual.forEach((n) => {
    const k = TOPO_KINDS[n.kind] || TOPO_KINDS.other;
    out.push({ key: n.key, uplink: valid(n.uplink) || (n.kind === 'internet' ? null : fallback(n.key)), name: n.name, sub: k.label, state: 'node', tile: icon(k.icon, { size: 26 }), x: n.topoX, y: n.topoY, node: n });
  });
  return out;
}

function updateZoomLabel() {
  const l = $('[data-zoom-label]');
  if (l) l.textContent = `${Math.round(S.devices.topo.zoom * 100)}%`;
}

/** Disegna (o ridisegna) la topologia; keep tiene scorrimento e riquadro, altrimenti adatta tutto alla vista. */
function drawTopology({ keep = false } = {}) {
  const host = $('#topo-host');
  const t = S.devices.topo;
  if (!host || !t.data) return;
  if (t.ctl && t.ctl.busy()) return;
  const nodes = topoNodes(t.data);
  if (!nodes.length) {
    t.scene = null;
    host.innerHTML = emptyState('network', 'Nessun dispositivo da disegnare', 'Avvia una scansione: i dispositivi dell\'archivio di questa rete compaiono qui ad albero.');
    return;
  }
  const before = keep && t.scene ? t.scene.frame : null;
  const left = host.scrollLeft;
  const top = host.scrollTop;
  t.scene = renderTopology(host, { nodes, collapsed: t.collapsed, zoom: t.zoom, moved: t.moved, keep: before });
  if (before) { host.scrollLeft = left; host.scrollTop = top; } else {
    t.zoom = setTopologyZoom(host, t.scene, fitZoom(host, t.scene));
    centerTopology(host, t.scene);
    updateZoomLabel();
  }
}

/** La risposta di «topology» entra nella memoria per rete e, se è la rete della scheda Topologia, nel disegno. */
function adoptTopology(data, { keep = true } = {}) {
  const t = S.devices.topo;
  t.byNet.set(data.network, data);
  const shown = t.data ? t.data.network : topoNetworkKey();
  if (!shown || shown === '*' || shown === data.network) {
    t.data = data;
    t.moved.clear();
    drawTopology({ keep });
  }
}

async function loadTopology() {
  const t = S.devices.topo;
  if (t.loading) return;
  t.loading = true;
  try {
    adoptTopology(await call('topology', { network: S.devices.network }), { keep: false });
  } catch (err) {
    const host = $('#topo-host');
    if (host) host.innerHTML = emptyState('alert', 'Topologia non disponibile', err.message);
  }
  t.loading = false;
}

/**
 * Una modifica (collegamento, spostamento, nodo) sulla rete indicata e il disegno aggiornato dal programma; aggiorna anche
 * la sezione Topologia del pannello aperto. Restituisce i dati nuovi, null se la modifica è stata rifiutata.
 */
async function topoCall(cmd, args, done, network = S.devices.network) {
  try {
    const data = await call(cmd, { network, ...args });
    adoptTopology(data);
    if (network && network !== data.network) S.devices.topo.byNet.set(network, data);
    if (S.devices.node) showDrawer(); else refreshTopoSection();
    if (done) toast(done, { tone: 'success' });
    return data;
  } catch (err) {
    toastError(err);
    if (S.devices.node) showDrawer(); else refreshTopoSection();
    loadTopology();
    return null;
  }
}

/** Dopo ogni ridisegno della pagina: eventi sul contenitore nuovo, poi i dati (quelli di prima subito, se ci sono). */
function mountTopology() {
  const host = $('#topo-host');
  if (!host) return;
  const t = S.devices.topo;
  t.scene = null;
  t.ctl = bindTopology(host, () => t.scene, {
    onOpen: (n) => { if (n.host) openDetail(n.host.id, n.host); else openTopoNode(n.node); },
    onMenu: (n, point) => topoMenu(n, point),
    onToggle: (key) => { if (t.collapsed.has(key)) t.collapsed.delete(key); else t.collapsed.add(key); drawTopology({ keep: true }); },
    onLink: (n, uplink) => topoCall('topologyLink', { key: n.key, uplink }, `${n.name} collegato a ${(t.scene.byKey.get(uplink) || { name: '' }).name}.`),
    onMove: (moves) => { moves.forEach((m) => t.moved.set(m.key, { x: m.x, y: m.y })); drawTopology({ keep: true }); topoCall('topologyMove', { moves }, null); },
    onZoom: (z) => { t.zoom = z; updateZoomLabel(); },
    onIdle: () => drawTopology({ keep: true }),
  });
  host.innerHTML = '<div class="loading-block"><span class="spinner"></span> Lettura della topologia…</div>';
  if (t.data && (t.data.network === S.devices.network || !S.devices.network || S.devices.network === '*')) drawTopology();
  loadTopology();
}

function topoMenu(n, point) {
  const stored = n.host ? n.host.uplink : n.node.uplink;
  const placed = n.host ? n.host.topoX != null : n.node.topoX != null;
  const items = [{ header: n.name }];
  if (n.host) items.push({ ic: 'server', label: 'Apri la scheda', run: () => openDetail(n.host.id, n.host) });
  else items.push({ ic: 'edit', label: 'Apri la scheda (nome, collegati)', run: () => openTopoNode(n.node) });
  items.push({ ic: 'link', label: 'Collega a…', run: () => topoLinkMenu(n, point) });
  if (stored) items.push({ ic: 'unlink', label: 'Scollega (collegamento automatico)', run: () => topoCall('topologyLink', { key: n.key, uplink: '' }, `${n.name}: collegamento automatico.`) });
  if (placed) items.push({ ic: 'refresh', label: 'Riporta nella disposizione automatica', run: () => topoCall('topologyMove', { moves: [{ key: n.key, x: null, y: null }] }, null) });
  if (n.node) items.push({ sep: true }, { ic: 'trash', label: 'Elimina il nodo', run: () => deleteTopoNode(n.node) });
  openMenu(point, items);
}

/** «Collega a…»: tutti i nodi a cui si può collegare (non se stesso, non il suo ramo): prima i nodi virtuali e il router. */
function topoLinkMenu(n, point) {
  const t = S.devices.topo;
  if (!t.scene) return;
  const branch = branchOf(n.key, t.scene.kids);
  const rank = (c) => (c.state === 'node' ? 0 : c.host && c.host.isGateway ? 1 : 2);
  const candidates = [...t.scene.byKey.values()]
    .filter((c) => c.key !== n.key && !branch.has(c.key) && c.key !== n.uplink)
    .sort((a, b) => rank(a) - rank(b) || a.name.localeCompare(b.name));
  if (!candidates.length) { toast('Nessun altro nodo a cui collegarlo.', { tone: 'info' }); return; }
  openMenu(point, [{ header: `Collega ${n.name} a…` }, ...candidates.map((c) => ({
    ic: c.state === 'node' ? (TOPO_KINDS[c.node.kind] || TOPO_KINDS.other).icon : 'server', label: c.name, hint: c.sub,
    run: () => topoCall('topologyLink', { key: n.key, uplink: c.key }, `${n.name} collegato a ${c.name}.`),
  }))]);
}

function topoAddMenu(el) {
  openMenu(el, [{ header: 'Aggiungi alla topologia' }, ...Object.entries(TOPO_KINDS).map(([kind, k]) => ({
    ic: k.icon, label: kind === 'internet' ? 'Nuvola Internet' : k.label, run: () => addTopoNode(kind),
  }))]);
}

async function addTopoNode(kind) {
  const k = TOPO_KINDS[kind];
  let name = k.label;
  if (kind !== 'internet') {
    const v = await promptDialog({ title: `Nuovo nodo: ${k.label.toLowerCase()}`, label: 'Nome', value: k.label, placeholder: 'Es. Switch armadio, AP piano terra', ic: k.icon, confirmText: 'Aggiungi' });
    if (v == null) return;
    name = v.trim() || k.label;
  }
  await topoCall('topologyNodeSave', { kind, name }, `«${name}» aggiunto: trascina i dispositivi sopra per collegarli.`);
}

async function deleteTopoNode(node, network = S.devices.network) {
  const ok = await confirmDialog({ title: `Eliminare «${node.name}»?`, message: 'I dispositivi collegati sotto passano al nodo sopra di lui (o al posto automatico).', confirmText: 'Elimina', tone: 'danger' });
  if (!ok) return;
  if (S.devices.node && S.devices.node.key === node.key) { S.devices.node = null; closeOverlays(); }
  await topoCall('topologyNodeDelete', { id: node.id }, `«${node.name}» eliminato: chi ci stava sotto è passato al nodo sopra.`, network);
}

// ---------------------------------------------------------------------------------------------------------------------
// Scheda Cambiamenti e scheda Scansioni
// ---------------------------------------------------------------------------------------------------------------------
function eventRow(e) {
  return `<button class="ev-row ${e.acknowledged ? '' : 'is-unseen'}" data-action="open-event" data-id="${e.id}" data-host="${e.hostId || ''}">
    <span class="pill ${KIND[e.kind] || ''}">${esc(e.kind)}</span>
    <span class="ev-text"><b class="trunc" title="${esc(e.title)}">${esc(e.title)}</b><small class="trunc" title="${esc(e.details)}">${esc(e.details)}</small></span>
    <span class="ev-time">${fmtShort(e.at)}</span>
  </button>`;
}

function eventsCard() {
  const events = S.devices.data.events || [];
  const unseen = events.filter((e) => !e.acknowledged).length;
  return `<section class="card table-card"><div class="card-head"><div class="card-title">${icon('history')}<h2>Cambiamenti</h2><span class="count">${events.length}</span>${unseen ? `<span class="pill warning">${plural(unseen, 'da vedere', 'da vedere')}</span>` : ''}</div>
      <button class="btn btn-sm" data-action="ack-all" ${unseen ? '' : 'disabled'}>${icon('check')} Segna tutto come visto</button></div>
    ${events.length ? `<div class="ev-list">${events.map(eventRow).join('')}</div>` : emptyState('history', 'Nessun cambiamento', 'Dalla seconda scansione di questa rete in poi compaiono qui i dispositivi nuovi, ricomparsi, cambiati o assenti.')}
    <div class="table-foot">${icon('info', { size: 14 })} Nuovo = mai visto prima in questa rete · Ricomparso = tornato dopo essere stato assente · Cambiato = IP, nome o porte diversi · Assente = non risponde da ${2} scansioni di questa rete. Un clic apre la scheda del dispositivo.</div>
  </section>`;
}

function scansCard() {
  const scans = S.devices.data.scans || [];
  const columns = [
    { key: 'startedAt', label: 'Data', cell: (s) => fmtDateTime(s.startedAt) },
    { key: 'target', label: 'Rete', cls: 'mono' },
    { key: 'hostsCount', label: 'Host', cls: 'num' },
    { key: 'durationMs', label: 'Durata', cls: 'num', cell: (s) => fmtSeconds(s.durationMs / 1000) },
    { key: 'changes', label: 'Differenze', sortable: false, cell: (s) => `<span class="pill-list">${s.newCount ? `<span class="pill warning">+${s.newCount} nuovi</span>` : ''}${s.changedCount ? `<span class="pill primary">~${s.changedCount} cambiati</span>` : ''}${s.goneCount ? `<span class="pill">−${s.goneCount} assenti</span>` : ''}${!s.newCount && !s.changedCount && !s.goneCount ? '<span class="faint">nessuna</span>' : ''}</span>` },
    { key: 'summary', label: 'Riepilogo', sortable: false, cell: (s) => `<span class="trunc w-summary" title="${esc(s.summary)}">${esc(s.summary)}</span>` },
  ];
  return `<section class="card table-card"><div class="card-head"><div class="card-title">${icon('clock')}<h2>Scansioni</h2><span class="count">${scans.length}</span></div></div>
    ${table({ columns, rows: scans, sort: null, cls: 'list-h', empty: emptyState('clock', 'Nessuna scansione registrata', 'Premi «Scansiona adesso» oppure avvia una scansione dalla pagina Rete.') })}
  </section>`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Pagina
// ---------------------------------------------------------------------------------------------------------------------
function headText() {
  const d = S.devices.data;
  if (!d) return 'Dispositivi visti nel tempo, rete per rete, con confronto tra le scansioni e avvisi sui cambiamenti.';
  return allNetworks() ? `Tutte le reti: ${d.digest}` : `«${shownNetworkName()}»: ${d.digest}`;
}

function render() {
  const d = S.devices.data;
  const head = pageHead(esc(headText()), `<span id="inv-scan-box">${scanControl()}</span>`);
  if (!d) return `${head}<div class="card"><div class="loading-block"><span class="spinner"></span> Lettura dell'archivio…</div></div>`;
  const unseen = (d.events || []).filter((e) => !e.acknowledged).length;
  const tab = (key, ic, label, extra = '') => `<button class="tab ${S.devices.tab === key ? 'is-active' : ''}" data-action="tab" data-tab="${key}">${icon(ic)}${label}${extra}</button>`;
  const tabs = `<div class="tabs">${tab('hosts', 'server', 'Dispositivi', ` <span class="count">${d.total}</span>`)}${tab('topology', 'network', 'Topologia')}${tab('events', 'history', 'Cambiamenti', unseen ? ` <span class="count is-warning">${unseen}</span>` : '')}${tab('scans', 'clock', 'Scansioni', ` <span class="count">${(d.scans || []).length}</span>`)}</div>`;
  let body;
  if (S.devices.tab === 'events') body = eventsCard();
  else if (S.devices.tab === 'scans') body = scansCard();
  else if (S.devices.tab === 'topology') body = topologyCard();
  else body = listCard();
  return `${head}${networkBar()}${tabs}${body}<p class="page-foot muted">${icon('info', { size: 14 })} ${esc(footerText())}</p>`;
}

function rerender() {
  if (S.page !== 'devices') return;
  const content = $('#content');
  // la lista dei dispositivi scorre dentro la sua scheda: dopo un ridisegno deve restare dov'era
  if (content) replaceKeepingScroll(content, render(), '.dev-list');
  if (S.devices.tab === 'topology') mountTopology();
}

function renderList() {
  const box = $('#dev-list');
  if (box && S.devices.data) box.innerHTML = renderDeviceRows(visibleDeviceHosts()); else rerender();
}

/** La scheda nel pannello si aggiorna solo se il pannello è aperto (non si apre da solo dopo una scansione). */
function renderDetail() {
  if (drawerEl() && (S.devices.selectedId || S.devices.node)) showDrawer();
}

/** Legge l'archivio della rete scelta; la risposta dice quale rete è stata risolta ('' diventa quella del PC). */
async function fetchInventory() {
  const s = S.devices;
  const data = await call('inventory', { query: s.q, onlyOnline: s.onlyOnline, network: s.network });
  s.network = data.network;
  if (S.st) S.st.inventory = data.watch;
  return data;
}

async function load() {
  const s = S.devices;
  if (s.loading) return;
  s.loading = true;
  try {
    s.data = await fetchInventory();
    s.topo.byNet.clear(); // i dati della topologia (presenza, nomi) si rileggono alla prossima scheda
    const ids = new Set(s.data.hosts.map((h) => h.id));
    for (const id of [...s.selected]) if (!ids.has(id)) s.selected.delete(id);
    rerender();
    if (s.pendingHost) {
      const id = s.pendingHost;
      s.pendingHost = null;
      s.tab = 'hosts';
      if (ids.has(id)) openDetail(id); else toast('Il dispositivo non è più nell\'archivio.', { tone: 'warning' });
    } else if (s.selectedId && drawerEl()) await loadDetail(s.selectedId);
  } catch (err) { toastError(err, 'Archivio non letto'); }
  s.loading = false;
}

async function loadDetail(id) {
  try {
    const det = await call('inventoryHost', { id });
    if (S.devices.selectedId !== id) return;
    S.devices.detail = det;
    S.devices.saved = false;
    renderDetail();
  } catch (err) {
    S.devices.selectedId = null;
    S.devices.detail = null;
    closeOverlays();
    toastError(err);
    load();
  }
}

async function save() {
  const det = S.devices.detail;
  if (!det) return;
  const alias = ($('#d-alias') || {}).value || '';
  const notes = ($('#d-notes') || {}).value || '';
  const trust = Number(($('.trust-seg .is-active') || { dataset: {} }).dataset.trust || 0);
  try {
    S.devices.detail = await call('inventorySave', { id: det.host.id, alias, notes, trust });
    S.devices.saved = true;
    renderDetail();
    await refreshList();
    toast(`Scheda di ${S.devices.detail.host.displayName} salvata.`, { tone: 'success' });
  } catch (err) { toastError(err, 'Scheda non salvata'); }
}

/** Ricarica elenco e digest senza perdere la scheda aperta. */
async function refreshList() {
  const s = S.devices;
  try {
    s.data = await fetchInventory();
    renderList();
    const info = $('.toolbar-info');
    if (info) info.textContent = s.data.hosts.length === s.data.total ? plural(s.data.total, 'dispositivo', 'dispositivi') : `${s.data.hosts.length} di ${s.data.total}`;
    const sub = $('.page-sub');
    if (sub) sub.textContent = headText();
  } catch (err) { toastError(err); }
}

async function forget() {
  const det = S.devices.detail;
  if (!det) return;
  const ok = await confirmDialog({ title: `Dimenticare ${det.host.displayName}?`, message: 'Il dispositivo e la sua storia spariscono dall\'archivio. Se ricompare nella rete viene segnalato come nuovo.', confirmText: 'Dimentica', tone: 'danger' });
  if (!ok) return;
  try {
    await call('inventoryForget', { id: det.host.id, network: S.devices.network });
    S.devices.selectedId = null;
    S.devices.detail = null;
    closeOverlays();
    toast(`${det.host.displayName} e la sua storia sono stati tolti dall'archivio: se ricompare nella rete sarà segnalato come nuovo.`, { tone: 'success', title: 'Dispositivo dimenticato' });
    load();
  } catch (err) { toastError(err); }
}

/** «Rinomina»: il nome della rete (il cliente, la sede) nell'archivio; lo prende anche il file della sessione. */
async function renameNetwork() {
  const d = S.devices.data;
  if (!d || d.network === '*') return;
  const n = shownNetwork();
  const name = await promptDialog({
    title: 'Nome della rete', sub: n ? n.label : shownNetworkName(), label: 'Nome', value: (n && n.alias) || '',
    placeholder: 'Es. Cliente Rossi, Casa, Sede di Milano', ic: 'network', confirmText: 'Salva',
  });
  if (name == null) return;
  try {
    S.devices.data = await call('inventoryNetwork', { key: d.network, alias: name.trim() });
    S.devices.network = S.devices.data.network;
    if (S.st) S.st.inventory = S.devices.data.watch;
    rerender();
    if (S.devices.selectedId) loadDetail(S.devices.selectedId);
    toast(name.trim() ? `La rete ora si chiama «${name.trim()}».` : 'La rete torna al nome della scansione.', { tone: 'success' });
    bus.dispatchEvent(new Event('state'));
  } catch (err) { toastError(err, 'Nome non salvato'); }
}

/** «DHCP»: il primo e l'ultimo indirizzo che il router assegna da solo in questa rete; campo vuoto = intervallo tolto. */
async function setDhcpRange() {
  const d = S.devices.data;
  if (!d || d.network === '*') return;
  const n = shownNetwork();
  const range = await promptDialog({
    title: 'Intervallo DHCP della rete', sub: n ? n.label : shownNetworkName(),
    label: 'Dal primo all\'ultimo indirizzo che il router assegna da solo (gli altri sono fissi)',
    value: (n && n.dhcpRange) || '', placeholder: '192.168.1.100-192.168.1.199 oppure 192.168.1.100-199 · vuoto = non impostato',
    ic: 'network', confirmText: 'Salva',
  });
  if (range == null) return;
  try {
    S.devices.data = await call('inventoryNetworkDhcp', { key: d.network, range: range.trim() });
    S.devices.network = S.devices.data.network;
    if (S.st) S.st.inventory = S.devices.data.watch;
    rerender();
    if (S.devices.selectedId) loadDetail(S.devices.selectedId);
    toast(range.trim() ? 'Intervallo DHCP salvato: nelle liste gli indirizzi fuori dall\'intervallo sono segnati «fisso».' : 'Intervallo DHCP tolto.', { tone: 'success' });
    bus.dispatchEvent(new Event('state'));
  } catch (err) { toastError(err, 'Intervallo non salvato'); }
}

async function forgetNetwork() {
  const d = S.devices.data;
  const n = shownNetwork();
  if (!d || !n || n.isCurrent) return;
  const ok = await confirmDialog({
    title: `Dimenticare la rete «${n.name}»?`, confirmText: 'Dimentica la rete', tone: 'danger',
    message: `${plural(n.hostsCount, 'dispositivo', 'dispositivi')}, la loro storia e ${plural(n.scansCount, 'scansione', 'scansioni')} spariscono dall'archivio. Le sessioni salvate non cambiano; se torni in quella rete l'archivio riparte da zero.`,
  });
  if (!ok) return;
  try {
    await call('inventoryForgetNetwork', { key: n.key });
    S.devices.network = '';
    S.devices.networkPinned = false;
    S.devices.selectedId = null;
    S.devices.detail = null;
    toast(`Rete «${n.name}» dimenticata.`, { tone: 'success' });
    load();
  } catch (err) { toastError(err); }
}

async function openEvent(id, hostId) {
  try {
    const r = await call('inventoryAck', { id });
    adopt({ state: r.state });
  } catch { /* l'evento si apre comunque */ }
  if (hostId) {
    S.devices.tab = 'hosts';
    S.devices.q = '';
    S.devices.onlyOnline = false;
    S.devices.pendingHost = Number(hostId);
  }
  load();
}

async function startScan() {
  try {
    adopt(await call('inventoryScan', {}));
    S.devices.data.watch = S.st.inventory;
    updateLive();
    toast('La scansione aggiorna l\'archivio della rete in cui sei: i dispositivi della sessione non cambiano.', { tone: 'info', title: 'Scansione dell\'archivio avviata', timeout: 5000 });
  } catch (err) { toastError(err, 'Scansione non avviata'); }
}

function hostMenu(anchor, id) {
  const h = (S.devices.data.hosts || []).find((x) => x.id === Number(id));
  if (!h) return;
  const ports = (h.ports || []).map((p) => ({ portNumber: p.port, protocol: 'TCP', serviceName: p.service, banner: p.banner }));
  openMenu(anchor, [
    { header: `${h.displayName} · ${h.ip}` },
    { ic: 'plug', label: 'Scansiona le porte', run: () => navigate('ports', { host: h.ip, start: true }) },
    { ic: 'radar', label: 'Scansiona la sua rete', run: () => navigate('network', { target: h.ip.replace(/\.\d+$/, '.0/24') }) },
    { sep: true },
    ...launchItems(h.ip, ports, { hikvision: isHikvision(h) }),
    { sep: true },
    { ic: 'copy', label: 'Copia l\'indirizzo IP', run: () => call('setClipboard', { text: h.ip }).then(() => toast('Indirizzo IP copiato.', { tone: 'success' })) },
    { ic: 'copy', label: 'Copia il MAC', disabled: !h.mac, run: () => call('setClipboard', { text: upperMac(h.mac) }).then(() => toast('MAC copiato.', { tone: 'success' })) },
  ]);
}

const actions = {
  'tab': (el) => { S.devices.tab = el.dataset.tab; rerender(); },
  'pick-host': (el) => { if (S.devices.selectMode) toggleSelected(Number(el.dataset.id)); else openDetail(el.dataset.id); },
  'select-mode': () => { S.devices.selectMode = !S.devices.selectMode; S.devices.selected.clear(); rerender(); },
  'topo-list': () => { S.devices.topoList = !S.devices.topoList; S.devices.topoFilter = ''; refreshTopoSection(); },
  'node-save': () => saveNode(),
  'node-delete': () => { const node = openNode(); if (node) deleteTopoNode(node, S.devices.node.network); },
  'node-unpin': () => { const node = openNode(); if (node) topoCall('topologyMove', { moves: [{ key: node.key, x: null, y: null }] }, `«${node.name}» torna al posto automatico nel disegno.`, S.devices.node.network); },
  'sel-all': () => { for (const h of visibleDeviceHosts()) S.devices.selected.add(h.id); renderList(); refreshBulkBar(); },
  'sel-none': () => { S.devices.selected.clear(); renderList(); refreshBulkBar(); },
  'bulk-trust': (el) => bulkTrust(Number(el.dataset.trust) || 0),
  'bulk-forget': () => bulkForget(),
  'topo-add': (el) => topoAddMenu(el),
  'topo-reset': async () => {
    if (!(await confirmDialog({ title: 'Disposizione automatica?', message: 'Ogni dispositivo torna al suo posto nell\'albero; i collegamenti restano.', confirmText: 'Riordina' }))) return;
    S.devices.topo.collapsed.clear();
    await topoCall('topologyReset', {}, 'Disposizione automatica.');
    drawTopology();
  },
  'topo-zoom': (el) => {
    const t = S.devices.topo;
    const host = $('#topo-host');
    if (!host || !t.scene) return;
    const z = el.dataset.zoom;
    const next = z === 'fit' ? fitZoom(host, t.scene) : z === '0' ? 1 : clampZoom(t.zoom * (z === '1' ? 1.2 : 1 / 1.2));
    t.zoom = setTopologyZoom(host, t.scene, next);
    updateZoomLabel();
    if (z === 'fit') centerTopology(host, t.scene);
  },
  'trust': (el) => { $$('.trust-seg button').forEach((b) => b.classList.toggle('is-active', b === el)); },
  'save-host': () => save(),
  'forget-host': () => forget(),
  'net-rename': () => renameNetwork(),
  'net-dhcp': () => setDhcpRange(),
  'net-forget': () => forgetNetwork(),
  'host-ports': () => { const h = S.devices.detail && S.devices.detail.host; if (h) navigate('ports', { host: h.ip, start: true }); },
  'host-tools': (el) => {
    const h = S.devices.detail && S.devices.detail.host;
    if (!h) return;
    openMenu(el, [{ header: h.ip }, ...launchItems(h.ip, (h.ports || []).map((p) => ({ portNumber: p.port, protocol: 'TCP', serviceName: p.service, banner: p.banner })), { hikvision: isHikvision(h) })], { above: true });
  },
  'open-event': (el) => openEvent(Number(el.dataset.id), el.dataset.host),
  'ack-all': async () => {
    try { adopt({ state: (await call('inventoryAck', { all: true, network: S.devices.network })).state }); load(); toast('Tutti i cambiamenti di questa rete segnati come visti.', { tone: 'success' }); } catch (err) { toastError(err); }
  },
  'inv-scan': () => startScan(),
  'inv-stop': () => call('inventoryStop').catch((e) => toastError(e)),
};

let searchTimer = 0;

on('inventory.progress', () => { if (S.page === 'devices') updateLive(); });
on('inventory.state', (w) => {
  // il PC è passato a un'altra rete: la pagina la segue, salvo che l'utente ne abbia scelta una apposta
  const before = S.devices.data && S.devices.data.current ? S.devices.data.current.key : null;
  if (S.devices.data) S.devices.data.watch = w;
  if (w && w.network && before && w.network.key !== before && !S.devices.networkPinned) {
    S.devices.network = '';
    S.devices.selectedId = null;
    S.devices.detail = null;
    S.devices.node = null;
    S.devices.topo.data = null;
    if (S.page === 'devices') { closeOverlays(); load(); }
    return;
  }
  if (S.page === 'devices') updateLive();
});
on('inventory.done', (d) => {
  S.devices.badgeSeen = false;
  if (d.diff && !d.diff.isFirst && !d.cancelled && !d.error && (S.page !== 'network' || d.automatic)) {
    const changes = d.diff.newCount + d.diff.returnedCount + d.diff.goneCount + d.diff.changedCount;
    toast(changes ? d.diff.summary : `${plural(d.found, 'dispositivo', 'dispositivi')}, nessun cambiamento.`, {
      tone: changes ? 'info' : 'success', title: d.automatic ? 'Scansione automatica completata' : 'Archivio aggiornato', timeout: 8000,
      action: changes ? { label: 'Cambiamenti', run: () => navigate('devices', { tab: 'events' }) } : null,
    });
  } else if (d.diff && d.diff.isFirst && !d.cancelled && (S.page !== 'network' || d.automatic)) {
    toast(`${plural(d.found, 'dispositivo', 'dispositivi')} nell'archivio di questa rete: dalla prossima scansione arrivano i confronti e gli avvisi.`, { tone: 'success', title: 'Archivio della rete creato', timeout: 9000 });
  } else if (d.error && d.automatic) toast(d.error, { tone: 'error', title: 'Scansione automatica non riuscita' });
  if (S.page === 'devices') load();
});
bus.addEventListener('state', () => { if (S.page === 'devices') updateLive(); });

definePage('devices', {
  title: 'Dispositivi', heading: 'Archivio della rete', icon: 'server', section: 'scan', order: 1,
  badge: () => (S.devices.badgeSeen ? 0 : ((S.st && S.st.inventory && S.st.inventory.unseen) || 0)),
  render,
  after: () => { S.devices.badgeSeen = true; bus.dispatchEvent(new Event('state')); load(); },
  // tornando sulla pagina si riparte dalla rete in cui si trova il PC; il pannello aperto si chiude cambiando pagina
  leave: () => { S.devices.networkPinned = false; S.devices.network = ''; S.devices.selectedId = null; S.devices.detail = null; S.devices.node = null; },
  enter: (params) => {
    if (params.host) { S.devices.pendingHost = Number(params.host); S.devices.q = ''; S.devices.onlyOnline = false; S.devices.tab = 'hosts'; S.devices.network = ''; load(); }
    if (params.tab) { S.devices.tab = params.tab; rerender(); }
  },
  actions,
  onInput: (el, field) => {
    if (field === 'q') { S.devices.q = el.value; clearTimeout(searchTimer); searchTimer = setTimeout(refreshList, 180); }
    if (field === 'topo-filter') {
      S.devices.topoFilter = el.value;
      clearTimeout(searchTimer);
      searchTimer = setTimeout(() => { const list = $('#topo-pick-list'); if (!list) return; refreshTopoSection(); const f = $('[data-field="topo-filter"]'); if (f) { f.focus(); f.setSelectionRange(f.value.length, f.value.length); } }, 120);
    }
  },
  onChange: (el, field) => {
    if (field === 'onlyOnline') { S.devices.onlyOnline = el.checked; refreshList(); }
    if (field === 'subnet') { S.devices.subnet = el.value || ''; rerender(); if (S.devices.selectedId) renderDetail(); }
    if (field === 'network') { S.devices.network = el.value; S.devices.subnet = ''; S.devices.networkPinned = true; S.devices.selectedId = null; S.devices.detail = null; S.devices.node = null; closeOverlays(); S.devices.topo.data = null; S.devices.topo.collapsed.clear(); load(); }
    const self = S.devices.node ? S.devices.node : S.devices.detail ? { key: S.devices.detail.host.key, network: S.devices.detail.host.network } : null;
    if (self && field === 'topo-uplink') pickUplink(self.key, self.network, el.value);
    if (self && field === 'topo-child') pickChild(self.key, self.network, el.value, el.checked);
  },
  onContext: (row, point) => hostMenu(point, row.dataset.id),
});
