/**
 * Legt einen lauffähigen Beispielprozess in einer leeren Flowzer-Instanz an.
 *
 * Zweck: Nach dem ersten Start ist die Ablage leer — Dashboard, Instanzen und
 * Aufgaben hätten dann nichts zu zeigen. Dieses Skript erzeugt einen einfachen
 * Rechnungsfreigabe-Prozess samt Formular und startet eine Instanz, damit die
 * Konsole sofort mit echten Daten geprüft werden kann.
 *
 *   node scripts/seed-demo.mjs [http://localhost:5182]
 *
 * Es ist ausdrücklich ein Entwicklungswerkzeug und gehört nicht in Produktion.
 */

const BASE_URL = (process.argv[2] ?? process.env.FLOWZER_API_URL ?? 'http://localhost:5182').replace(/\/+$/, '');

// Entspricht dem System-Fallback-Benutzer der API; im Development-Modus wird der
// Header ausgewertet, sodass Schreiboperationen einen aufgelösten Benutzer haben.
const USER_ID = 'd266f2b6-e96e-4d4a-9c20-c8e541394df0';

const DEFINITION_ID = 'flowzer-demo-rechnungsfreigabe';
const PROCESS_ID = 'Process_Rechnungsfreigabe';
const FORM_NAME = 'Rechnungsfreigabe';

const FORM_SCHEMA = {
  display: 'form',
  components: [
    {
      type: 'textfield',
      key: 'rechnungsnummer',
      label: 'Rechnungsnummer',
      defaultValue: 'RE-2026-0417',
      validate: { required: true },
      input: true,
    },
    {
      type: 'number',
      key: 'betrag',
      label: 'Betrag (EUR)',
      defaultValue: 4812,
      validate: { required: true },
      input: true,
    },
    { type: 'textfield', key: 'lieferant', label: 'Lieferant', defaultValue: 'Kontorwerk GmbH', input: true },
    {
      type: 'select',
      key: 'kostenstelle',
      label: 'Kostenstelle',
      data: {
        values: [
          { label: 'KST-400 · Marketing', value: 'KST-400' },
          { label: 'KST-120 · Büromaterial', value: 'KST-120' },
        ],
      },
      defaultValue: 'KST-400',
      input: true,
    },
    { type: 'checkbox', key: 'geprueft', label: 'Sachlich und rechnerisch geprüft', input: true },
    { type: 'textarea', key: 'notiz', label: 'Freigabe-Notiz', input: true },
    {
      type: 'radio',
      key: 'entscheidung',
      label: 'Entscheidung',
      values: [
        { label: 'Freigeben', value: 'freigabe' },
        { label: 'Ablehnen', value: 'ablehnung' },
      ],
      defaultValue: 'freigabe',
      validate: { required: true },
      input: true,
    },
  ],
};

const BPMN = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  id="${DEFINITION_ID}" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="${PROCESS_ID}" name="Rechnungsfreigabe" isExecutable="true">
    <bpmn:startEvent id="Start_Rechnung" name="Rechnung eingegangen">
      <bpmn:outgoing>Flow_1</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:userTask id="Task_Freigabe" name="Freigabe erteilen">
      <bpmn:extensionElements>
        <zeebe:formDefinition formKey="${FORM_NAME}" />
        <zeebe:assignmentDefinition candidateGroups="Buchhaltung" />
        <zeebe:taskSchedule dueDate="PT48H" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_1</bpmn:incoming>
      <bpmn:outgoing>Flow_2</bpmn:outgoing>
    </bpmn:userTask>
    <!-- Das default-Attribut ist Pflicht: Die Engine bricht ab, wenn ein ausgehender
         Fluss weder eine Bedingung trägt noch als Standardfluss markiert ist. -->
    <bpmn:exclusiveGateway id="Gateway_Entscheidung" name="Freigegeben?" default="Flow_4">
      <bpmn:incoming>Flow_2</bpmn:incoming>
      <bpmn:outgoing>Flow_3</bpmn:outgoing>
      <bpmn:outgoing>Flow_4</bpmn:outgoing>
    </bpmn:exclusiveGateway>
    <bpmn:endEvent id="End_Freigegeben" name="Freigegeben">
      <bpmn:incoming>Flow_3</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:endEvent id="End_Abgelehnt" name="Abgelehnt">
      <bpmn:incoming>Flow_4</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_Rechnung" targetRef="Task_Freigabe" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_Freigabe" targetRef="Gateway_Entscheidung" />
    <bpmn:sequenceFlow id="Flow_3" name="freigegeben" sourceRef="Gateway_Entscheidung" targetRef="End_Freigegeben">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=entscheidung = "freigabe"</bpmn:conditionExpression>
    </bpmn:sequenceFlow>
    <bpmn:sequenceFlow id="Flow_4" name="abgelehnt" sourceRef="Gateway_Entscheidung" targetRef="End_Abgelehnt" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="${PROCESS_ID}">
      <bpmndi:BPMNShape id="Shape_Start" bpmnElement="Start_Rechnung">
        <dc:Bounds x="160" y="182" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="140" y="225" width="80" height="27" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Shape_Task" bpmnElement="Task_Freigabe">
        <dc:Bounds x="260" y="160" width="120" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Shape_Gateway" bpmnElement="Gateway_Entscheidung" isMarkerVisible="true">
        <dc:Bounds x="445" y="175" width="50" height="50" />
        <bpmndi:BPMNLabel><dc:Bounds x="437" y="145" width="67" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Shape_EndOk" bpmnElement="End_Freigegeben">
        <dc:Bounds x="592" y="182" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="580" y="225" width="61" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Shape_EndNok" bpmnElement="End_Abgelehnt">
        <dc:Bounds x="592" y="292" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="585" y="335" width="51" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Edge_1" bpmnElement="Flow_1">
        <di:waypoint x="196" y="200" /><di:waypoint x="260" y="200" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Edge_2" bpmnElement="Flow_2">
        <di:waypoint x="380" y="200" /><di:waypoint x="445" y="200" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Edge_3" bpmnElement="Flow_3">
        <di:waypoint x="495" y="200" /><di:waypoint x="592" y="200" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Edge_4" bpmnElement="Flow_4">
        <di:waypoint x="470" y="225" /><di:waypoint x="470" y="310" /><di:waypoint x="592" y="310" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`;

async function call(path, { method = 'GET', body, rawBody, contentType } = {}) {
  const response = await fetch(`${BASE_URL}${path}`, {
    method,
    headers: {
      Accept: 'application/json, text/plain',
      'X-Flowzer-UserId': USER_ID,
      ...(rawBody !== undefined
        ? { 'Content-Type': contentType ?? 'application/xml' }
        : body !== undefined
          ? { 'Content-Type': 'application/json' }
          : {}),
    },
    body: rawBody ?? (body !== undefined ? JSON.stringify(body) : undefined),
  });

  const text = await response.text();
  let parsed;
  try {
    parsed = text ? JSON.parse(text) : null;
  } catch {
    parsed = text;
  }

  if (!response.ok) {
    const detail = parsed?.errorMessage ?? parsed?.title ?? text;
    throw new Error(`${method} ${path} → ${response.status}: ${detail}`);
  }

  if (parsed && typeof parsed === 'object' && parsed.successful === false) {
    throw new Error(`${method} ${path} → ${parsed.errorMessage}`);
  }

  return parsed;
}

async function main() {
  console.log(`Flowzer-API: ${BASE_URL}`);

  // 1. Formular anlegen (Metadaten + erste Version). Der Name ist zugleich der
  //    Form-Key, über den der User-Task das Formular findet.
  const existingForms = (await call('/form/meta'))?.result ?? [];
  let formId = existingForms.find((form) => form.name === FORM_NAME)?.formId;

  if (!formId) {
    formId = crypto.randomUUID();
    await call(`/form/meta/${formId}`, { method: 'POST', body: { formId, name: FORM_NAME } });
    await call('/form', {
      method: 'POST',
      body: { formId, version: { major: 1, minor: 0 }, formData: JSON.stringify(FORM_SCHEMA) },
    });
    console.log(`✓ Formular „${FORM_NAME}" angelegt (${formId})`);
  } else {
    console.log(`· Formular „${FORM_NAME}" existiert bereits (${formId})`);
  }

  // 2. Katalogeintrag anlegen — ohne ihn lehnt die API den Deploy ab.
  // Auch der Katalog liefert den einheitlichen Umschlag; das Ergebnis steht in `result`.
  const metaResponse = await call('/definition/meta');
  const metas = metaResponse?.result ?? metaResponse ?? [];
  if (!metas.some((meta) => meta.definitionId === DEFINITION_ID)) {
    await call('/definition/meta', {
      method: 'POST',
      body: {
        definitionId: DEFINITION_ID,
        name: 'Rechnungsfreigabe',
        description: 'Eingangsrechnungen prüfen, freigeben und zur Zahlung übergeben.',
      },
    });
    console.log('✓ Katalogeintrag „Rechnungsfreigabe" angelegt');
  } else {
    console.log('· Katalogeintrag existiert bereits');
  }

  // 3. Definition deployen.
  const deployed = await call('/definition/deploy', { method: 'POST', rawBody: BPMN });
  const version = deployed.result.version;
  console.log(`✓ Deployt als v${version.major}.${version.minor}`);

  // 4. Eine Instanz starten, damit Instanzliste und Aufgaben gefüllt sind.
  const instance = await call(`/definition/meta/${DEFINITION_ID}/instance`, { method: 'POST' });
  console.log(`✓ Instanz gestartet: ${instance.result.instanceId}`);

  const tasks = (await call('/usertask'))?.result ?? [];
  console.log(`✓ Offene Aufgaben: ${tasks.length}`);
  for (const task of tasks) {
    console.log(`   · ${task.name} (Form-Key: ${task.formKey ?? '—'}, fällig: ${task.dueDate ?? '—'})`);
  }
}

main().catch((error) => {
  console.error(`✗ ${error.message}`);
  process.exitCode = 1;
});
