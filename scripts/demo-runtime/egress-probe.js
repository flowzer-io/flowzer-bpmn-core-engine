#!/usr/bin/env node
// Testzweck: Echte Chromium-Gegenproben nur gegen eigene kurzlebige Loopback-TLS-/Markerlistener.
// Keine Images, Container, IdPs oder WAN-Ziele. Temporärer Testschlüssel bleibt ausschließlich im Speicher.
const assert = require('node:assert/strict');
const https = require('node:https');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { spawnSync } = require('node:child_process');
const { chromium } = require('playwright');
const { openProxy } = require('./loopback-proxy');
const { installNetworkGuards } = require('./restricted-test');
const BASE = 'https://flowzer.test:8443';

function certificate() {
  // stdout wird nicht protokolliert: Schlüssel und Zertifikat liegen nur in diesem Prozess.
  const result = spawnSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
    '-subj', '/CN=flowzer.test', '-keyout', '/dev/stdout', '-out', '/dev/stdout'],
  { encoding: 'utf8', timeout: 15000, maxBuffer: 65536, stdio: ['ignore', 'pipe', 'ignore'] });
  assert.equal(result.status, 0, 'Synthetisches TLS-Fixture nicht verfügbar');
  const key = result.stdout.match(/-----BEGIN (?:RSA )?PRIVATE KEY-----[\s\S]*?-----END (?:RSA )?PRIVATE KEY-----/);
  const cert = result.stdout.match(/-----BEGIN CERTIFICATE-----[\s\S]*?-----END CERTIFICATE-----/);
  assert.ok(key && cert, 'Synthetisches TLS-Fixture unvollständig');
  return { key: key[0], cert: cert[0] };
}
async function listener(tls, port, handle) {
  const sockets = new Set();const server = https.createServer(tls, handle);
  server.on('connection', socket => { sockets.add(socket);socket.once('close', () => sockets.delete(socket)); });
  server.on('clientError', (_error, socket) => socket.destroy());
  await new Promise((resolve, reject) => {
    server.once('error', () => reject(new Error('Eigener Testport nicht verfügbar.')));
    server.listen(port, '127.0.0.1', resolve);
  });
  return { server, port: server.address().port, close: async () => {
    for (const socket of sockets) socket.destroy();
    await new Promise(resolve => server.close(resolve));
  } };
}
async function ownMarkerControl(port) {
  await new Promise((resolve, reject) => {
    const request = https.get({ host:'127.0.0.1',port,path:'/control',rejectUnauthorized:false,agent:false },
      response => { response.resume();response.on('end', resolve); });
    request.once('error', reject);request.setTimeout(3000, () => request.destroy(new Error('Control unavailable.')));
  });
}
function calibratedLocalProxy(authority, port) {
  // Negative Beweiskalibrierung: nur genau der eigene Marker darf in einer IN-MEMORY-Kopie durch.
  // Selbst diese absichtliche Testmutation besitzt keine DNS-/WAN-Route. Produktbytes bleiben unverändert.
  let source = fs.readFileSync(path.join(__dirname,'loopback-proxy.js'),'utf8');
  const oldSet = "new Set(['flowzer.test:8443', 'auth.flowzer.test:8443'])";
  const oldConnect = "net.connect({ host: '127.0.0.1', port: 8443 })";
  assert.equal(source.split(oldSet).length, 2);assert.equal(source.split(oldConnect).length, 2);
  source = source.replace(oldSet, `new Set(['flowzer.test:8443', 'auth.flowzer.test:8443', '${authority}'])`)
    .replace(oldConnect, `net.connect({ host: '127.0.0.1', port: request.url === '${authority}' ? ${port} : 8443 })`);
  const sandbox = { module:{exports:{}}, require, Set, Promise, Error };
  vm.runInNewContext(source, sandbox);
  return sandbox.module.exports.openProxy;
}
async function main() {
  assert.equal(require('playwright/package.json').version,'1.59.1','Gepinnter Browservertrag fehlt');
  for (const key of ['PLAYWRIGHT_DISABLE_FORCED_CHROMIUM_PROXIED_LOOPBACK','PW_TEST_CONNECT_WS_ENDPOINT',
    'PW_TEST_CONNECT_HEADERS','PW_TEST_REUSE_CONTEXT','NODE_OPTIONS']) assert.ok(!process.env[key], 'Fremder Browsertransport nicht erlaubt');
  const output = path.resolve(process.argv[2] || '');
  assert.equal(path.basename(output),'egress-result.json','Geschlossener Reportpfad fehlt');
  assert.ok(!fs.existsSync(output),'Kein Reportoverwrite');
  const calibrate = process.argv[3] === '--calibrate-local-only';
  assert.ok(process.argv.length === 3 || (process.argv.length === 4 && calibrate), 'Unbekannter Probenmodus');
  const tls = certificate();let markerRequests=0;let workerRequests=0;let marker;let allowed;let proxy;let browser;let phase='fixture';
  const result = { calibrated:calibrate, positive_control_requests:0, allowed_page:0, redirect_ip_blocked:0,
    redirect_host_blocked:0,direct_ip_blocked:0,websocket_blocked:0,serviceworker_blocked:0,marker_requests:0,marker_assertion_red:0,success:false };
  try {
    marker = await listener(tls,0,(_request,response)=> { markerRequests+=1;response.writeHead(200);response.end('owned-marker'); });
    await ownMarkerControl(marker.port);assert.equal(markerRequests,1,'Marker kontrolliert erreichbar');
    result.positive_control_requests=markerRequests;markerRequests=0;
    allowed = await listener(tls,8443,(request,response)=> {
      if (request.url==='/redirect-ip' || request.url==='/redirect-host') {
        const host=request.url==='/redirect-ip'?'127.0.0.1':'forbidden.test';
        response.writeHead(302,{Location:`https://${host}:${marker.port}/marker`});response.end();return;
      }
      if (request.url==='/worker.js') { workerRequests+=1;response.writeHead(200,{'Content-Type':'application/javascript'});response.end("fetch('https://127.0.0.1:'+"+marker.port+");");return; }
      response.writeHead(200,{'Content-Type':'text/html'});response.end('<!doctype html><title>owned-probe</title>');
    });
    const open = calibrate ? calibratedLocalProxy(`127.0.0.1:${marker.port}`,marker.port) : openProxy;
    proxy = await open();
    const proxyOptions = {server:proxy.server,bypass:proxy.bypass};
    browser = await chromium.launch({headless:true,proxy:proxyOptions,args:['--disable-background-networking',
      '--disable-component-update','--disable-quic','--force-webrtc-ip-handling-policy=disable_non_proxied_udp']});
    const context = await browser.newContext({proxy:proxyOptions,ignoreHTTPSErrors:true,serviceWorkers:'block'});
    const blocked = await installNetworkGuards(context);let page=await context.newPage();
    phase='allowed';const response=await page.goto(BASE,{timeout:15000});assert.equal(response.status(),200);assert.equal(await page.title(),'owned-probe');result.allowed_page=1;
    for (const [endpoint,key] of [['/redirect-ip','redirect_ip_blocked'],['/redirect-host','redirect_host_blocked']]) {
      phase=key;const before=proxy.blocked();let failed=false;
      try { await page.goto(BASE+endpoint,{timeout:8000}); } catch { failed=true; }
      // Zuerst der wirkliche Effektbeweis, nicht nur eine Browserfehlermeldung oder Counterattrappe.
      assert.equal(markerRequests,0,'Verbotener eigener Marker erhielt einen Request');
      assert.ok(failed && proxy.blocked()>before,'Redirect scheitert vor neuer Zielverbindung');result[key]=1;
    }
    phase='direct_ip';let before=blocked();let failed=false;
    try { await page.goto(`https://127.0.0.1:${marker.port}/direct`,{timeout:5000}); } catch { failed=true; }
    assert.ok(failed && blocked()>before);assert.equal(markerRequests,0);result.direct_ip_blocked=1;
    // Eine absichtlich fehlgeschlagene Navigation darf keinen Folge-Assert mit ERR_ABORTED verfälschen.
    await page.close();page=await context.newPage();
    phase='websocket';await page.goto(BASE,{timeout:15000});before=blocked();const proxyBefore=proxy.blocked();
    await page.evaluate(port=>new Promise(resolve=> {
      const ws=new WebSocket(`wss://127.0.0.1:${port}/socket`);ws.onclose=()=>resolve(true);ws.onerror=()=>resolve(true);setTimeout(()=>resolve(false),3000);
    }),marker.port);
    assert.ok(blocked()>before || proxy.blocked()>proxyBefore,'WebSocket scheitert an tatsächlicher Netzgrenze');
    assert.equal(markerRequests,0);result.websocket_blocked=1;
    phase='serviceworker';const sw=await page.evaluate(async()=> { const registration=await navigator.serviceWorker.register('/worker.js');
      return { registered:registration!==undefined, existing:(await navigator.serviceWorker.getRegistrations()).length }; });
    assert.deepEqual(sw,{registered:false,existing:0});assert.equal(workerRequests,0);assert.equal(markerRequests,0);result.serviceworker_blocked=1;
    await context.close();result.success=true;
  } catch (error) {
    result.failure_phase=phase;result.assertion_failed=error.code==='ERR_ASSERTION';
    result.failure_source=error.message.startsWith('page.evaluate:')?'evaluation':(error.message.startsWith('page.goto:')?'navigation':'closed_other');
    // Nur der konkrete numerische Marker-Assert zählt als rote Negativkalibrierung.
    if (error.code === 'ERR_ASSERTION' && error.actual > 0 && error.expected === 0
      && error.message.startsWith('Verbotener eigener Marker erhielt einen Request')) result.marker_assertion_red=1;
    throw error;
  } finally {
    if (browser) await browser.close();if (proxy) await proxy.close();
    if (allowed) await allowed.close();if (marker) await marker.close();
    result.marker_requests=markerRequests;
    // Endlicher, eigener Zahlenreport, auch bei Fehlern. Keine Browserexceptions/URLs/TLSmaterial persistieren.
    fs.writeFileSync(output,JSON.stringify(result)+'\n',{flag:'wx',mode:0o600});
  }
}
module.exports = { certificate, listener };
if (require.main === module) main().then(()=>process.stdout.write('Hermetische Chromium-Grenzprobe erfolgreich.\n')).catch(()=> {
  process.stderr.write('Hermetische Chromium-Grenzprobe fehlgeschlagen; kein Runtime-Go.\n');process.exitCode=1;
});
