const { test, expect } = require('@playwright/test');
const { buildPlainStartXml, createDefinitionMeta, deployDefinition, saveForm, randomUUID } = require('../support/flowzer-api');

// Testzweck: Eine serverseitige Ablehnung bleibt im Startdialog sichtbar und nimmt weder
// Text noch Fokusführung. Die eigentlichen Serverregeln prüft die separate JWT-API-Suite.
test('Feldfehler bleiben im Startdialog, die Eingabe bleibt erhalten', async ({ page, request }) => {
  const name = `Validierung ${randomUUID().slice(0, 8)}`;
  await saveForm(request, { name, schema: JSON.stringify({ components: [
    { type: 'textfield', key: 'reason', label: 'Begründung', input: true, validate: { required: true } },
  ] }) });
  const definitionId = await createDefinitionMeta(request, { name });
  const xml = buildPlainStartXml({ definitionId }).replace(/(<bpmn:startEvent[^>]*>)/,
    `$1<bpmn:extensionElements><zeebe:formDefinition xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" formKey="${name}" /></bpmn:extensionElements>`);
  await deployDefinition(request, { xml });
  await page.route(`**/api/definition/meta/${definitionId}/instance`, route => route.fulfill({
    status: 422, contentType: 'application/problem+json', body: JSON.stringify({
      status: 422, successful: false, errorMessage: 'Invalid input', errors: { reason: ['text.max_length'] },
    }),
  }));
  await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);
  await page.getByRole('button', { name: 'Starten', exact: true }).click();
  const dialog = page.getByRole('dialog');
  const input = dialog.locator('input[name="data[reason]"]');
  await input.fill('Diese Eingabe bleibt erhalten');
  await dialog.getByRole('button', { name: 'Starten', exact: true }).click();
  const alert = dialog.getByRole('alert');
  await expect(alert).toContainText('Begründung');
  await expect(alert).toContainText('Die Eingabe ist zu lang.');
  await expect(alert).toBeFocused();
  await expect(input).toHaveValue('Diese Eingabe bleibt erhalten');
});
