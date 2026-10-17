// Categorie dei dispositivi (Core\Utils\DeviceClassifier.vb): icona e colore per la tabella degli host. L'icona viene
// dal catalogo Material Design Icons (core/mdi.js): quella scelta dall'utente, altrimenti quella del tipo riconosciuto
// dalla scansione (TYPE_ICONS), altrimenti quella della categoria. Finché il tracciato non è arrivato resta l'icona a
// tratto della categoria.
import { icon } from './icons.js';
import { mdiSvg, tintStyle } from './mdi.js';

const CATEGORIES = {
  'Domotica / IoT': { ic: 'house', mdi: 'home-automation', tone: 'info' },
  'Smart TV': { ic: 'tv', mdi: 'television', tone: 'warning' },
  'Smartphone / Tablet': { ic: 'smartphone', mdi: 'cellphone', tone: 'success' },
  'Router / Access Point': { ic: 'router', mdi: 'router-network', tone: 'primary' },
  'Telecamera IP': { ic: 'camera', mdi: 'cctv', tone: 'danger' },
  "Centrale d'allarme": { ic: 'shield', mdi: 'alarm-panel', tone: 'danger' },
  'Stampante': { ic: 'printer', mdi: 'printer-wireless', tone: '' },
  'NAS / Storage': { ic: 'hardDrive', mdi: 'nas', tone: 'primary' },
  'Assistente Vocale': { ic: 'speaker', mdi: 'speaker-wireless', tone: 'info' },
  'Console Gaming': { ic: 'gamepad', mdi: 'controller', tone: 'success' },
  'PC / Server': { ic: 'monitor', mdi: 'desktop-tower-monitor', tone: '' },
};
const OTHER = { ic: 'cpu', mdi: 'devices', tone: '' };

/**
 * Icona per il tipo riconosciuto dalla scansione (le parole del tipo, dal più preciso): vale prima della categoria.
 * I nomi sono verificati sul catalogo (Tests\IconCatalogTests.vb).
 */
export const TYPE_ICONS = [
  [/home assistant|hub domotico/, 'home-automation'],
  [/mqtt|gateway iot/, 'hub'],
  [/plc|modbus/, 'factory'],
  [/centrale d'allarme/, 'alarm-panel'],
  [/chromecast|nest cast/, 'cast-connected'],
  [/fire tv|lettore multimediale/, 'set-top-box'],
  [/telecamera|nvr/, 'cctv'],
  [/stampante/, 'printer-wireless'],
  [/\bnas\b/, 'nas'],
  [/dispositivo google/, 'cast-audio'],
  [/sonos/, 'speaker'],
  [/assistente vocale/, 'speaker-wireless'],
  [/iphone|smartphone \(|smartphone android/, 'cellphone'],
  [/tablet \(|ipad/, 'tablet'],
  [/mac privato/, 'cellphone-wireless'],
  [/fritz|modem/, 'router-wireless'],
  [/ubiquiti|unifi|access point/, 'access-point-network'],
  [/shelly|interruttore|relè/, 'light-switch'],
  [/philips hue|illuminazione/, 'lightbulb-group'],
  [/raspberry/, 'raspberry-pi'],
  [/esp32|esp8266|sensore|modulo/, 'chip'],
  [/tuya|smart iot/, 'power-socket-eu'],
  [/automazione/, 'home-automation'],
  [/smart tv|televisione/, 'television'],
  [/console/, 'controller'],
  [/apple/, 'tablet-cellphone'],
  [/samsung/, 'devices'],
  [/macchina virtuale|hypervisor/, 'layers'],
  [/router|switch|dispositivo di rete|apparato di rete|gateway/, 'router-network'],
  [/windows|desktop|laptop/, 'desktop-tower-monitor'],
  [/workstation|server/, 'server'],
  [/linux|unix/, 'penguin'],
];

export function categoryOf(host) {
  return CATEGORIES[host && host.deviceCategory] || OTHER;
}

/** Il nome dell'icona MDI del tipo di dispositivo (o della sua categoria). */
export function typeIcon(host) {
  const t = String((host && host.deviceType) || '').toLowerCase();
  const hit = TYPE_ICONS.find(([re]) => re.test(t));
  return hit ? hit[1] : categoryOf(host).mdi;
}

/** Quadratino con l'icona: scelta dall'utente (nel suo colore), altrimenti quella del tipo nel colore della categoria. */
export function catIcon(host, large = false) {
  const c = categoryOf(host);
  const title = host && host.deviceCategory ? host.deviceCategory : 'Altro';
  const custom = host && host.icon ? mdiSvg(host.icon) : '';
  if (custom) return `<span class="cat-icon ${host.iconColor ? 'is-tinted' : 'primary'} ${large ? 'cat-lg' : ''}"${tintStyle(host.iconColor)} title="${title}">${custom}</span>`;
  const auto = mdiSvg(typeIcon(host));
  return `<span class="cat-icon ${c.tone} ${large ? 'cat-lg' : ''}" title="${title}">${auto || icon(c.ic)}</span>`;
}
