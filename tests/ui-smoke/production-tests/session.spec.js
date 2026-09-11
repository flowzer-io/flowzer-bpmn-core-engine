const { test, expect } = require('@playwright/test');

/** Ausschließlich synthetische Sitzungs-/Listenantworten, keine echte Anmeldung oder Fachmutation. */
async function mockBff(page, authenticated) {
  await page.route('**/config.json', route => route.fulfill({
    json: { apiBaseUrl: '/api', bffEnabled: true, accent: 'iris' },
  }));
  await page.route('**/bff/session', route => route.fulfill(authenticated
    ? { json: { id: 'production-smoke', name: 'Production Smoke', capabilities: ['access', 'modeler', 'operator'] } }
    : { status: 401, body: '' }));
  await page.route('**/api/**', route => {
    // Diagnose ist ein Objekt, die übrigen hier benötigten Leseendpunkte sind Listen.
    const result = new URL(route.request().url()).pathname.endsWith('/operations/diagnostics')
      ? {
          checkedAtUtc: '2026-09-11T00:00:00Z', environment: 'ProductionSmoke',
          storage: { activeInstances: 0, completedInstances: 0, failedInstances: 0, pendingMessages: 0,
            pendingTimers: 0, openUserTasks: 0, pendingSignals: 0, pendingServices: 0 },
          timerScheduler: { enabled: false, pollIntervalSeconds: 5, status: 'Disabled',
            lastProcessedTimers: 0, successfulTickCount: 0, failedTickCount: 0, totalProcessedTimers: 0 },
          instrumentation: { meterName: 'test', activitySourceName: 'test', notes: '' },
          observability: { enabled: false, consoleExporterEnabled: false, otlpExporterEnabled: false,
            serviceName: 'test', serviceVersion: 'test' },
        }
      : [];
    return route.fulfill({ json: { successful: true, result } });
  });
}

// Testzweck: Provider und Hooks aus lokalen file:-Paketen müssen im Produktionsbundle
// denselben QueryClient-Kontext verwenden; Dev-Optimierungen dürfen den Fehler nicht verdecken.
test('Produktionsbundle zeigt nach BFF-Anmeldung Dashboard und Aufgaben', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') console.error(message.text()); });
  await mockBff(page, true);
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Prozess starten', exact: true })).toBeVisible();
  await expect(page.getByText('Keine offenen Aufgaben', { exact: true })).toBeVisible();
  await page.goto('/tasks');
  await expect(page.getByRole('heading', { name: 'Alles erledigt', exact: true })).toBeVisible();
  await expect(page.getByText('Something went wrong!', { exact: true })).toHaveCount(0);
  expect(errors).toEqual([]);
});

// Testzweck: Der Produktions-Smoke darf die Sitzungssperre nicht durch lokale Entwicklungs-
// Defaults umgehen; ohne gültige BFF-Sitzung bleiben die Fachseiten hinter dem Login.
test('Produktionsbundle verlangt bei abgelaufener Sitzung die Anmeldung', async ({ page }) => {
  await mockBff(page, false);
  await page.goto('/tasks');
  await expect(page.getByRole('button', { name: 'Anmelden', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Prozess starten', exact: true })).toHaveCount(0);
});
