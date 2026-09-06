const { test, expect } = require('@playwright/test');

const {
  buildPlainStartXml,
  createDefinitionMeta,
  deployDefinition,
  saveForm,
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

/**
 * Legt einen Workflow mit genau einer Aufgabe an, deren Formular eine Auswahl und ein
 * verstecktes Feld enthaelt — die beiden Bauteile, aus denen die wiederverwendbaren
 * Formulare bestehen.
 */
async function seedFormTask(request) {
  const marke = randomUUID().slice(0, 8);
  const formularName = `Freigabe ${marke}`;
  await saveForm(request, {
    name: formularName,
    schema: JSON.stringify({
      display: 'form',
      components: [
        { type: 'hidden', key: 'vorgang', label: '', hideLabel: true, input: true },
        {
          type: 'radio', key: 'entscheidung', label: 'Freigabe erteilt?', input: true, inline: true,
          values: [
            { label: 'Freigegeben', value: 'freigegeben' },
            { label: 'Abgelehnt', value: 'abgelehnt' }
          ]
        }
      ]
    })
  });

  const name = `Formular ${marke}`;
  const definitionId = await createDefinitionMeta(request, { name });
  const pid = `Process_${marke}`;
  const xml = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  id="${definitionId}" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="${pid}" isExecutable="true">
    <bpmn:startEvent id="Start_${marke}"><bpmn:outgoing>F1_${marke}</bpmn:outgoing></bpmn:startEvent>
    <bpmn:sequenceFlow id="F1_${marke}" sourceRef="Start_${marke}" targetRef="Task_${marke}" />
    <bpmn:userTask id="Task_${marke}" name="Freigeben">
      <bpmn:extensionElements><zeebe:formDefinition formKey="${formularName}" /></bpmn:extensionElements>
      <bpmn:incoming>F1_${marke}</bpmn:incoming>
      <bpmn:outgoing>F2_${marke}</bpmn:outgoing>
    </bpmn:userTask>
    <bpmn:sequenceFlow id="F2_${marke}" sourceRef="Task_${marke}" targetRef="End_${marke}" />
    <bpmn:endEvent id="End_${marke}"><bpmn:incoming>F2_${marke}</bpmn:incoming></bpmn:endEvent>
  </bpmn:process>
</bpmn:definitions>`;
  await deployDefinition(request, { xml });
  await startProcessInstance(request, { definitionId });
  return { name, formularName };
}

/**
 * Ein Ablauf, den die Gliederung vollstaendig abbildet: ein Schritt mit Formular und ein
 * Tor mit Bedingung und Standardweg.
 */
function buildOutlineXml({ definitionId, marke, formularName }) {
  return `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  id="${definitionId}" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="Process_${marke}" isExecutable="true">
    <bpmn:startEvent id="Start_${marke}" name="Antrag da" />
    <bpmn:userTask id="Task_${marke}" name="Antrag pruefen">
      <bpmn:extensionElements>
        <zeebe:formDefinition formKey="${formularName}" />
        <zeebe:assignmentDefinition candidateGroups="Vorgesetzte" />
      </bpmn:extensionElements>
    </bpmn:userTask>
    <bpmn:exclusiveGateway id="Gw_${marke}" name="Genug Budget?" default="Nein_${marke}" />
    <bpmn:endEvent id="Ja_${marke}" name="Freigegeben" />
    <bpmn:endEvent id="Nein_End_${marke}" name="Abgelehnt" />
    <bpmn:sequenceFlow id="F1_${marke}" sourceRef="Start_${marke}" targetRef="Task_${marke}" />
    <bpmn:sequenceFlow id="F2_${marke}" sourceRef="Task_${marke}" targetRef="Gw_${marke}" />
    <bpmn:sequenceFlow id="Ja_Flow_${marke}" name="ja" sourceRef="Gw_${marke}" targetRef="Ja_${marke}">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=entscheidung = "ja"</bpmn:conditionExpression>
    </bpmn:sequenceFlow>
    <bpmn:sequenceFlow id="Nein_${marke}" name="sonst" sourceRef="Gw_${marke}" targetRef="Nein_End_${marke}" />
  </bpmn:process>
</bpmn:definitions>`;
}

/** Ein Ablauf mit einer Aufgabe, die die Gliederung nicht kennt: eine schlichte `bpmn:task`. */
function buildUnsupportedXml({ definitionId, marke }) {
  return `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  id="${definitionId}" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="Process_${marke}" isExecutable="true">
    <bpmn:startEvent id="Start_${marke}" name="Start" />
    <bpmn:task id="Task_${marke}" name="Irgendetwas" />
    <bpmn:endEvent id="End_${marke}" name="Fertig" />
    <bpmn:sequenceFlow id="F1_${marke}" sourceRef="Start_${marke}" targetRef="Task_${marke}" />
    <bpmn:sequenceFlow id="F2_${marke}" sourceRef="Task_${marke}" targetRef="End_${marke}" />
  </bpmn:process>
</bpmn:definitions>`;
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

  // Testzweck: Auf Telefonbreite navigiert die untere Reiterleiste, die Seitenleiste ist
  // weg, und die Aufgabenliste weicht der geoeffneten Aufgabe. Vorher war die Konsole dort
  // gar nicht bedienbar: Die Seitenleiste nahm zwei Drittel der Breite, und Liste und
  // Formular standen als Streifen nebeneinander.
  test('Auf Telefonbreite fuehrt die untere Reiterleiste', async ({ page, request }) => {
    await seedWorkflow(request);
    await page.setViewportSize({ width: 375, height: 812 });

    await page.goto('/tasks');
    const reiter = page.getByRole('navigation', { name: 'Hauptbereiche' });
    await expect(reiter).toBeVisible();
    await expect(page.locator('aside')).toBeHidden();

    // Nichts darf seitlich aus dem Bild laufen.
    const ueberlauf = await page.evaluate(
      () => document.documentElement.scrollWidth - window.innerWidth,
    );
    expect(ueberlauf, 'Die Seite laesst sich seitlich schieben.').toBeLessThanOrEqual(0);

    await reiter.getByRole('link', { name: /Instanzen/ }).click();
    await expect(page.getByRole('heading', { name: 'Instanzen' })).toBeVisible();
  });

  // Testzweck: Eine Auswahl mit `inline` steht nebeneinander, und ein verstecktes Feld
  // zeigt nichts an. Beides ging vorher schief: Die Regeln der Konsole griffen nur auf
  // `.form-check`, Form.io setzt bei einem Radio aber `.radio.form-check-inline` — die
  // Optionen standen ungestylt untereinander. Und ein Feld vom Typ `hidden` zeigte
  // trotzdem seine Beschriftung.
  test('Ein Aufgabenformular zeichnet Auswahl und verstecktes Feld richtig', async ({ page, request }) => {
    await seedFormTask(request);

    await page.goto('/tasks');
    await expect(page.getByText('Freigabe erteilt?')).toBeVisible();

    const optionen = page.locator('.formio-surface [role="radiogroup"] .form-check-input');
    await expect(optionen).toHaveCount(2);

    const erste = await optionen.nth(0).boundingBox();
    const zweite = await optionen.nth(1).boundingBox();
    expect(erste, 'Die erste Wahlmoeglichkeit wird nicht gezeichnet.').not.toBeNull();
    expect(zweite, 'Die zweite Wahlmoeglichkeit wird nicht gezeichnet.').not.toBeNull();
    expect(
      zweite.x,
      'Die Wahlmoeglichkeiten stehen untereinander, obwohl das Formular sie nebeneinander verlangt.'
    ).toBeGreaterThan(erste.x);

    const verstecktesFeld = page.locator('.formio-component-hidden');
    await expect(verstecktesFeld).toHaveCount(1);
    await expect(verstecktesFeld, 'Das versteckte Feld zeigt Text an.').toHaveText('');
  });

  // Testzweck: Ein Formular, das ein deployter Workflow benutzt, laesst sich in der
  // Oberflaeche nicht wegklicken — und die Person erfaehrt, warum. Der Schutz sitzt in der
  // API; ohne diesen Weg bliebe ungeprueft, ob die Konsole die Begruendung ueberhaupt zeigt.
  test('Ein benutztes Formular laesst sich nicht loeschen', async ({ page, request }) => {
    const { formularName } = await seedFormTask(request);

    await page.goto('/forms');
    await page.getByRole('button', { name: formularName, exact: false }).first().click();

    await page.getByRole('button', { name: `${formularName} löschen` }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('button', { name: 'Endgültig löschen' }).click();

    await expect(page.getByText(/wird von .* benutzt/)).toBeVisible();
    await page.goto('/forms');
    await expect(page.getByRole('button', { name: formularName, exact: false }).first()).toBeVisible();
  });

  // Testzweck: Ein Formular laesst sich aus der Oberflaeche entfernen. Bisher liess sich der
  // Formularbestand nur befuellen — ein Testformular blieb fuer immer stehen.
  test('Ein Formular laesst sich loeschen', async ({ page, request }) => {
    const name = `Formular ${randomUUID().slice(0, 8)}`;
    await saveForm(request, {
      name,
      schema: JSON.stringify({ display: 'form', components: [] })
    });

    await page.goto('/forms');
    await page.getByRole('button', { name, exact: false }).first().click();

    await page.getByRole('button', { name: `${name} löschen` }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Endgültig löschen' }).click();

    await expect(page.getByText(name, { exact: true })).toHaveCount(0);
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

  // Testzweck: Die Gliederung zeigt denselben Workflow als Liste statt als Diagramm —
  // Schritt mit Formular, Tor mit Bedingung. Sie ist die zweite Oberflaeche neben dem
  // Modeler und muss ohne bpmn-js zeichnen.
  test('Die Gliederung zeigt den Ablauf als Liste', async ({ page, request }) => {
    const marke = randomUUID().slice(0, 8);
    const formularName = `Gliederung ${marke}`;
    await saveForm(request, { name: formularName, schema: JSON.stringify({ display: 'form', components: [] }) });

    const definitionId = await createDefinitionMeta(request, { name: `Gliederung ${marke}` });
    await deployDefinition(request, { xml: buildOutlineXml({ definitionId, marke, formularName }) });

    await page.goto(`/workflows/${encodeURIComponent(definitionId)}/gliederung`);

    await expect(page.getByText('Antrag pruefen', { exact: true })).toBeVisible();
    await expect(page.getByText('Genug Budget?', { exact: true })).toBeVisible();
    await expect(page.getByText('=entscheidung = "ja"', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Speichern' })).toBeEnabled();
  });

  // Testzweck: Der wichtigste Punkt der Gliederung. Ein Modell, das sie nicht vollstaendig
  // abbildet, muss sichtbar gemeldet werden und darf nicht gespeichert werden koennen —
  // sonst gingen die nicht dargestellten Teile beim Speichern still verloren.
  test('Ein nicht abbildbarer Workflow sperrt das Speichern in der Gliederung', async ({ page, request }) => {
    const marke = randomUUID().slice(0, 8);
    const definitionId = await createDefinitionMeta(request, { name: `Unbekannt ${marke}` });
    await deployDefinition(request, { xml: buildUnsupportedXml({ definitionId, marke }) });

    await page.goto(`/workflows/${encodeURIComponent(definitionId)}/gliederung`);

    await expect(page.getByText('Dieser Workflow lässt sich in der Gliederung nicht vollständig abbilden')).toBeVisible();
    await expect(page.getByText('bpmn:task', { exact: false })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Speichern' })).toBeDisabled();
  });
});
