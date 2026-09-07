import { describe, expect, it } from 'vitest';

import { startStepFor } from './useStartWorkflow';

// Testzweck: Der Startablauf hängt an genau einer Entscheidung — ohne Startformular sofort
// starten, mit Formular erst ausfüllen lassen. Fiele sie falsch aus, liefe entweder ein
// Workflow ohne seine Startwerte los oder es öffnete sich ein leerer Dialog.
describe('startStepFor', () => {
  it('startet sofort, wenn der Workflow kein Startformular hat', () => {
    expect(startStepFor(null)).toEqual({ kind: 'start' });
  });

  it('lässt das Startformular ausfüllen', () => {
    expect(startStepFor({ formData: '{"display":"form"}' })).toEqual({
      kind: 'form',
      schema: '{"display":"form"}',
    });
  });

  it('öffnet den Dialog auch ohne hinterlegtes Schema — der Renderer meldet den Mangel', () => {
    expect(startStepFor({})).toEqual({ kind: 'form', schema: '' });
  });
});
