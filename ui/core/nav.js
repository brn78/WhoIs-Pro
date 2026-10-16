// Passaggio da una pagina all'altra con parametri (es. "Scansiona le porte di questo host"): app.js registra la
// funzione, le pagine la chiamano senza importarsi a vicenda.
let navigator = () => {};

export function setNavigator(fn) { navigator = fn; }

/** navigate('ports', { host: '192.168.1.10', start: true }) */
export function navigate(page, params = {}) { navigator(page, params); }
