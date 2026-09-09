import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { BpmnDiagnostic } from '@/lib/modeling/diagnostics';

import { BpmnDiagnosticsPanel } from './BpmnDiagnosticsPanel';

const diagnostics: BpmnDiagnostic[] = [{
  code: 'bpmn.user-task.form-required',
  severity: 'error',
  message: 'Die Aufgabe braucht ein Formular.',
  elementId: 'Task_Review',
  propertyPath: 'zeebe:formDefinition.formKey',
  source: 'server',
}];

describe('BpmnDiagnosticsPanel', () => {
  // Testzweck: Elementbezogene Fehler müssen dauerhaft und tastaturbedienbar
  // anwählbar sein, damit Nutzer nicht anhand einer technischen ID suchen müssen.
  it('macht das betroffene Element als Schaltfläche anwählbar', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(<BpmnDiagnosticsPanel diagnostics={diagnostics} onSelectElement={onSelect} />);

    expect(screen.getByRole('region', { name: 'Modellprüfung' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Task_Review/ }));
    expect(onSelect).toHaveBeenCalledWith('Task_Review');
  });

  // Testzweck: Ein globaler Fehler ohne Element-ID bleibt verständlich sichtbar,
  // darf aber keinen nicht existierenden Fokus-Sprung anbieten.
  it('zeigt nicht adressierbare Diagnosen ohne Sprungschaltfläche', () => {
    render(<BpmnDiagnosticsPanel diagnostics={[{ ...diagnostics[0]!, elementId: undefined }]} />);
    expect(screen.getByText('Die Aufgabe braucht ein Formular.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Task_Review/ })).not.toBeInTheDocument();
  });
});
