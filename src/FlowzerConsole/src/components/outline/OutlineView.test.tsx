import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';

import { OutlineView } from './OutlineView';
import { readOutline } from '@/lib/outline/read';

const XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  id="Definitions_1">
  <bpmn:process id="Process_1" isExecutable="true">
    <bpmn:startEvent id="Start_1" name="Urlaub geplant" />
    <bpmn:userTask id="Task_Antrag" name="Urlaubsantrag stellen">
      <bpmn:extensionElements>
        <zeebe:formDefinition formKey="Urlaubsantrag" />
        <zeebe:assignmentDefinition candidateGroups="Vorgesetzte" />
        <zeebe:taskSchedule dueDate="PT48H" />
      </bpmn:extensionElements>
    </bpmn:userTask>
    <bpmn:exclusiveGateway id="Gw_1" name="Genug Tage?" default="Flow_nein" />
    <bpmn:exclusiveGateway id="Gw_2" name="Fachlich frei?" default="Flow_nein_2" />
    <bpmn:userTask id="Task_Ja" name="Urlaub eintragen" />
    <bpmn:endEvent id="End_Ja" name="Urlaub genehmigt" />
    <bpmn:serviceTask id="Task_Ablehnung" name="Ablehnung mitteilen">
      <bpmn:extensionElements>
        <zeebe:taskDefinition type="urlaub-ablehnung-mitteilen" />
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:endEvent id="End_Nein" name="Antrag abgelehnt" />
    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_Antrag" />
    <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_Antrag" targetRef="Gw_1" />
    <bpmn:sequenceFlow id="Flow_ja" name="ja" sourceRef="Gw_1" targetRef="Gw_2">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=tageAusreichend = "ja"</bpmn:conditionExpression>
    </bpmn:sequenceFlow>
    <bpmn:sequenceFlow id="Flow_nein" name="nicht genug Tage" sourceRef="Gw_1" targetRef="Task_Ablehnung" />
    <bpmn:sequenceFlow id="Flow_ja_2" name="freigegeben" sourceRef="Gw_2" targetRef="Task_Ja">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=entscheidung = "ja"</bpmn:conditionExpression>
    </bpmn:sequenceFlow>
    <bpmn:sequenceFlow id="Flow_nein_2" name="abgelehnt" sourceRef="Gw_2" targetRef="Task_Ablehnung" />
    <bpmn:sequenceFlow id="Flow_3" sourceRef="Task_Ja" targetRef="End_Ja" />
    <bpmn:sequenceFlow id="Flow_4" sourceRef="Task_Ablehnung" targetRef="End_Nein" />
  </bpmn:process>
</bpmn:definitions>`;

function show() {
  const document = readOutline(XML).document!;
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <OutlineView document={document} selectedId={null} editable onSelect={() => {}} onChange={() => {}} />
    </QueryClientProvider>,
  );
}

// Testzweck: Die Gliederung muss lesbar sein, ohne dass jemand ein Diagramm
// bedienen kann — Schritte untereinander, Zweige mit Bedingung, und der leere
// Zweig sagt, wo es weitergeht, statt den Schritt zu verdoppeln.
describe('OutlineView', () => {
  it('zeigt Schritte mit Formular, Zuständigkeit und Frist', () => {
    show();

    expect(screen.getByText('Urlaubsantrag stellen')).toBeInTheDocument();
    expect(screen.getByText('Urlaubsantrag')).toBeInTheDocument();
    expect(screen.getByText('Vorgesetzte')).toBeInTheDocument();
    expect(screen.getByText('PT48H')).toBeInTheDocument();
    expect(screen.getByText('urlaub-ablehnung-mitteilen')).toBeInTheDocument();
  });

  it('zeigt die Verzweigung mit Beschriftung und Bedingung', () => {
    show();

    expect(screen.getByText('Genug Tage?')).toBeInTheDocument();
    expect(screen.getByText('ja')).toBeInTheDocument();
    expect(screen.getByText('nicht genug Tage')).toBeInTheDocument();
    expect(screen.getByText('=tageAusreichend = "ja"')).toBeInTheDocument();
  });

  it('nennt beim leeren Zweig den Schritt, mit dem es weitergeht', () => {
    const { container } = show();

    // Der Hinweistext steht neben Symbol und Schaltflaeche; deshalb der Blick auf den ganzen Text.
    expect(container.textContent).toContain('weiter mit „Ablehnung mitteilen"');
  });

  it('bietet Verschieben und Entfernen an jeder Zeile an', () => {
    show();

    expect(screen.getByLabelText('Nach unten: Urlaubsantrag stellen')).toBeInTheDocument();
    expect(screen.getByLabelText('Entfernen: Ablehnung mitteilen')).toBeInTheDocument();
  });
});
