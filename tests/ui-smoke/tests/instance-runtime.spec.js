const { test, expect } = require('@playwright/test');

const diagramXml = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI" targetNamespace="test">
  <bpmn:process id="Process_Runtime" isExecutable="true">
    <bpmn:startEvent id="Start" name="Gestartet"><bpmn:outgoing>F1</bpmn:outgoing></bpmn:startEvent>
    <bpmn:sequenceFlow id="F1" sourceRef="Start" targetRef="Review" />
    <bpmn:userTask id="Review" name="Prüfen"><bpmn:incoming>F1</bpmn:incoming></bpmn:userTask>
  </bpmn:process>
  <bpmndi:BPMNDiagram id="Diagram"><bpmndi:BPMNPlane id="Plane" bpmnElement="Process_Runtime">
    <bpmndi:BPMNShape id="Start_di" bpmnElement="Start"><dc:Bounds x="150" y="100" width="36" height="36" /></bpmndi:BPMNShape>
    <bpmndi:BPMNShape id="Review_di" bpmnElement="Review"><dc:Bounds x="260" y="78" width="120" height="80" /></bpmndi:BPMNShape>
    <bpmndi:BPMNEdge id="F1_di" bpmnElement="F1"><di:waypoint x="186" y="118" /><di:waypoint x="260" y="118" /></bpmndi:BPMNEdge>
  </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
</bpmn:definitions>`;

for (const viewport of [{ width: 1440, height: 960 }, { width: 390, height: 844 }]) {
  // Testzweck: Die objektberechtigte Runtime-Projektion zeigt auf Desktop und Mobil
  // Diagramm, Klartextstatus und echte Ereignisse ohne lineare x-von-y-Behauptung.
  test(`Laufzeitdiagramm und Ereignisse bei ${viewport.width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    const instanceId = '7c9388cc-233f-4f81-bcae-276b84531437';
    await page.route(`**/api/instance/${instanceId}/runtime-diagram`, route => route.fulfill({
      json: { successful: true, result: {
        instanceId, definitionId: 'a6c16d51-8d1b-4410-8c4b-b5288d794822',
        processId: 'Process_Runtime', state: 2, snapshotAtUtc: '2026-09-09T10:00:00Z',
        diagramXml,
        nodes: [
          { flowNodeId: 'Start', status: 1, tokenCount: 1, lastChangedAtUtc: '2026-09-09T09:59:00Z' },
          { flowNodeId: 'Review', status: 0, tokenCount: 3, lastChangedAtUtc: '2026-09-09T10:00:00Z' },
        ],
        events: [
          { id: 'event-1', flowNodeId: 'Start', state: 4, occurredAtUtc: '2026-09-09T09:59:00Z' },
          { id: 'event-2', flowNodeId: 'Review', state: 1, occurredAtUtc: '2026-09-09T10:00:00Z' },
        ],
      } },
    }));
    await page.route(`**/api/instance/${instanceId}/history`, route => route.fulfill({
      json: { successful: true, result: { instanceId, events: [] } },
    }));
    await page.route(`**/api/instance/${instanceId}/subscription/**`, route => route.fulfill({
      json: { successful: true, result: [] },
    }));
    await page.route(`**/api/instance/${instanceId}`, route => route.fulfill({
      json: { successful: true, result: {
        instanceId, definitionId: 'a6c16d51-8d1b-4410-8c4b-b5288d794822',
        relatedDefinitionId: 'urlaub', relatedDefinitionName: 'Urlaubsantrag – September',
        state: 'Waiting', canInspect: true, startedAt: '2026-09-09T09:58:00Z',
        tokens: [
          {
            id: 'master-token', parentTokenId: null, currentFlowNodeId: 'Process_Runtime', state: 'Active',
            variables: { requestId: 'REQ-42', days: 5 }, outputData: null,
            startTime: '2026-09-09T09:58:00Z', lastStateChangeTime: '2026-09-09T09:58:00Z',
          },
          {
            id: 'token-1', parentTokenId: 'master-token', currentFlowNodeId: 'Review', state: 'Active',
            variables: { reviewer: 'anna' }, outputData: { decision: 'open' },
            startTime: '2026-09-09T09:59:00Z', lastStateChangeTime: '2026-09-09T10:00:00Z',
          },
          {
            id: 'token-2', parentTokenId: 'master-token', currentFlowNodeId: 'Review', state: 'Active',
            variables: { reviewer: 'bert' }, outputData: null,
            startTime: '2026-09-09T09:59:10Z', lastStateChangeTime: '2026-09-09T10:00:00Z',
          },
          {
            id: 'token-3', parentTokenId: 'master-token', currentFlowNodeId: 'Review', state: 'Active',
            variables: { reviewer: 'carla' }, outputData: {},
            startTime: '2026-09-09T09:59:20Z', lastStateChangeTime: '2026-09-09T10:00:00Z',
          },
        ],
        userTaskSubscriptionCount: 1, messageSubscriptionCount: 0,
        signalSubscriptionCount: 0, serviceSubscriptionCount: 0,
      } },
    }));

    await page.goto(`/instances/${instanceId}`);

    await expect(page.getByRole('img', { name: 'BPMN-Laufzeitdiagramm' })).toBeVisible();
    await expect(page.getByRole('list', { name: 'Legende der Laufzeitzustände' })).toBeVisible();
    await expect(page.getByRole('button', { name: /Prüfen Aktiv ×3/ })).toBeVisible();
    await expect(page.locator('.flowzer-token')).toHaveCount(1);
    await expect(page.locator('.flowzer-token')).toHaveText('3');
    await expect(page.getByText('requestId')).toBeVisible();
    await expect(page.getByText('"REQ-42"')).toBeVisible();
    await page.getByRole('tab', { name: 'Schrittdaten' }).click();
    await expect(page.getByRole('article', { name: 'Ausführung 1 von 3' })).toContainText('"anna"');
    await expect(page.getByText('Für diese Ausführung ist noch kein Output-Snapshot persistiert.')).toBeVisible();
    await expect(page.getByText('Für diese Ausführung wurde ein leerer Output-Snapshot persistiert.')).toBeVisible();
    await page.getByRole('tab', { name: 'Verlauf' }).click();
    await expect(page.getByRole('list', { name: 'Engine-Ereignisse' })).toContainText('Prüfen');
    await expect(page.getByText(/\d+ von \d+ Elementen/)).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`runtime-${viewport.width}.png`), fullPage: true });
  });
}
