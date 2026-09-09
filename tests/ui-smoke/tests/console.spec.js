const { test, expect } = require('@playwright/test');

const {
  buildMessageStartUserTaskXml,
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
 * Laesst die Konsole ohne Modelliererrolle laufen.
 *
 * Der BFF liefert die wirksamen Faehigkeiten serverseitig. Fuer diesen reinen UI-Test wird
 * seine datensparsame Sessionprojektion ohne `modeler` nachgebildet; die API-Autorisierung
 * selbst pruefen die .NET-Integrationstests.
 */
async function ohneModelliererrolle(page) {
  await page.route('**/config.json', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ apiBaseUrl: '/api', bffEnabled: true })
    })
  );
  await page.route('**/bff/session', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ id: 'ui-smoke', name: 'UI Smoke', capabilities: ['access', 'operator', 'worker'] })
    })
  );
  // Die API laeuft im Smoke absichtlich mit Authentication=None und besitzt deshalb kein
  // echtes BFF-Cookie. Globale Aufgaben- und Meldungsabfragen werden fuer diesen reinen
  // Rollen-UI-Test leer beantwortet, damit ihr erwartetes 401 die nachgebildete Sitzung
  // nicht beendet. Die fachliche Autorisierung dieser Endpunkte pruefen API-Tests.
  await page.route('**/api/usertask*', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ successful: true, result: [] })
    })
  );
  await page.route('**/api/instance', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ successful: true, result: [] })
    })
  );
  await page.route('**/api/notifications*', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ successful: true, result: [] })
    })
  );
}

/** Die Form im Diagramm — `data-element-id` und `transform` setzt diagram-js selbst. */
function shapeOf(page, elementId) {
  return page.locator(`.bpmn-surface .djs-shape[data-element-id="${elementId}"]`);
}

/**
 * Zieht eine Form mit der Maus. bpmn-js beginnt das Verschieben erst nach einer Schwelle
 * und braucht Zwischenschritte — ein einzelner Sprung von A nach B loest gar nichts aus.
 */
async function ziehe(page, locator, dx, dy) {
  const box = await locator.boundingBox();
  const x = box.x + box.width / 2;
  const y = box.y + box.height / 2;

  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x + dx / 2, y + dy / 2, { steps: 5 });
  await page.mouse.move(x + dx, y + dy, { steps: 5 });
  await page.mouse.up();
}

/** Oeffnet den Modeler eines Workflows und wartet, bis die Form im Diagramm steht. */
async function oeffneModeler(page, definitionId) {
  const startEventId = `StartEvent_${definitionId.replace(/[^A-Za-z0-9_]/g, '_')}`;

  await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);
  const startEvent = shapeOf(page, startEventId);
  await expect(startEvent).toBeVisible();

  return startEvent;
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
 * Legt einen deployten Workflow mit einer menschlichen Aufgabe samt Diagrammdaten an —
 * ohne die kann bpmn-js nichts zeichnen und nichts angeklickt werden.
 */
async function seedModelerWorkflow(request) {
  const marke = randomUUID().slice(0, 8);
  const formularName = `Antrag ${marke}`;
  await saveForm(request, {
    name: formularName,
    schema: JSON.stringify({ display: 'form', components: [] })
  });

  const name = `Modeler ${marke}`;
  const definitionId = await createDefinitionMeta(request, { name });
  const xml = buildMessageStartUserTaskXml({
    definitionId,
    messageName: `Start_${marke}`,
    formKey: formularName,
    userTaskName: 'Antrag pruefen'
  });
  await deployDefinition(request, { xml });

  const userTaskId = `Activity_${definitionId.replace(/[^A-Za-z0-9_]/g, '_')}_Review`;
  return { name, definitionId, formularName, userTaskId };
}

/**
 * Legt einen Workflow mit einer Aufgabe an, deren Eigenschaftenleiste laenger als das
 * Fenster wird: Formular, Zuweisung, Frist und sechs Zuordnungen.
 */
async function seedTaskWithLongPanel(request) {
  const marke = randomUUID().slice(0, 8);
  const formularName = `Lang ${marke}`;
  await saveForm(request, {
    name: formularName,
    schema: JSON.stringify({ display: 'form', components: [] })
  });

  const definitionId = await createDefinitionMeta(request, { name: `Lange Leiste ${marke}` });
  const s = definitionId.replace(/[^A-Za-z0-9_]/g, '_');
  const taskId = `Task_${s}`;
  const outputs = Array.from({ length: 6 }, (_, i) =>
    `<zeebe:output source="=wert${i + 1}" target="ziel${i + 1}" />`).join('\n          ');
  const xml = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  id="${definitionId}" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="Process_${s}" isExecutable="true">
    <bpmn:startEvent id="Start_${s}"><bpmn:outgoing>F1_${s}</bpmn:outgoing></bpmn:startEvent>
    <bpmn:sequenceFlow id="F1_${s}" sourceRef="Start_${s}" targetRef="${taskId}" />
    <bpmn:userTask id="${taskId}" name="Pruefen">
      <bpmn:extensionElements>
        <zeebe:formDefinition formKey="${formularName}" />
        <zeebe:assignmentDefinition candidateGroups="Pruefer" />
        <zeebe:taskSchedule dueDate="PT48H" />
        <zeebe:ioMapping>
          <zeebe:input source="=vorgang" target="vorgang" />
          ${outputs}
        </zeebe:ioMapping>
      </bpmn:extensionElements>
      <bpmn:incoming>F1_${s}</bpmn:incoming>
      <bpmn:outgoing>F2_${s}</bpmn:outgoing>
    </bpmn:userTask>
    <bpmn:sequenceFlow id="F2_${s}" sourceRef="${taskId}" targetRef="End_${s}" />
    <bpmn:endEvent id="End_${s}"><bpmn:incoming>F2_${s}</bpmn:incoming></bpmn:endEvent>
  </bpmn:process>
  <bpmndi:BPMNDiagram id="D_${s}">
    <bpmndi:BPMNPlane id="P_${s}" bpmnElement="Process_${s}">
      <bpmndi:BPMNShape id="Start_${s}_di" bpmnElement="Start_${s}"><dc:Bounds x="160" y="100" width="36" height="36" /></bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="${taskId}_di" bpmnElement="${taskId}"><dc:Bounds x="250" y="78" width="120" height="80" /></bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="End_${s}_di" bpmnElement="End_${s}"><dc:Bounds x="430" y="100" width="36" height="36" /></bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="F1_${s}_di" bpmnElement="F1_${s}"><di:waypoint x="196" y="118" /><di:waypoint x="250" y="118" /></bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="F2_${s}_di" bpmnElement="F2_${s}"><di:waypoint x="370" y="118" /><di:waypoint x="430" y="118" /></bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`;
  await deployDefinition(request, { xml });

  return { definitionId, taskId };
}

/**
 * Legt einen deployten Workflow an, dessen Startereignis ein Startformular traegt. Wer ihn
 * startet, muss das Formular zuerst ausfuellen.
 *
 * `components` erlaubt ein anderes Startformular als das voreingestellte Textfeld — etwa
 * eines mit Datumsfeld, das im Dialog einen Kalender aufklappt.
 */
async function seedStartFormWorkflow(
  request,
  components = [{ type: 'textfield', key: 'antragsteller', label: 'Antragsteller', input: true }]
) {
  const marke = randomUUID().slice(0, 8);
  const formularName = `Antragsdaten ${marke}`;
  await saveForm(request, {
    name: formularName,
    schema: JSON.stringify({
      display: 'form',
      components
    })
  });

  const name = `Startformular ${marke}`;
  const definitionId = await createDefinitionMeta(request, { name });
  const s = definitionId.replace(/[^A-Za-z0-9_]/g, '_');
  const xml = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  id="${definitionId}" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="Process_${s}" isExecutable="true">
    <bpmn:startEvent id="Start_${s}" name="Antrag stellen">
      <bpmn:extensionElements>
        <zeebe:formDefinition formKey="${formularName}" />
      </bpmn:extensionElements>
      <bpmn:outgoing>F1_${s}</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:sequenceFlow id="F1_${s}" sourceRef="Start_${s}" targetRef="End_${s}" />
    <bpmn:endEvent id="End_${s}" name="Fertig">
      <bpmn:incoming>F1_${s}</bpmn:incoming>
    </bpmn:endEvent>
  </bpmn:process>
  <bpmndi:BPMNDiagram id="D_${s}">
    <bpmndi:BPMNPlane id="P_${s}" bpmnElement="Process_${s}">
      <bpmndi:BPMNShape id="Start_${s}_di" bpmnElement="Start_${s}">
        <dc:Bounds x="173" y="102" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="End_${s}_di" bpmnElement="End_${s}">
        <dc:Bounds x="332" y="102" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="F1_${s}_di" bpmnElement="F1_${s}">
        <di:waypoint x="209" y="120" />
        <di:waypoint x="332" y="120" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`;
  await deployDefinition(request, { xml });

  return { name, definitionId, formularName };
}

/** Beschriftung des Datumsfeldes — ueber sie findet der Test die Eingabe wieder. */
const DATUMSFELD = 'Erster Urlaubstag';

/**
 * Ein Datumsfeld wie im Urlaubsantrag — Form.io haengt daran einen flatpickr-Kalender.
 *
 * Die Kalendereinstellungen (`component.widget`) baut Form.io selbst aus diesen Angaben; ein
 * eigener `widget`-Block im Schema waere wirkungslos. `showMeridian: false` stellt die Uhr
 * auf 24 Stunden — sonst haengt an der Stunde noch eine AM/PM-Schaltflaeche.
 */
function datumsfeld({ mitUhrzeit }) {
  return {
    type: 'datetime',
    key: 'von',
    label: DATUMSFELD,
    input: true,
    enableDate: true,
    enableTime: mitUhrzeit,
    timePicker: { showMeridian: false },
    format: mitUhrzeit ? 'dd.MM.yyyy HH:mm' : 'dd.MM.yyyy',
    allowInput: true
  };
}

/** Oeffnet den Startdialog eines Workflows und gibt ihn zurueck. */
async function oeffneStartdialog(page, definitionId) {
  await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);
  await page.getByRole('button', { name: 'Starten', exact: true }).click();

  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  return dialog;
}

/**
 * Das sichtbare Eingabefeld des Datumsfelds. flatpickr legt ueber das eigentliche Feld ein
 * zweites zur Anzeige (`altInput`) und versteckt das erste — nur das zweite ist bedienbar,
 * und nur es traegt die Beschriftung.
 */
function datumseingabe(dialog) {
  return dialog.getByRole('textbox', { name: DATUMSFELD });
}

/**
 * Klappt den Kalender des Feldes auf und waehlt einen Tag des laufenden Monats.
 *
 * Der Griff wird wiederholt, bis er durchgeht: Form.io laedt flatpickr nach und die
 * Entwicklungsfassung baut das Formular wegen React StrictMode zweimal auf — der Kalender
 * des ersten Anlaufs verschwindet dabei mitsamt seinen Tagen wieder. Der Kalender wird
 * bewusst auf der ganzen Seite gesucht, nicht im Dialog: Ohne Zutun haengt flatpickr ihn
 * ans <body>, und genau das soll der Test bemerken.
 */
async function waehleTagImKalender(dialog, feld) {
  const kalender = dialog.page().locator('.flatpickr-calendar.open');

  await expect(async () => {
    if ((await kalender.count()) === 0) {
      await feld.click({ timeout: 2000 });
    }

    await expect(kalender, 'Der Kalender klappt nicht auf.').toBeVisible({ timeout: 2000 });
    // Die Randtage gehoeren zum Nachbarmonat und wuerden den Monat umblaettern.
    await kalender
      .locator('.flatpickr-day:not(.prevMonthDay):not(.nextMonthDay):not(.flatpickr-disabled)')
      .nth(9)
      .click({ timeout: 2000 });
  }).toPass({ timeout: 25_000 });

  return kalender;
}

/**
 * Legt einen Workflow mit einem Timer-Ereignis und einer Nachricht an — die beiden Angaben,
 * die die Engine auswertet und die vorher nur im Camunda Modeler einzustellen waren.
 */
async function seedEventWorkflow(request) {
  const marke = randomUUID().slice(0, 8);
  const name = `Ereignisse ${marke}`;
  const definitionId = await createDefinitionMeta(request, { name });
  const s = definitionId.replace(/[^A-Za-z0-9_]/g, '_');
  const xml = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  id="${definitionId}" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:message id="Msg_${s}" name="Antrag ${marke}" />
  <bpmn:process id="Process_${s}" isExecutable="true">
    <bpmn:startEvent id="Start_${s}" name="Antrag kommt">
      <bpmn:outgoing>F1_${s}</bpmn:outgoing>
      <bpmn:messageEventDefinition id="MED_${s}" messageRef="Msg_${s}" />
    </bpmn:startEvent>
    <bpmn:intermediateCatchEvent id="Timer_${s}" name="Warten">
      <bpmn:incoming>F1_${s}</bpmn:incoming><bpmn:outgoing>F2_${s}</bpmn:outgoing>
      <bpmn:timerEventDefinition id="TED_${s}"><bpmn:timeDuration>PT1H</bpmn:timeDuration></bpmn:timerEventDefinition>
    </bpmn:intermediateCatchEvent>
    <bpmn:endEvent id="End_${s}"><bpmn:incoming>F2_${s}</bpmn:incoming></bpmn:endEvent>
    <bpmn:sequenceFlow id="F1_${s}" sourceRef="Start_${s}" targetRef="Timer_${s}" />
    <bpmn:sequenceFlow id="F2_${s}" sourceRef="Timer_${s}" targetRef="End_${s}" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="D_${s}"><bpmndi:BPMNPlane id="P_${s}" bpmnElement="Process_${s}">
    <bpmndi:BPMNShape id="Start_${s}_di" bpmnElement="Start_${s}"><dc:Bounds x="160" y="102" width="36" height="36" /></bpmndi:BPMNShape>
    <bpmndi:BPMNShape id="Timer_${s}_di" bpmnElement="Timer_${s}"><dc:Bounds x="280" y="102" width="36" height="36" /></bpmndi:BPMNShape>
    <bpmndi:BPMNShape id="End_${s}_di" bpmnElement="End_${s}"><dc:Bounds x="400" y="102" width="36" height="36" /></bpmndi:BPMNShape>
    <bpmndi:BPMNEdge id="F1_${s}_di" bpmnElement="F1_${s}"><di:waypoint x="196" y="120" /><di:waypoint x="280" y="120" /></bpmndi:BPMNEdge>
    <bpmndi:BPMNEdge id="F2_${s}_di" bpmnElement="F2_${s}"><di:waypoint x="316" y="120" /><di:waypoint x="400" y="120" /></bpmndi:BPMNEdge>
  </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
</bpmn:definitions>`;
  await deployDefinition(request, { xml });
  return { name, definitionId, timerId: `Timer_${s}`, startId: `Start_${s}`, nachrichtenname: `Antrag ${marke}` };
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

  // Testzweck: Die Seite selbst darf nicht scrollen, wenn die Eigenschaftenleiste des
  // Modelers laenger als das Fenster ist. Die versteckten Beschriftungen ihrer Knoepfe sind
  // absolut positioniert und verlaengerten das Dokument um die Ueberlaenge der Leiste — die
  // ganze Konsole liess sich nach unten schieben, in Safari wie in Chromium.
  test('Die Seite bleibt bei einer langen Eigenschaftenleiste unscrollbar', async ({ page, request }) => {
    const { definitionId, taskId } = await seedTaskWithLongPanel(request);

    await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);
    await expect(shapeOf(page, taskId)).toBeVisible();
    await shapeOf(page, taskId).click();
    await expect(page.getByRole('button', { name: 'Zuordnung 6 entfernen' })).toBeAttached();

    const masse = await page.evaluate(() => ({
      leiste: document.querySelector('.bpmn-surface')?.parentElement?.lastElementChild?.scrollHeight ?? 0,
      dokument: document.documentElement.scrollHeight,
      fenster: window.innerHeight
    }));
    expect(masse.leiste, 'Die Leiste muss laenger als das Fenster sein, sonst prueft der Test nichts.').toBeGreaterThan(masse.fenster);
    expect(masse.dokument, 'Das Dokument ist hoeher als das Fenster.').toBe(masse.fenster);
  });
  for (const [route, description, locate] of [
    ['/', 'Startseite', (page) => page.getByRole('button', { name: 'Prozess starten' })],
    ['/workflows', 'Workflows', (page) => page.getByRole('heading', { name: 'Workflows', level: 1 })],
    ['/instances', 'Instanzen', (page) => page.getByRole('heading', { name: 'Instanzen', level: 1 })],
    ['/forms', 'Formulare', (page) => page.getByRole('heading', { name: 'Formulare', level: 1 })],
    ['/form-sections', 'Formularabschnitte', (page) => page.getByRole('heading', { name: 'Formularabschnitte', level: 1 })],
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

  // Testzweck: Das Panel des Modelers ist ein eigenes und zeigt Flowzers Begriffe statt des
  // vollen Zeebe-Umfangs. Geprueft wird an der menschlichen Aufgabe, weil dort alle vier
  // Abschnitte zusammenkommen — und weil das Formular aus dem Bestand vorbelegt sein muss.
  test('Das Eigenschaften-Panel zeigt die Felder einer menschlichen Aufgabe', async ({ page, request }) => {
    const { definitionId, formularName, userTaskId } = await seedModelerWorkflow(request);

    await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);
    await page.locator(`.djs-element[data-element-id="${userTaskId}"]`).click();

    for (const abschnitt of ['Formular', 'Zuweisung', 'Frist', 'Zuordnungen']) {
      await expect(
        page.getByRole('heading', { name: abschnitt, exact: true }),
        `Der Abschnitt „${abschnitt}" fehlt im Eigenschaften-Panel.`
      ).toBeVisible();
    }

    await expect(page.getByRole('combobox', { name: 'Formular', exact: true })).toHaveValue(formularName);
  });

  // Testzweck: Die Markierung am Element sagt, dass an dieser Aufgabe ein Formular haengt.
  // Ohne sie ist dem Diagramm nicht anzusehen, welche Aufgabe schon eine Eingabemaske hat.
  test('Eine Aufgabe mit Formular ist im Diagramm markiert', async ({ page, request }) => {
    const { definitionId } = await seedModelerWorkflow(request);

    await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);

    await expect(page.locator('.flowzer-form-badge')).toHaveCount(1);
  });

  // Testzweck: Ein Formular laesst sich im Workflow selbst anlegen und wird mit ihm
  // gespeichert. Der Weg beruehrt beide neuen Teile — das eigene Panel schreibt
  // `zeebe:userTaskForm` ins Diagramm, und die Uebersicht zeigt es als Formular des
  // Workflows statt als eines aus dem Bestand.
  test('Ein Formular laesst sich im Workflow selbst anlegen', async ({ page, request }) => {
    const { definitionId, userTaskId } = await seedModelerWorkflow(request);

    await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);
    await page.locator(`.djs-element[data-element-id="${userTaskId}"]`).click();

    await page.getByRole('tab', { name: 'In diesem Workflow' }).click();
    await page.getByLabel('Formular im Workflow').selectOption({ label: 'Neues Formular anlegen …' });

    const dialog = page.getByRole('dialog', { name: 'Formular in diesem Workflow' });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Übernehmen' }).click();
    await expect(dialog).toBeHidden();

    // Die Aufgabe haengt jetzt an einem Formular des Workflows.
    await expect(page.getByRole('button', { name: 'Felder bearbeiten' })).toBeVisible();

    // Ohne Auswahl zeigt das Panel den ganzen Workflow — dort steht die Formularuebersicht.
    await page.locator('.bpmn-surface .djs-container').click({ position: { x: 400, y: 520 } });
    await expect(page.getByRole('heading', { name: 'Formulare in diesem Workflow' })).toBeVisible();
    await expect(page.getByText('Im Workflow', { exact: true })).toBeVisible();
    // Die Uebersicht verlinkt die Aufgabe, die das Formular benutzt.
    await expect(page.getByRole('button', { name: 'Antrag pruefen', exact: true })).toBeVisible();
  });

  // Testzweck: Timer und Nachricht wertet die Engine aus, und beide lassen sich hier
  // einstellen. Ohne diese Felder waere der Modellierer fuer solche Workflows unbrauchbar —
  // sie liessen sich zeichnen, aber nicht vollstaendig belegen.
  test('Das Panel stellt Zeitangabe und Nachricht ein', async ({ page, request }) => {
    const { definitionId, timerId, startId, nachrichtenname } = await seedEventWorkflow(request);

    await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);

    await page.locator(`.djs-element[data-element-id="${timerId}"]`).click();
    const zeitangabe = page.getByRole('region', { name: 'Zeitangabe' });
    await expect(zeitangabe.getByRole('textbox', { name: 'Wert' })).toHaveValue('PT1H');

    // Die Art bestimmt, was im Feld stehen darf; der Wechsel darf die Angabe nicht verlieren.
    await zeitangabe.getByRole('tab', { name: 'Zyklus' }).click();
    const wert = zeitangabe.getByRole('textbox', { name: 'Wert' });
    await wert.fill('R3/PT2H');
    await wert.press('Enter');

    // Mehrere Abschnitte haben ein Feld „Name"; der Bereichsname macht sie unterscheidbar.
    await page.locator(`.djs-element[data-element-id="${startId}"]`).click();
    const nachricht = page.getByRole('region', { name: 'Nachricht' });
    await expect(nachricht.getByRole('textbox', { name: 'Name' })).toHaveValue(nachrichtenname);

    const schluessel = nachricht.getByRole('textbox', { name: 'Korrelationsschlüssel' });
    await schluessel.fill('=antragsnummer');
    await schluessel.press('Enter');

    // Erst das Speichern beweist, dass die Engine das Ergebnis auch liest.
    await page.getByRole('button', { name: 'Speichern' }).click();
    await expect(page.getByText(/^Version v\d+\.\d+ gespeichert$/)).toBeVisible();

    // Und erst das erneute Laden beweist, dass die alte Zeitangabe wirklich weg ist: Stuenden
    // Dauer und Zyklus beide im XML, naehme die Engine die Dauer — und das Speichern waere
    // trotzdem gruen.
    await page.reload();
    await page.locator(`.djs-element[data-element-id="${timerId}"]`).click();
    const nachDemLaden = page.getByRole('region', { name: 'Zeitangabe' });
    await expect(nachDemLaden.getByRole('tab', { name: 'Zyklus' })).toHaveAttribute('aria-selected', 'true');
    await expect(nachDemLaden.getByRole('textbox', { name: 'Wert' })).toHaveValue('R3/PT2H');
  });

  // Testzweck: Die Gegenprobe zur Sperre unten. Mit Modelliererrolle bleibt der Modeler ein
  // Modeler — Kontextpad, Verschieben und der Hinweis auf ungespeicherte Aenderungen
  // funktionieren. Ohne diesen Test koennte die Sperre alles lahmlegen und die Pruefung
  // darunter trotzdem gruen sein.
  test('Mit Modelliererrolle laesst sich das Diagramm bearbeiten', async ({ page, request }) => {
    const { definitionId } = await seedWorkflow(request);
    const startEvent = await oeffneModeler(page, definitionId);
    const vorher = await startEvent.getAttribute('transform');

    await startEvent.click();
    await expect(page.locator('.djs-context-pad')).toBeVisible();

    await ziehe(page, startEvent, 90, 70);

    await expect(startEvent).not.toHaveAttribute('transform', vorher);
    await expect(page.getByText('Ungespeicherte Änderungen')).toBeVisible();
  });

  // Testzweck: Ohne Modelliererrolle ist die Zeichenflaeche eine Ansicht. Vorher liessen
  // sich Elemente anlegen, verschieben und loeschen, obwohl Speichern und Deployen gar
  // nicht angeboten wurden: Die Seite warnte beim Verlassen vor Aenderungen, die niemand
  // mehr loswurde.
  test('Ohne Modelliererrolle bleibt das Diagramm eine Ansicht', async ({ page, request }) => {
    const { definitionId } = await seedWorkflow(request);
    await ohneModelliererrolle(page);

    const startEvent = await oeffneModeler(page, definitionId);
    const vorher = await startEvent.getAttribute('transform');

    await expect(page.getByText('Nur Ansicht')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Speichern' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Deployen' })).toHaveCount(0);

    // Keine Palette: Ohne Anbieter zeichnet diagram-js sie gar nicht erst.
    await expect(page.locator('.djs-palette')).toHaveCount(0);

    // Auswaehlen bleibt erlaubt — dafuer ist das Eigenschaften-Panel da —, das Kontextpad
    // mit Anhaengen, Verbinden und Loeschen geht dabei aber nicht auf.
    await startEvent.click();
    await expect(page.locator('.djs-context-pad')).toHaveCount(0);

    await ziehe(page, startEvent, 90, 70);

    // `transform` steht in Diagrammkoordinaten: Ein Verschieben der Zeichenflaeche
    // veraenderte den Wert nicht, ein Verschieben des Elements schon.
    await expect(startEvent).toHaveAttribute('transform', vorher);
    await expect(page.getByText('Ungespeicherte Änderungen')).toHaveCount(0);
  });

  // Testzweck: Die Sperre nimmt der Zeichenflaeche das Bearbeiten, dem Panel aber nicht den
  // Inhalt. Auswaehlen muss weiter gehen, sonst waere die Ansicht eine Blackbox: Das Panel
  // zeigt nur, was gerade gewaehlt ist.
  test('Ohne Modelliererrolle bleiben die Eigenschaften lesbar', async ({ page, request }) => {
    const { definitionId } = await seedWorkflow(request);
    await ohneModelliererrolle(page);

    const startEvent = await oeffneModeler(page, definitionId);
    await startEvent.click();

    const nameFeld = page
      .getByRole('region', { name: 'Allgemein' })
      .getByRole('textbox', { name: 'Name', exact: true });
    await expect(nameFeld).toHaveValue('Start');
    await expect(nameFeld, 'Das Eigenschaften-Panel nimmt weiterhin Eingaben an.').toBeDisabled();

    await expect(page.getByText('Ungespeicherte Änderungen')).toHaveCount(0);
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

  // Testzweck: Traegt der Workflow ein Startformular, wird es vor dem Start ausgefuellt.
  // Ohne diesen Weg bliebe ungeprueft, ob die Konsole das Formular ueberhaupt anfordert —
  // sie startete dann sofort, und die API lehnte den Start ohne Werte ab.
  test('Ein Workflow mit Startformular fragt vor dem Start seine Werte ab', async ({ page, request }) => {
    const { definitionId, name } = await seedStartFormWorkflow(request);

    await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);
    await page.getByRole('button', { name: 'Starten', exact: true }).click();

    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('heading', { name: `\u201E${name}" starten` })).toBeVisible();
    await expect(dialog.getByText('Antragsteller')).toBeVisible();
  });

  // Testzweck: Ein Datumsfeld im Startdialog laesst sich ueber den Kalender ausfuellen.
  // Der Kalender haengt sonst am <body>, den der modale Dialog fuer Zeigereingaben sperrt:
  // Der Klick auf einen Tag traf das Overlay, galt flatpickr als Klick nach draussen und
  // schloss den Kalender wieder. Von Hand tippen ging, auswaehlen nicht.
  test('Ein Datumsfeld im Startdialog laesst sich ueber den Kalender ausfuellen', async ({ page, request }) => {
    const { definitionId } = await seedStartFormWorkflow(request, [datumsfeld({ mitUhrzeit: false })]);

    const dialog = await oeffneStartdialog(page, definitionId);
    const feld = datumseingabe(dialog);

    await waehleTagImKalender(dialog, feld);

    await expect(feld, 'Der angeklickte Tag steht nicht im Eingabefeld.').not.toHaveValue('');
    await expect(dialog, 'Der Klick in den Kalender hat den Startdialog geschlossen.').toBeVisible();
  });

  // Testzweck: Auch die Uhrzeit laesst sich im Startdialog stellen. Sie sitzt in eigenen
  // Zahlenfeldern unten im Kalender — sie braucht neben dem Zeigerklick auch den Fokus,
  // den die Fokusfalle des Dialogs ausserhalb seines Inhalts nicht zulaesst.
  test('Ein Datumsfeld mit Uhrzeit laesst sich im Startdialog stellen', async ({ page, request }) => {
    const { definitionId } = await seedStartFormWorkflow(request, [datumsfeld({ mitUhrzeit: true })]);

    const dialog = await oeffneStartdialog(page, definitionId);
    const feld = datumseingabe(dialog);

    const kalender = await waehleTagImKalender(dialog, feld);
    const mitDatum = await feld.inputValue();

    // Auch hier gilt der Neuaufbau aus waehleTagImKalender: erst wiederholen, dann urteilen.
    await expect(async () => {
      await kalender.locator('.numInputWrapper:has(input.flatpickr-hour) .arrowUp').click({ timeout: 2000 });
      await expect(
        feld,
        'Die Stundenschaltflaeche im Kalender aendert die Uhrzeit nicht.'
      ).not.toHaveValue(mitDatum, { timeout: 1000 });
    }).toPass({ timeout: 15_000 });

    await expect(dialog, 'Der Klick in den Kalender hat den Startdialog geschlossen.').toBeVisible();
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
