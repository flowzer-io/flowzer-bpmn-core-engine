const { defineConfig } = require('@playwright/test');

const frontendUrl = process.env.FLOWZER_FRONTEND_URL || 'http://localhost:5269';
const apiUrl = process.env.FLOWZER_API_URL || 'http://localhost:5182';
const frontendServerOrigin = new URL(frontendUrl).origin;
const apiServerOrigin = new URL(apiUrl).origin;
const reuseExistingServer = !process.env.CI;
const webServerEnvironment = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: process.env.ASPNETCORE_ENVIRONMENT || 'Development'
};

module.exports = defineConfig({
  testDir: './tests',
  fullyParallel: false,
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
