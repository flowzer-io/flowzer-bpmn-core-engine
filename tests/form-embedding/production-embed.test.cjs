const { test, expect } = require('../ui-smoke/node_modules/@playwright/test');
const { readFileSync } = require('node:fs');
const { spawnSync } = require('node:child_process');
const { resolve } = require('node:path');
const apiOrigin = 'https://flowzer.example.test';
const hostOrigin = 'https://host.example.test';
const taskId = '10000000-0000-4000-8000-000000000001';
const subject = { subject: { kind: 'user', id: 'synthetic-substitute' }, displayName: 'Demo Vertretung', detail: '', isActive: true, isSelectable: true };
const schema = { flowzer: { contractVersion: 2 }, components: [
  { type: 'textfield', key: 'answer', label: 'Antwort', input: true, validate: { required: true } },
  { type: 'datetime', key: 'date', label: 'Datum', input: true, enableTime: false },
  { type: 'flowzerSubject', key: 'substitute', label: 'Vertretung', input: true, flowzer: { subjectSelection: { allowUsers: true, allowGroups: false, activeOnly: true } } },
] };
const snapshot = { userTaskId: taskId, hostOrigin, taskRevision: 7, form: { formData: JSON.stringify(schema) },
  context: {}, draft: { userTaskId: taskId, revision: 1, data: { answer: 'Gespeicherter Zwischenstand' } } };
const policy = spawnSync('sh', [resolve(__dirname, '../../deploy/console/embedding-policy.sh')], {
  encoding: 'utf8', env: { PATH: process.env.PATH, FLOWZER_EMBED_API_ORIGIN: apiOrigin, FLOWZER_EMBED_HOST_ORIGINS: hostOrigin },
});
if (policy.status !== 0) throw new Error('Produktionsrichtlinie konnte nicht erzeugt werden.');
const csp = /Content-Security-Policy "([^"]+)"/.exec(policy.stdout)?.[1];
if (!csp) throw new Error('Produktions-CSP fehlt.');

/** Statisches unverändertes Produktionsbundle plus synthetische Backend-Antworten.
 * HTTPS-Routing verändert keine Browser-Sicherheitsflags, Sandbox, CSP oder TLS-Optionen.
 * Echte Keycloak-/Deployment-/45-Minuten-Abnahme bleibt ein separates Gate. */
async function setup(page, { rejectedLink = false, host = hostOrigin } = {}) {
  const errors = [], violations = [], redemptions = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') violations.push(message.text()); });
  await page.addInitScript(() => {
    self.__cspViolations = [];
    document.addEventListener('securitypolicyviolation', event => self.__cspViolations.push(event.violatedDirective));
  });
  await page.route('**/*', async route => {
    const request = route.request(); const url = new URL(request.url());
    if (url.origin === host && url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: `<!doctype html><script>
      window.calls = []; window.draftRevision = 1; window.saveMode = 'normal'; window.completeMode = 'normal';
      window.addEventListener('message', event => {
        const frame = document.querySelector('iframe'); const data = event.data;
        if (!frame || event.source !== frame.contentWindow || event.origin !== 'null' || data.version !== 1) return;
        if (data.kind === 'flowzer.embed.ready') {
          window.embedSession = data.sessionId;
          // Ein opaque Frame hat keine targetbare Origin. Quelle, Nonce und
          // anschließender privater Port binden genau diesen Frame-Ladevorgang.
          frame.contentWindow.postMessage({ kind: 'flowzer.embed.connect', version: 1, sessionId: data.sessionId }, '*');
        } else if (data.kind === 'flowzer.embed.connected' && data.sessionId === window.embedSession && event.ports.length === 1) {
          const port = event.ports[0];
          port.onmessage = async event => {
            const request = event.data;
            if (request.kind !== 'request' || request.sessionId !== window.embedSession) return;
            window.calls.push(request);
            let result = null, error;
            if (request.operation === 'draft.save') {
              if (window.saveMode === 'conflict') error = { code: 'flowzer.revision_conflict' };
              else result = { userTaskId: '${taskId}', revision: ++window.draftRevision, data: request.payload.data };
            } else if (request.operation === 'directory.search') result = { generationId: 'synthetic-generation', items: [${JSON.stringify(subject)}] };
            else if (request.operation === 'directory.resolve') result = [${JSON.stringify(subject)}];
            else if (request.operation === 'task.complete') result = window.completeMode === 'malformed' ? {} : { completed: true };
            else error = { code: 'flowzer.access_denied' };
            const response = { kind: 'response', sessionId: request.sessionId, id: request.id, result, error };
            if (request.operation === 'task.complete' && window.completeMode === 'hold') { window.confirmCompletion = () => port.postMessage(response); return; }
            port.postMessage(response);
          };
        }
      });
      </script><iframe title="Workflowformular" sandbox="allow-scripts" src="${apiOrigin}/embed.html#${'A'.repeat(43)}"></iframe>` });
    if (url.origin !== apiOrigin) return route.abort();
    if (url.pathname === '/form-embed/redeem') {
      if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers: {
        'Access-Control-Allow-Origin': 'null', 'Access-Control-Allow-Methods': 'POST', 'Access-Control-Allow-Headers': 'content-type',
      } });
      redemptions.push({ method: request.method(), headers: request.headers(), body: request.postDataJSON() });
      return route.fulfill({ status: rejectedLink ? 403 : 200, contentType: 'application/json', headers: {
        'Access-Control-Allow-Origin': 'null', 'Cache-Control': 'no-store',
      }, body: JSON.stringify(rejectedLink ? { successful: false } : { successful: true, result: snapshot }) });
    }
    if (url.pathname === '/embed.html') return route.fulfill({ contentType: 'text/html', headers: {
      'Content-Security-Policy': csp, 'Referrer-Policy': 'no-referrer', 'Cache-Control': 'no-store',
    }, body: readFileSync(resolve(__dirname, '../../src/FlowzerConsole/dist/embed.html')) });
    if (['/embed-assets/embed.js', '/embed-assets/embed.css'].includes(url.pathname)) return route.fulfill({
      contentType: url.pathname.endsWith('.js') ? 'text/javascript' : 'text/css', headers: { 'Access-Control-Allow-Origin': 'null' },
      body: readFileSync(resolve(__dirname, '../../src/FlowzerConsole/dist', url.pathname.slice(1))),
    });
    return route.abort();
  });
  await page.goto(host);
  return { frame: page.frameLocator('iframe'), errors, violations, redemptions };
}

for (const locale of ['de-DE', 'en-US']) {
  test.describe(locale, () => {
    test.use({ locale });
    // Testzweck: Die echte Read-only-Einlösung, bestehende Felder/Kalender/Directory,
    // manuelle Teilentwürfe und anschließender Abschluss funktionieren im Produktions-CSP.
    test('Produktionsformular behält Eingaben nach 46 Minuten und nutzt nur den privaten Hostkanal', async ({ page }) => {
      const { frame, errors, violations, redemptions } = await setup(page);
      const answer = frame.getByLabel('Antwort', { exact: false });
      await expect(answer).toHaveValue('Gespeicherter Zwischenstand', { timeout: 25000 });
      const child = page.frames().find(item => item !== page.mainFrame());
      expect(new URL(child.url()).hash).toBe('');
      const restrictions = await child.evaluate(() => {
        let storageDenied = false, cookieDenied = false;
        try { localStorage.getItem('test'); } catch { storageDenied = true; }
        try { document.cookie; } catch { cookieDenied = true; }
        return { storageDenied, cookieDenied, origin: self.origin };
      });
      expect(restrictions).toEqual({ storageDenied: true, cookieDenied: true, origin: 'null' });
      await frame.getByLabel('Datum', { exact: false }).click();
      await expect(frame.locator('.flatpickr-calendar.open')).toBeVisible();
      await expect(frame.locator('.flatpickr-weekday').first()).toHaveText(locale === 'de-DE' ? 'Mo' : 'Sun');
      // Directory-Interaktion vor der virtuellen Uhr: laufende React-/Form.io-
      // Debounce-/Layout-Timer sollen nicht mit einem Zeitsprung kombiniert werden.
      await answer.click();
      await frame.getByRole('searchbox', { name: 'Vertretung suchen' }).fill('Demo');
      await frame.getByRole('option', { name: 'Demo Vertretung', exact: true }).click();
      await answer.fill('');
      await frame.getByRole('button', { name: 'Zwischenstand speichern' }).click();
      await expect(frame.getByRole('status')).toHaveText('Zwischenstand gespeichert');
      expect(await page.evaluate(() => calls.find(item => item.operation === 'draft.save').payload)).toMatchObject({
        expectedRevision: 1, expectedTaskRevision: 7, data: { answer: '' },
      });
      await frame.getByRole('button', { name: 'Absenden', exact: true }).click();
      expect(await page.evaluate(() => calls.filter(item => item.operation === 'task.complete').length)).toBe(0);
      await answer.fill('Eingaben bleiben nach Token-Erneuerung bestehen');
      // Virtuelle Browserzeit prüft fehlende UI-Deadline; keine Behauptung über
      // reale Keycloak-Tokens oder echte 45 Minuten Betriebszeit.
      await page.clock.install(); await page.clock.fastForward(46 * 60 * 1000); await page.clock.resume();
      await expect(answer).toHaveValue('Eingaben bleiben nach Token-Erneuerung bestehen');
      await page.evaluate(() => { window.saveMode = 'conflict'; });
      await frame.getByRole('button', { name: 'Zwischenstand speichern' }).click();
      await expect(frame.getByRole('alert')).toContainText('neuerer Zwischenstand');
      await expect(answer).toHaveValue('Eingaben bleiben nach Token-Erneuerung bestehen');
      await page.evaluate(() => { window.saveMode = 'normal'; window.completeMode = 'normal'; });
      await frame.getByRole('button', { name: 'Zwischenstand speichern' }).click();
      await expect(frame.getByRole('status')).toHaveText('Zwischenstand gespeichert');
      await frame.getByRole('button', { name: 'Absenden', exact: true }).click();
      await expect(frame.getByRole('status')).toHaveText('Aufgabe abgeschlossen.');
      const calls = await page.evaluate(() => window.calls);
      expect(calls.filter(item => item.operation === 'task.complete')).toHaveLength(1);
      expect(calls.find(item => item.operation === 'task.complete').payload.data.substitute).toEqual(subject.subject);
      expect(redemptions).toHaveLength(1);
      expect(redemptions[0].method).toBe('POST');
      expect(redemptions[0].headers.origin).toBe('null');
      expect(redemptions[0].headers.authorization).toBeUndefined();
      expect(redemptions[0].headers.cookie).toBeUndefined();
      expect(redemptions[0].headers.referer).toBeUndefined();
      expect(redemptions[0].body).toEqual({ secret: 'A'.repeat(43) });
      expect(errors).toEqual([]); expect(violations).toEqual([]);
      expect(await child.evaluate(() => self.__cspViolations)).toEqual([]);
    });
  });
}
// Testzweck: Verbrauchte/abgelaufene Einstiege melden sicher einen Fehler, ohne Login,
// Retry oder Erzeugung eines Mutationskanals; Secrets erscheinen nicht in der Meldung.
test('abgewiesener Einstieg startet weder Login noch Hostaktionen', async ({ page }) => {
  const { frame, redemptions } = await setup(page, { rejectedLink: true });
  await expect(frame.getByRole('alert')).toHaveText('Das Formular konnte nicht sicher geöffnet werden. Bitte öffne es erneut über TickyTask.');
  expect(redemptions).toHaveLength(1);
  expect(await page.evaluate(() => window.calls)).toEqual([]);
  expect(await page.evaluate(() => window.embedSession)).toBeUndefined();
});

// Testzweck: Eine langsame erfolgreiche Entscheidung sperrt Eingaben ohne
// Remount; keine nachträglichen Edits gehen beim späteren Erfolg verloren.
test('laufender Abschluss sperrt den bestehenden Feldbaum bis zur Bestätigung', async ({ page }) => {
  const { frame } = await setup(page); const answer = frame.getByLabel('Antwort', { exact: false });
  await expect(answer).toHaveValue('Gespeicherter Zwischenstand');
  await frame.getByLabel('Datum', { exact: false }).click();
  await expect(frame.locator('.flatpickr-calendar.open')).toBeVisible();
  await page.evaluate(() => { window.completeMode = 'hold'; });
  // Der geöffnete Kalender überlagert hier geometrisch die Aktionsleiste.
  // Native Tastaturaktivierung (kein force-Klick) prüft Fokus/Popup-Schließen.
  await frame.getByRole('button', { name: 'Absenden', exact: true }).press('Enter');
  await expect.poll(() => page.evaluate(() => window.calls.filter(item => item.operation === 'task.complete').length)).toBe(1);
  await expect(answer).toBeDisabled(); await expect(frame.locator('fieldset')).toHaveAttribute('inert', '');
  await expect(frame.getByLabel('Vertretung suchen', { exact: true })).toBeDisabled();
  await expect(frame.locator('.flatpickr-calendar.open')).toHaveCount(0);
  await expect(answer).toHaveValue('Gespeicherter Zwischenstand');
  await expect(frame.getByRole('button', { name: 'Zwischenstand speichern' })).toBeDisabled();
  await page.evaluate(() => window.confirmCompletion());
  await expect(frame.getByRole('status')).toHaveText('Aufgabe abgeschlossen.');
});
// Testzweck: Ein malformer Erfolg entfernt nichts; der unklare Auftrag bleibt
// gesperrt und wird mit denselben Daten und derselben Idempotenz identisch geklärt.
test('unklarer Abschluss wiederholt exakt denselben Auftrag ohne neuen Renderer', async ({ page }) => {
  const { frame, redemptions } = await setup(page); const answer = frame.getByLabel('Antwort', { exact: false });
  await expect(answer).toHaveValue('Gespeicherter Zwischenstand');
  await page.evaluate(() => { window.completeMode = 'malformed'; });
  await frame.getByRole('button', { name: 'Absenden', exact: true }).click();
  await expect(frame.getByRole('alert')).toContainText('derzeit nicht verfügbar');
  await expect(answer).toBeDisabled(); await expect(answer).toHaveValue('Gespeicherter Zwischenstand');
  await page.evaluate(() => { window.completeMode = 'normal'; });
  await frame.getByRole('button', { name: 'Abschluss erneut bestätigen' }).click();
  await expect(frame.getByRole('status')).toHaveText('Aufgabe abgeschlossen.');
  const attempts = await page.evaluate(() => window.calls.filter(item => item.operation === 'task.complete'));
  expect(attempts).toHaveLength(2); expect(attempts[1].payload).toEqual(attempts[0].payload);
  expect(redemptions).toHaveLength(1);
});
// Testzweck: Ein nicht freigegebener Parent scheitert bereits an der tatsächlichen
// Browser-CSP, bevor ein Link eingelöst oder ein Hostkanal erzeugt werden kann.
test('frame-ancestors blockiert nicht freigegebene Einbettungsseiten', async ({ page }) => {
  const { violations, redemptions } = await setup(page, { host: 'https://other.example.test' });
  await expect.poll(() => violations.filter(value => value.includes('frame-ancestors')).length).toBeGreaterThan(0);
  expect(redemptions).toHaveLength(0);
  expect(await page.evaluate(() => window.calls)).toEqual([]);
});
