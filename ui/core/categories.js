// Categorie dei dispositivi (Core\Utils\DeviceClassifier.vb): icona e colore per le tabelle e le schede. L'icona viene
// dal catalogo Material Design Icons (core/mdi.js): quella scelta dall'utente, altrimenti quella del tipo riconosciuto
// dalla scansione (TYPE_ICONS), altrimenti quella della categoria. Finché il tracciato non è arrivato resta l'icona a
// tratto della categoria. Restano anche i nomi delle categorie usate dalle sessioni salvate con le versioni precedenti.
import { icon } from './icons.js';
import { mdiSvg, tintStyle } from './mdi.js';

const CATEGORIES = {
  'Rete': { ic: 'router', mdi: 'router-network', tone: 'primary' },
  'Server': { ic: 'server', mdi: 'server', tone: 'info' },
  'Virtualizzazione': { ic: 'cpu', mdi: 'layers', tone: 'info' },
  'Postazione': { ic: 'monitor', mdi: 'desktop-tower-monitor', tone: '' },
  'Storage': { ic: 'hardDrive', mdi: 'nas', tone: 'primary' },
  'Stampante': { ic: 'printer', mdi: 'printer-wireless', tone: '' },
  'Telecamera': { ic: 'camera', mdi: 'cctv', tone: 'danger' },
  "Centrale d'allarme": { ic: 'shield', mdi: 'alarm-panel', tone: 'danger' },
  'Smart TV': { ic: 'tv', mdi: 'television', tone: 'warning' },
  'Domotica': { ic: 'house', mdi: 'home-automation', tone: 'warning' },
  'Mobile': { ic: 'smartphone', mdi: 'cellphone', tone: 'success' },
  'Console': { ic: 'gamepad', mdi: 'controller', tone: 'success' },
  // sessioni delle versioni 2.0–2.5
  'Domotica / IoT': { ic: 'house', mdi: 'home-automation', tone: 'warning' },
  'Smartphone / Tablet': { ic: 'smartphone', mdi: 'cellphone', tone: 'success' },
  'Router / Access Point': { ic: 'router', mdi: 'router-network', tone: 'primary' },
  'Telecamera IP': { ic: 'camera', mdi: 'cctv', tone: 'danger' },
  'NAS / Storage': { ic: 'hardDrive', mdi: 'nas', tone: 'primary' },
  'Assistente Vocale': { ic: 'speaker', mdi: 'speaker-wireless', tone: 'warning' },
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
  [/mqtt|gateway iot|gateway zigbee/, 'hub'],
  [/plc|modbus/, 'factory'],
  [/centrale d'allarme/, 'alarm-panel'],
  [/chromecast|nest cast|google cast|google tv/, 'cast-connected'],
  [/fire tv|lettore multimediale|roku|shield tv|apple tv/, 'set-top-box'],
  [/telecamera|nvr|videoregistratore/, 'cctv'],
  [/stampante/, 'printer-wireless'],
  [/\bnas\b|file server/, 'nas'],
  [/dispositivo google/, 'cast-audio'],
  [/sonos|diffusore/, 'speaker'],
  [/assistente vocale/, 'speaker-wireless'],
  [/iphone|smartphone \(|smartphone android|smartphone/, 'cellphone'],
  [/tablet \(|ipad|tablet/, 'tablet'],
  [/mac privato|mac casuale/, 'cellphone-wireless'],
  [/fritz|modem/, 'router-wireless'],
  [/ubiquiti|unifi|access point|ripetitore/, 'access-point-network'],
  [/shelly|interruttore|relè|sonoff|tasmota/, 'light-switch'],
  [/philips hue|illuminazione|wled/, 'lightbulb-group'],
  [/raspberry/, 'raspberry-pi'],
  [/esp32|esp8266|esphome|sensore|modulo/, 'chip'],
  [/tuya|smart iot/, 'power-socket-eu'],
  [/automazione|domotico/, 'home-automation'],
  [/smart tv|televisione/, 'television'],
  [/console/, 'controller'],
  [/apple/, 'tablet-cellphone'],
  [/samsung/, 'devices'],
  [/macchina virtuale|hypervisor|hyper-v|proxmox|esxi|vcenter|xenserver|nutanix|docker|kubernetes/, 'layers'],
  [/firewall|router|switch|dispositivo di rete|apparato di rete|gateway/, 'router-network'],
  [/windows|desktop|laptop|portatile|postazione/, 'desktop-tower-monitor'],
  [/workstation|server|controller di dominio/, 'server'],
  [/linux|unix/, 'penguin'],
];

/** Nome della categoria di un host della sessione (deviceCategory) o dell'archivio (category). */
export const categoryName = (host) => (host && (host.deviceCategory || host.category)) || 'Altro';

export function categoryOf(host) {
  return CATEGORIES[categoryName(host)] || OTHER;
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
  const title = categoryName(host);
  const custom = host && host.icon ? mdiSvg(host.icon) : '';
  if (custom) return `<span class="cat-icon ${host.iconColor ? 'is-tinted' : 'primary'} ${large ? 'cat-lg' : ''}"${tintStyle(host.iconColor)} title="${title}">${custom}</span>`;
  const auto = mdiSvg(typeIcon(host));
  return `<span class="cat-icon ${c.tone} ${large ? 'cat-lg' : ''}" title="${title}">${auto || icon(c.ic)}</span>`;
}
