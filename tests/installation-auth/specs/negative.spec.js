// Negativfälle auf API-Ebene (Bearer) und Rollenentzug mit laufender BFF-Sitzung.
const { test, expect } = require('@playwright/test');
const { apiRequest, decodeJwtPayload } = require('../support/http');
const { expectCapabilities, loginWithBrowser } = require('../support/bff');
const { ensureBaseline, getUserToken, grantApiRole, removeApiRole } = require('../support/keycloak');
const { ACCESS_TOKEN_LIFETIME_SECONDS, USERS } = require('../support/constants');

// Reiner Modeler-Endpunkt: [Authorize(Policy = Modeler)] ohne Seiteneffekte.
const MODELER_ENDPOINT = '/form/compatibility';

test.describe('Negativfaelle', () => {
  test.beforeAll(async () => {
    await ensureBaseline();
  });

  test.afterAll(async () => {
    // Ausgangszustand auch nach einem fehlgeschlagenen Rollenentzug wiederherstellen.
    await ensureBaseline();
  });

  // Testzweck: Ein gueltig signiertes Token desselben Realms fuer eine fremde Audience wird
  // abgewiesen (401), auch wenn die Person in Flowzer alle Rollen besitzt.
  test('Bearer mit fremder Audience wird mit 401 abgewiesen', async () => {
    const token = await getUserToken(USERS.alice, 'other-audience-cli');
    expect(decodeJwtPayload(token).aud).toBe('other-api');

    const response = await apiRequest('/definition/meta', { bearer: token });
    expect(response.status).toBe(401);
  });

  // Testzweck: Eine angemeldete Person ohne Zugangsrolle erhaelt 403 mit der Einordnung
  // "application" (fuer Flowzer nicht freigeschaltet).
  test('Bearer ohne Zugangsrolle erhaelt 403 application', async () => {
    const token = await getUserToken(USERS.carol);
    expect(decodeJwtPayload(token).resource_access?.['flowzer-api']).toBeUndefined();

    const response = await apiRequest('/definition/meta', { bearer: token });
    expect(response.status).toBe(403);
    expect(response.headers['x-flowzer-access-denied']).toBe('application');
  });

  // Testzweck: Zugang ohne Modellierrolle: Lesen gelingt, ein Modeler-Endpunkt und das Anlegen
  // auf oberster Ebene werden mit 403 "capability" abgewiesen.
  test('Bearer mit Zugang, aber ohne Modeler-Rolle erhaelt 403 capability', async () => {
    const token = await getUserToken(USERS.bob);

    const read = await apiRequest('/definition/meta', { bearer: token });
    expect(read.status, 'Kontrollfall: Zugang vorhanden').toBe(200);

    const policyEndpoint = await apiRequest(MODELER_ENDPOINT, { bearer: token });
    expect(policyEndpoint.status).toBe(403);
    expect(policyEndpoint.headers['x-flowzer-access-denied']).toBe('capability');

    const create = await apiRequest(`/definition/new?name=${encodeURIComponent('Abnahme ohne Modeler')}`, {
      method: 'POST',
      bearer: token
    });
    expect(create.status).toBe(403);
    expect(create.headers['x-flowzer-access-denied']).toBe('capability');
  });

  // Testzweck: Ein Rollenentzug im Identity Provider wirkt auf neue Bearer sofort und auf eine
  // bestehende BFF-Sitzung spaetestens nach Ablauf des Access-Tokens, weil der serverseitige
  // Refresh die Rollen ersetzt; die Sitzung selbst und der Zugang bleiben bestehen.
  test('Rollenentzug wirkt auf neue Bearer und nach Tokenablauf auf die BFF-Sitzung', async ({ page }) => {
    await loginWithBrowser(page, USERS.alice);
    await expectCapabilities(page, { access: true, modeler: true });

    const tokenBefore = await getUserToken(USERS.alice);
    expect((await apiRequest(MODELER_ENDPOINT, { bearer: tokenBefore })).status, 'Kontrollfall vor dem Entzug').toBe(200);

    await removeApiRole(USERS.alice.username, 'modeler');
    try {
      const tokenAfter = await getUserToken(USERS.alice);
      expect(decodeJwtPayload(tokenAfter).resource_access['flowzer-api'].roles).not.toContain('modeler');
      const denied = await apiRequest(MODELER_ENDPOINT, { bearer: tokenAfter });
      expect(denied.status).toBe(403);
      expect(denied.headers['x-flowzer-access-denied']).toBe('capability');

      // Bewusst ueber die volle Access-Token-Laufzeit warten: Das ist der zugesicherte Vertrag.
      await page.waitForTimeout((ACCESS_TOKEN_LIFETIME_SECONDS + 5) * 1_000);
      await expectCapabilities(page, { access: true, modeler: false, operator: true, worker: true });
    } finally {
      await grantApiRole(USERS.alice.username, 'modeler');
    }

    const tokenRestored = await getUserToken(USERS.alice);
    expect((await apiRequest(MODELER_ENDPOINT, { bearer: tokenRestored })).status, 'Rolle wieder erteilt').toBe(200);
    await expectCapabilities(page, { modeler: true });
  });
});
