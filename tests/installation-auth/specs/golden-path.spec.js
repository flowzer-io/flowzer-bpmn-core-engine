// Golden Path: frisch installierter Stack, Anmeldung über den BFF, Sitzung, CSRF und Abmeldung.
const { test, expect } = require('@playwright/test');
const { apiRequest } = require('../support/http');
const { fetchInPage, loginWithBrowser, readSession } = require('../support/bff');
const { ensureBaseline, findUser } = require('../support/keycloak');
const { BASE_URL, ISSUER, USERS } = require('../support/constants');

test.describe('Golden Path', () => {
  test.beforeAll(async () => {
    await ensureBaseline();
  });

  // Testzweck: Die Bereitschaftsprobe ueber TLS-Proxy und Konsolen-nginx meldet eine gesunde
  // Installation mit PostgreSQL auf aktuellem Migrationsstand.
  test('Readiness ist Healthy und die Migrationen sind aktuell', async () => {
    const response = await apiRequest('/health/ready');
    expect(response.status).toBe(200);
    const readiness = response.json().result;
    expect(readiness.status).toBe('Healthy');
    expect(readiness.storage).toBe('Ready');
    expect(readiness.details.migrationState).toBe('UpToDate');
    expect(readiness.details.pendingMigrationCount).toBe(0);
  });

  // Testzweck: Ohne Sitzung und ohne Bearer weist ein Fachendpunkt anonym mit 401 ab und
  // leitet nicht auf eine Anmeldeseite um.
  test('Anonymer Fachaufruf wird mit 401 abgewiesen', async () => {
    const response = await apiRequest('/definition/meta');
    expect(response.status).toBe(401);
    expect(response.headers.location).toBeUndefined();
  });

  // Testzweck: Vollstaendige BFF-Anmeldung gegen Keycloak (vertraulicher Code-Flow mit PKCE),
  // Rollen in der Sitzung, Cookie-Attribute, CSRF-Schutz des Logouts und Sitzungsende.
  test('Anmeldung, Sitzung, Cookies, CSRF und Abmeldung', async ({ page, context }) => {
    const { authorizationRequest, formShown } = await loginWithBrowser(page, USERS.alice);
    expect(formShown, 'Keycloak-Anmeldeformular ausgefuellt').toBe(true);

    await test.step('Autorisierungsanfrage: vertraulicher Code-Flow ueber den BFF', async () => {
      expect(authorizationRequest, 'Keycloak-Anmeldeseite wurde angezeigt').toBeDefined();
      expect(`${authorizationRequest.origin}${authorizationRequest.pathname}`).toBe(`${ISSUER}/protocol/openid-connect/auth`);
      const parameters = authorizationRequest.searchParams;
      expect(parameters.get('client_id')).toBe('flowzer-bff');
      if (parameters.has('request_uri')) {
        // Pushed Authorization Request: Der .NET-Handler schickt response_type, redirect_uri und
        // die PKCE-Challenge serverseitig an Keycloak; im Browser steht nur die Referenz.
        // Keycloak erzwingt am Client S256 und die exakte Redirect-URI, der erfolgreiche
        // Ruecksprung belegt beides.
        expect(parameters.get('request_uri')).toMatch(/^urn:ietf:params:oauth:request_uri:/);
        expect(parameters.has('code_challenge')).toBe(false);
      } else {
        expect(parameters.get('response_type')).toBe('code');
        expect(parameters.get('code_challenge_method')).toBe('S256');
        expect(parameters.get('code_challenge')).toBeTruthy();
        expect(parameters.get('redirect_uri')).toBe(`${BASE_URL}/bff/signin-oidc`);
      }
      expect(new URL(page.url()).origin).toBe(BASE_URL);
    });

    await test.step('Sitzung zeigt Identitaet und alle vier Faehigkeiten', async () => {
      const session = await readSession(page);
      expect(session.status).toBe(200);
      const alice = await findUser(USERS.alice.username);
      expect(session.body.id).toBe(alice.id);
      expect(session.body.id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
      for (const capability of ['access', 'modeler', 'operator', 'worker']) {
        expect(session.body.capabilities).toContain(capability);
      }
    });

    await test.step('Sitzungscookie: __Host-, HttpOnly, Secure, SameSite=Lax, ohne Tokens', async () => {
      const cookies = await context.cookies(BASE_URL);
      const sessionCookie = cookies.find(cookie => cookie.name === '__Host-Flowzer-Session');
      expect(sessionCookie, 'Sitzungscookie vorhanden').toBeDefined();
      expect(sessionCookie.httpOnly).toBe(true);
      expect(sessionCookie.secure).toBe(true);
      expect(sessionCookie.sameSite).toBe('Lax');
      expect(sessionCookie.path).toBe('/');
      expect(sessionCookie.domain).toBe('flowzer.test');
      // Keine JWTs im Browser: weder im Sitzungscookie noch in einem anderen Flowzer-Cookie.
      for (const cookie of cookies) {
        expect(cookie.value).not.toMatch(/^eyJ[\w-]+\.eyJ[\w-]+\./);
      }
    });

    await test.step('CSRF: schreibender Aufruf ohne Header wird mit 400 abgewiesen', async () => {
      const withoutToken = await fetchInPage(page, '/bff/logout', { method: 'POST' });
      expect(withoutToken.status).toBe(400);
      expect(withoutToken.headers['content-type']).toContain('application/problem+json');
      expect((await readSession(page)).status, 'Sitzung besteht weiter').toBe(200);
    });

    await test.step('CSRF: mit Token aus /bff/csrf gelingt die Abmeldung', async () => {
      const csrfResponse = await fetchInPage(page, '/bff/csrf');
      expect(csrfResponse.status).toBe(200);
      const csrf = JSON.parse(csrfResponse.text);
      expect(csrf.headerName).toBe('X-Flowzer-CSRF');
      expect(csrf.requestToken).toBeTruthy();

      const csrfCookie = (await context.cookies(BASE_URL)).find(cookie => cookie.name === '__Host-Flowzer-Csrf');
      expect(csrfCookie, 'Antiforgery-Cookie vorhanden').toBeDefined();
      expect(csrfCookie.httpOnly).toBe(true);
      expect(csrfCookie.secure).toBe(true);
      expect(csrfCookie.sameSite).toBe('Strict');

      const logout = await fetchInPage(page, '/bff/logout', {
        method: 'POST',
        headers: { [csrf.headerName]: csrf.requestToken }
      });
      // Vertrag des BffControllers: 204 No Content.
      expect(logout.status).toBe(204);
    });

    await test.step('Nach der Abmeldung ist die Sitzung beendet', async () => {
      expect((await readSession(page)).status).toBe(401);
      expect((await fetchInPage(page, '/definition/meta')).status).toBe(401);
    });

    await test.step('Grenze: Abmeldung ist lokal, die SSO-Sitzung bei Keycloak besteht weiter', async () => {
      // Kein IdP-Logout (offen fuer R1c): Ein erneuter Login kommt ohne Formular zurueck.
      const relogin = await loginWithBrowser(page, USERS.alice);
      expect(relogin.formShown).toBe(false);
      expect((await readSession(page)).status).toBe(200);
      test.info().annotations.push({
        type: 'Grenze',
        description: 'POST /bff/logout beendet nur die Flowzer-Sitzung; die Keycloak-SSO-Sitzung bleibt bestehen.'
      });
    });
  });
});
