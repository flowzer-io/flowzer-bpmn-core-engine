import type { FormDto, ProcessVariables } from '@/lib/api/types';

/** Der Workflow, der gestartet werden soll — mehr braucht der Ablauf nicht. */
export interface StartableWorkflow {
  definitionId: string;
  name: string;
}

/** Was nach dem Startformular geschieht: sofort starten oder erst ausfüllen lassen. */
export type StartStep = { kind: 'start' } | { kind: 'form'; schema: string };

/**
 * Die Entscheidung nach dem Abruf des Startformulars. Ohne Formular startet der Workflow
 * sofort — der frühere Ablauf, an dem sich nichts ändern soll.
 *
 * Bewusst als eigene Funktion: So lässt sich die Regel ohne Oberfläche prüfen.
 */
export function startStepFor(startForm: FormDto | null): StartStep {
  if (startForm === null) return { kind: 'start' };
  return { kind: 'form', schema: startForm.formData ?? '' };
}

/** Woran ein Start gescheitert ist — der Abruf des Formulars oder der Start selbst. */
export type StartFailure = 'form' | 'start';

/** Der Stand des Ablaufs, aus dem die Oberfläche ihre Sperren ableitet. */
export interface StartFlowState {
  /**
   * Workflows, deren Startknopf gesperrt ist: Das Formular wird geholt, sein Dialog steht
   * offen oder der Start läuft.
   */
  busy: ReadonlySet<string>;
  /** Workflows, deren Start gerade läuft — daran hängt der Ladezustand des Dialogs. */
  starting: ReadonlySet<string>;
}

/**
 * Die Anschlüsse nach aussen. Alles, was der Ablauf tut, geht hier durch — deshalb lässt er
 * sich ohne React, ohne Netz und ohne DOM prüfen.
 */
export interface StartFlowPorts<TInstance> {
  /** Holt das Startformular; `null` heisst „dieser Workflow hat keines". */
  loadStartForm: (workflow: StartableWorkflow) => Promise<FormDto | null>;
  startInstance: (workflow: StartableWorkflow, variables?: ProcessVariables) => Promise<TInstance>;
  /** Der Workflow hat ein Startformular: Es muss ausgefüllt werden, bevor er läuft. */
  onFormRequired: (workflow: StartableWorkflow, schema: string) => void;
  onStarted: (workflow: StartableWorkflow, instance: TInstance) => void;
  onFailed: (workflow: StartableWorkflow, stage: StartFailure, error: unknown) => void;
  onStateChange: (state: StartFlowState) => void;
}

export interface StartFlow {
  /** Startet den Workflow oder fordert sein Startformular an. */
  start: (workflow: StartableWorkflow) => Promise<void>;
  /** Startet den Workflow mit den Werten aus seinem Startformular. */
  submit: (workflow: StartableWorkflow, variables: ProcessVariables) => Promise<void>;
  /** Der Dialog wurde geschlossen: Der Workflow ist wieder frei. */
  cancel: (definitionId: string) => void;
}

/**
 * Der Startablauf eines Workflows: Startformular holen, ausfüllen lassen, Instanz starten.
 *
 * Die Verriegelung liegt bewusst hier und nicht im React-State: Zwei Klicks vor dem nächsten
 * Rendern sehen denselben State und kämen beide durch — hier sieht der zweite Klick die
 * Kennung des ersten und läuft ins Leere. Verriegelt wird je Workflow, damit ein zweiter
 * Workflow nebenher starten kann; die Konsole hat drei Stellen, an denen gestartet wird.
 */
export function createStartFlow<TInstance>(ports: StartFlowPorts<TInstance>): StartFlow {
  const busy = new Set<string>();
  const starting = new Set<string>();

  function publish() {
    ports.onStateChange({ busy: new Set(busy), starting: new Set(starting) });
  }

  function release(definitionId: string) {
    const wasBusy = busy.delete(definitionId);
    const wasStarting = starting.delete(definitionId);
    if (wasBusy || wasStarting) publish();
  }

  /**
   * @param releasesOnFailure Ob der Workflow nach einem Fehlschlag wieder frei ist. Beim Start
   * ohne Formular ja; hält den Workflow dagegen ein offener Dialog, bleibt er gesperrt — dort
   * stehen die Eingaben, mit denen es der Benutzer erneut versuchen soll.
   */
  async function runStart(
    workflow: StartableWorkflow,
    variables: ProcessVariables | undefined,
    releasesOnFailure: boolean,
  ) {
    const { definitionId } = workflow;
    if (starting.has(definitionId)) return;

    starting.add(definitionId);
    busy.add(definitionId);
    publish();

    try {
      const instance = await ports.startInstance(workflow, variables);
      starting.delete(definitionId);
      busy.delete(definitionId);
      publish();
      ports.onStarted(workflow, instance);
    } catch (error) {
      starting.delete(definitionId);
      if (releasesOnFailure) busy.delete(definitionId);
      publish();
      // Jeder Start meldet sich selbst. Über die Rückgabe der Mutation und nicht über deren
      // Callbacks: Ein zweiter Start derselben Mutation löst den Beobachter des ersten ab,
      // dessen Callbacks blieben dann aus — ohne Meldung und mit offenem Dialog.
      ports.onFailed(workflow, 'start', error);
    }
  }

  return {
    async start(workflow) {
      const { definitionId } = workflow;
      if (busy.has(definitionId)) return;

      busy.add(definitionId);
      publish();

      let startForm: FormDto | null;
      try {
        startForm = await ports.loadStartForm(workflow);
      } catch (error) {
        release(definitionId);
        ports.onFailed(workflow, 'form', error);
        return;
      }

      const step = startStepFor(startForm);
      if (step.kind === 'form') {
        // Die Sperre bleibt: Der offene Dialog gehört zu diesem Start und hält ihn, bis er
        // gestartet oder geschlossen ist.
        ports.onFormRequired(workflow, step.schema);
        return;
      }

      await runStart(workflow, undefined, true);
    },

    submit(workflow, variables) {
      return runStart(workflow, variables, false);
    },

    cancel(definitionId) {
      release(definitionId);
    },
  };
}
