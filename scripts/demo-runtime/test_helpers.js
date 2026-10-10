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
  assert.equal(typeof bound._egress, 'function', 'Proxy-Lebenszeitfixture fehlt');
  const options = await bound.proxy({ _egress: {server:'http://127.0.0.1:7777',bypass:'<-loopback>'} }, value => value);
  assert.equal(options.server, 'http://127.0.0.1:7777');assert.equal(options.bypass, '<-loopback>');
});
