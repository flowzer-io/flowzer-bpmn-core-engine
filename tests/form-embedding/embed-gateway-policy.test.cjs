const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const { resolve } = require('node:path');
const script = resolve(__dirname, '../../deploy/console/embedding-policy.sh');
function run(values) { return spawnSync('sh', [script], { encoding: 'utf8', env: { PATH: process.env.PATH, ...values } }); }
// Testzweck: Ein nicht konfiguriertes Embed ist ein echtes 404, nicht die SPA oder ein Login.
test('embedding without complete opt-in is closed', () => {
  const result = run({}); assert.equal(result.status, 0); assert.match(result.stdout, /location = \/embed\.html/); assert.match(result.stdout, /return 404/);
});
// Testzweck: Exakte Host-Allowlist, CSP-Sandbox und einziger Read-only-Connectpfad; keine Same-Origin-/Eval-Ausnahme.
test('complete opt-in emits isolated exact-host policy', () => {
  const result = run({ FLOWZER_EMBED_API_ORIGIN: 'https://flowzer.example.test', FLOWZER_EMBED_HOST_ORIGINS: 'https://host.example.test https://second.example.test:8443' });
  assert.equal(result.status, 0); assert.match(result.stdout, /frame-ancestors https:\/\/host\.example\.test https:\/\/second\.example\.test:8443;/);
  assert.match(result.stdout, /sandbox allow-scripts;/); assert.match(result.stdout, /connect-src https:\/\/flowzer\.example\.test\/form-embed\/redeem;/);
  assert.match(result.stdout, /Access-Control-Allow-Origin "null"/); assert.doesNotMatch(result.stdout, /allow-same-origin|unsafe-eval|Allow-Credentials/);
});
// Testzweck: Unvollständige oder einschleusbare Runtimewerte dürfen niemals nginx-Konfiguration erzeugen.
test('rejects partial and unsafe origins', () => {
  for (const value of ['https://host.test;add_header', 'http://host.test', 'https://host.test/path', 'https://user@host.test', 'https://host.test?query', 'https://host..test', 'https://-host.test', 'https://host.test:65536', 'https://host.test\nhttps://other.test', 'https://host.test\thttps://other.test'])
    assert.notEqual(run({ FLOWZER_EMBED_API_ORIGIN: 'https://flowzer.example.test', FLOWZER_EMBED_HOST_ORIGINS: value }).status, 0);
  assert.notEqual(run({ FLOWZER_EMBED_HOST_ORIGINS: 'https://host.example.test' }).status, 0);
});

// Testzweck: Eine versehentlich überbreite Allowlist und manipulierte API-Origin
// stoppen den Runtime-Start statt Framing oder Verbindungsziele still zu erweitern.
test('bounds allowlist and validates API origin independently', () => {
  assert.notEqual(run({ FLOWZER_EMBED_API_ORIGIN: 'https://api.test/path', FLOWZER_EMBED_HOST_ORIGINS: 'https://host.test' }).status, 0);
  assert.notEqual(run({ FLOWZER_EMBED_API_ORIGIN: 'https://api.test', FLOWZER_EMBED_HOST_ORIGINS: Array(9).fill('https://host.test').join(' ') }).status, 0);
});

// Testzweck: Auch die einzeln konfigurierte API-Origin darf keine mehrzeiligen
// Werte akzeptieren; grep/awk-Zeilenprüfung allein wäre kein Ein-Origin-Vertrag.
test('rejects CR and LF in API origin before emitting policy', () => {
  for (const value of ['https://api.test\nevil.test', 'https://api.test\r\nevil.test', 'https://api.test\n'])
    assert.notEqual(run({ FLOWZER_EMBED_API_ORIGIN: value, FLOWZER_EMBED_HOST_ORIGINS: 'https://host.test' }).status, 0);
});
