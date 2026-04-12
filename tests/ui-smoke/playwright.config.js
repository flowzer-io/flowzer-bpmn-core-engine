const fs = require('fs');
const path = require('path');
const { defineConfig } = require('@playwright/test');

const frontendUrl = process.env.FLOWZER_FRONTEND_URL || 'http://localhost:5269';
const apiUrl = process.env.FLOWZER_API_URL || 'http://localhost:5182';
const frontendServerOrigin = new URL(frontendUrl).origin;
const apiServerOrigin = new URL(apiUrl).origin;
const playwrightTempRoot = path.resolve(__dirname, '.tmp');
const managedStorageRoot = path.resolve(
  process.env.PLAYWRIGHT_MANAGED_STORAGE_ROOT || path.join(playwrightTempRoot, 'storage')
);
const reuseExistingServer = !process.env.CI && process.env.PLAYWRIGHT_REUSE_EXISTING_SERVER === '1';

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

const webServerEnvironment = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: process.env.ASPNETCORE_ENVIRONMENT || 'Development',
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
    baseURL: frontendUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  webServer: process.env.PLAYWRIGHT_SKIP_WEBSERVERS
    ? undefined
    : [
        {
          command: `dotnet run --project ../../src/WebApiEngine/WebApiEngine.csproj --configuration Release --no-build --no-launch-profile --urls ${apiServerOrigin}`,
          env: webServerEnvironment,
          url: `${apiServerOrigin}/swagger/index.html`,
          reuseExistingServer,
          timeout: 120_000
        },
        {
          command: `dotnet run --project ../../src/FlowzerFrontend/FlowzerFrontend.csproj --configuration Release --no-build --no-launch-profile --urls ${frontendServerOrigin}`,
          env: webServerEnvironment,
          url: frontendServerOrigin,
          reuseExistingServer,
          timeout: 120_000
        }
      ]
});
