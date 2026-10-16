// Ponte con il programma (UI\AppBridge.vb): call('comando', {argomenti}) manda la richiesta a WebView2 e restituisce
// una Promise con il risultato; on('evento', fn) riceve gli eventi che il programma manda da sé (avanzamento delle
// scansioni, fine, messaggi).
const webview = window.chrome && window.chrome.webview;
const pending = new Map();
const listeners = new Map();
let lastId = 0;

if (webview) {
  webview.addEventListener('message', (e) => {
    const msg = e.data || {};
    if (msg.event) {
      for (const fn of listeners.get(msg.event) || []) {
        try { fn(msg.data); } catch (err) { console.error(msg.event, err); }
      }
      return;
    }
    const p = pending.get(msg.id);
    if (!p) return;
    pending.delete(msg.id);
    if (msg.ok) p.resolve(msg.result); else p.reject(new Error(msg.error || 'Operazione non riuscita.'));
  });
}

export const available = !!webview;

export function call(cmd, args = {}) {
  if (!webview) return Promise.reject(new Error('Questa pagina funziona solo dentro WhoIs Pro (WhoIsPro.exe).'));
  const id = ++lastId;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    webview.postMessage(JSON.stringify({ id, cmd, args }));
  });
}

export function on(event, fn) {
  if (!listeners.has(event)) listeners.set(event, new Set());
  listeners.get(event).add(fn);
  return () => listeners.get(event).delete(fn);
}
