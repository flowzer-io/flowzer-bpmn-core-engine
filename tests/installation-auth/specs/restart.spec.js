// API-Neustart: prozesslokale Sitzungen enden sauber, der Schlüsselring bleibt erhalten.
const { test, expect } = require('@playwright/test');
const { composeOrThrow } = require('../support/compose');
const { apiRequest, waitFor } = require('../support/http');
const { expectCapabilities, loginWithBrowser, readSession } = require('../support/bff');
const { ensureBaseline } = require('../support/keycloak');
const { USERS } = require('../support/constants');

const KEYRING_PATH = '/var/lib/flowzer/data-protection';

function listKeyringFiles() {
  return composeOrThrow(['exec', '-T', 'api', 'ls', '-1', KEYRING_PATH], { timeoutMs: 30_000 })
    .split('\n')
    .map(line => line.trim())
    .filter(line => /^key-[0-9a-f-]+\.xml$/.test(line))
    .sort();
}

test.describe('API-Neustart', () => {
  test.beforeAll(async () => {
    await ensureBaseline();
  });

  // Testzweck: Nach `docker compose restart api` liefert eine vorher gueltige Cookie-Sitzung
  // 401 statt 500, die Installation wird wieder bereit, der Data-Protection-Schluesselring im
  // Volume bleibt unveraendert erhalten und eine erneute Anmeldung gelingt.
  test('Neustart beendet Sitzungen sauber und behaelt den Schluesselring', async ({ page }) => {
    await loginWithBrowser(page, USERS.bob);
    await expectCapabilities(page, { access: true, modeler: false });
    const keysBefore = listKeyringFiles();
    expect(keysBefore.length, 'Schluesseldatei im Keyring-Volume').toBeGreaterThan(0);

    composeOrThrow(['restart', 'api'], { timeoutMs: 180_000 });

    await waitFor('API nach Neustart bereit', async () => {
      const response = await apiRequest('/health/ready', { timeoutMs: 5_000 });
      return response.status === 200 && response.json().result.status === 'Healthy';
    }, { timeoutMs: 180_000, intervalMs: 2_000 });

    const oldSession = await readSession(page);
    expect(oldSession.status, 'alte Sitzung nach Neustart').toBe(401);

    expect(listKeyringFiles(), 'Keyring nach Neustart').toEqual(keysBefore);

    // Die SSO-Sitzung bei Keycloak besteht weiter; die erneute Anmeldung gelingt ohne Formular.
    const relogin = await loginWithBrowser(page, USERS.bob);
    expect(relogin.formShown).toBe(false);
    const session = await expectCapabilities(page, { access: true, modeler: false });
    expect(session.name).toBeTruthy();
  });
});
