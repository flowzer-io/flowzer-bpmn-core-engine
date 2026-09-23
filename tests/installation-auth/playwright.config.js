const { defineConfig } = require('@playwright/test');
const { BASE_URL } = require('./support/constants');

// Die Abnahme läuft gegen den bereits gestarteten Compose-Stack (siehe run.sh). Chromium
// löst die Testnamen über Host-Resolver-Regeln auf den lokal veröffentlichten TLS-Port auf;
// Zertifikatsfehler ignoriert nur der Browser, die Node-Seite prüft gegen die Test-CA.
module.exports = defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  // Zustandsbehaftete Abnahme: Eine Wiederholung würde echte Befunde verdecken.
  retries: 0,
  timeout: 180_000,
  expect: { timeout: 15_000 },
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL: BASE_URL,
    ignoreHTTPSErrors: true,
    locale: 'de-DE',
    launchOptions: {
      args: ['--host-resolver-rules=MAP flowzer.test 127.0.0.1, MAP auth.flowzer.test 127.0.0.1']
    },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  // Feste Reihenfolge: erst die Konfigurationsprüfung, dann die Abläufe, zuletzt der
  // Neustart, der alle Browser-Sitzungen der API beendet.
  projects: [
    { name: 'check-config', testMatch: /check-config\.spec\.js$/ },
    { name: 'flows', testMatch: /(golden-path|negative|directory)\.spec\.js$/, dependencies: ['check-config'] },
    { name: 'restart', testMatch: /restart\.spec\.js$/, dependencies: ['flows'] }
  ]
});
