const { test, expect } = require('@playwright/test');
const fs = require('fs/promises');
const path = require('path');

// Testzweck: Markiert die Design-Preview-Screenshots als bewusst separaten Opt-in-Lauf außerhalb des normalen UI-Smokes.
test.skip(
  process.env.FLOWZER_CAPTURE_DESIGN_PREVIEW !== '1',
  'Design-Preview-Screenshots werden nur im expliziten Preview-Lauf erzeugt.'
);

const previewDirectory = path.resolve(__dirname, '..', 'design-preview');

const previewPages = [
  {
    path: '/',
    heading: 'Dashboard',
    fileName: '01-dashboard-desktop.png',
    viewport: { width: 1440, height: 1200 }
  },
  {
    path: '/tasks',
    heading: 'My tasks',
    fileName: '02-task-inbox-desktop.png',
    viewport: { width: 1440, height: 1200 }
  },
  {
    path: '/models',
    heading: 'Workflows',
    fileName: '03-workflows-desktop.png',
    viewport: { width: 1440, height: 1200 }
  },
  {
    path: '/instances',
    heading: 'Instances',
    fileName: '04-instances-desktop.png',
    viewport: { width: 1440, height: 1200 }
  },
  {
    path: '/forms',
    heading: 'Forms',
    fileName: '05-forms-desktop.png',
    viewport: { width: 1440, height: 1200 }
  },
  {
    path: '/',
    heading: 'Dashboard',
    fileName: '06-dashboard-mobile.png',
    viewport: { width: 390, height: 900 }
  },
  {
    path: '/tasks',
    heading: 'My tasks',
    fileName: '07-task-inbox-mobile.png',
    viewport: { width: 390, height: 900 }
  }
];

test.beforeAll(async () => {
  await fs.rm(previewDirectory, { recursive: true, force: true });
  await fs.mkdir(previewDirectory, { recursive: true });
});

for (const previewPage of previewPages) {
  // Testzweck: Erzeugt einen visuellen Review-Screenshot einer Kernseite als PR-/CI-Artefakt.
  test(`Design-Preview-Screenshot für ${previewPage.fileName}`, async ({ page }) => {
    await page.setViewportSize(previewPage.viewport);
    await page.goto(previewPage.path, { waitUntil: 'networkidle' });

    await expect(page.getByRole('heading', { level: 1, name: previewPage.heading })).toBeVisible();
    await expect(page.locator('#blazor-error-ui')).toBeHidden();

    await page.screenshot({
      path: path.join(previewDirectory, previewPage.fileName),
      fullPage: true
    });
  });
}
