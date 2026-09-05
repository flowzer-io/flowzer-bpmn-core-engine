const fs = require('fs');
const path = require('path');
const { defineConfig } = require('@playwright/test');

// Die Konsole laeuft im Smoke bewusst ueber den Vite-Entwicklungsserver:
// Ohne konfigurierten Identity Provider meldet sie dort einen Entwicklungsbenutzer an.
// Ein Produktionsbuendel zeigte stattdessen die Anmeldeseite und kaeme nie zu den Seiten,
// die hier geprueft werden sollen.
const consoleUrl = process.env.FLOWZER_CONSOLE_URL || 'http://localhost:5290';
const apiUrl = process.env.FLOWZER_API_URL || 'http://localhost:5288';
const consoleServerOrigin = new URL(consoleUrl).origin;
const apiServerOrigin = new URL(apiUrl).origin;
const consolePort = new URL(consoleUrl).port || '5290';
const playwrightTempRoot = path.resolve(__dirname, '.tmp');
const managedStorageRoot = path.resolve(
  process.env.PLAYWRIGHT_MANAGED_STORAGE_ROOT || path.join(playwrightTempRoot, 'storage')
);
const reuseExistingServer = !process.env.CI && process.env.PLAYWRIGHT_REUSE_EXISTING_SERVER === '1';
const apiEnvironmentName = process.env.FLOWZER_API_ENVIRONMENT || 'Development';

function isPathWithin(parentPath, childPath)
{
  const relativePath = path.relative(parentPath, childPath);
  return relativePath === '' || (!relativePath.startsWith('..') && !path.isAbsolute(relativePath));
}

if (!process.env.PLAYWRIGHT_SKIP_WEBSERVERS)
{
  if (!isPathWithin(playwrightTempRoot, managedStorageRoot))
  {
    throw new Error(
      `PLAYWRIGHT_MANAGED_STORAGE_ROOT must be located under ${playwrightTempRoot}. Received: ${managedStorageRoot}`
    );
  }

  fs.rmSync(managedStorageRoot, { recursive: true, force: true });
  fs.mkdirSync(managedStorageRoot, { recursive: true });
}

const sharedWebServerEnvironment = {
  ...process.env,
  FLOWZER_STORAGE_ROOT: managedStorageRoot
};

module.exports = defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  timeout: 60_000,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL: consoleUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  webServer: process.env.PLAYWRIGHT_SKIP_WEBSERVERS
    ? undefined
    : [
        {
          command: `dotnet run --project ../../src/WebApiEngine/WebApiEngine.csproj --configuration Release --no-build --no-launch-profile --urls ${apiServerOrigin}`,
          env: { ...sharedWebServerEnvironment, ASPNETCORE_ENVIRONMENT: apiEnvironmentName },
          url: `${apiServerOrigin}/swagger/index.html`,
          reuseExistingServer,
          timeout: 120_000
        },
        {
          // `FLOWZER_API_URL` steuert das Proxy-Ziel von /api in vite.config.ts.
          command: `npm run dev -- --port ${consolePort} --strictPort`,
          cwd: path.resolve(__dirname, '../../src/FlowzerConsole'),
          env: { ...sharedWebServerEnvironment, FLOWZER_API_URL: apiServerOrigin },
          url: consoleServerOrigin,
          reuseExistingServer,
          timeout: 180_000
        }
      ]
});
