import { describe, expect, it } from 'vitest';

import type { UserTaskDraftDto } from './api/types';
import {
  cloneProcessVariables,
  createTaskDraftState,
  taskDraftReducer,
  type TaskDraftState,
} from './taskDraft';

const draft = (data: Record<string, unknown>, revision = 1): UserTaskDraftDto => ({
  userTaskId: 'task-1',
  revision,
  updatedAtUtc: '2026-09-08T12:00:00Z',
  data,
});

function readyState(data: Record<string, unknown> = {}): TaskDraftState {
  let state = createTaskDraftState('task-1', { seed: 'value' });
  state = taskDraftReducer(state, { type: 'hydrate', draft: draft(data) });
  return state;
}

describe('Aufgabenentwurf-Zustand', () => {
  // Testzweck: Form.io darf verschachtelte Werte verändern, ohne den Query-Cache
  // oder die ursprünglichen Taskvariablen mitzumutieren.
  it('klont verschachtelte Eingabedaten tief', () => {
    const source = { person: { name: 'Ada' }, tags: ['a'] };
    const copy = cloneProcessVariables(source);
    (copy.person as { name: string }).name = 'Grace';
    (copy.tags as string[]).push('b');

    expect(source).toEqual({ person: { name: 'Ada' }, tags: ['a'] });
  });

  // Testzweck: Der erste Serverstand wird genau einmal als Formulargrundlage übernommen.
  it('hydratisiert einen geladenen Entwurf', () => {
    const state = taskDraftReducer(
      createTaskDraftState('task-1', { seed: 'value' }),
      { type: 'hydrate', draft: draft({ reason: 'Serverwert' }) },
    );

    expect(state.currentData).toEqual({ reason: 'Serverwert' });
    expect(state.initialData).toEqual({ reason: 'Serverwert' });
    expect(state.revision).toBe(1);
    expect(state.dirty).toBe(false);
    expect(state.formInstanceKey).toBe(1);
  });

  // Testzweck: Die festgelegte Revision-0-/Leerobjekt-Antwort lädt die bestehenden
  // Taskvariablen, ohne fälschlich einen gespeicherten Draft zu behaupten.
  it('behandelt Revision 0 mit leerem Objekt als fehlenden Draft', () => {
    const state = taskDraftReducer(
      createTaskDraftState('task-1', { seed: 'value' }),
      { type: 'hydrate', draft: draft({}, 0) },
    );

    expect(state.currentData).toEqual({ seed: 'value' });
    expect(state.hasDraft).toBe(false);
    expect(state.revision).toBe(0);
  });

  // Testzweck: Ein späterer Refetch darf lokale Eingaben nicht überschreiben oder
  // den Form.io-Editor neu initialisieren.
  it('ignoriert Refetch-Hydratisierung nach der ersten Übernahme', () => {
    let state = readyState({ reason: 'lokal vorbereitet' });
    state = taskDraftReducer(state, { type: 'change', data: { reason: 'meine Eingabe' } });
    const before = state;

    state = taskDraftReducer(state, { type: 'hydrate', draft: draft({ reason: 'anderer Tab' }, 2) });

    expect(state.currentData).toEqual(before.currentData);
    expect(state.initialData).toEqual(before.initialData);
    expect(state.revision).toBe(before.revision);
    expect(state.formInstanceKey).toBe(before.formInstanceKey);
  });

  // Testzweck: Ein explizit angeforderter Serverstand darf lokale Eingaben ersetzen;
  // der neue Form.io-Grundwert erhält dafür eine neue Instanzkennung.
  it('übernimmt Serverstand nur mit force', () => {
    let state = readyState({ reason: 'lokal' });
    state = taskDraftReducer(state, { type: 'change', data: { reason: 'ungespeichert' } });
    state = taskDraftReducer(state, {
      type: 'hydrate',
      draft: draft({ reason: 'Serverstand' }, 2),
      force: true,
    });

    expect(state.currentData).toEqual({ reason: 'Serverstand' });
    expect(state.initialData).toEqual({ reason: 'Serverstand' });
    expect(state.revision).toBe(2);
    expect(state.dirty).toBe(false);
    expect(state.formInstanceKey).toBe(2);
  });

  // Testzweck: Eine erfolgreiche Speicherung aktualisiert die Revision, ohne die
  // sichtbare Form.io-Eingabe durch die Serverantwort zu ersetzen.
  it('markiert nach erfolgreichem Speichern als sauber', () => {
    let state = readyState({ reason: 'alt' });
    state = taskDraftReducer(state, { type: 'change', data: { reason: 'neu' } });
    state = taskDraftReducer(state, { type: 'saveStarted' });
    state = taskDraftReducer(state, {
      type: 'saveSucceeded',
      taskId: 'task-1',
      draft: draft({ reason: 'neu' }, 2),
    });

    expect(state.currentData).toEqual({ reason: 'neu' });
    expect(state.initialData).toEqual({ reason: 'alt' });
    expect(state.revision).toBe(2);
    expect(state.dirty).toBe(false);
    expect(state.saveState).toBe('saved');
    expect(state.formInstanceKey).toBe(1);
  });

  // Testzweck: Ein 409 bewahrt die lokale Eingabe und macht den Konflikt sichtbar,
  // statt stillschweigend eine fremde Version zu laden.
  it('bewahrt Eingaben bei 409-Konflikt', () => {
    let state = readyState({ reason: 'alt' });
    state = taskDraftReducer(state, { type: 'change', data: { reason: 'meine Eingabe' } });
    state = taskDraftReducer(state, { type: 'saveStarted' });
    state = taskDraftReducer(state, {
      type: 'saveFailed',
      taskId: 'task-1',
      error: new Error('Konflikt'),
      conflict: true,
    });

    expect(state.currentData).toEqual({ reason: 'meine Eingabe' });
    expect(state.dirty).toBe(true);
    expect(state.saveState).toBe('conflict');
    expect(state.error).toBeInstanceOf(Error);
  });

  // Testzweck: Verwerfen setzt nach erfolgreichem DELETE auf die ursprünglichen
  // Taskvariablen zurück und entfernt den lokalen Entwurf.
  it('setzt nach Verwerfen auf den Task-Grundwert zurück', () => {
    let state = readyState({ reason: 'Serverstand' });
    state = taskDraftReducer(state, { type: 'change', data: { reason: 'lokal' } });
    state = taskDraftReducer(state, { type: 'discardSucceeded', taskId: 'task-1' });

    expect(state.currentData).toEqual({ seed: 'value' });
    expect(state.initialData).toEqual({ seed: 'value' });
    expect(state.revision).toBe(0);
    expect(state.dirty).toBe(false);
    expect(state.formInstanceKey).toBe(2);
  });

  // Testzweck: Eine spaete Mutation der zuvor geoeffneten Aufgabe darf nach einem
  // Taskwechsel weder Daten noch Revision oder Fehlerstatus der neuen Aufgabe veraendern.
  it('ignoriert spaete Antworten einer zuvor geoeffneten Aufgabe', () => {
    let state = readyState({ reason: 'erste Aufgabe' });
    state = taskDraftReducer(state, {
      type: 'taskChanged',
      taskId: 'task-2',
      fallbackData: { reason: 'zweite Aufgabe' },
    });
    const before = state;

    state = taskDraftReducer(state, {
      type: 'saveSucceeded',
      taskId: 'task-1',
      draft: draft({ reason: 'spaete Antwort' }, 3),
    });
    state = taskDraftReducer(state, {
      type: 'hydrate',
      draft: draft({ reason: 'spaeter Refetch' }, 4),
      force: true,
    });
    state = taskDraftReducer(state, {
      type: 'saveFailed',
      taskId: 'task-1',
      error: new Error('spaeter Fehler'),
      conflict: true,
    });
    state = taskDraftReducer(state, { type: 'discardSucceeded', taskId: 'task-1' });

    expect(state).toEqual(before);
  });
});
