const { test, expect } = require('@playwright/test');

// Synthetische Autorenentwürfe: echter Form.io-Builder, aber keine produktiven Schreibzugriffe.
async function mockAuthoring(page, schemaOverride) {
  const formId = '11111111-1111-4111-8111-111111111111';
  const componentFormId = '22222222-2222-4222-8222-222222222222';
  const folderId = '33333333-3333-4333-8333-333333333333';
  const schema = schemaOverride ?? { display: 'form', components: [{ type: 'textfield', key: 'reason', label: 'Begründung', input: true }] };
  let draft = { formId, revision: 1, hasDraft: true, formData: JSON.stringify(schema) };
  await page.route('**/config.json', route => route.fulfill({ json: { apiBaseUrl: '/api', bffEnabled: true, accent: 'iris' } }));
  await page.route('**/bff/session', route => route.fulfill({ json: { id: 'author', name: 'Autor', capabilities: ['access', 'modeler'] } }));
  await page.route('**/bff/csrf', route => route.fulfill({ json: { requestToken: 'synthetic', headerName: 'X-CSRF' } }));
  await page.route('**/api/**', route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    let result = [];
    if (path === '/api/form/meta') result = [
      { formId, name: 'Editor-Smoke' },
      { formId: componentFormId, name: 'Zustelladresse', folderId },
    ];
    if (path === '/api/form/folders') result = [{ id: folderId, name: 'Personal' }];
    if (path === `/api/form/${componentFormId}/versions`) result = [{
      id: '44444444-4444-4444-8444-444444444444',
      formId: componentFormId,
      version: { major: 0, minor: 1 },
    }];
    if (path.endsWith('/draft')) {
      if (request.method() === 'PUT') draft = { ...draft, revision: draft.revision + 1, formData: request.postDataJSON().formData };
      result = draft;
    }
    if (path.endsWith('/preview')) {
      const requested = JSON.parse(request.postDataJSON().formData);
      requested.components = requested.components.flatMap(component => component.type === 'flowzerForm'
        ? [{ type: 'textfield', key: 'street', label: 'Straße', input: true }]
        : [component]);
      result = { formData: JSON.stringify(requested), validationProfile: 'flowzer.forms/2' };
    }
    return route.fulfill({ json: { successful: true, result } });
  });
  return { componentFormId, readDraft: () => JSON.parse(draft.formData) };
}

// Testzweck: Die echte Form.io-Palette muss eigene Felder öffnen und speichern können;
// Klassen-Mocks erkennen den obligatorischen tabs-Zugriff der Bibliothek nicht.
test('Benutzer-/Gruppenfeld lässt sich im Produktionseditor einfügen und erneut bearbeiten', async ({ page }) => {
  test.setTimeout(60_000);
  const { readDraft: saved } = await mockAuthoring(page);
  const errors = [];
  page.on('pageerror', error => { errors.push(error.message); console.error('Browserfehler:', error.message); });
  await page.goto('/forms');
  await page.getByRole('button', { name: 'Bearbeiten', exact: true }).click();
  const palette = page.locator('[data-type="flowzerSubject"]');
  await expect(palette).toBeVisible();
  const target = page.locator('.formio-builder .drag-container').first();
  await target.scrollIntoViewIfNeeded();
  // Oberhalb des Builders kann die Formularbibliothek zusätzliche Werkzeuge anzeigen.
  // Deshalb muss auch die konkrete Palette sichtbar sein, bevor Mauskoordinaten ermittelt werden.
  await palette.scrollIntoViewIfNeeded();
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
  await expect(dialog.getByLabel('Was darf ausgewählt werden?', { exact: true })).toHaveValue('user');
  await dialog.locator('input[name="data[label]"]').fill('Vertretung');
  await dialog.getByLabel('Was darf ausgewählt werden?', { exact: true }).selectOption('all');
  await dialog.getByRole('button', { name: 'Übernehmen', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await page.getByRole('button', { name: 'Entwurf speichern', exact: true }).click();
  await expect.poll(() => saved().components.some(component => component.type === 'flowzerSubject'
    && component.label === 'Vertretung' && component.flowzer.subjectSelection.allowGroups === true)).toBe(true);
  for (let i = 0; i < 3; i++) {
    await page.getByRole('button', { name: 'Vorschau ansehen', exact: true }).click();
    await expect(page.getByText('Vertretung', { exact: true }).first()).toBeVisible();
    await page.getByRole('button', { name: 'Bearbeiten', exact: true }).click();
    await expect(page.locator('.formio-component-flowzerSubject')).toBeVisible();
  }
  const subjectComponent = page.locator('.builder-component').filter({ has: page.locator('.formio-component-flowzerSubject') });
  await subjectComponent.hover();
  await subjectComponent.getByRole('button', { name: 'Edit button. Click to open component settings modal window', exact: true }).click();
  await expect(dialog.locator('input[name="data[label]"]')).toHaveValue('Vertretung');
  await expect(dialog.getByLabel('Was darf ausgewählt werden?', { exact: true })).toHaveValue('all');
  await dialog.getByRole('button', { name: 'Abbrechen', exact: true }).click();
  expect(errors).toEqual([]);
});

for (const [layout, viewport] of [
  ['Desktop', { width: 1280, height: 900 }],
  ['Schmal', { width: 820, height: 1000 }],
  ['Mobil', { width: 390, height: 844 }],
]) {
  // Testzweck: Die gemeinsame Bibliothek muss im echten Produktionsbundle auf Desktop und
  // Mobil eine konkrete Formularversion einfügen und über die Servervorschau darstellen.
  test(`Formular-Komponente lässt sich im ${layout}-Layout einfügen und anzeigen`, async ({ page }) => {
    test.setTimeout(60_000);
    await page.setViewportSize(viewport);
    const { componentFormId, readDraft } = await mockAuthoring(page);

    await page.goto('/forms');
    await page.getByRole('button', { name: 'Bearbeiten', exact: true }).click();
    const subformPalette = page.locator('[data-type="flowzerForm"]');
    // Testzweck: Lange deutsche Palettennamen dürfen nicht horizontal abgeschnitten werden.
    await expect.poll(() => page.locator('[data-type="flowzerSubject"]').evaluate(
      element => element.scrollWidth <= element.clientWidth + 1,
    )).toBe(true);
    // Testzweck: Subformulare auch per Tastatur und auf Mobil ohne Ziehen hinzufügen.
    if (layout === 'Desktop') await subformPalette.press('Enter');
    else await subformPalette.click();
    const dialog = page.locator('.formio-dialog');
    await dialog.getByLabel('Formular', { exact: true }).selectOption(componentFormId);
    await dialog.getByLabel('Konkrete Version', { exact: true }).selectOption('0.1');
    await dialog.getByRole('button', { name: 'Auswahl übernehmen', exact: true }).click();
    await dialog.locator('[ref="saveButton"]').click();

    await expect(page.getByText('Zustelladresse · v0.1', { exact: true })).toBeVisible();
    // Testzweck: Erneutes Bearbeiten behält die feste Referenz, ohne UUID oder Version einzutippen.
    const subform = page.locator('.builder-component').filter({ has: page.locator('.formio-component-flowzerForm') });
    await subform.getByRole('button', { name: 'Edit button. Click to open component settings modal window', exact: true }).click();
    await expect(dialog.getByLabel('Formular', { exact: true })).toHaveValue(componentFormId);
    await expect(dialog.getByLabel('Konkrete Version', { exact: true })).toHaveValue('0.1');
    await dialog.getByRole('button', { name: 'Abbrechen', exact: true }).click();
    await page.getByRole('button', { name: 'Entwurf speichern', exact: true }).click();
    await expect.poll(() => readDraft().components.some(component => component.type === 'flowzerForm'
      && component.formId === componentFormId && component.version === '0.1')).toBe(true);
    await page.getByRole('button', { name: 'Vorschau ansehen', exact: true }).click();
    await expect(page.getByText('Straße', { exact: true }).first()).toBeVisible();
  });
}

for (const width of [1280, 390]) {
  // Testzweck: Echtes Form.io übernimmt JSON und verschachtelte Werte, liefert Live-Ausgabe
  // und Standardwerte; Fehler oder Kopieren dürfen weder Datenverlust noch Schreibaufrufe auslösen.
  test(`JSON-Vorschau belegt Felder vor und zeigt echte Ausgaben bei ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockAuthoring(page, { display: 'form', components: [
      { type: 'textfield', key: 'reason', label: 'Begründung', input: true, defaultValue: 'Standard' },
      { type: 'container', key: 'address', label: 'Adresse', input: true, components: [
        { type: 'textfield', key: 'city', label: 'Ort', input: true, defaultValue: 'Standardort' },
      ] },
    ] });
    const writes = [];
    page.on('request', request => {
      const path = new URL(request.url()).pathname;
      if (path.startsWith('/api/') && request.method() !== 'GET' && !path.endsWith('/preview')) writes.push(path);
    });
    await page.goto('/forms');
    await page.getByText('JSON-Eingabe', { exact: true }).click();
    await page.getByText('JSON-Ausgabe', { exact: true }).click();
    const input = page.getByLabel('JSON-Testdaten', { exact: true });
    const output = page.getByLabel('Aktuelle Formularwerte als JSON', { exact: true });
    await expect.poll(async () => JSON.parse(await output.inputValue() || '{}').reason).toBe('Standard');
    await input.fill(JSON.stringify({ reason: 'Urlaub', address: { city: 'Bocholt' } }));
    await expect(page.getByRole('textbox', { name: 'Begründung', exact: true })).toHaveValue('Standard');
    await page.getByRole('button', { name: 'Eingabe übernehmen', exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Begründung', exact: true })).toHaveValue('Urlaub');
    await expect(page.getByRole('textbox', { name: 'Ort', exact: true })).toHaveValue('Bocholt');
    await page.getByRole('textbox', { name: 'Begründung', exact: true }).fill('Bearbeitet');
    await expect.poll(async () => JSON.parse(await output.inputValue() || '{}').reason).toBe('Bearbeitet');
    expect(JSON.parse(await output.inputValue()).address.city).toBe('Bocholt');
    await input.fill('{invalid}');
    await page.getByRole('button', { name: 'Eingabe übernehmen', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('Ungültiges JSON');
    await expect(page.getByRole('textbox', { name: 'Begründung', exact: true })).toHaveValue('Bearbeitet');
    await page.getByRole('button', { name: 'Testdaten zurücksetzen', exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Begründung', exact: true })).toHaveValue('Standard');
    await expect.poll(async () => JSON.parse(await output.inputValue() || '{}').address?.city).toBe('Standardort');
    // Testzweck: Testdaten dürfen beim Wechsel des Katalogformulars nicht übertragen werden.
    await input.fill('{"reason":"Nur für dieses Formular"}');
    await page.getByRole('button', { name: 'Eingabe übernehmen', exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Begründung', exact: true })).toHaveValue('Nur für dieses Formular');
    await page.getByRole('button', { name: 'Zustelladresse Personal · Form-Key: Zustelladresse', exact: true }).click();
    await expect(page.getByRole('textbox', { name: 'Begründung', exact: true })).toHaveValue('Standard');
    await page.getByText('JSON-Eingabe', { exact: true }).click();
    await expect(input).toHaveValue('{}');
    expect(writes).toEqual([]);
  });
}
