// Categorie dei dispositivi (Core\Utils\DeviceClassifier.vb): icona e colore per la tabella degli host.
import { icon } from './icons.js';

const CATEGORIES = {
  'Domotica / IoT': { ic: 'house', tone: 'info' },
  'Smart TV': { ic: 'tv', tone: 'warning' },
  'Smartphone / Tablet': { ic: 'smartphone', tone: 'success' },
  'Router / Access Point': { ic: 'router', tone: 'primary' },
  'Telecamera IP': { ic: 'camera', tone: 'danger' },
  "Centrale d'allarme": { ic: 'shield', tone: 'danger' },
  'Stampante': { ic: 'printer', tone: '' },
  'NAS / Storage': { ic: 'hardDrive', tone: 'primary' },
  'Assistente Vocale': { ic: 'speaker', tone: 'info' },
  'Console Gaming': { ic: 'gamepad', tone: 'success' },
  'PC / Server': { ic: 'monitor', tone: '' },
};
const OTHER = { ic: 'cpu', tone: '' };

export function categoryOf(host) {
  return CATEGORIES[host && host.deviceCategory] || OTHER;
}

/** Quadratino con l'icona della categoria. */
export function catIcon(host, large = false) {
  const c = categoryOf(host);
  return `<span class="cat-icon ${c.tone} ${large ? 'cat-lg' : ''}" title="${host && host.deviceCategory ? host.deviceCategory : 'Altro'}">${icon(c.ic)}</span>`;
}
