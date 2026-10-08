const { test, expect } = require('../ui-smoke/node_modules/@playwright/test');
const { createServer } = require('node:http');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');

// Testzweck: Das echte Produktionsbundle des vorhandenen Renderers muss im opaque-origin
// Frame mit eigenem Offline-Setup ohne unsafe-eval, Storage oder Cookie funktionieren
// und Eingaben behalten. Das ist noch kein vollständiger Host-/Directory-Durchstich.
for (const locale of ['de-DE', 'en-US']) {
test.describe(locale, () => {
test.use({ locale });
test('Existing renderer must work under opaque sandbox and strict CSP', async ({ page }) => {
  const root = resolve(__dirname, '.probe-dist');
  const server = createServer((req, res) => {
    if (req.url === '/') { res.setHeader('Content-Type', 'text/html; charset=utf-8'); return res.end('<iframe title="Formular" sandbox="allow-scripts" src="/frame"></iframe>'); }
    if (req.url === '/frame') {
      res.setHeader('Content-Type', 'text/html; charset=utf-8');
      res.setHeader('Content-Security-Policy', "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors 'self'");
      return res.end('<!doctype html><link rel="stylesheet" crossorigin="anonymous" href="/flowzer-console.css"><div id="root"></div><script src="/probe.js"></script>');
    }
    if (req.url === '/probe.js') {
      res.setHeader('Content-Type', 'text/javascript; charset=utf-8');
      return res.end(readFileSync(resolve(root, 'probe.js')));
    }
    if (req.url === '/flowzer-console.css') {
      res.setHeader('Content-Type', 'text/css; charset=utf-8');
      // Nur ein öffentliches statisches Stylesheet, keine API-/Sessionfreigabe.
      // Der Kalender liest CSSOM; ein opaque Frame braucht dafür CORS ohne Cookies.
      res.setHeader('Access-Control-Allow-Origin', 'null');
      return res.end(readFileSync(resolve(root, 'flowzer-console.css')));
    }
    res.statusCode = 404; res.end();
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  try {
    // Reine Browserinstrumentierung: weder Sicherheitsflags noch Cookie-/Storage-
    // Verhalten ändern; auch intern abgefangene CSP-Verstöße müssen das Gate sperren.
    await page.addInitScript(() => {
      self.__flowzerCspViolations = [];
      document.addEventListener('securitypolicyviolation', event =>
        self.__flowzerCspViolations.push(event.violatedDirective));
    });
    const errors = [], blockedResources = [];
    page.on('pageerror', err => { errors.push(err.message); console.log('Sandbox page error:', err.message); });
    page.on('console', msg => { if (msg.type() === 'error') { blockedResources.push(msg.text()); console.log('Sandbox console error:', msg.text()); } });
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    const frame = page.frameLocator('iframe');
    await expect(frame.getByLabel('Antwort', { exact: false })).toHaveValue('Zwischenstand', { timeout: 20000 });
    await frame.getByLabel('Antwort', { exact: false }).fill('Bearbeitung bleibt erhalten');
    await frame.getByRole('button', { name: 'Zwischenstand lesen' }).click();
    await expect(frame.locator('html')).toHaveAttribute('data-saved', 'Bearbeitung bleibt erhalten');
    // Das echte Datumsfeld darf nicht nur vorhanden sein: sein Kalender muss ohne
    // CDN-Skripte/-CSS aufgebaut und bedienbar sein, sonst fehlt der Urlaubsfall.
    await frame.getByLabel('Datum', { exact: false }).click();
    await expect(frame.locator('.flatpickr-calendar.open')).toBeVisible();
    await expect(frame.locator('.flatpickr-weekday').first()).toHaveText(locale === 'de-DE' ? 'Mo' : 'Sun');
    await frame.getByLabel('Antwort', { exact: false }).click();
    await expect(frame.locator('.flatpickr-calendar.open')).toHaveCount(0);
    await frame.getByRole('button', { name: 'Assetgrenze prüfen' }).click();
    await expect(frame.locator('html')).toHaveAttribute('data-asset-denied', 'true');
    expect(errors).toEqual([]);
    expect(blockedResources).toEqual([]);
    expect(await page.frames()[1].evaluate(() => self.__flowzerCspViolations)).toEqual([]);
    const restrictions = await page.frames()[1].evaluate(() => {
      let storageDenied = false, cookieDenied = false;
      try { localStorage.getItem('test'); } catch { storageDenied = true; }
      try { document.cookie; } catch { cookieDenied = true; }
      return { storageDenied, cookieDenied, origin: self.origin };
    });
    expect(restrictions).toEqual({ storageDenied: true, cookieDenied: true, origin: 'null' });
  } finally { server.closeAllConnections(); await new Promise(r => server.close(r)); }
});
});
}
