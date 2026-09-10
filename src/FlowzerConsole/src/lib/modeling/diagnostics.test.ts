import { describe, expect, it } from 'vitest';

import { ApiError } from '@/lib/api/client';

import { normalizeBpmnDiagnostics } from './diagnostics';

describe('BPMN-Diagnosen', () => {
  // Testzweck: Der stabile Problem-Details-Vertrag muss die betroffene BPMN-ID und
  // die Eigenschaft erhalten, damit die Oberfläche das Element anwählen kann.
  it('normalisiert elementbezogene Problem-Details', () => {
    const error = new ApiError('Modell ungültig', {
      status: 422,
      url: '/definition/deploy',
      body: {
        code: 'bpmn.model.invalid',
        issues: [{
          code: 'bpmn.user-task.form-required',
          severity: 'error',
          elementId: 'Task_Review',
          propertyPath: 'zeebe:formDefinition.formKey',
          message: 'Die Aufgabe braucht ein Formular.',
        }],
      },
    });

    expect(normalizeBpmnDiagnostics(error)).toEqual([{
      code: 'bpmn.user-task.form-required',
      severity: 'error',
      elementId: 'Task_Review',
      propertyPath: 'zeebe:formDefinition.formKey',
      message: 'Die Aufgabe braucht ein Formular.',
      source: 'server',
    }]);
  });

  // Testzweck: Legacy-Fehler ohne den neuen Vertrag dürfen nicht als scheinbar
  // adressierbare Modellfehler dargestellt werden; der Aufrufer zeigt dafür einen Toast.
  it('ignoriert Legacy- und fremde Problemantworten', () => {
    expect(normalizeBpmnDiagnostics(new ApiError('alt', {
      status: 400,
      url: '/definition',
      body: { successful: false, errorMessage: 'alt' },
    }))).toEqual([]);
    expect(normalizeBpmnDiagnostics(new ApiError('fremd', {
      status: 422,
      url: '/definition',
      body: { code: 'form.invalid', issues: [] },
    }))).toEqual([]);
  });

  // Testzweck: Fehlende oder manipulierte Detailfelder dürfen keine falschen
  // Sprungziele erzeugen und werden als sichere, nicht adressierbare Diagnose verworfen.
  it('verwirft ungültige Detailfelder', () => {
    expect(normalizeBpmnDiagnostics(new ApiError('ungültig', {
      status: 422,
      url: '/definition',
      body: {
        code: 'bpmn.model.invalid',
        issues: [
          { code: 'bpmn.bad', severity: 'error', elementId: 42, message: 'nicht gültig' },
          { code: 'bpmn.ok', severity: 'warning', message: 'Hinweis' },
        ],
      },
    }))).toEqual([
      {
        code: 'bpmn.bad',
        severity: 'error',
        message: 'nicht gültig',
        source: 'server',
      },
      {
        code: 'bpmn.ok',
        severity: 'warning',
        message: 'Hinweis',
        source: 'server',
      },
    ]);
  });
});
