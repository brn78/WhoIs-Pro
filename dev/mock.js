// Ponte finto per l'anteprima nel browser (dev\preview.html): risponde ai comandi della pagina come farebbe
// AppBridge.vb, con una rete dimostrativa. Non finisce nel programma (la cartella dev\ non è incorporata).
(function () {
  const now = Date.now();
  const ago = (min) => new Date(now - min * 60000).toISOString();
  const target = new EventTarget();
  const send = (data) => setTimeout(() => target.dispatchEvent(Object.assign(new Event('message'), { data })), 30);
  const event = (name, data) => send({ event: name, data });

  const H = (ip, hostname, mac, vendor, type, category, ms, ttl, os, first, last = 1) => ({
    ipAddress: ip, hostname, macAddress: mac, macVendor: vendor, deviceType: type, deviceCategory: category, deviceIcon: '',
    status: 'Online', responseTimeMs: ms, ttl, osGuess: os, openPortsCount: 0, firstSeen: ago(first), lastSeen: ago(last), notes: '',
  });
  const hosts = [
    H('192.168.1.1', 'fritz.box', '3C:A6:2F:11:22:01', 'AVM GmbH', 'Router FRITZ!Box', 'Router / Access Point', 1, 64, 'Linux / Android / iOS / macOS', 9000),
    H('192.168.1.2', 'unifi-ap-salone', '74:83:C2:4A:10:9E', 'Ubiquiti Inc.', 'Access point UniFi', 'Router / Access Point', 2, 64, 'Linux / Android / iOS / macOS', 9000),
    H('192.168.1.10', 'nas-ufficio', '00:11:32:AB:CD:10', 'Synology Incorporated', 'NAS Synology', 'NAS / Storage', 1, 64, 'Linux / Android / iOS / macOS', 8000),
    H('192.168.1.20', 'homeassistant', 'B8:27:EB:5C:44:20', 'Raspberry Pi Foundation', 'Home Assistant', 'Domotica / IoT', 3, 64, 'Linux / Android / iOS / macOS', 7000),
    H('192.168.1.31', 'shellyplus1-cucina', 'C8:F0:9E:31:00:31', 'Espressif Inc.', 'Modulo Shelly', 'Domotica / IoT', 18, 255, 'Cisco / Router / Network Device', 6000),
    H('192.168.1.32', 'shelly-tapparelle', 'C8:F0:9E:31:00:32', 'Espressif Inc.', 'Modulo Shelly', 'Domotica / IoT', 22, 255, 'Cisco / Router / Network Device', 6000),
    H('192.168.1.40', 'cam-ingresso', 'BC:AD:28:40:40:40', 'Hangzhou Hikvision Digital Technology Co.,Ltd.', 'Telecamera Hikvision', 'Telecamera IP', 4, 64, 'Linux / Android / iOS / macOS', 5000),
    H('192.168.1.41', 'cam-giardino', 'BC:AD:28:40:40:41', 'Hangzhou Hikvision Digital Technology Co.,Ltd.', 'Telecamera Hikvision', 'Telecamera IP', 5, 64, 'Linux / Android / iOS / macOS', 5000),
    H('192.168.1.50', 'centrale-allarme', '00:1C:B3:50:50:50', 'Ksenia Security S.r.l.', 'Centrale Ksenia', "Centrale d'allarme", 6, 64, 'Linux / Android / iOS / macOS', 4000),
    { ...H('192.168.1.60', 'stampante-hp', '3C:52:82:60:60:60', 'HP Inc.', 'Stampante HP', 'Stampante', 9, 255, 'Cisco / Router / Network Device', 3000, 50), status: 'Offline' },
    H('192.168.1.70', 'samsung-tv-sala', '8C:79:F5:70:70:70', 'Samsung Electronics Co.,Ltd', 'Smart TV Samsung', 'Smart TV', 12, 64, 'Linux / Android / iOS / macOS', 2000),
    H('192.168.1.80', 'PC-UFFICIO', '58:11:22:80:80:80', 'Dell Inc.', 'Computer Dell', 'PC / Server', 1, 128, 'Windows (PC / Server)', 1500),
    H('192.168.1.101', 'iphone-di-marco', '7A:12:9B:01:01:01', 'MAC privato (casuale)', 'Telefono', 'Smartphone / Tablet', 35, 64, 'Linux / Android / iOS / macOS', 95, 2),
    H('192.168.1.102', '', 'D2:45:11:02:02:02', 'MAC privato (casuale)', 'Telefono', 'Smartphone / Tablet', 48, 64, 'Linux / Android / iOS / macOS', 25, 1),
    H('192.168.1.150', 'echo-cucina', '44:00:49:50:50:50', 'Amazon Technologies Inc.', 'Amazon Echo', 'Assistente Vocale', 15, 64, 'Linux / Android / iOS / macOS', 12, 1),
  ];
  const P = (host, port, service, banner = '', protocol = 'TCP', status = 'Open') => ({ host, portNumber: port, protocol, status, serviceName: service, banner, responseTimeMs: 2, timestamp: ago(9) });
  const ports = [
    P('192.168.1.1', 53, 'DNS'), P('192.168.1.1', 80, 'HTTP', 'HTTP/1.1 200 OK · Server: FRITZ!OS'), P('192.168.1.1', 443, 'HTTPS', 'TLS · FRITZ!Box 7590'),
    P('192.168.1.10', 22, 'SSH', 'SSH-2.0-OpenSSH_8.2'), P('192.168.1.10', 445, 'SMB'), P('192.168.1.10', 5000, 'Synology DSM', 'HTTP/1.1 200 OK · nginx'),
    P('192.168.1.20', 8123, 'Home Assistant', 'HTTP/1.1 200 OK · Python/3.12 aiohttp'), P('192.168.1.40', 80, 'HTTP', 'HTTP/1.1 200 OK · Server: webs'),
    P('192.168.1.40', 554, 'RTSP', 'RTSP/1.0 200 OK'), P('192.168.1.40', 8000, 'Hikvision SDK'), P('192.168.1.41', 554, 'RTSP', 'RTSP/1.0 200 OK'),
    P('192.168.1.50', 443, 'HTTPS', 'TLS · Ksenia lares'), P('192.168.1.80', 3389, 'RDP'), P('192.168.1.1', 1900, 'SSDP / UPnP', '', 'UDP', 'Open|Filtered'),
  ];
  const whois = [{
    queryTarget: 'example.com', serverUsed: 'whois.verisign-grs.com', registrar: 'RESERVED-Internet Assigned Numbers Authority',
    creationDate: '1995-08-14T04:00:00Z', expirationDate: '2026-08-13T04:00:00Z', updatedDate: '2025-08-14T07:01:39Z',
    registrantOrg: 'Internet Assigned Numbers Authority', country: 'US', nameServers: ['A.IANA-SERVERS.NET', 'B.IANA-SERVERS.NET'],
    domainStatus: ['clientDeleteProhibited', 'clientTransferProhibited', 'clientUpdateProhibited'], referralServer: 'whois.iana.org',
    isSuccess: true, errorMessage: '', queryTimeMs: 412, queriedAt: ago(30),
    rawText: '   Domain Name: EXAMPLE.COM\n   Registry Domain ID: 2336799_DOMAIN_COM-VRSN\n   Registrar WHOIS Server: whois.iana.org\n   Updated Date: 2025-08-14T07:01:39Z\n   Creation Date: 1995-08-14T04:00:00Z\n   Registry Expiry Date: 2026-08-13T04:00:00Z\n   Registrar: RESERVED-Internet Assigned Numbers Authority\n   Domain Status: clientDeleteProhibited\n   Domain Status: clientTransferProhibited\n   Domain Status: clientUpdateProhibited\n   Name Server: A.IANA-SERVERS.NET\n   Name Server: B.IANA-SERVERS.NET\n   DNSSEC: signedDelegation',
  }];
  const D = (recordType, value, ttl = 3600, priority = 0) => ({ host: 'example.com', recordType, value, ttl, priority });
  const dns = [
    D('A', '23.192.228.80', 300), D('A', '23.215.0.136', 300), D('AAAA', '2600:1406:3a00:21::173e:2e65', 300),
    D('NS', 'a.iana-servers.net', 86400), D('NS', 'b.iana-servers.net', 86400), D('MX', '.', 86400, 0),
    D('TXT', 'v=spf1 -all', 86400), D('TXT', '_k2n1y4vw3qtb4skdx9e7dxt97qrmmq9', 86400),
  ];
  const T = (n, ip, hostname, rtts, status = 'OK') => ({ target: '1.1.1.1', hopNumber: n, ipAddress: ip, hostname, roundTripTimes: rtts, status });
  const trace = [
    T(1, '192.168.1.1', 'fritz.box', [1, 1, 1]), T(2, '100.64.0.1', '', [8, 7, 9]), T(3, '10.250.12.1', '', [9, 10, 9]),
    T(4, '*', '', [], 'TimedOut'), T(5, '195.22.205.116', '', [12, 12, 13]), T(6, '195.22.196.215', '', [13, 14, 13]),
    T(7, '141.101.73.2', '', [14, 13, 14]), T(8, '1.1.1.1', 'one.one.one.one', [13, 14, 13]),
  ];
  const C = (localPort, remoteAddress, remotePort, state, processName, processId) => ({ protocol: 'TCP', localAddress: '192.168.1.80', localPort, remoteAddress, remotePort, state, processId, processName, processPath: `C:\\Program Files\\${processName}` });
  const netstat = [
    C(52114, '140.82.112.25', 443, 'Established', 'msedge.exe', 8120), C(52130, '13.107.42.14', 443, 'Established', 'OneDrive.exe', 6204),
    C(52201, '192.168.1.10', 445, 'Established', 'System', 4), C(52240, '192.168.1.40', 554, 'Established', 'WhoIsPro.exe', 9312),
    C(52288, '1.1.1.1', 443, 'Established', 'msedge.exe', 8120), C(52301, '20.42.65.92', 443, 'TimeWait', 'svchost.exe', 1420),
    { protocol: 'TCP', localAddress: '0.0.0.0', localPort: 135, remoteAddress: '0.0.0.0', remotePort: 0, state: 'Listen', processId: 1088, processName: 'svchost.exe', processPath: 'C:\\Windows\\System32\\svchost.exe' },
    { protocol: 'TCP', localAddress: '0.0.0.0', localPort: 445, remoteAddress: '0.0.0.0', remotePort: 0, state: 'Listen', processId: 4, processName: 'System', processPath: '' },
    { protocol: 'TCP', localAddress: '0.0.0.0', localPort: 3389, remoteAddress: '0.0.0.0', remotePort: 0, state: 'Listen', processId: 1380, processName: 'svchost.exe', processPath: 'C:\\Windows\\System32\\svchost.exe' },
  ];
  const scans = [
    { kind: 'network', target: '192.168.1.0/24', at: ago(4), seconds: 11.8, found: 15, cancelled: false, error: '' },
    { kind: 'ports', target: '192.168.1.40', at: ago(9), seconds: 6.2, found: 2, cancelled: false, error: '' },
    { kind: 'ports', target: '192.168.1.10', at: ago(16), seconds: 7.9, found: 3, cancelled: false, error: '' },
    { kind: 'trace', target: '1.1.1.1', at: ago(48), seconds: 9.4, found: 8, cancelled: false, error: '' },
    { kind: 'network', target: '192.168.1.0/24', at: ago(190), seconds: 12.4, found: 13, cancelled: false, error: '' },
  ];
  scans.reverse(); // nel programma lo storico è in ordine di tempo, la più recente in fondo
  const session = { hosts, ports, whois, dns, trace, scans };

  const adapters = [
    { name: 'Ethernet', description: 'Intel(R) Ethernet Connection I219-LM', ip: '192.168.1.80', mask: '255.255.255.0', gateway: '192.168.1.1', mac: '58:11:22:80:80:80', cidr: '192.168.1.0/24' },
    { name: 'Wi-Fi', description: 'Intel(R) Wi-Fi 6E AX211 160MHz', ip: '10.0.0.23', mask: '255.255.255.0', gateway: '10.0.0.1', mac: 'A4:6B:B6:23:23:23', cidr: '10.0.0.0/24' },
  ];
  const configs = [
    { name: 'Ethernet', description: adapters[0].description, up: true, dhcp: false, ip: '192.168.1.80', mask: '255.255.255.0', gateway: '192.168.1.1', dns: ['192.168.1.1', '1.1.1.1'], mac: adapters[0].mac },
    { name: 'Wi-Fi', description: adapters[1].description, up: true, dhcp: true, ip: '10.0.0.23', mask: '255.255.255.0', gateway: '10.0.0.1', dns: ['10.0.0.1'], mac: adapters[1].mac },
    { name: 'Ethernet 2', description: 'Realtek USB GbE Family Controller', up: false, dhcp: true, ip: '', mask: '', gateway: '', dns: [], mac: '00:E0:4C:68:00:01' },
  ];
  const settings = {
    autoStart: true, trayIcon: true, closeToTray: true, startMinimized: false, theme: 'auto', autoScan: false, autoScanDelay: 10,
    profiles: [
      { id: 'p1', name: 'Cantiere Rossi', adapter: 'Ethernet', dhcp: false, ip: '192.168.10.250', mask: '255.255.255.0', gateway: '192.168.10.1', dns1: '192.168.10.1', dns2: '' },
      { id: 'p2', name: 'Ufficio (DHCP)', adapter: 'Ethernet', dhcp: true, ip: '', mask: '', gateway: '', dns1: '', dns2: '' },
    ],
    dataFolder: 'C:\\Users\\Tecnico\\AppData\\Local\\WhoIsPro', exePath: 'C:\\Users\\Tecnico\\AppData\\Local\\Programs\\WhoIs Pro\\WhoIsPro.exe',
  };
  const state = () => ({
    version: '2.4.0',
    session: {
      title: 'Villa Bianchi, impianto ottobre', notes: '', createdAt: ago(240), modifiedAt: ago(4), checksum: '9f2c61d0a4be7713c1f0e8d2b5a64477aa10b3c9e8f1d2a7c6b5e4f3a2b1c0d9',
      file: 'C:\\Users\\Tecnico\\Documents\\Sessioni\\villa-bianchi.whoispro', fileName: 'villa-bianchi.whoispro', dirty: true,
      counts: { hosts: hosts.length, ports: ports.length, whois: whois.length, dns: dns.length, trace: trace.length },
    },
    oui: { count: 58214, file: 'C:\\Users\\Tecnico\\AppData\\Local\\WhoIsPro\\manuf.txt', updating: false },
    adapters,
    presets: {
      ports: [
        { key: 'top20', name: 'Le 20 più comuni', ports: '21-23,25,53,80,110,111,135,139,143,443,445,993,995,1723,3306,3389,5900,8080' },
        { key: 'web', name: 'Web', ports: '80,443,8000,8080,8443,8888' },
        { key: 'cam', name: 'Telecamere e domotica', ports: '80,443,554,8000,8123,8443,37777,1883,502' },
      ],
      whoisServers: ['whois.nic.it', 'whois.verisign-grs.com', 'whois.ripe.net', 'whois.iana.org'],
    },
    running: { network: false, ports: false, trace: false, oui: false },
    update: { current: '2.4.0', version: '2.4.0', available: false, checkedAt: ago(300), publishedAt: ago(3000), installed: true, hasSetup: true, pageUrl: 'https://github.com/brn78/WhoIs-Pro/releases/latest' },
    online: true, theme: 'auto', copyright: '© 2026 Bruno Leonardi. Tutti i diritti riservati.',
  });

  // traffico: onde lente con qualche picco, come una rete vera
  let t = 0;
  const totals = { Ethernet: { rx: 8.4e9, tx: 1.2e9 }, 'Wi-Fi': { rx: 3.1e8, tx: 6.0e7 } };
  let cpuNow = 18;
  function dashboard() {
    t += 1;
    const wave = (base, amp, f, ph) => Math.max(0, base + amp * Math.sin(t / f + ph) + (Math.random() - 0.4) * amp * 0.9 + (Math.random() < 0.06 ? amp * 4 : 0));
    const eRx = wave(1.6e6, 1.1e6, 4, 0); const eTx = wave(1.8e5, 1.4e5, 5, 1);
    const wRx = wave(4e4, 3e4, 6, 2); const wTx = wave(1e4, 8e3, 7, 3);
    totals.Ethernet.rx += eRx * 2; totals.Ethernet.tx += eTx * 2; totals['Wi-Fi'].rx += wRx * 2; totals['Wi-Fi'].tx += wTx * 2;
    cpuNow = Math.min(96, Math.max(3, cpuNow + (Math.random() - 0.5) * 9));
    return {
      at: new Date().toISOString(),
      traffic: [
        { ...configs[0], id: '{eth0}', connected: true, type: 'Ethernet', speedMbps: 1000, rxBps: eRx, txBps: eTx, rxTotal: totals.Ethernet.rx, txTotal: totals.Ethernet.tx },
        { ...configs[1], id: '{wifi0}', connected: true, type: 'Wi-Fi', speedMbps: 1201, rxBps: wRx, txBps: wTx, rxTotal: totals['Wi-Fi'].rx, txTotal: totals['Wi-Fi'].tx },
        { ...configs[configs.length - 1], id: '{eth2}', connected: false, type: 'Ethernet', speedMbps: 0, rxBps: 0, txBps: 0, rxTotal: 0, txTotal: 0 },
      ],
      system: {
        cpu: { usage: Math.round(cpuNow * 10) / 10, name: '13th Gen Intel(R) Core(TM) i7-1365U', logical: 12 },
        memory: { total: 34089213952, used: 19327352832 + Math.round(Math.random() * 3e8), percent: 57 },
        disk: { name: 'C:', total: 511101108224, used: 318767104000, free: 192334004224 },
        os: { name: 'Windows 11 Pro', version: '25H2', machine: 'PC-TECNICO', user: 'Tecnico', uptimeSeconds: 196000 + t * 2 },
      },
      internet: { online: true, latencyMs: Math.round(38 + Math.random() * 14) },
    };
  }

  const computer = () => ({
    cpu: { name: '13th Gen Intel(R) Core(TM) i7-1365U', mhz: 1800, logical: 12, usage: cpuNow, arch: 'X64' },
    memory: { total: 34089213952, available: 14761861120, used: 19327352832, percent: 57, pageTotal: 39000000000, pageAvailable: 17000000000 },
    disks: [{ name: 'C:', label: 'Windows', format: 'NTFS', type: 'Disco fisso', total: 511101108224, free: 192334004224, used: 318767104000 }],
    os: { name: 'Windows 11 Pro', version: '25H2', build: '26300.1000', uptimeSeconds: 196000, machine: 'PC-TECNICO', user: 'Tecnico', domain: 'PC-TECNICO', dotnet: '.NET 10.0.0', bootAt: ago(3266) },
    gpu: 'Intel(R) Iris(R) Xe Graphics', serialPorts: [{ port: 'COM3', device: 'USB Serial Port', bus: 'USB', driver: 'FTDI' }],
  });

  const handlers = {
    // ?autoscan=5 nell'indirizzo dell'anteprima: avviso della scansione all'avvio con 5 secondi
    start: () => ({ state: state(), session, autoScan: Number(new URLSearchParams(location.search).get('autoscan') ?? -1) }),
    state, adapters: () => adapters, settings: () => settings, adapterConfigs: () => configs,
    computer, dashboard, netstat: () => netstat,
    hostLabel: (a) => {
      const h = hosts.find((x) => x.ipAddress === a.ip);
      if (!h) throw new Error(`${a.ip} non è più nella sessione.`);
      h.customName = a.name || ''; h.notes = a.notes || '';
      if ('icon' in a) { h.icon = a.icon || ''; h.iconColor = h.icon ? a.iconColor || '' : ''; }
      return { host: h, state: state() };
    },
    // icone: un campione del catalogo (dev\mdi-sample.js), cercato per nome
    iconsMeta: () => ({ version: '7.4.47', count: (window.MDI_SAMPLE || []).length, categories: [
      { key: '@rete', label: '★ Rete e informatica', count: 120, curated: true }, { key: 'Home Automation', label: 'Casa e domotica', count: 40, curated: false }] }),
    iconsSearch: (a) => {
      const words = String(a.q || '').toLowerCase().split(/\s+/).filter(Boolean);
      const list = (window.MDI_SAMPLE || []).filter((i) => words.every((w) => i.name.includes(w))
        && (a.variant === 'all' || (a.variant === 'outline') === i.name.includes('outline')));
      const offset = a.offset || 0;
      return { total: list.length, items: list.slice(offset, offset + (a.limit || 200)) };
    },
    iconPaths: (a) => Object.fromEntries((window.MDI_SAMPLE || []).filter((i) => (a.names || []).includes(i.name)).map((i) => [i.name, i.path])),
    rescanHost: (a) => {
      const h = hosts.find((x) => x.ipAddress === a.ip);
      if (h) { h.status = 'Online'; h.lastSeen = new Date().toISOString(); }
      const scan = { kind: 'host', target: a.ip, at: new Date().toISOString(), seconds: 0.4, found: h ? 1 : 0, cancelled: false, error: '' };
      return { ip: a.ip, online: !!h, host: h || null, scan, state: state() };
    },
    saveSettings: (a) => Object.assign(settings, a),
    sessionInfo: (a) => { const s = state(); s.session.title = a.title; s.session.notes = a.notes; return { state: s }; },
    checkInternet: () => null, setClipboard: () => null, openUrl: () => null, launch: () => null,
  };

  const view = {
    addEventListener: (type, fn) => target.addEventListener(type, fn),
    postMessage(json) {
      const { id, cmd, args } = JSON.parse(json);
      const fn = handlers[cmd];
      if (!fn) { send({ id, ok: false, error: `Anteprima: comando «${cmd}» non simulato.` }); return; }
      try { send({ id, ok: true, result: fn(args || {}) }); } catch (err) { send({ id, ok: false, error: err.message }); }
    },
  };
  const existing = window.chrome && window.chrome.webview;
  if (existing) {
    // dentro WebView2 (immagini del README dal programma vero): l'oggetto c'è già e non si sostituisce, se ne
    // rimpiazzano i metodi
    for (const key of ['postMessage', 'addEventListener']) {
      try { Object.defineProperty(existing, key, { value: view[key], configurable: true, writable: true }); } catch { existing[key] = view[key]; }
    }
  } else {
    window.chrome = window.chrome || {};
    window.chrome.webview = view;
  }
  window.__mockEvent = event;
})();
