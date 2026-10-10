// Testzweck: Kalibrierungsdiagnosen ohne echten Browser, OpenSSL, Prozesse oder Socketzugriffe.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

async function failedProbe(failure) {
  let report;
  const blocked = () => { throw new Error('Nicht freigegebene synthetische Operation'); };
  const sandbox = {
    process: { env: {}, argv: ['node', 'synthetic', '/owned/egress-result.json', '--calibrate-local-only'] },
    __dirname: __dirname, module: { exports: {} }, Set, Promise, Error,
    require: name => {
      if (name === 'node:assert/strict') return assert;
      if (name === 'node:path') return path;
      if (name === 'node:vm') return { runInNewContext: blocked };
      if (name === 'node:https') return { createServer: blocked, get: blocked };
      if (name === 'node:fs') return {
        existsSync: () => false,
        writeFileSync: (_output, raw, options) => {
          assert.equal(options.flag, 'wx');assert.equal(options.mode, 0o600);
          assert.equal(report, undefined);report = JSON.parse(raw);
        }
      };
      if (name === 'node:child_process') return { spawnSync: () => {
        if (failure === 'certificate-exit') return { status: 2, stdout: 'never-save-private-key', stderr: 'never-save-private-key' };
        if (failure === 'certificate-format') return { status: 0, stdout: 'never-save-private-key' };
        blocked();
      } };
      if (name === 'playwright') {
        if (failure === 'module') { const error = new Error('never-save-token');error.code = 'MODULE_NOT_FOUND';throw error; }
        return { chromium: { launch: blocked } };
      }
      if (name === 'playwright/package.json') return { version: failure === 'contract' ? '0.0.0' : '1.59.1' };
      if (name === './loopback-proxy') return { openProxy: blocked };
      if (name === './restricted-test') return { installNetworkGuards: blocked };
      blocked();
    }
  };
  try {
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'egress-probe.js'), 'utf8'), sandbox);
    await sandbox.main();
    assert.fail('Die synthetische fehlgeschlagene Probe muss geschlossen scheitern.');
  } catch { /* Absichtliches Scheitern; nicht der Fehlertext, sondern der echte geschlossene Beleg zählt. */ }
  assert.ok(report, 'Auch Vorstufenfehler benötigen den eigenen geschlossenen Zahlenbericht');
  assert.equal(report.success, false);assert.equal(report.marker_assertion_red, 0);
  assert.equal(JSON.stringify(report).includes('never-save'), false);
  return report;
}

test('Fehlender Playwright-Import schreibt geschlossene I/O-Diagnose vor jeder Runtime', async () => {
  // Testzweck: MODULE_NOT_FOUND zählt nicht als erwarteter Marker-RED und darf nie seine Rohmeldung speichern.
  const report = await failedProbe('module');
  assert.deepEqual(report.failures, [{ phase: 'modules', error: 'io', exit_code: null }]);
});

test('Falscher gepinnter Browservertrag bleibt Validierung statt Kalibrierungserfolg', async () => {
  // Testzweck: Ein echter Vertrags-Assert vor Zertifikat/Listenern braucht einen Bericht, nicht Exit1 ohne Diagnose.
  const report = await failedProbe('contract');
  assert.deepEqual(report.failures, [{ phase: 'contract', error: 'validation', exit_code: null }]);
});

test('Synthetischer Zertifikatprozess erhält nur seinen wirklich beobachteten Exitstatus', async () => {
  // Testzweck: Kein OpenSSL läuft; Mock-Exit2 bleibt exakt2, nie Browserfehler, Null oder Schlüsselmaterial.
  const report = await failedProbe('certificate-exit');
  assert.deepEqual(report.failures, [{ phase: 'certificate', error: 'process_exit', exit_code: 2 }]);
});

test('Unvollständiges synthetisches TLS-Material bleibt wertefreie Validierung', async () => {
  // Testzweck: Auch Exit0 mit unbrauchbarem synthetischem Material ist keine bestandene Kalibrierung.
  const report = await failedProbe('certificate-format');
  assert.deepEqual(report.failures, [{ phase: 'certificate', error: 'validation', exit_code: null }]);
});

async function cleanupProbe(closeErrors) {
  let report;let markerHandle;const closed=[];
  const privateFixture='-----BEGIN PRIVATE KEY-----\nsynthetic-not-a-key\n-----END PRIVATE KEY-----\n'
    +'-----BEGIN CERTIFICATE-----\nsynthetic-not-a-certificate\n-----END CERTIFICATE-----';
  const error=()=>{const value=new Error('never-save-private');value.code='EACCES';return value;};
  const close=name=>{closed.push(name);if(closeErrors.includes(name))throw error();};
  const proxy={server:'http://127.0.0.1:7777',bypass:'<-loopback>',blocked:()=>0,close:async()=>close('proxy')};
  const page={goto:async url=>{if(url.endsWith('/redirect-ip'))markerHandle({}, {writeHead(){},end(){}});return {status:()=>200};},title:async()=>'owned-probe'};
  const context={newPage:async()=>page,close:async()=>{}};
  let listenerIndex=0;
  const https={createServer:(_tls,handle)=>{
    const name=listenerIndex++===0?'marker':'allowed';if(name==='marker')markerHandle=handle;
    return {on(){return this;},once(){return this;},listen(_port,_host,done){done();},
      address:()=>({port:name==='marker'?45678:8443}),close(done){close(name);done();}};
  },get:(_options,done)=>{
    markerHandle({}, {writeHead(){},end(){}});done({resume(){},on(_event,callback){callback();}});
    return {once(){return this;},setTimeout(){return this;}};
  }};
  const sandbox={process:{env:{},argv:['node','synthetic','/owned/egress-result.json','--calibrate-local-only']},
    __dirname, module:{exports:{}},Set,Promise,Error,require:name=>{
      if(name==='node:assert/strict')return assert;
      if(name==='node:path')return path;
      if(name==='node:https')return https;
      if(name==='node:child_process')return {spawnSync:()=>({status:0,stdout:privateFixture})};
      if(name==='node:fs')return {existsSync:()=>false,
        readFileSync:()=>"new Set(['flowzer.test:8443', 'auth.flowzer.test:8443']); net.connect({ host: '127.0.0.1', port: 8443 })",
        writeFileSync:(_output,raw)=>{assert.equal(report,undefined);report=JSON.parse(raw);}};
      if(name==='node:vm')return {runInNewContext:(_source,scope)=>{scope.module.exports.openProxy=async()=>proxy;}};
      if(name==='playwright')return {chromium:{launch:async()=>({newContext:async()=>context,close:async()=>close('browser')})}};
      if(name==='playwright/package.json')return {version:'1.59.1'};
      if(name==='./loopback-proxy')return {openProxy:async()=>proxy};
      if(name==='./restricted-test')return {installNetworkGuards:async()=>()=>0};
      assert.fail('Nicht freigegebene synthetische Operation');
    }};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'egress-probe.js'),'utf8'),sandbox);
  await assert.rejects(sandbox.main());
  assert.deepEqual(closed,['browser','proxy','allowed','marker']);
  assert.equal(report.success,false);assert.equal(report.marker_assertion_red,1);assert.equal(report.marker_requests,1);
  assert.equal(JSON.stringify(report).includes('never-save'),false);assert.equal(JSON.stringify(report).includes('PRIVATE KEY'),false);
  return report;
}

test('Marker-RED verbirgt keine Closefehler und jeder eigene Besitz erhält seinen Schließversuch', async () => {
  // Testzweck: Rein gemockte Listener/Browser; Primär-Assert bleibt zuerst, höchstens3 geschlossene Ursachen folgen.
  for(const errors of [['browser'],['proxy','marker'],['browser','proxy','allowed','marker']]) {
    const report=await cleanupProbe(errors);
    assert.deepEqual(report.failures,[{phase:'redirect_ip_blocked',error:'validation',exit_code:null},
      ...errors.slice(0,2).map(name=>({phase:name+'_close',error:'io',exit_code:null}))]);
  }
});

test('Synthetisches TLS-Fixture schreibt nur über den OpenSSL-Stdout-Sentinel in den RAM-Pipe', () => {
  // Testzweck: Kein echter Prozess/Schlüssel; /dev/stdout darf bei Node-Pipes niemals als Datei wieder geöffnet werden.
  const material='-----BEGIN PRIVATE KEY-----\nsynthetic-not-a-key\n-----END PRIVATE KEY-----\n'
    +'-----BEGIN CERTIFICATE-----\nsynthetic-not-a-certificate\n-----END CERTIFICATE-----';
  const seen=[];
  const sandbox={process:{env:{},argv:[]},module:{exports:{}},Set,Promise,Error,require:name=>{
    if(name==='node:assert/strict')return assert;
    if(name==='node:path')return path;
    if(name==='node:child_process')return {spawnSync:(command,args,options)=>{
      seen.push({command,args:JSON.parse(JSON.stringify(args)),options:JSON.parse(JSON.stringify(options))});
      return {status:0,stdout:material};
    }};
    if(name==='node:fs'||name==='node:https'||name==='node:vm')return {};
    if(name==='./loopback-proxy')return {openProxy:()=>assert.fail('Kein Listener')};
    assert.fail('Kein Package-/Browserimport bei reiner Zertifikat-Unit');
  }};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'egress-probe.js'),'utf8'),sandbox);
  const result=sandbox.module.exports.certificate();assert.ok(result.key&&result.cert);
  assert.deepEqual(seen,[{command:'openssl',args:['req','-x509','-newkey','rsa:2048','-nodes','-days','1',
    '-subj','/CN=flowzer.test','-keyout','-','-out','-'],options:{encoding:'utf8',timeout:15000,
    maxBuffer:65536,stdio:['ignore','pipe','ignore']}}]);
});
