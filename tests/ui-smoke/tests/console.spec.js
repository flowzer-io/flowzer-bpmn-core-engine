const { test, expect } = require('@playwright/test');

const {
  buildPlainStartXml,
  createDefinitionMeta,
  deployDefinition,
  startProcessInstance,
  randomUUID
} = require('../support/flowzer-api');

/**
 * Smoke-Tests der Flowzer Console.
 *
 * Zweck ist nicht, Verhalten im Detail zu pruefen — dafuer gibt es die Unit-Tests der
 * Konsole. Hier geht es um das, was nur im echten Browser auffaellt: dass jede Kernseite
 * ueberhaupt zeichnet, dass die Konsole die API unter demselben Ursprung erreicht und dass
 * der Modeler eine sichtbare Zeichenflaeche bekommt.
 */

/** Legt einen deployten Workflow mit laufender Instanz an, damit die Seiten Inhalt haben. */
async function seedWorkflow(request) {
  const name = `Smoke ${randomUUID().slice(0, 8)}`;
  const definitionId = await createDefinitionMeta(request, { name });
  const xml = buildPlainStartXml({ definitionId });
  await deployDefinition(request, { xml });
  const instance = await startProcessInstance(request, { definitionId });
  return { name, definitionId, instance };
}

test.describe('Konsole', () => {
  for (const [route, description, locate] of [
    ['/', 'Startseite', (page) => page.getByRole('button', { name: 'Prozess starten' })],
    ['/workflows', 'Workflows', (page) => page.getByRole('heading', { name: 'Workflows', level: 1 })],
    ['/instances', 'Instanzen', (page) => page.getByRole('heading', { name: 'Instanzen', level: 1 })],
    ['/forms', 'Formulare', (page) => page.getByRole('heading', { name: 'Formulare', level: 1 })],
    ['/tasks', 'Aufgaben', (page) => page.getByText('Zu erledigen', { exact: true })]
  ]) {
    // Testzweck: Jede Hauptseite zeichnet ihren Inhalt — eine Seite, die beim Laden
    // abbricht, faellt hier auf, bevor sie jemand im Betrieb oeffnet. Geprueft wird je
    // Seite ein Element, das unabhaengig vom Datenbestand da ist.
    test(`Route ${route} zeichnet die Seite „${description}"`, async ({ page }) => {
      await page.goto(route);
      await expect(locate(page)).toBeVisible();
    });
  }

  // Testzweck: Die Konsole spricht die API ueber denselben Ursprung an. Antwortete das
  // Gateway auf einen API-Pfad mit der Startseite, bliebe der Katalog leer statt zu
  // scheitern — der Fehler waere von aussen unsichtbar.
  test('Ein deployter Workflow erscheint im Katalog', async ({ page, request }) => {
    const { name } = await seedWorkflow(request);

    await page.goto('/workflows');
    await expect(page.getByText(name, { exact: true })).toBeVisible();
  });

  // Testzweck: Der Modeler bekommt eine sichtbare Zeichenflaeche. Genau das fehlte, als
  // die Seite ihre Hoehe ueber eine Prozentangabe gegen einen Flex-Container bezog: In
  // Safari blieb die Flaeche 0 Pixel hoch, der Modeler schien nicht zu starten.
  test('Der Modeler oeffnet mit sichtbarem Diagramm', async ({ page, request }) => {
    const { name, definitionId } = await seedWorkflow(request);

    await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);
    await expect(page.getByRole('button', { name })).toBeVisible();

    const canvas = page.locator('.bpmn-surface .djs-container').first();
    await expect(canvas).toBeVisible();

    const box = await canvas.boundingBox();
    expect(box, 'Der Modeler hat keine Zeichenflaeche.').not.toBeNull();
    expect(box.height, 'Die Zeichenflaeche des Modelers ist nicht hoch genug.').toBeGreaterThan(200);

    // Die Palette wird erst gezeichnet, wenn bpmn-js vollstaendig hochgelaufen ist.
    await expect(page.locator('.djs-palette')).toBeVisible();
  });

  // Testzweck: Anlegen fragt zuerst den Namen und legt erst danach an. Vorher entstand
  // sofort ein Eintrag „New Definition"; ein Abbruch liess ihn im Katalog zurueck.
  test('Neuer Workflow fragt zuerst nach dem Namen', async ({ page }) => {
    const name = `Dialog ${randomUUID().slice(0, 8)}`;

    await page.goto('/workflows');
    await page.getByRole('button', { name: 'Neuer Workflow' }).click();

    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();

    // Ohne Namen darf nichts angelegt werden.
    await expect(dialog.getByRole('button', { name: 'Anlegen und öffnen' })).toBeDisabled();

    await dialog.getByRole('textbox').fill(name);
    await dialog.getByRole('button', { name: 'Anlegen und öffnen' }).click();

    // Nach dem Anlegen steht der eingegebene Name im Modeler.
    await expect(page.getByRole('button', { name })).toBeVisible();
  });

  // Testzweck: Ein Workflow laesst sich aus der Oberflaeche wieder entfernen — der
  // Katalog liess sich vorher nur befuellen, nicht aufraeumen.
  test('Ein Workflow laesst sich loeschen', async ({ page, request }) => {
    const name = `Loeschen ${randomUUID().slice(0, 8)}`;
    await createDefinitionMeta(request, { name });

    await page.goto('/workflows');
    const deleteButton = page.getByRole('button', { name: `${name} löschen` });
    await deleteButton.click();

    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Endgültig löschen' }).click();

    await expect(page.getByText(name, { exact: true })).toHaveCount(0);
  });
});
