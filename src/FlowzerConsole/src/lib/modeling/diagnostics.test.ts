import { describe, expect, it } from 'vitest';

import { ApiError } from '@/lib/api/client';

import { normalizeBpmnDiagnostics, normalizeBpmnWarnings } from './diagnostics';

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

  // Testzweck: Die Codes des Fähigkeitsvertrags v9 — ereignisbasiertes und inklusives Tor,
  // Ereignis-Subprozess, Eskalationsstart — müssen die deutsche Handlungsanweisung bekommen
  // statt der technischen Servermeldung.
  it('ersetzt die Servermeldung der v9-Codes durch eine Handlungsanweisung', () => {
    const codes = [
      'bpmn.inclusive_gateway.condition_required',
      'bpmn.inclusive_gateway.default.invalid_reference',
      'bpmn.event_based_gateway.invalid_target',
      'bpmn.event_based_gateway.outgoing_required',
      'bpmn.event_based_gateway.condition_not_allowed',
      'bpmn.event_subprocess.start_required',
      'bpmn.start_event.event_subprocess_only',
    ];

    const diagnostics = normalizeBpmnDiagnostics(new ApiError('Modell ungültig', {
      status: 422,
      url: '/definition/deploy',
      body: {
        code: 'bpmn.model.invalid',
        issues: codes.map((code) => ({ code, severity: 'error', message: 'technische Meldung' })),
      },
    }));

    expect(diagnostics).toHaveLength(codes.length);
    for (const diagnostic of diagnostics) {
      expect(diagnostic.message).not.toBe('technische Meldung');
      expect(diagnostic.message.length).toBeGreaterThan(20);
    }
  });

  // Testzweck: Hinweise einer erfolgreichen Prüfung müssen als Warnung mit Sprungziel
  // ankommen — sie begleiten eine Veröffentlichung, sie verhindern sie nicht.
  it('liest die Warnungen einer erfolgreichen Prüfung', () => {
    expect(normalizeBpmnWarnings({
      contractVersion: '9',
      elements: [],
      warnings: [{
        code: 'bpmn.user_task.form_missing',
        severity: 'warning',
        elementId: 'Human',
        propertyPath: 'extensionElements.formDefinition.formKey',
        message: 'technische Meldung',
      }],
    })).toEqual([{
      code: 'bpmn.user_task.form_missing',
      severity: 'warning',
      elementId: 'Human',
      propertyPath: 'extensionElements.formDefinition.formKey',
      message: expect.stringContaining('kein Formular') as unknown as string,
      source: 'server',
    }]);
  });

  // Testzweck: Eine Antwort ohne Warnungen darf keine erfinden — sonst stünde nach jedem
  // erfolgreichen Speichern ein Hinweisfeld ohne Inhalt.
  it('meldet ohne Warnungen nichts', () => {
    expect(normalizeBpmnWarnings({ contractVersion: '9', elements: [] })).toEqual([]);
    expect(normalizeBpmnWarnings(undefined)).toEqual([]);
    expect(normalizeBpmnWarnings({ warnings: [{ severity: 'warning' }] })).toEqual([]);
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
