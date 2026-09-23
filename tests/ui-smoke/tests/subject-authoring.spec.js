const { test, expect } = require('@playwright/test');
const { saveForm, randomUUID } = require('../support/flowzer-api');

// Testzweck: Der echte Form.io-Dialog bietet Suchfilter statt JSON, übernimmt sie in
// die ungespeicherte Vorschau und lässt dort eine Auswahl ohne Prozessstart ausprobieren.
test('Benutzerfeld lässt sich einfach konfigurieren und ohne Instanz ausprobieren', async ({ page, request }, testInfo) => {
  const name = `Personenauswahl ${randomUUID().slice(0, 8)}`;
  const form = await saveForm(request, { name, schema: JSON.stringify({ flowzer: { contractVersion: 2 }, components: [
    { type: 'flowzerSubject', key: 'representative', label: 'Vertretung', input: true, flowzer: { subjectSelection: { allowUsers: true } } }
  ] }) });
  const groupId = '11111111-1111-4111-8111-111111111111';
  const userId = '22222222-2222-4222-8222-222222222222';
  const group = { subject: { kind: 'group', id: groupId }, displayName: 'Team Einkauf', detail: '/Firma/Einkauf', isActive: true, isSelectable: true };
  const user = { subject: { kind: 'user', id: userId }, displayName: 'Anna Beispiel', email: 'anna@example.test', username: 'anna', detail: 'external-subject', isActive: true, isSelectable: true };
  const previews = [];
  await page.route('**/identity-directory/authoring-forms/**/subjects/*', async route => {
    const body = route.request().postDataJSON();
    if (body.formData) previews.push(body);
    const items = route.request().url().endsWith('/resolve')
      ? [group, user].filter(item => body.subjects.some(ref => ref.id === item.subject.id))
      : body.kind === 'group' ? [group] : [user];
    await route.fulfill({ json: { successful: true, result: { generationId: groupId, items } } });
  });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/forms');
  await page.getByRole('button', { name, exact: false }).first().click();
  await page.getByRole('button', { name: 'Bearbeiten', exact: true }).click();
  await page.locator('.formio-component-flowzerSubject').hover();
  await page.getByRole('button', { name: 'Edit button. Click to open component settings modal window', exact: true }).click();
  await expect(page.getByLabel('Was darf ausgewählt werden?')).toHaveValue('user');
  await page.getByLabel('Was darf ausgewählt werden?').selectOption('all');
  await page.getByRole('searchbox', { name: 'Benutzer aus diesen Gruppen suchen' }).fill('Einkauf');
  await page.getByRole('option', { name: /Team Einkauf/ }).click();
  await page.getByLabel('Mehrere Benutzer oder Gruppen erlauben').check();
  await expect(page.getByLabel('Technischer Schlüssel')).not.toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('subject-settings.png'), animations: 'disabled' });
  await page.locator('[ref="saveButton"]').click();
  await page.getByRole('button', { name: 'Vorschau ansehen', exact: true }).click();
  await page.getByRole('searchbox', { name: 'Vertretung suchen' }).fill('anna');
  await expect(page.getByRole('option', { name: /Anna Beispiel.*anna@example.test/ })).toBeVisible();
  await page.getByRole('option', { name: /Anna Beispiel/ }).click();
  await expect(page.getByLabel('Vertretung ausgewählt')).toContainText('Anna Beispiel');
  const searched = previews.findLast(body => body.query === 'anna');
  expect(searched.fieldKey).toBe('representative');
  const field = JSON.parse(searched.formData).components.find(field => field.key === 'representative');
  expect(field.multiple).toBe(true);
  expect(field.flowzer.subjectSelection).toMatchObject({ allowUsers: true, allowGroups: true, userMemberOfGroupIds: [groupId] });
  await page.screenshot({ path: testInfo.outputPath('subject-preview.png'), animations: 'disabled' });
  expect(errors).toEqual([]);
  expect(form.formId).toBeTruthy();
});

// Testzweck: Auch das erstmalige Einfügen aus der echten Palette muss sofort den
// einfachen Dialog öffnen, ohne alten Form.io-tabs-Fehler oder technische Pflichtangaben.
test('Neues Benutzerfeld startet mit einfacher Standardkonfiguration', async ({ page, request }) => {
  const name = `Neues Personenfeld ${randomUUID().slice(0, 8)}`;
  await saveForm(request, { name, schema: JSON.stringify({ components: [] }) });
  await page.goto('/forms');
  await page.getByRole('button', { name, exact: false }).first().click();
  await page.getByRole('button', { name: 'Bearbeiten', exact: true }).click();
  await page.locator('[data-type="flowzerSubject"]').dragTo(page.locator('.drag-container').first());
  await expect(page.getByLabel('Was darf ausgewählt werden?')).toHaveValue('user');
  await expect(page.getByLabel('Mehrere Benutzer oder Gruppen erlauben')).not.toBeChecked();
  await expect(page.getByLabel('E-Mail-Adresse', { exact: true })).toBeChecked();
  await page.getByLabel('Was darf ausgewählt werden?').selectOption('group');
  await expect(page.getByRole('searchbox', { name: 'Bestimmte Gruppen suchen' })).toBeVisible();
  await expect(page.getByRole('searchbox', { name: 'Bestimmte Benutzer suchen' })).toHaveCount(0);
  await page.locator('[ref="saveButton"]').click();
  await page.getByRole('button', { name: 'Vorschau ansehen', exact: true }).click();
  await expect(page.getByPlaceholder('Gruppe suchen …')).toBeVisible();
});
