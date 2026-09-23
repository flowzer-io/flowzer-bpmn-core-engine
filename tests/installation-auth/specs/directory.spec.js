// Lesender Keycloak-Verzeichnisabgleich: manueller Lauf, workflowgebundene Suche und die
// dokumentierte Grenze bei deaktivierten Personen.
const { test, expect } = require('@playwright/test');
const { apiRequest, decodeJwtPayload, httpRequest, waitFor } = require('../support/http');
const { ensureBaseline, getUserToken, passwordGrant, setUserEnabled } = require('../support/keycloak');
const { expectCapabilities, loginWithBrowser, readSession } = require('../support/bff');
const { AUTH_URL, REALM, USERS } = require('../support/constants');

async function directoryStatus(token) {
  const response = await apiRequest('/identity-directory/status', { bearer: token });
  expect(response.status, 'GET /identity-directory/status').toBe(200);
  return response.json().result;
}

/**
 * Stellt einen manuellen Lauf ein und wartet, bis ein neuer erfolgreicher Stand publiziert ist.
 * 409 heisst nur, dass lokal bereits ein Lauf aktiv ist (z. B. der Startlauf); dann erneut.
 */
async function synchronizeDirectory(token) {
  // Der Vergleichsstand wird unmittelbar vor dem angenommenen Aufruf gelesen: Nach einem 202
  // war lokal kein anderer Lauf reserviert, die naechste neue Generation stammt also von hier.
  let before;
  const accepted = await waitFor('POST /identity-directory/sync angenommen', async () => {
    before = await directoryStatus(token);
    const response = await apiRequest('/identity-directory/sync', { method: 'POST', bearer: token });
    if (response.status === 409) {
      return null;
    }

    expect(response.status).toBe(202);
    return response.json().result;
  }, { timeoutMs: 60_000, intervalMs: 1_000 });
  expect(accepted.state).toBe('queued');

  return waitFor('Verzeichnisabgleich erfolgreich abgeschlossen', async () => {
    const status = await directoryStatus(token);
    if (status.state === 'failed' && status.failedAtUtc !== before.failedAtUtc) {
      throw new Error(`Abgleich fehlgeschlagen: ${status.errorCode} ${status.errorMessage}`);
    }

    const newGeneration = status.activeGenerationId && status.activeGenerationId !== before.activeGenerationId;
    return status.state === 'succeeded' && newGeneration ? status : null;
  }, { timeoutMs: 60_000, intervalMs: 500 });
}

async function searchSubjects(token, definitionId, query) {
  const response = await apiRequest(
    `/identity-directory/workflows/${encodeURIComponent(definitionId)}/subjects?query=${encodeURIComponent(query)}&kind=user&limit=20`,
    { bearer: token });
  expect(response.status, 'workflowgebundene Subjektsuche').toBe(200);
  return response.json().result.items;
}

test.describe.serial('Verzeichnisabgleich', () => {
  let definitionId;

  test.beforeAll(async () => {
    await ensureBaseline();
    const aliceToken = await getUserToken(USERS.alice);
    const response = await apiRequest(`/definition/new?name=${encodeURIComponent('Abnahme Verzeichnissuche')}`, {
      method: 'POST',
      bearer: aliceToken
    });
    expect(response.status, 'Workflow fuer die Suche anlegen').toBe(200);
    definitionId = response.json().result.definitionId;
  });

  test.afterAll(async () => {
    await ensureBaseline();
    // Den aktiven Stand wieder an den Ausgangszustand angleichen (fuer --keep und Folgelaeufe).
    await synchronizeDirectory(await getUserToken(USERS.alice));
  });

  // Testzweck: Das Servicekonto des Abgleichs ist in Keycloak nur lesend berechtigt: Sein Token
  // traegt ausschliesslich die explizit zugeordneten Leserechte (fullScopeAllowed=false), und ein
  // Schreibversuch an der Admin-API wird abgewiesen (Vorgabe aus OPERATIONS.md).
  test('Servicekonto des Abgleichs darf nicht schreiben', async () => {
    const tokenResponse = await httpRequest(`${AUTH_URL}/realms/${REALM}/protocol/openid-connect/token`, {
      method: 'POST',
      form: {
        grant_type: 'client_credentials',
        client_id: 'flowzer-directory',
        client_secret: 'flowzer-directory-test-secret-not-for-production'
      }
    });
    expect(tokenResponse.status).toBe(200);
    const serviceToken = tokenResponse.json().access_token;
    const resourceAccess = decodeJwtPayload(serviceToken).resource_access;
    expect(Object.keys(resourceAccess)).toEqual(['realm-management']);
    // view-users enthaelt in Keycloak query-users als zusammengesetzte Rolle.
    expect([...resourceAccess['realm-management'].roles].sort()).toEqual(['query-groups', 'query-users', 'view-users']);

    const read = await httpRequest(`${AUTH_URL}/admin/realms/${REALM}/users?max=1`, {
      headers: { authorization: `Bearer ${serviceToken}` }
    });
    expect(read.status, 'Lesen erlaubt').toBe(200);

    const write = await httpRequest(`${AUTH_URL}/admin/realms/${REALM}/users`, {
      method: 'POST',
      headers: { authorization: `Bearer ${serviceToken}` },
      json: { username: 'should-not-exist', enabled: true }
    });
    expect(write.status).toBe(403);
  });

  // Testzweck: Ein Operator stoesst den Abgleich an; der Status meldet danach einen neuen,
  // erfolgreichen Stand mit allen synthetischen Personen, Gruppen und Mitgliedschaften.
  test('Operator startet den Abgleich und der Status zeigt Erfolg', async () => {
    const aliceToken = await getUserToken(USERS.alice);
    const status = await synchronizeDirectory(aliceToken);
    expect(status.enabled).toBe(true);
    expect(status.userCount).toBe(4);
    expect(status.groupCount).toBe(2);
    expect(status.membershipCount).toBe(2);
    expect(status.errorCode).toBeFalsy();

    // Nur Operatoren duerfen den Abgleich starten.
    const bobToken = await getUserToken(USERS.bob);
    const denied = await apiRequest('/identity-directory/sync', { method: 'POST', bearer: bobToken });
    expect(denied.status).toBe(403);
    expect(denied.headers['x-flowzer-access-denied']).toBe('capability');
  });

  // Testzweck: Die workflowgebundene Suche findet eine aktive Person aus Keycloak.
  test('Workflowgebundene Suche findet dave', async () => {
    const aliceToken = await getUserToken(USERS.alice);
    const items = await searchSubjects(aliceToken, definitionId, 'dave');
    const dave = items.find(item => item.username === 'dave');
    expect(dave, 'dave im Suchergebnis').toBeDefined();
    expect(dave.subject.kind).toBe('user');
    expect(dave.isActive).toBe(true);
  });

  // Testzweck: Nach Deaktivierung in Keycloak endet die BFF-Sitzung beim naechsten
  // serverseitigen Refresh, und nach erneutem Abgleich bietet die Suche dave nicht mehr an.
  // Dokumentierte Grenze: Ein vorher ausgestellter Bearer wird weiter angenommen, weil Flowzer
  // Tokens nicht gegen den Provider oder das Verzeichnis nachprueft.
  test('Deaktivierte Person verschwindet aus der Suche; alter Bearer gilt bis Ablauf (Grenze)', async ({ page }) => {
    const aliceToken = await getUserToken(USERS.alice);
    await loginWithBrowser(page, USERS.dave);
    await expectCapabilities(page, { access: true, modeler: false });
    const daveToken = await getUserToken(USERS.dave);
    const daveExpiresAt = decodeJwtPayload(daveToken).exp * 1_000;

    await setUserEnabled(USERS.dave.username, false);

    const newGrant = await passwordGrant(USERS.dave);
    expect(newGrant.status, 'Keycloak stellt dave kein neues Token mehr aus').toBe(400);
    expect(newGrant.body.error).toBe('invalid_grant');

    // Grenze sofort nach der Deaktivierung pruefen, weit innerhalb der 30 s Tokenlaufzeit und
    // unabhaengig von der Dauer des Abgleichs.
    const withOldToken = await apiRequest('/definition/meta', { bearer: daveToken });
    expect(withOldToken.status, 'Grenze: alter Bearer wird nach der Deaktivierung weiter angenommen').toBe(200);
    test.info().annotations.push({
      type: 'Grenze',
      description: 'Deaktivierung in Keycloak entzieht einem ausgestellten Bearer nicht den Zugang; er gilt bis zum Ablauf.'
    });

    // Der Refresh der BFF-Sitzung scheitert an Keycloak mit invalid_grant; die Sitzung endet.
    // Mit 30 s Tokenlaufzeit und 60 s Erneuerungsvorlauf geschieht das bei der naechsten Anfrage.
    expect((await readSession(page)).status, 'BFF-Sitzung nach Deaktivierung').toBe(401);

    await synchronizeDirectory(aliceToken);
    const items = await searchSubjects(aliceToken, definitionId, 'dave');
    expect(items.find(item => item.username === 'dave')).toBeUndefined();

    // Reine Beobachtung ohne Zusicherung: Wie lange die JWT-Pruefung den Bearer ueber exp hinaus
    // annimmt, haengt an ihrer Uhrentoleranz und ist kein Abnahmekriterium.
    await new Promise(resolve => setTimeout(resolve, Math.max(0, daveExpiresAt - Date.now()) + 2_000));
    const afterExpiry = await apiRequest('/definition/meta', { bearer: daveToken });
    const observation = `Alter Bearer 2 s nach exp: HTTP ${afterExpiry.status}`;
    console.log(`[Beobachtung] ${observation}`);
    test.info().annotations.push({ type: 'Beobachtung', description: observation });
  });
});
