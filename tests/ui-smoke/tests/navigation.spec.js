const { test, expect } = require('@playwright/test');

const ignoredFailedRequestSuffixes = [
  '/favicon.ico'
];

function isIgnoredFailedRequest(url) {
  return ignoredFailedRequestSuffixes.some(suffix => url.endsWith(suffix));
}

const smokePages = [
  {
    path: '/',
    heading: 'Welcome to Flowzer',
    ready: async page => {
      await expect(page.getByText('Welcome to Flowzer', { exact: false })).toBeVisible();
    }
  },
  {
    path: '/models',
    heading: 'Workflows',
    ready: async page => {
      await expect(page.locator('#new-button')).toContainText('New workflow');
    }
  },
  {
    path: '/forms',
    heading: 'Forms',
    ready: async page => {
      await expect(page.locator('#new-button')).toContainText('New form');
    }
  },
  {
    path: '/instances',
    heading: 'Instances',
    ready: async page => {
      await expect(page.getByText('Showing: All instances')).toBeVisible();
    }
  },
  {
    path: '/instances/all',
    heading: 'Instances',
    ready: async page => {
      await expect(page.getByText('Showing: All instances')).toBeVisible();
    }
  },
  {
    path: '/instances/active',
    heading: 'Instances',
    ready: async page => {
      await expect(page.getByText('Showing: Active instances')).toBeVisible();
    }
  },
  {
    path: '/instances/done',
    heading: 'Instances',
    ready: async page => {
      await expect(page.getByText('Showing: Completed instances')).toBeVisible();
    }
  },
  {
    path: '/instances/error',
    heading: 'Instances',
    ready: async page => {
      await expect(page.getByText('Showing: Failed instances')).toBeVisible();
    }
  }
];

for (const smokePage of smokePages) {
  // Testzweck: Prüft für jede Kernroute, dass die Seite ohne fatale Frontend-Fehler lädt und ihre Grundelemente sichtbar sind.
  test(`${smokePage.path} lädt ohne fatale UI-Fehler`, async ({ page }) => {
    const pageErrors = [];
    const failedRequests = [];

    page.on('pageerror', error => pageErrors.push(error.message));
    page.on('requestfailed', request => {
      if (!isIgnoredFailedRequest(request.url())) {
        failedRequests.push(`${request.method()} ${request.url()}`);
      }
    });

    await page.goto(smokePage.path, { waitUntil: 'networkidle' });

    await expect(page.getByRole('heading', { level: 1, name: smokePage.heading })).toBeVisible();
    await smokePage.ready(page);
    await expect(page.locator('#blazor-error-ui')).toBeHidden();
    expect(pageErrors, `Unerwartete PageErrors auf ${smokePage.path}`).toEqual([]);
    expect(failedRequests, `Unerwartete fehlgeschlagene Requests auf ${smokePage.path}`).toEqual([]);
  });
}

// Testzweck: Prüft, dass die Hauptnavigation zwischen den wichtigsten Kernseiten korrekt routet.
test('Hauptnavigation springt zwischen den Kernseiten', async ({ page }) => {
  await page.goto('/', { waitUntil: 'networkidle' });

  const topNav = page.locator('.app-topnav');

  await topNav.getByRole('link', { name: 'Workflows', exact: true }).click();
  await expect(page).toHaveURL(/\/models$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Workflows' })).toBeVisible();

  await topNav.getByRole('link', { name: 'Forms', exact: true }).click();
  await expect(page).toHaveURL(/\/forms$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Forms' })).toBeVisible();

  await topNav.getByRole('link', { name: 'Instances', exact: true }).click();
  await expect(page).toHaveURL(/\/instances$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Instances' })).toBeVisible();
});

// Testzweck: Prüft, dass die Instanzfilter-Navigation ausschließlich auf gültige Frontend-Routen verzweigt.
test('Instanzfilter-Navigation bleibt innerhalb gültiger Frontend-Routen', async ({ page }) => {
  await page.goto('/instances', { waitUntil: 'networkidle' });

  await page.locator('#instances-filter-all').click();
  await expect(page).toHaveURL(/\/instances\/all$/);
  await expect(page.getByText('Showing: All instances')).toBeVisible();

  await page.locator('#instances-filter-active').click();
  await expect(page).toHaveURL(/\/instances\/active$/);
  await expect(page.getByText('Showing: Active instances')).toBeVisible();

  await page.locator('#instances-filter-done').click();
  await expect(page).toHaveURL(/\/instances\/done$/);
  await expect(page.getByText('Showing: Completed instances')).toBeVisible();

  await page.locator('#instances-filter-error').click();
  await expect(page).toHaveURL(/\/instances\/error$/);
  await expect(page.getByText('Showing: Failed instances')).toBeVisible();
});

// Testzweck: Prüft, dass die Dashboard-Zusammenfassung als direkter Einstieg in die wichtigsten Kernbereiche funktioniert.
test('Dashboard-Zusammenfassung verlinkt die wichtigsten Kernbereiche direkt', async ({ page }) => {
  await page.goto('/', { waitUntil: 'networkidle' });

  await page.locator('#home-summary-workflows').click();
  await expect(page).toHaveURL(/\/models$/);

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.locator('#home-summary-active-instances').click();
  await expect(page).toHaveURL(/\/instances\/active$/);

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.locator('#home-summary-forms').click();
  await expect(page).toHaveURL(/\/forms$/);
});

// Testzweck: Prüft, dass eine aktive Suche im Workflow-Katalog explizit zurückgesetzt werden kann, ohne die Seite neu zu laden.
test('Workflow-Katalog bietet einen klaren Such-Reset an', async ({ page }) => {
  await page.goto('/models', { waitUntil: 'networkidle' });

  const searchField = page.locator('input[placeholder="Search workflows by name, id or description"]').first();
  await searchField.fill('definitely-no-existing-workflow');

  await expect(page.locator('#models-empty-state')).toContainText('No workflows match');
  await expect(page.locator('#models-clear-search')).toBeVisible();

  await page.locator('#models-clear-search').click();

  await expect(searchField).toHaveValue('');
  await expect(page.locator('#models-clear-search')).toHaveCount(0);
});

// Testzweck: Prüft, dass ungültige Modellrouten kontrolliert einen Inline-Fehler statt eines fatalen Blazor-Absturzes zeigen.
test('Ungültige Modellrouten zeigen einen Inline-Fehler statt eines fatalen Blazor-Absturzes', async ({ page }) => {
  const pageErrors = [];

  page.on('pageerror', error => pageErrors.push(error.message));

  await page.goto('/definition/definition-does-not-exist', { waitUntil: 'networkidle' });

  await expect(page.locator('#definition-load-error')).toContainText('Could not open the workflow', { timeout: 20_000 });
  await expect(page.locator('#definition-retry-button')).toBeVisible();
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  expect(pageErrors, 'Unerwartete PageErrors auf einer ungültigen Modellroute').toEqual([]);
});
