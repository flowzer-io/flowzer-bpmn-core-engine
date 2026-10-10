// Testzweck: Unit-Verträge der zusätzlichen Netz-/Reportergrenzen ohne Playwright, Netz oder Container.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const SafeReporter = require('./safe-reporter');

function fixture() {
  let bound;
  const sandbox = { URL, process: {env:{}}, module: { exports: {} }, require: name => {
    if (name === './loopback-proxy') return {openProxy:async()=>({server:'http://127.0.0.1:7777',bypass:'<-loopback>',blocked:()=>0,close:async()=>{}})};
    assert.equal(name, '@playwright/test');
    return { test: { extend: value => { bound = value; return {}; } },
      expect: (value, message) => ({ toBe: expected => assert.equal(value, expected, message) }) };
  } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'restricted-test.js'), 'utf8'), sandbox);
  return bound;
}

async function request(url, websocket = false) {
  let intercepted; let socket; let continued = 0; let aborted = 0;
  const context = { route: async (_pattern, fn) => { intercepted = fn; },
    routeWebSocket: async (_pattern, fn) => { socket = fn; } };
  await fixture()._networkGuard[0]({ context, _egress: {blocked:()=>0} }, async () => {
    if (websocket) {
      assert.equal(typeof socket, 'function', 'WebSocketgrenze fehlt');
      socket({ close: () => { aborted += 1; } });
    } else {
      await intercepted({ request: () => ({ url: () => url }),
        continue: () => { continued += 1; }, abort: () => { aborted += 1; } });
    }
  });
  return { continued, aborted };
}

test('Nur die beiden lokalen HTTPS-Origins ohne URL-Credentials werden vermittelt', async () => {
  // Testzweck: Fremde Hosts, echte IdPs, andere Ports und versteckte URL-Credentials werden blockiert.
  for (const url of ['https://flowzer.test:8443/health', 'https://auth.flowzer.test:8443/realm']) {
    assert.deepEqual(await request(url), { continued: 1, aborted: 0 });
  }
  for (const url of ['https://demo.tickytask.de/', 'https://auth.maass.it/', 'https://flowzer.test/',
    'http://flowzer.test:8443/', 'https://user:secret@flowzer.test:8443/', 'not-url']) {
    await assert.rejects(request(url), /Keine Browseranfragen außerhalb/);
  }
});

test('Kein WebSocket kann die HTTPS-Anfragegrenze umgehen', async () => {
  // Testzweck: Nebenroute muss geschlossen sein; keine unbeabsichtigten IP-/Host-Verbindungen.
  await assert.rejects(request('wss://foreign.invalid/', true), /Keine Browseranfragen außerhalb/);
});

test('Reporter persistiert ausschließlich Zähler und überschreibt nie einen Beleg', () => {
  // Testzweck: Titel, Attachments, Fehler und Tokens dürfen auch bei Fehlern kein Artefakt verlassen.
  const folder = fs.mkdtempSync(path.join(os.tmpdir(), 'flowzer-report-unit-'));
  const previous = process.env.FLOWZER_RUNTIME_REPORT;
  try {
    process.env.FLOWZER_RUNTIME_REPORT = path.join(folder, 'auth-result.json');
    const reporter = new SafeReporter();
    reporter.onBegin({}, { allTests: () => [1, 2] });
    reporter.onTestEnd({ title: 'never-save-token' }, { status: 'passed', error: 'never-save-token' });
    reporter.onTestEnd({}, { status: 'failed', attachments: ['never-save-token'] });
    reporter.onError({ message: 'never-save-token' });
    reporter.onStdOut('never-save-token');reporter.onStdErr('never-save-token');
    reporter.onEnd({ status: 'failed', errors: ['never-save-token'] });
    const raw = fs.readFileSync(process.env.FLOWZER_RUNTIME_REPORT, 'utf8');
    assert.deepEqual(JSON.parse(raw), { total: 2, passed: 1, failed: 1, skipped: 0, interrupted: 0, errors: 1, success: false });
    assert.ok(!raw.includes('never-save-token'));
    assert.throws(() => reporter.onEnd({ status: 'passed' }), /EEXIST/);
    assert.equal(fs.readFileSync(process.env.FLOWZER_RUNTIME_REPORT, 'utf8'), raw);
  } finally {
    if (previous === undefined) delete process.env.FLOWZER_RUNTIME_REPORT;
    else process.env.FLOWZER_RUNTIME_REPORT = previous;
    fs.rmSync(folder, { recursive: true });
  }
});

test('Eigener CONNECT-Proxy wird als tatsächliche Contextoption eingebunden', async () => {
  // Testzweck: Reine Originroute genügt nicht; Context muss zwingend den hopfesten Proxy verwenden.
  const bound = fixture();
  assert.equal(typeof bound.proxy, 'function', 'Contextproxy fehlt');
  assert.ok(Array.isArray(bound._egress), 'Worker-Proxy-Lebenszeitfixture fehlt');
  assert.equal(bound._egress[1].scope, 'worker');
  const options = await bound.proxy({ _egress: {server:'http://127.0.0.1:7777',bypass:'<-loopback>'} }, value => value);
  assert.equal(options.server, 'http://127.0.0.1:7777');assert.equal(options.bypass, '<-loopback>');
});

test('Auch der Browserprozess ist vor Contextstart an den lokalen Proxy gebunden', async () => {
  // Testzweck: Browser-Hintergrundverkehr darf keinen ungesicherten direkten Netzwerkpfad haben.
  const bound = fixture();
  assert.ok(Array.isArray(bound.launchOptions), 'Browserprozess-Proxy fehlt');
  assert.equal(bound.launchOptions[1].scope, 'worker');
  let options;
  await bound.launchOptions[0]({ _egress: {server:'http://127.0.0.1:7777',bypass:'<-loopback>'},
    browserName:'chromium', launchOptions: {args:['--disable-background-networking']} }, value => { options=value; });
  assert.equal(options.proxy.server,'http://127.0.0.1:7777');
  assert.equal(options.proxy.bypass,'<-loopback>');
  assert.equal(options.args[0],'--disable-background-networking');
  assert.ok(options.args.includes('--disable-quic'));
  assert.ok(options.args.includes('--force-webrtc-ip-handling-policy=disable_non_proxied_udp'));
});

test('Reporter bindet Fehltests nur an numerische Suiteordinale, niemals Roh-IDs oder Fehler', () => {
  // Testzweck: Stabile TestCase.id bleibt nur RAM-Zuordnung; failed/timedOut exportieren
  // ihre bekannte 1-basierte Position, während Titel, Authfehler und Anhänge ungelesen bleiben.
  const folder=fs.mkdtempSync(path.join(os.tmpdir(),'flowzer-ordinal-unit-'));
  const previous=process.env.FLOWZER_RUNTIME_REPORT;
  try {
    process.env.FLOWZER_RUNTIME_REPORT=path.join(folder,'auth-result.json');
    const tests=[{id:'synthetic-session-id-1'},{id:'synthetic-session-id-2'},{id:'synthetic-session-id-3'}];
    Object.defineProperty(tests[1],'title',{get(){throw new Error('Titel darf nicht gelesen werden');}});
    const reporter=new SafeReporter();reporter.onBegin({}, {allTests:()=>tests});
    reporter.onTestEnd(tests[0],{status:'passed'});
    const failure={status:'failed'};
    Object.defineProperty(failure,'error',{get(){throw new Error('Rohfehler darf nicht gelesen werden');}});
    reporter.onTestEnd({id:tests[1].id},failure);reporter.onTestEnd(tests[2],{status:'timedOut'});
    reporter.onEnd({status:'failed'});
    const raw=fs.readFileSync(process.env.FLOWZER_RUNTIME_REPORT,'utf8');
    assert.deepEqual(JSON.parse(raw),{total:3,passed:1,failed:2,skipped:0,interrupted:0,errors:0,
      success:false,failed_test_indexes:[2,3]});
    assert.ok(!raw.includes('synthetic-session-id'));assert.ok(!raw.includes('Titel'));
  } finally {
    if(previous===undefined)delete process.env.FLOWZER_RUNTIME_REPORT;else process.env.FLOWZER_RUNTIME_REPORT=previous;
    fs.rmSync(folder,{recursive:true});
  }
});

test('Unbekannte oder mehrdeutige Test-ID erhält keine erfundene Ordinalnull', () => {
  // Testzweck: Fehlende/duplizierte Sitzungsidentität bleibt ohne Kennung; bestehende
  // sieben Zählerfelder und wx-Bindung bleiben unverändert, kein Erfolg wird erfunden.
  const folder=fs.mkdtempSync(path.join(os.tmpdir(),'flowzer-ordinal-unknown-'));
  const previous=process.env.FLOWZER_RUNTIME_REPORT;
  try {
    process.env.FLOWZER_RUNTIME_REPORT=path.join(folder,'auth-result.json');
    const reporter=new SafeReporter();reporter.onBegin({}, {allTests:()=>[{id:'same'},{id:'same'}]});
    reporter.onTestEnd({id:'same'},{status:'failed'});reporter.onTestEnd({id:'unknown'},{status:'failed'});
    reporter.onEnd({status:'failed'});
    assert.deepEqual(JSON.parse(fs.readFileSync(process.env.FLOWZER_RUNTIME_REPORT,'utf8')),
      {total:2,passed:0,failed:2,skipped:0,interrupted:0,errors:0,success:false});
  } finally {
    if(previous===undefined)delete process.env.FLOWZER_RUNTIME_REPORT;else process.env.FLOWZER_RUNTIME_REPORT=previous;
    fs.rmSync(folder,{recursive:true});
  }
});


// Testzweck: Der Beobachter verwendet ausschließlich VM-/HTTP-/Dateimocks.
// Keine echte Anfrage, kein Listener, kein zusätzlicher Runtimeprozess.
function discoveryFixture(output, failWrite=false) {
  if(arguments.length===0)output='/synthetic/auth-result.json';
  const writes=[],attempts=[];
  const sandbox={process:{env:{FLOWZER_RUNTIME_REPORT:output}},module:{exports:{}},require:name=>{
    assert.equal(name,'fs');return {writeFileSync:(file,raw,options)=>{
      attempts.push({file,flag:options.flag,mode:options.mode});
      assert.equal(file,'/synthetic/discovery-result.json');assert.equal(options.flag,'wx');assert.equal(options.mode,0o600);
      if(failWrite)throw new Error('synthetic-write-error-must-not-escape');
      assert.equal(writes.length,0,'Keine Beobachtung überschreiben');writes.push(JSON.parse(raw));
    }};
  }};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'safe-reporter.js'),'utf8'),sandbox);
  const observe=sandbox.module.exports.discoveryRequest;
  assert.equal(typeof observe,'function','Beobachter der bestehenden Discovery-Anfrage fehlt');
  return {observe,writes,attempts};
}

const syntheticIssuer='https://synthetic.invalid/realms/synthetic';
const discoveryUrl=syntheticIssuer+'/.well-known/openid-configuration';

test('Discovery beobachtet dieselbe einzige Anfrage und gibt dieselbe Response unverändert zurück',async()=>{
  // Status/Issuer/PKCE werden nur aus vorhandener Antwort berechnet; Rohmaterial bleibt ungelesen.
  const {observe,writes}=discoveryFixture();const options={headers:{synthetic:'not-exported'}};
  const response={status:200,text:JSON.stringify({issuer:syntheticIssuer,
    code_challenge_methods_supported:['S256'],irrelevant:'synthetic-private-body'} )};
  for(const key of ['headers','json'])Object.defineProperty(response,key,{get(){throw new Error('Rohgetter verboten');}});
  let calls=0;
  assert.equal(await observe((...args)=>{calls++;assert.equal(args[0],discoveryUrl);
    assert.equal(args[1],options);return Promise.resolve(response);},syntheticIssuer,[discoveryUrl,options]),response);
  assert.equal(calls,1);assert.deepEqual(writes,[{status:200,transport_code:null,issuer_matches:true,pkce_s256:true}]);
  assert.ok(!JSON.stringify(writes).includes('synthetic'));
});

test('Alle Nicht-Discovery-Aufrufe bleiben exakt unangetastet und ohne Datei',async()=>{
  // Token/Admin/API und abweichende Query/Credentials dürfen keine Beobachtung oder neuen HTTP-Aufruf auslösen.
  for(const url of [syntheticIssuer+'/protocol/openid-connect/token',discoveryUrl+'?extra',
    'https://other.invalid/.well-known/openid-configuration']) {
    const {observe,writes}=discoveryFixture();const response={};const promise=Promise.resolve(response);let calls=0;
    assert.equal(observe((...args)=>{calls++;assert.equal(args[0],url);return promise;},syntheticIssuer,[url]),promise);
    assert.equal(calls,1);assert.equal(writes.length,0);assert.equal(await promise,response);
  }
});

test('Native Transportcodes werden nur feste Zahlen, Originalfehler bleibt identisch',async()=>{
  // Nie message/stack/address/cause lesen; unbekannter/fehlender Code ist null statt Erfolg oder Nullcode.
  const codes=['ENOTFOUND','EAI_AGAIN','ECONNREFUSED','ECONNRESET','ETIMEDOUT','EPROTO',
    'ERR_TLS_CERT_ALTNAME_INVALID','UNABLE_TO_VERIFY_LEAF_SIGNATURE','SELF_SIGNED_CERT_IN_CHAIN',
    'DEPTH_ZERO_SELF_SIGNED_CERT','UNABLE_TO_GET_ISSUER_CERT_LOCALLY','CERT_HAS_EXPIRED','ERR_SSL_WRONG_VERSION_NUMBER'];
  for(const [index,code] of [...codes,'synthetic-unknown-code',undefined].entries()) {
    const {observe,writes}=discoveryFixture();const error={code};
    for(const key of ['message','stack','address','cause'])Object.defineProperty(error,key,{get(){throw new Error('Rohfehler verboten');}});
    let calls=0;await assert.rejects(observe(()=>{calls++;return Promise.reject(error);},syntheticIssuer,[discoveryUrl]),e=>e===error);
    assert.equal(calls,1);assert.deepEqual(writes,[{status:null,transport_code:index<codes.length?index+1:null,
      issuer_matches:null,pkce_s256:null}]);assert.ok(!JSON.stringify(writes).includes('synthetic'));
  }
});

test('Nicht-JSON und unbekannter Status bleiben nullable, falsche Discovery-Felder bleiben false',async()=>{
  // Keine erfundene erfolgreiche Antwort bei Transport-/Parsefehlern; normale 503 ist tatsächlich 503.
  for(const [response,expected] of [
    [{status:503,text:'synthetic-non-json'},{status:503,transport_code:null,issuer_matches:null,pkce_s256:null}],
    [{status:200,text:'{}'},{status:200,transport_code:null,issuer_matches:false,pkce_s256:false}],
    [{status:200,text:JSON.stringify({issuer:'other',code_challenge_methods_supported:['plain']})},
      {status:200,transport_code:null,issuer_matches:false,pkce_s256:false}],
    [{status:true,text:'{}'},{status:null,transport_code:null,issuer_matches:null,pkce_s256:null}]]) {
    const {observe,writes}=discoveryFixture();assert.equal(await observe(()=>Promise.resolve(response),syntheticIssuer,[discoveryUrl]),response);
    assert.deepEqual(writes,[expected]);
  }
});

test('Beobachtungs-Dateifehler ersetzen weder Originalantwort noch Originalfehler und haben keinen Fallback',async()=>{
  // wx/0600 und feste eigene Reportbindung; fehlende/böse Bindung führt zu keiner Datei/URL-/Rohfehlerausgabe.
  const response={status:200,text:'{}'};const error={code:'ECONNREFUSED'};
  for(const output of ['/synthetic/auth-result.json',undefined,'relative/auth-result.json','/synthetic/../auth-result.json','/synthetic/other.json']) {
    const {observe,writes,attempts}=discoveryFixture(output,true);
    assert.equal(await observe(()=>Promise.resolve(response),syntheticIssuer,[discoveryUrl]),response);
    await assert.rejects(observe(()=>Promise.reject(error),syntheticIssuer,[discoveryUrl]),e=>e===error);
    assert.equal(writes.length,0);
    assert.equal(attempts.length,output==='/synthetic/auth-result.json'?2:0);
    for(const row of attempts)assert.deepEqual(row,{file:'/synthetic/discovery-result.json',flag:'wx',mode:0o600});
  }
});
