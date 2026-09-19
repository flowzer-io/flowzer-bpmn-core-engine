import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ImportWorkflowDialog } from './ImportWorkflowDialog';

const CAMUNDA7_XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:camunda="http://camunda.org/schema/1.0/bpmn"
                  id="Definitions_Alt" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="bestellprozess" name="Bestellprozess" isExecutable="true">
    <bpmn:serviceTask id="Task_Bonitaet" name="Bonität prüfen"
                      camunda:type="external" camunda:topic="bonitaet-pruefen" camunda:asyncBefore="true" />
    <bpmn:scriptTask id="Task_Rabatt" name="Rabatt berechnen" scriptFormat="groovy" />
  </bpmn:process>
</bpmn:definitions>`;

const PLAIN_XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  id="Definitions_Neu" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="einfach" name="Einfacher Ablauf" isExecutable="true">
    <bpmn:startEvent id="Start_1" />
  </bpmn:process>
</bpmn:definitions>`;

function bpmnFile(xml: string, fileName: string): File {
  return new File([xml], fileName, { type: 'application/xml' });
}

async function chooseFile(xml: string, fileName: string) {
  const user = userEvent.setup();
  await user.upload(screen.getByLabelText('BPMN-Datei'), bpmnFile(xml, fileName));
  return user;
}

describe('ImportWorkflowDialog', () => {
  // Testzweck: Der Bericht muss vor dem Anlegen dastehen — mit dem, was übernommen wurde,
  // was entfällt und was nachzuarbeiten ist.
  it('zeigt den Bericht in drei Gruppen, sobald eine Camunda-7-Datei gewählt ist', async () => {
    render(
      <ImportWorkflowDialog open onOpenChange={vi.fn()} onCreate={vi.fn()} onOpenModeler={vi.fn()} />,
    );

    await chooseFile(CAMUNDA7_XML, 'Bestellung.bpmn');

    await screen.findByText(/stammt aus Camunda 7/);
    expect(screen.getByText(/Übernommen/)).toBeInTheDocument();
    expect(screen.getByText(/Nacharbeit nötig/)).toBeInTheDocument();
    expect(screen.getByText(/Entfallen/)).toBeInTheDocument();

    // Je Element eine Zeile, mit dem, was mit ihm geschehen ist.
    expect(screen.getByText(/External Task wird Flowzer-Auftrag/)).toBeInTheDocument();
    expect(screen.getByText(/camunda:asyncBefore entfernt/)).toBeInTheDocument();
    expect(screen.getAllByText(/Von Flowzer nicht ausgeführt/).length).toBeGreaterThan(0);
  });

  // Testzweck: Der Name kommt aus dem Prozess, nicht aus der Dateikennung — sonst stünde
  // im Katalog eine technische Kennung.
  it('belegt den Namen aus dem Prozessnamen vor', async () => {
    render(
      <ImportWorkflowDialog open onOpenChange={vi.fn()} onCreate={vi.fn()} onOpenModeler={vi.fn()} />,
    );

    await chooseFile(CAMUNDA7_XML, 'Bestellung.bpmn');

    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveValue('Bestellprozess'));
  });

  // Testzweck: Angelegt wird mit dem übersetzten Modell, nicht mit der Originaldatei —
  // sonst liefe der Import ins Leere.
  it('legt den Workflow mit dem übersetzten XML an', async () => {
    const onCreate = vi.fn().mockResolvedValue('definition_1');
    render(
      <ImportWorkflowDialog open onOpenChange={vi.fn()} onCreate={onCreate} onOpenModeler={vi.fn()} />,
    );

    const user = await chooseFile(CAMUNDA7_XML, 'Bestellung.bpmn');
    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveValue('Bestellprozess'));
    await user.click(screen.getByRole('button', { name: 'Als neuen Workflow anlegen' }));

    await waitFor(() => expect(onCreate).toHaveBeenCalledTimes(1));
    const workflow = onCreate.mock.calls[0]![0] as { name: string; xml: string; fromCamunda7: boolean };

    expect(workflow.name).toBe('Bestellprozess');
    expect(workflow.fromCamunda7).toBe(true);
    expect(workflow.xml).toContain('<zeebe:taskDefinition type="bonitaet-pruefen"/>');
    expect(workflow.xml).not.toContain('camunda:');
  });

  // Testzweck: Nach dem Anlegen bleibt der Bericht lesbar; der Modellierer wird erst auf
  // Wunsch geöffnet. Sonst wäre die einzige Gelegenheit, ihn zu lesen, vorbei.
  it('lässt den Bericht nach dem Anlegen stehen und öffnet den Modellierer auf Klick', async () => {
    const onOpenModeler = vi.fn();
    const onOpenChange = vi.fn();
    render(
      <ImportWorkflowDialog
        open
        onOpenChange={onOpenChange}
        onCreate={vi.fn().mockResolvedValue('definition_1')}
        onOpenModeler={onOpenModeler}
      />,
    );

    const user = await chooseFile(CAMUNDA7_XML, 'Bestellung.bpmn');
    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveValue('Bestellprozess'));
    await user.click(screen.getByRole('button', { name: 'Als neuen Workflow anlegen' }));

    const open = await screen.findByRole('button', { name: 'Im Modellierer öffnen' });
    expect(screen.getByText(/External Task wird Flowzer-Auftrag/)).toBeInTheDocument();
    expect(onOpenModeler).not.toHaveBeenCalled();

    await user.click(open);
    expect(onOpenModeler).toHaveBeenCalledWith('definition_1');
    expect(onOpenChange).toHaveBeenLastCalledWith(false);
  });

  // Testzweck: Eine gewöhnliche BPMN-Datei ist über denselben Weg importierbar; ohne
  // etwas zu berichten führt der Import ohne Umweg in den Modellierer.
  it('importiert eine Datei ohne Camunda-Angaben ohne Zwischenschritt', async () => {
    const onOpenModeler = vi.fn();
    const onCreate = vi.fn().mockResolvedValue('definition_2');
    render(
      <ImportWorkflowDialog open onOpenChange={vi.fn()} onCreate={onCreate} onOpenModeler={onOpenModeler} />,
    );

    const user = await chooseFile(PLAIN_XML, 'Einfach.bpmn');
    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveValue('Einfacher Ablauf'));
    expect(screen.getByText(/keine Camunda-7-Angaben/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Als neuen Workflow anlegen' }));

    await waitFor(() => expect(onOpenModeler).toHaveBeenCalledWith('definition_2'));
    expect((onCreate.mock.calls[0]![0] as { fromCamunda7: boolean }).fromCamunda7).toBe(false);
  });

  // Testzweck: Eine unlesbare Datei darf nicht als leerer Workflow durchgehen.
  it('meldet eine unlesbare Datei und bietet das Anlegen nicht an', async () => {
    render(
      <ImportWorkflowDialog open onOpenChange={vi.fn()} onCreate={vi.fn()} onOpenModeler={vi.fn()} />,
    );

    await chooseFile('Das ist kein BPMN.', 'Kaputt.bpmn');

    await screen.findByText('Die Datei ist kein lesbares BPMN-XML.');
    expect(screen.getByRole('button', { name: 'Als neuen Workflow anlegen' })).toBeDisabled();
  });

  // Testzweck: Scheitert das Anlegen, bleibt die gewählte Datei stehen — wer sie erneut
  // suchen müsste, verlöre den Bericht gleich mit.
  it('behält Datei und Bericht, wenn das Anlegen scheitert', async () => {
    const onCreate = vi.fn().mockRejectedValue(new Error('Keine Berechtigung'));
    render(
      <ImportWorkflowDialog open onOpenChange={vi.fn()} onCreate={onCreate} onOpenModeler={vi.fn()} />,
    );

    const user = await chooseFile(CAMUNDA7_XML, 'Bestellung.bpmn');
    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveValue('Bestellprozess'));
    await user.click(screen.getByRole('button', { name: 'Als neuen Workflow anlegen' }));

    await screen.findByText('Keine Berechtigung');
    expect(screen.getByText(/External Task wird Flowzer-Auftrag/)).toBeInTheDocument();
  });
});
