import { describe, expect, it } from 'vitest';

import {
  describeFormKey,
  embeddedFormKey,
  newEmbeddedFormId,
  parseFormKey,
  storedFormKey,
} from './formKey';

// Testzweck: Der Form-Key ist der Vertrag zwischen Modeler und Engine. Wird er hier anders
// gelesen als im UserTaskFormResolver, zeigt die Konsole ein anderes Formular an als die
// Aufgabe später bekommt.
describe('parseFormKey', () => {
  it('erkennt ein Formular aus dem Bestand ohne Version', () => {
    expect(parseFormKey('Urlaubsantrag')).toEqual({ kind: 'stored', name: 'Urlaubsantrag', version: null });
  });

  it('trennt eine angehängte Version ab', () => {
    expect(parseFormKey('Urlaubsantrag:1.0')).toEqual({
      kind: 'stored',
      name: 'Urlaubsantrag',
      version: '1.0',
    });
  });

  it('lässt einen Doppelpunkt im Namen stehen, wenn kein Versionssuffix folgt', () => {
    expect(parseFormKey('Prüfung: Detail')).toEqual({
      kind: 'stored',
      name: 'Prüfung: Detail',
      version: null,
    });
  });

  it('erkennt ein Formular aus dem Workflow an Camundas Präfix', () => {
    expect(parseFormKey('camunda-forms:bpmn:Form_Urlaub_ab12cd')).toEqual({
      kind: 'embedded',
      formId: 'Form_Urlaub_ab12cd',
    });
  });

  it('behandelt einen leeren Schlüssel als „kein Formular“', () => {
    expect(parseFormKey(undefined)).toEqual({ kind: 'none' });
    expect(parseFormKey('   ')).toEqual({ kind: 'none' });
  });
});

describe('embeddedFormKey und storedFormKey', () => {
  it('erzeugen Schlüssel, die parseFormKey wieder zerlegt', () => {
    expect(parseFormKey(embeddedFormKey('Form_1'))).toEqual({ kind: 'embedded', formId: 'Form_1' });
    expect(parseFormKey(storedFormKey('Urlaubsantrag'))).toEqual({
      kind: 'stored',
      name: 'Urlaubsantrag',
      version: null,
    });
    expect(parseFormKey(storedFormKey('Urlaubsantrag', '2.1'))).toEqual({
      kind: 'stored',
      name: 'Urlaubsantrag',
      version: '2.1',
    });
  });

  it('lässt eine leere Versionsangabe weg, statt einen Doppelpunkt anzuhängen', () => {
    expect(storedFormKey('Urlaubsantrag', '  ')).toBe('Urlaubsantrag');
  });
});

describe('describeFormKey', () => {
  it('beschreibt beide Herkünfte lesbar', () => {
    expect(describeFormKey('Urlaubsantrag:1.0')).toBe('Urlaubsantrag · v1.0');
    expect(describeFormKey('camunda-forms:bpmn:Form_Urlaub')).toBe('Form_Urlaub');
    expect(describeFormKey(null)).toBe('Kein Formular');
  });
});

describe('newEmbeddedFormId', () => {
  it('erzeugt eine Kennung ohne Sonderzeichen aus dem Aufgabennamen', () => {
    expect(newEmbeddedFormId('Urlaub prüfen!')).toMatch(/^Form_Urlaub_prufen_[a-z0-9]+$/);
  });

  it('fällt auf einen festen Stamm zurück, wenn der Name nichts Brauchbares hergibt', () => {
    expect(newEmbeddedFormId('  —  ')).toMatch(/^Form_Formular_[a-z0-9]+$/);
  });
});
