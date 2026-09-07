import { describe, expect, it, vi } from 'vitest';

import {
  createStartFlow,
  startStepFor,
  type StartFlowPorts,
  type StartFlowState,
  type StartableWorkflow,
} from './startFlow';
import type { FormDto } from '@/lib/api/types';

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

const urlaub: StartableWorkflow = { definitionId: 'urlaub', name: 'Urlaubsantrag' };
const spesen: StartableWorkflow = { definitionId: 'spesen', name: 'Spesen' };

const withForm: FormDto = { formData: '{"display":"form"}' };

/** Ein von Hand aufgelöstes Promise — damit ein Abruf im Test offen bleiben kann. */
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolveFn, rejectFn) => {
    resolve = resolveFn;
    reject = rejectFn;
  });
  return { promise, resolve, reject };
}

/**
 * Der Ablauf mit lauter Attrappen. Vorbelegt startet jeder Workflow ohne Formular; die Tests
 * ersetzen einzelne Anschlüsse.
 */
function setup(overrides: Partial<StartFlowPorts<string>> = {}) {
  const state: { current: StartFlowState } = {
    current: { busy: new Set(), starting: new Set() },
  };

  const ports: StartFlowPorts<string> = {
    loadStartForm: vi.fn<StartFlowPorts<string>['loadStartForm']>().mockResolvedValue(null),
    startInstance: vi
      .fn<StartFlowPorts<string>['startInstance']>()
      .mockImplementation((workflow) => Promise.resolve(`instanz-${workflow.definitionId}`)),
    onFormRequired: vi.fn(),
    onStarted: vi.fn(),
    onFailed: vi.fn(),
    onStateChange: (next) => {
      state.current = next;
    },
    ...overrides,
  };

  return { flow: createStartFlow(ports), ports, state };
}

// Testzweck: Die Verriegelung des Startablaufs. Sie liegt in einer eigenen Einheit, weil sie
// synchron greifen muss — über React-State kämen zwei Klicks vor dem nächsten Rendern beide
// durch und der Workflow liefe doppelt los.
describe('createStartFlow', () => {
  it('lässt aus zwei Klicks auf denselben Workflow genau einen Abruf und einen Start werden', async () => {
    const { flow, ports } = setup();

    await Promise.all([flow.start(urlaub), flow.start(urlaub)]);

    expect(ports.loadStartForm).toHaveBeenCalledTimes(1);
    expect(ports.startInstance).toHaveBeenCalledTimes(1);
    expect(ports.onStarted).toHaveBeenCalledTimes(1);
  });

  it('übergeht einen zweiten Start, solange das Startformular offen ist', async () => {
    const { flow, ports, state } = setup({
      loadStartForm: vi.fn<StartFlowPorts<string>['loadStartForm']>().mockResolvedValue(withForm),
    });

    await flow.start(urlaub);
    expect(ports.onFormRequired).toHaveBeenCalledWith(urlaub, '{"display":"form"}');
    expect(state.current.busy.has('urlaub')).toBe(true);

    await flow.start(urlaub);

    expect(ports.loadStartForm).toHaveBeenCalledTimes(1);
    expect(ports.onFormRequired).toHaveBeenCalledTimes(1);
    expect(ports.startInstance).not.toHaveBeenCalled();
  });

  it('gibt den Workflow frei, sobald der Dialog geschlossen wird', async () => {
    const { flow, ports, state } = setup({
      loadStartForm: vi.fn<StartFlowPorts<string>['loadStartForm']>().mockResolvedValue(withForm),
    });

    await flow.start(urlaub);
    flow.cancel('urlaub');
    expect(state.current.busy.has('urlaub')).toBe(false);

    await flow.start(urlaub);

    // Und der Abruf läuft erneut: Ein Bestandsformular kann sich zwischen zwei Starts geändert
    // haben, ausgefüllt werden soll die Fassung, die der Start gleich erwartet.
    expect(ports.loadStartForm).toHaveBeenCalledTimes(2);
  });

  it('startet zwei verschiedene Workflows nebeneinander und meldet jeden für sich', async () => {
    const { flow, ports, state } = setup();

    await Promise.all([flow.start(urlaub), flow.start(spesen)]);

    expect(ports.startInstance).toHaveBeenCalledTimes(2);
    expect(ports.onStarted).toHaveBeenCalledWith(urlaub, 'instanz-urlaub');
    expect(ports.onStarted).toHaveBeenCalledWith(spesen, 'instanz-spesen');
    expect(state.current.busy.size).toBe(0);
  });

  it('meldet jeden Fehlschlag beim Workflow, zu dem er gehört', async () => {
    const { flow, ports } = setup({
      startInstance: vi
        .fn<StartFlowPorts<string>['startInstance']>()
        .mockImplementation((workflow) =>
          workflow.definitionId === 'spesen'
            ? Promise.reject(new Error('Spesen abgelehnt'))
            : Promise.resolve('instanz-urlaub'),
        ),
    });

    await Promise.all([flow.start(urlaub), flow.start(spesen)]);

    expect(ports.onStarted).toHaveBeenCalledExactlyOnceWith(urlaub, 'instanz-urlaub');
    expect(ports.onFailed).toHaveBeenCalledExactlyOnceWith(spesen, 'start', expect.any(Error));
  });

  it('gibt den Workflow wieder frei, wenn der Abruf des Startformulars scheitert', async () => {
    const loadStartForm = vi
      .fn<StartFlowPorts<string>['loadStartForm']>()
      .mockRejectedValueOnce(new Error('Netz weg'))
      .mockResolvedValue(null);
    const { flow, ports, state } = setup({ loadStartForm });

    await flow.start(urlaub);

    expect(ports.onFailed).toHaveBeenCalledExactlyOnceWith(urlaub, 'form', expect.any(Error));
    expect(state.current.busy.has('urlaub')).toBe(false);

    await flow.start(urlaub);

    expect(ports.startInstance).toHaveBeenCalledTimes(1);
  });

  it('lässt den Erfolg eines nebenher gestarteten Workflows den offenen Dialog unberührt', async () => {
    const { flow, ports, state } = setup({
      loadStartForm: vi
        .fn<StartFlowPorts<string>['loadStartForm']>()
        .mockImplementation((workflow) =>
          Promise.resolve(workflow.definitionId === 'urlaub' ? withForm : null),
        ),
    });

    await flow.start(urlaub);
    await flow.start(spesen);

    expect(ports.onStarted).toHaveBeenCalledExactlyOnceWith(spesen, 'instanz-spesen');
    // Der Dialog des Urlaubsantrags hält seine Sperre weiter — sein Start ist nicht gemeint.
    expect(state.current.busy.has('urlaub')).toBe(true);
    expect(state.current.busy.has('spesen')).toBe(false);
  });

  it('startet aus dem Dialog heraus nur einmal, auch bei zwei Klicks auf „Starten"', async () => {
    const { flow, ports, state } = setup({
      loadStartForm: vi.fn<StartFlowPorts<string>['loadStartForm']>().mockResolvedValue(withForm),
    });

    await flow.start(urlaub);
    await Promise.all([flow.submit(urlaub, { tage: 3 }), flow.submit(urlaub, { tage: 3 })]);

    expect(ports.startInstance).toHaveBeenCalledExactlyOnceWith(urlaub, { tage: 3 });
    expect(state.current.busy.has('urlaub')).toBe(false);
  });

  it('hält den Dialog offen, wenn der Start aus dem Formular scheitert', async () => {
    const { flow, ports, state } = setup({
      loadStartForm: vi.fn<StartFlowPorts<string>['loadStartForm']>().mockResolvedValue(withForm),
      startInstance: vi
        .fn<StartFlowPorts<string>['startInstance']>()
        .mockRejectedValueOnce(new Error('Pflichtfeld fehlt'))
        .mockResolvedValue('instanz-urlaub'),
    });

    await flow.start(urlaub);
    await flow.submit(urlaub, { tage: 3 });

    expect(ports.onFailed).toHaveBeenCalledExactlyOnceWith(urlaub, 'start', expect.any(Error));
    // Die Eingaben bleiben stehen, der zweite Versuch geht durch.
    expect(state.current.busy.has('urlaub')).toBe(true);
    expect(state.current.starting.has('urlaub')).toBe(false);

    await flow.submit(urlaub, { tage: 3 });

    expect(ports.onStarted).toHaveBeenCalledExactlyOnceWith(urlaub, 'instanz-urlaub');
  });

  it('hält die Sperre, wenn der Dialog während des laufenden Starts geschlossen wird', async () => {
    const start = deferred<string>();
    const { flow, ports, state } = setup({
      loadStartForm: vi.fn<StartFlowPorts<string>['loadStartForm']>().mockResolvedValue(withForm),
      startInstance: vi.fn<StartFlowPorts<string>['startInstance']>(() => start.promise),
    });

    await flow.start(urlaub);
    const laufend = flow.submit(urlaub, { tage: 3 });

    // Escape oder Klick daneben schliesst den Dialog — der Start läuft trotzdem weiter, und ein
    // Klick auf den Startknopf dahinter darf keinen zweiten auslösen.
    flow.cancel('urlaub');
    expect(state.current.busy.has('urlaub')).toBe(true);
    await flow.start(urlaub);
    expect(ports.loadStartForm).toHaveBeenCalledTimes(1);

    start.reject(new Error('Start abgelehnt'));
    await laufend;

    // Ohne Dialog gibt es keinen zweiten Versuch: Der Workflow ist nach dem Fehlschlag frei.
    expect(ports.onFailed).toHaveBeenCalledExactlyOnceWith(urlaub, 'start', expect.any(Error));
    expect(state.current.busy.has('urlaub')).toBe(false);
  });

  it('zeigt den laufenden Start an, solange er läuft — daran hängt der Ladezustand des Dialogs', async () => {
    const start = deferred<string>();
    const { flow, state } = setup({
      loadStartForm: vi.fn<StartFlowPorts<string>['loadStartForm']>().mockResolvedValue(withForm),
      startInstance: vi.fn<StartFlowPorts<string>['startInstance']>(() => start.promise),
    });

    await flow.start(urlaub);
    expect(state.current.starting.has('urlaub')).toBe(false);

    const laufend = flow.submit(urlaub, { tage: 3 });

    expect(state.current.starting.has('urlaub')).toBe(true);

    start.resolve('instanz-urlaub');
    await laufend;

    expect(state.current.starting.has('urlaub')).toBe(false);
    expect(state.current.busy.has('urlaub')).toBe(false);
  });
});
