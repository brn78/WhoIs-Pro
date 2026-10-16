// Stato della pagina condiviso tra app.js e le pagine, e registro delle pagine.
export const S = {
  st: null,                       // stato dal programma: sessione (senza elenchi), database OUI, schede di rete, modelli
  data: { hosts: [], ports: [], whois: [], dns: [], trace: [], scans: [], perf: [] },   // gli elenchi della sessione
  page: 'overview',
  collapsed: false,
};

/**
 * Pagine: definePage(chiave, { title, icon, section, render, after, actions, onInput, onChange, onSubmit, onSort }).
 * render() restituisce l'HTML; after() gira dopo che è nel documento; actions sono i data-action propri della pagina.
 */
export const PAGES = {};
export const NAV = [];

export function definePage(key, def) {
  PAGES[key] = def;
  NAV.push({ key, ...def });
}

/** Una scansione finita (evento *.done con il suo record): nello storico della sessione, una volta sola. */
export function addScan(scan) {
  if (!scan || S.data.scans.includes(scan)) return;
  S.data.scans.push(scan);
  if (S.data.scans.length > 200) S.data.scans.splice(0, S.data.scans.length - 200);
}

/** Avvisi tra moduli: 'state' (stato cambiato), 'session' (elenchi sostituiti), 'data' (elementi aggiunti). */
export const bus = new EventTarget();
export const emit = (name, detail) => bus.dispatchEvent(new CustomEvent(name, { detail }));

export function setState(st) {
  if (!st) return;
  S.st = st;
  emit('state');
}

export function setSession(data) {
  if (!data) return;
  S.data = {
    hosts: data.hosts || [], ports: data.ports || [], whois: data.whois || [], dns: data.dns || [], trace: data.trace || [],
    scans: data.scans || [], perf: data.perf || [],
  };
  emit('session');
}

/** Applica la risposta dei comandi che cambiano lo stato ({ state, session?, result }) e restituisce il risultato. */
export function adopt(res) {
  if (!res) return null;
  if (res.state) setState(res.state);
  if (res.session) setSession(res.session);
  return 'result' in res ? res.result : res;
}

/** La sessione ha dati non salvati: lo stato lo sa (dirty), ma gli elementi arrivati con gli eventi lo anticipano. */
export function markDirty() {
  if (S.st && S.st.session && !S.st.session.dirty) {
    S.st.session.dirty = true;
    emit('state');
  }
}

export const counts = () => ({
  hosts: S.data.hosts.length, ports: S.data.ports.length, whois: S.data.whois.length, dns: S.data.dns.length, trace: S.data.trace.length,
});

export const isRunning = () => {
  const r = (S.st && S.st.running) || {};
  return !!(r.network || r.ports || r.trace || r.oui || r.autoPorts || r.perf);
};
