const path = require('node:path');
const { defineConfig } = require('@playwright/test');

// Separat vom Dev-Smoke: nur das echte Auslieferungsbundle, keine API/Secrets nötig.
module.exports = defineConfig({
  testDir: './production-tests',
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env.CI,
  timeout: 30_000,
  outputDir: 'test-results-production',
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report-production' }]],
  use: {
    baseURL: 'http://127.0.0.1:5291',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: 'npm run preview -- --host 127.0.0.1 --port 5291 --strictPort',
    cwd: path.resolve(__dirname, '../../src/FlowzerConsole'),
    url: 'http://127.0.0.1:5291',
    reuseExistingServer: false,
  },
});
