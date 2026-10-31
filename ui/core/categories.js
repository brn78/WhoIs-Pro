// Categorie dei dispositivi (Core\Utils\DeviceClassifier.vb): icona e colore per le tabelle e le schede.
// Restano anche i nomi delle categorie usate dalle sessioni salvate con le versioni precedenti.
import { icon } from './icons.js';

const CATEGORIES = {
  'Rete': { ic: 'router', tone: 'primary' },
  'Server': { ic: 'server', tone: 'info' },
  'Virtualizzazione': { ic: 'cpu', tone: 'info' },
  'Postazione': { ic: 'monitor', tone: '' },
  'Storage': { ic: 'hardDrive', tone: 'primary' },
  'Stampante': { ic: 'printer', tone: '' },
  'Telecamera': { ic: 'camera', tone: 'danger' },
  'Smart TV': { ic: 'tv', tone: 'warning' },
  'Domotica': { ic: 'house', tone: 'warning' },
  'Mobile': { ic: 'smartphone', tone: 'success' },
  'Console': { ic: 'gamepad', tone: 'success' },
  // sessioni delle versioni 2.0–2.2
  'Domotica / IoT': { ic: 'house', tone: 'warning' },
  'Smartphone / Tablet': { ic: 'smartphone', tone: 'success' },
  'Router / Access Point': { ic: 'router', tone: 'primary' },
  'Telecamera IP': { ic: 'camera', tone: 'danger' },
  'NAS / Storage': { ic: 'hardDrive', tone: 'primary' },
  'Assistente Vocale': { ic: 'speaker', tone: 'warning' },
  'Console Gaming': { ic: 'gamepad', tone: 'success' },
  'PC / Server': { ic: 'monitor', tone: '' },
};
const OTHER = { ic: 'cpu', tone: '' };

/** Nome della categoria di un host della sessione (deviceCategory) o dell'archivio (category). */
export const categoryName = (host) => (host && (host.deviceCategory || host.category)) || 'Altro';

export function categoryOf(host) {
  return CATEGORIES[categoryName(host)] || OTHER;
}

/** Quadratino con l'icona della categoria. */
export function catIcon(host, large = false) {
  const c = categoryOf(host);
  return `<span class="cat-icon ${c.tone} ${large ? 'cat-lg' : ''}" title="${categoryName(host)}">${icon(c.ic)}</span>`;
}
