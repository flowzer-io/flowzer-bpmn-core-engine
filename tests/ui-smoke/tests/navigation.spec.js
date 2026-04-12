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
    readyText: 'Showing: All instances'
  },
  {
    path: '/instances/all',
    heading: 'Instances',
    readyText: 'Showing: All instances'
  },
  {
    path: '/instances/active',
    heading: 'Instances',
    readyText: 'Showing: Active instances'
  },
  {
    path: '/instances/done',
    heading: 'Instances',
    readyText: 'Showing: Completed instances'
  },
  {
    path: '/instances/error',
    heading: 'Instances',
    readyText: 'Showing: Failed instances'
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

test('Instanzfilter-Navigation bleibt innerhalb gültiger Frontend-Routen', async ({ page }) => {
  await page.goto('/instances', { waitUntil: 'networkidle' });

  await page.getByRole('link', { name: 'All Instances' }).click();
  await expect(page).toHaveURL(/\/instances\/all$/);
  await expect(page.getByText('Showing: All instances')).toBeVisible();

  await page.getByRole('link', { name: 'Active Instances' }).click();
  await expect(page).toHaveURL(/\/instances\/active$/);
  await expect(page.getByText('Showing: Active instances')).toBeVisible();

  await page.getByRole('link', { name: 'Done Instances' }).click();
  await expect(page).toHaveURL(/\/instances\/done$/);
  await expect(page.getByText('Showing: Completed instances')).toBeVisible();

  await page.getByRole('link', { name: 'Failed Instances' }).click();
  await expect(page).toHaveURL(/\/instances\/error$/);
  await expect(page.getByText('Showing: Failed instances')).toBeVisible();
});


test('Ungültige Modellrouten zeigen einen Inline-Fehler statt eines fatalen Blazor-Absturzes', async ({ page }) => {
  const pageErrors = [];

  page.on('pageerror', error => pageErrors.push(error.message));

  await page.goto('/definition/definition-does-not-exist', { waitUntil: 'networkidle' });

  await expect(page.locator('#definition-load-error')).toContainText('Could not load model.', { timeout: 20_000 });
  await expect(page.locator('#definition-retry-button')).toBeVisible();
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  expect(pageErrors, 'Unerwartete PageErrors auf einer ungültigen Modellroute').toEqual([]);
});
