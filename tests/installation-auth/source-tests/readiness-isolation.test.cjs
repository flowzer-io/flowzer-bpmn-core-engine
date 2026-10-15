// Hermetische Regression der Spec-Registrierung, keine Keycloak-/TLS-/Runtime-Abnahme.
// Der echte Readiness-Testkörper wird ausgeführt; nur I/O und Playwright-Registrierung
// sind isoliert. So kann ein kaputtes Auth-Fixture das Installationsgate nicht verdecken.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const SPEC_PATH = path.join(__dirname, '../specs/golden-path.spec.js');
const READINESS_TITLE = 'Readiness ist Healthy und die Migrationen sind aktuell';
const BASELINE_ERROR = new Error('synthetic admin fixture unavailable');

/**
 * Registriert die echte Spec mit isolierten Abhängigkeiten. Beschränkte Describe-/
 * beforeAll-Semantik genügt hier; kein Ersatz für die eigentliche Playwright-Abnahme.
 * Scope-Referenzen bleiben erhalten, damit später deklarierte Hooks ebenfalls gelten.
 */
function registerGoldenPath(response) {
  const root = { beforeAll: [] };
  const scopes = [root];
  const registrations = [];
  const calls = { baseline: 0, requests: [] };
  const registerTest = (title, body) => registrations.push({ title, body, scopes: [...scopes] });
  registerTest.describe = (_title, body) => {
    scopes.push({ beforeAll: [] });
    try {
      body();
    } finally {
      scopes.pop();
    }
  };
  registerTest.beforeAll = body => scopes.at(-1).beforeAll.push(body);

  const dependencies = {
    '@playwright/test': {
      test: registerTest,
      expect: actual => ({ toBe: expected => assert.strictEqual(actual, expected) })
    },
    '../support/http': {
      apiRequest: async pathname => {
        calls.requests.push(pathname);
        assert.strictEqual(pathname, '/health/ready');
        return response;
      }
    },
    '../support/keycloak': {
      ensureBaseline: async () => {
        calls.baseline += 1;
        throw BASELINE_ERROR;
      }
    },
    '../support/bff': {},
    '../support/constants': {}
  };

  new vm.Script(fs.readFileSync(SPEC_PATH, 'utf8'), { filename: SPEC_PATH }).runInNewContext({
    require: name => {
      assert.ok(Object.hasOwn(dependencies, name), `Unexpected dependency: ${name}`);
      return dependencies[name];
    }
  });

  return { registrations, calls };
}

/** Führt sämtliche Vorbedingungen und den tatsächlich registrierten Testkörper aus. */
async function runRegistered(registration) {
  assert.ok(registration, 'Expected test registration must exist');
  for (const scope of registration.scopes) {
    for (const beforeAll of scope.beforeAll) {
      await beforeAll();
    }
  }
  await registration.body({});
}

function healthyResponse() {
  return {
    status: 200,
    json: () => ({ result: {
      status: 'Healthy',
      storage: 'Ready',
      details: { migrationState: 'UpToDate', pendingMigrationCount: 0 }
    } })
  };
}

// Testzweck: Readiness erreicht trotz bewusst defektem Admin-Fixture seine eigene API und
// bleibt unabhängig von Keycloak-Adminoperationen; der Installationsvertrag bleibt exakt.
test('Readiness runs without the unavailable Keycloak baseline', async () => {
  const { registrations, calls } = registerGoldenPath(healthyResponse());
  await runRegistered(registrations.find(item => item.title === READINESS_TITLE));
  assert.strictEqual(calls.baseline, 0);
  assert.deepStrictEqual(calls.requests, ['/health/ready']);
});

for (const title of [
  'Anonymer Fachaufruf wird mit 401 abgewiesen',
  'Anmeldung, Sitzung, Cookies, CSRF und Abmeldung',
  'Schreibender Cookie-Aufruf mit fremdem Origin wird trotz CSRF-Token abgewiesen',
  'Ungueltiger Bearer faellt bei bestehender Cookie-Sitzung nicht auf das Cookie zurueck'
]) {
  // Testzweck: Kein Auth-Test verliert sein Baseline-Gate durch die Trennung; ein
  // fehlschlagendes Admin-Fixture stoppt jeden vor fachlichem API- oder Browser-I/O.
  test(`Authentication retains the baseline gate: ${title}`, async () => {
    const { registrations, calls } = registerGoldenPath(healthyResponse());
    const registration = registrations.find(item => item.title === title);
    await assert.rejects(() => runRegistered(registration), error => error === BASELINE_ERROR);
    assert.strictEqual(calls.baseline, 1);
    assert.deepStrictEqual(calls.requests, []);
  });
}

const invalidResponses = [
  ['non-success HTTP status', response => { response.status = 503; }],
  ['unhealthy storage probe', response => { response.json().result.status = 'Unhealthy'; }],
  ['unready storage', response => { response.json().result.storage = 'Unavailable'; }],
  ['pending migrations', response => { response.json().result.details.migrationState = 'Pending'; }],
  ['unknown migrations', response => { response.json().result.details.migrationState = 'Unknown'; }],
  ['nonzero pending count', response => { response.json().result.details.pendingMigrationCount = 1; }]
];

for (const [name, invalidate] of invalidResponses) {
  // Testzweck: Jede bisherige Readiness-/Migrationsassertion bleibt als harte
  // Installationsbedingung erhalten; keine Lockerung als Ausweg aus einem roten Lauf.
  test(`Readiness still rejects ${name}`, async () => {
    const response = healthyResponse();
    const payload = response.json();
    response.json = () => payload;
    invalidate(response);
    const { registrations, calls } = registerGoldenPath(response);
    await assert.rejects(
      () => runRegistered(registrations.find(item => item.title === READINESS_TITLE)),
      error => error.code === 'ERR_ASSERTION'
    );
    assert.strictEqual(calls.baseline, 0);
    assert.deepStrictEqual(calls.requests, ['/health/ready']);
  });
}

// Testzweck: Die feste Registrierung und damit die bisherige Source-Reihenfolge
// bleiben unverändert; es werden keine bestehenden Auth-Fälle gelöscht oder ersetzt.
test('Golden Path keeps all five test titles in their original order', () => {
  const { registrations } = registerGoldenPath(healthyResponse());
  assert.deepStrictEqual(registrations.map(item => item.title), [
    READINESS_TITLE,
    'Anonymer Fachaufruf wird mit 401 abgewiesen',
    'Anmeldung, Sitzung, Cookies, CSRF und Abmeldung',
    'Schreibender Cookie-Aufruf mit fremdem Origin wird trotz CSRF-Token abgewiesen',
    'Ungueltiger Bearer faellt bei bestehender Cookie-Sitzung nicht auf das Cookie zurueck'
  ]);
});
