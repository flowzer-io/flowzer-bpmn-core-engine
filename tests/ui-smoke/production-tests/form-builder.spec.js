const { test, expect } = require('@playwright/test');

// Synthetische Autorenentwürfe: echter Form.io-Builder, aber keine produktiven Schreibzugriffe.
async function mockAuthoring(page) {
  const formId = '11111111-1111-4111-8111-111111111111';
  const schema = { display: 'form', components: [{ type: 'textfield', key: 'reason', label: 'Begründung', input: true }] };
  let draft = { formId, revision: 1, hasDraft: true, formData: JSON.stringify(schema) };
  await page.route('**/config.json', route => route.fulfill({ json: { apiBaseUrl: '/api', bffEnabled: true, accent: 'iris' } }));
  await page.route('**/bff/session', route => route.fulfill({ json: { id: 'author', name: 'Autor', capabilities: ['access', 'modeler'] } }));
  await page.route('**/bff/csrf', route => route.fulfill({ json: { requestToken: 'synthetic', headerName: 'X-CSRF' } }));
  await page.route('**/api/**', route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    let result = [];
    if (path === '/api/form/meta') result = [{ formId, name: 'Editor-Smoke' }];
    if (path.endsWith('/draft')) {
      if (request.method() === 'PUT') draft = { ...draft, revision: draft.revision + 1, formData: request.postDataJSON().formData };
      result = draft;
    }
    if (path.endsWith('/preview')) result = { formData: request.postDataJSON().formData, validationProfile: 'flowzer.forms/2' };
    return route.fulfill({ json: { successful: true, result } });
  });
  return () => JSON.parse(draft.formData);
}

// Testzweck: Die echte Form.io-Palette muss eigene Felder öffnen und speichern können;
// Klassen-Mocks erkennen den obligatorischen tabs-Zugriff der Bibliothek nicht.
test('Benutzer-/Gruppenfeld lässt sich im Produktionseditor einfügen und erneut bearbeiten', async ({ page }) => {
  test.setTimeout(60_000);
  const saved = await mockAuthoring(page);
  const errors = [];
  page.on('pageerror', error => { errors.push(error.message); console.error('Browserfehler:', error.message); });
  await page.goto('/forms');
  await page.getByRole('tab', { name: 'Felder', exact: true }).click();
  const palette = page.locator('[data-type="flowzerSubject"]');
  await expect(palette).toBeVisible();
  const target = page.locator('.formio-builder .drag-container').first();
  await target.scrollIntoViewIfNeeded();
  const sourceBox = await palette.boundingBox();
  const targetBox = await target.boundingBox();
  expect(sourceBox).not.toBeNull();
  expect(targetBox).not.toBeNull();
  // Dragula benötigt echte Zwischenbewegungen, nicht nur einen HTML5-drop-Event.
  await page.mouse.move(sourceBox.x + sourceBox.width / 2, sourceBox.y + sourceBox.height / 2);
  await page.mouse.down();
  await page.mouse.move(sourceBox.x + sourceBox.width / 2 + 10, sourceBox.y + sourceBox.height / 2 + 10, { steps: 5 });
  await page.mouse.move(targetBox.x + targetBox.width / 2, targetBox.y + targetBox.height - 10, { steps: 20 });
  await page.mouse.up();
  const dialog = page.locator('.formio-dialog');
  await expect(dialog.getByText('Gruppen auswählbar', { exact: true })).toBeVisible();
  await dialog.locator('input[name="data[label]"]').fill('Vertretung');
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await page.getByRole('button', { name: 'Entwurf speichern', exact: true }).click();
  await expect.poll(() => saved().components.some(component => component.type === 'flowzerSubject' && component.label === 'Vertretung')).toBe(true);
  for (let i = 0; i < 3; i++) {
    await page.getByRole('tab', { name: 'Vorschau', exact: true }).click();
    await expect(page.getByText('Vertretung', { exact: true }).first()).toBeVisible();
    await page.getByRole('tab', { name: 'Felder', exact: true }).click();
    await expect(page.locator('.formio-component-flowzerSubject')).toBeVisible();
  }
  const subjectComponent = page.locator('.builder-component').filter({ has: page.locator('.formio-component-flowzerSubject') });
  await subjectComponent.hover();
  await subjectComponent.getByRole('button', { name: 'Edit button. Click to open component settings modal window', exact: true }).click();
  await expect(dialog.locator('input[name="data[label]"]')).toHaveValue('Vertretung');
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(errors).toEqual([]);
});
