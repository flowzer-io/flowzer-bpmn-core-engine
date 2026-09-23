// Browserseitige Hilfen für den BFF: Anmeldung über die echte Keycloak-Seite und Aufrufe aus
// dem Seitenkontext. Die Aufrufe laufen per fetch in Chromium, damit Cookies, Origin und
// SameSite genau so wirken wie in der Konsole.
const { expect } = require('@playwright/test');

const FLOWZER_HOST = 'flowzer.test';
const AUTH_HOST = 'auth.flowzer.test';
const LOGIN_STEP_TIMEOUT_MS = 45_000;

function isConsoleUrl(url) {
  return url.hostname === FLOWZER_HOST && !url.pathname.startsWith('/bff/');
}

/**
 * Meldet eine Testperson an. Besteht bereits eine SSO-Sitzung bei Keycloak, kehrt der Browser
 * ohne Formular zurück; beide Wege enden auf der Konsole unter flowzer.test.
 * Liefert die beobachtete Autorisierungsanfrage und ob das Anmeldeformular nötig war.
 */
async function loginWithBrowser(page, user, { returnTo = '/' } = {}) {
  let authorizationRequest;
  const onRequest = request => {
    const url = new URL(request.url());
    if (url.hostname === AUTH_HOST && url.pathname.endsWith('/protocol/openid-connect/auth')) {
      authorizationRequest = url;
    }
  };
  page.on('request', onRequest);

  try {
    await page.goto(`/bff/login?returnTo=${encodeURIComponent(returnTo)}`);

    // Mit SSO-Sitzung zeigt Keycloak nur kurz die sich selbst absendende form_post-Seite;
    // deshalb auf das Formular ODER die Konsole warten, nicht auf den momentanen Host.
    // Promise.any: Es zaehlt das erste erfolgreiche Ergebnis; ein Fehler nur, wenn beide
    // Wege scheitern. Der erste Login nach dem Start von Keycloak kann laenger dauern.
    const usernameField = page.locator('input[name="username"]');
    let outcome;
    try {
      outcome = await Promise.any([
        usernameField.waitFor({ state: 'visible', timeout: LOGIN_STEP_TIMEOUT_MS }).then(() => 'form'),
        page.waitForURL(isConsoleUrl, { timeout: LOGIN_STEP_TIMEOUT_MS }).then(() => 'console')
      ]);
    } catch {
      throw new Error(`Anmeldung fuer ${user.username}: weder Anmeldeformular noch Konsole erreicht (${page.url()}).`);
    }

    const formShown = outcome === 'form' && !isConsoleUrl(new URL(page.url()));
    if (formShown) {
      await usernameField.fill(user.username);
      await page.locator('input[name="password"]').fill(user.password);
      await page.locator('#kc-login').click();
    }

    await page.waitForURL(isConsoleUrl, { timeout: LOGIN_STEP_TIMEOUT_MS });
    return { authorizationRequest, formShown };
  } finally {
    page.off('request', onRequest);
  }
}

/** fetch im Seitenkontext (gleiche Origin, Cookies inklusive). */
async function fetchInPage(page, path, init = {}) {
  return page.evaluate(async ({ path: targetPath, init: requestInit }) => {
    const response = await fetch(targetPath, { credentials: 'same-origin', cache: 'no-store', ...requestInit });
    const text = await response.text();
    return {
      status: response.status,
      headers: Object.fromEntries(response.headers.entries()),
      text
    };
  }, { path, init });
}

/**
 * Cookie-Header für Aufrufe außerhalb der Seite (Node-Seite), z. B. um einen fremden Origin
 * zu setzen, den ein Browser-fetch nie senden würde.
 */
async function cookieHeaderFor(context, url) {
  const cookies = await context.cookies(url);
  return cookies.map(cookie => `${cookie.name}=${cookie.value}`).join('; ');
}

async function readSession(page) {
  const response = await fetchInPage(page, '/bff/session');
  return { status: response.status, body: response.status === 200 ? JSON.parse(response.text) : null };
}

async function expectCapabilities(page, expected) {
  const session = await readSession(page);
  expect(session.status, 'GET /bff/session').toBe(200);
  for (const [capability, present] of Object.entries(expected)) {
    expect(session.body.capabilities.includes(capability), `Faehigkeit ${capability}`).toBe(present);
  }

  return session.body;
}

module.exports = { cookieHeaderFor, expectCapabilities, fetchInPage, loginWithBrowser, readSession };
