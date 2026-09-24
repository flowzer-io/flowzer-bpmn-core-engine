// Golden Path: frisch installierter Stack, Anmeldung über den BFF, Sitzung, CSRF und Abmeldung.
const { test, expect } = require('@playwright/test');
const { apiRequest } = require('../support/http');
const { cookieHeaderFor, fetchInPage, loginWithBrowser, logoutWithBrowser, readSession } = require('../support/bff');
const { ensureBaseline, findUser, getUserToken } = require('../support/keycloak');
const { AUTH_URL, BASE_URL, ISSUER, USERS } = require('../support/constants');

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
  // Rollen in der Sitzung, Cookie-Attribute, CSRF-Schutz des Logouts, Sitzungsende und
  // RP-initiated Logout, der auch die SSO-Sitzung bei Keycloak beendet.
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

      // Mit Authentication:Bff:ProviderLogout=true: 200 und die Abmeldeadresse bei Keycloak.
      const logout = await logoutWithBrowser(page);
      expect(logout.status).toBe(200);
      const endSession = new URL(logout.redirectTo);
      expect(endSession.origin).toBe(AUTH_URL);
      expect(endSession.pathname).toBe(`${new URL(ISSUER).pathname}/protocol/openid-connect/logout`);
      expect(endSession.searchParams.get('post_logout_redirect_uri')).toBe(`${BASE_URL}/`);
      expect(endSession.searchParams.get('client_id')).toBe('flowzer-bff');
      expect(endSession.searchParams.get('id_token_hint')).toMatch(/^eyJ[\w-]+\.eyJ[\w-]+\.[\w-]+$/);
      // Keycloak beendet die SSO-Sitzung ohne Rueckfrage und leitet auf die Konsole zurueck.
      expect(logout.finalUrl).toBe(`${BASE_URL}/`);
    });

    await test.step('Nach der Abmeldung ist die Sitzung beendet', async () => {
      expect((await readSession(page)).status).toBe(401);
      expect((await fetchInPage(page, '/definition/meta')).status).toBe(401);
      const cookies = await context.cookies(BASE_URL);
      expect(cookies.find(cookie => cookie.name === '__Host-Flowzer-Session'), 'Sitzungscookie entfernt').toBeUndefined();
    });

    await test.step('Abmeldung beendet auch die SSO-Sitzung bei Keycloak', async () => {
      // Ohne SSO-Sitzung verlangt Keycloak beim naechsten Login wieder das Formular.
      const relogin = await loginWithBrowser(page, USERS.alice);
      expect(relogin.formShown).toBe(true);
      expect((await readSession(page)).status).toBe(200);
    });
  });

  // Testzweck: Ein schreibender Cookie-Aufruf mit fremdem oder fehlendem Origin wird trotz
  // gueltigem CSRF-Token mit 400 abgewiesen; die Sitzung bleibt davon unberuehrt.
  test('Schreibender Cookie-Aufruf mit fremdem Origin wird trotz CSRF-Token abgewiesen', async ({ page, context }) => {
    await loginWithBrowser(page, USERS.bob);
    const csrf = JSON.parse((await fetchInPage(page, '/bff/csrf')).text);
    const cookie = await cookieHeaderFor(context, BASE_URL);
    expect(cookie).toContain('__Host-Flowzer-Session=');
    expect(cookie).toContain('__Host-Flowzer-Csrf=');

    for (const origin of ['https://evil.example', undefined]) {
      const headers = { cookie, [csrf.headerName]: csrf.requestToken, ...(origin ? { origin } : {}) };
      const response = await apiRequest('/bff/logout', { method: 'POST', headers });
      expect(response.status, `Origin ${origin ?? '(fehlt)'}`).toBe(400);
      expect(response.headers['content-type']).toContain('application/problem+json');
      expect(response.json().detail).toBe('Invalid request origin.');
    }

    // Kontrollfall: Derselbe Token aus der Seite (gleiche Origin) beendet die Sitzung. Mit
    // Provider-Logout antwortet der BFF mit 200 und der Abmeldeadresse bei Keycloak.
    expect((await readSession(page)).status, 'Sitzung besteht nach den Ablehnungen').toBe(200);
    const logout = await fetchInPage(page, '/bff/logout', {
      method: 'POST',
      headers: { [csrf.headerName]: csrf.requestToken }
    });
    expect(logout.status).toBe(200);
    const redirectTo = JSON.parse(logout.text).redirectTo;
    expect(redirectTo.startsWith(`${ISSUER}/protocol/openid-connect/logout?`), 'Abmeldeadresse bei Keycloak').toBe(true);
    expect((await readSession(page)).status, 'Sitzung nach der Abmeldung').toBe(401);
  });

  // Testzweck: Ein ungueltiger Bearer (kaputte Signatur) fuehrt bei bestehender Cookie-Sitzung zu
  // 401; die Anfrage faellt nicht auf das Cookie zurueck.
  test('Ungueltiger Bearer faellt bei bestehender Cookie-Sitzung nicht auf das Cookie zurueck', async ({ page, context }) => {
    await loginWithBrowser(page, USERS.bob);
    const cookie = await cookieHeaderFor(context, BASE_URL);

    const withCookie = await apiRequest('/definition/meta', { headers: { cookie } });
    expect(withCookie.status, 'Kontrollfall: Cookie-Sitzung allein').toBe(200);

    const [header, payload, signature] = (await getUserToken(USERS.bob)).split('.');
    const brokenSignature = `${signature.slice(0, -4)}${signature.slice(-4) === 'AAAA' ? 'BBBB' : 'AAAA'}`;
    const brokenToken = `${header}.${payload}.${brokenSignature}`;

    const withBrokenBearer = await apiRequest('/definition/meta', { bearer: brokenToken, headers: { cookie } });
    expect(withBrokenBearer.status).toBe(401);
    expect((await readSession(page)).status, 'Cookie-Sitzung bleibt gueltig').toBe(200);
  });
});
