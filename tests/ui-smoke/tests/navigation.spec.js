const { test, expect } = require('@playwright/test');

const smokePages = [
  {
    path: '/',
    heading: 'Welcome to Flowzer',
    readyText: 'Welcome to Flowzer'
  },
  {
    path: '/models',
    heading: 'Models',
    readyText: 'New Model'
  },
  {
    path: '/forms',
    heading: 'Forms',
    readyText: 'New Form'
  },
  {
    path: '/instances',
    heading: 'Instances',
    readyText: 'All Instances'
  }
];

for (const smokePage of smokePages) {
  test(`${smokePage.path} lädt ohne fatale UI-Fehler`, async ({ page }) => {
    const pageErrors = [];
    const failedRequests = [];

    page.on('pageerror', error => pageErrors.push(error.message));
    page.on('requestfailed', request => failedRequests.push(request.url()));

    await page.goto(smokePage.path, { waitUntil: 'networkidle' });

    await expect(page.getByRole('heading', { level: 1, name: smokePage.heading })).toBeVisible();
    await expect(page.getByText(smokePage.readyText, { exact: false })).toBeVisible();
    await expect(page.locator('#blazor-error-ui')).toBeHidden();
    expect(pageErrors, `Unerwartete PageErrors auf ${smokePage.path}`).toEqual([]);
    expect(failedRequests, `Fehlgeschlagene Requests auf ${smokePage.path}`).toEqual([]);
  });
}

test('Hauptnavigation springt zwischen den Kernseiten', async ({ page }) => {
  await page.goto('/', { waitUntil: 'networkidle' });

  await page.getByRole('link', { name: 'Models' }).click();
  await expect(page).toHaveURL(/\/models$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Models' })).toBeVisible();

  await page.getByRole('link', { name: 'Forms' }).click();
  await expect(page).toHaveURL(/\/forms$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Forms' })).toBeVisible();

  await page.getByRole('link', { name: 'Instances' }).click();
  await expect(page).toHaveURL(/\/instances$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Instances' })).toBeVisible();
});
