import { useNavigate } from '@tanstack/react-router';
import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { toast } from 'sonner';

import {
  createStartFlow,
  type StartFlow,
  type StartFlowState,
  type StartableWorkflow,
} from './startFlow';
import { definitionsApi } from '@/lib/api/endpoints';
import { queryKeys, useStartInstance } from '@/lib/api/queries';
import type { ProcessInstanceInfoDto, ProcessVariables } from '@/lib/api/types';

export type { StartStep, StartableWorkflow } from './startFlow';

/** Der Workflow, dessen Startformular gerade ausgefüllt wird. Es gibt genau einen Dialog. */
interface PendingForm {
  workflow: StartableWorkflow;
  schema: string;
}

const NO_STATE: StartFlowState = { busy: new Set(), starting: new Set() };

/**
 * Startet einen Workflow aus der Oberfläche — mit Startformular über einen Dialog, ohne
 * Startformular sofort.
 *
 * Die drei Stellen, an denen die Konsole startet (Katalog, Übersicht, Modeler), teilen sich
 * diesen Ablauf samt seiner Rückmeldungen. Vorher stand die Toast-Logik dreimal da und lief
 * auseinander.
 *
 * Der Ablauf selbst liegt in `createStartFlow` und kennt React nicht; dieser Haken hängt ihn
 * nur an State, Query-Cache, Mutation und Meldungen.
 */
export function useStartWorkflow() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const startInstance = useStartInstance();

  const [state, setState] = useState<StartFlowState>(NO_STATE);
  const [pending, setPendingState] = useState<PendingForm | null>(null);
  const [serverError, setServerError] = useState<{ definitionId: string; error: unknown } | null>(null);

  // Der offene Dialog auch als Ref: Die Rückmeldungen des Ablaufs kommen aus Promises und
  // müssten sonst mit dem State rechnen, den sie beim Anlegen gesehen haben.
  const pendingRef = useRef<PendingForm | null>(null);

  function setPending(next: PendingForm | null) {
    pendingRef.current = next;
    setPendingState(next);
  }

  // Die Anschlüsse ändern sich mit jedem Rendern, der Ablauf lebt aber über alle hinweg.
  const deps = useRef({ navigate, queryClient, startInstance });
  deps.current = { navigate, queryClient, startInstance };

  const flowRef = useRef<StartFlow | null>(null);
  if (flowRef.current === null) {
    flowRef.current = createStartFlow<ProcessInstanceInfoDto>({
      // Bewusst mit `staleTime: 0`: Der Query-Client hält Antworten sonst fünf Sekunden für
      // frisch, und ein Bestandsformular kann sich zwischen zwei Starts geändert haben.
      // Ausgefüllt werden soll die Fassung, die der Start gleich erwartet.
      loadStartForm: (workflow) =>
        deps.current.queryClient.fetchQuery({
          queryKey: queryKeys.definitionStartForm(workflow.definitionId),
          queryFn: ({ signal }) => definitionsApi.getStartForm(workflow.definitionId, signal),
          staleTime: 0,
          retry: false,
        }),

      startInstance: (workflow, variables) =>
        deps.current.startInstance.mutateAsync({
          definitionId: workflow.definitionId,
          variables,
        }),

      onFormRequired: (workflow, schema) => {
        setServerError(null);
        const previous = pendingRef.current;
        // Es gibt genau einen Dialog. Verdrängt ein anderer Workflow den offenen, muss dessen
        // Sperre fallen — sonst bliebe sein Startknopf für immer gesperrt.
        if (previous && previous.workflow.definitionId !== workflow.definitionId) {
          flowRef.current?.cancel(previous.workflow.definitionId);
        }
        setPending({ workflow, schema });
      },

      onStarted: (workflow, instance) => {
        // Nur den eigenen Dialog schliessen: Startet nebenher ein Workflow ohne Formular,
        // dürfte dessen Erfolg dem offenen Formular nicht die Eingaben nehmen.
        if (pendingRef.current?.workflow.definitionId === workflow.definitionId) {
          setPending(null);
        }
        toast.success(`„${workflow.name}" gestartet`, {
          action: {
            label: 'Öffnen',
            onClick: () => void deps.current.navigate({ to: `/instances/${instance.instanceId}` }),
          },
        });
      },

      onFailed: (workflow, stage, error) => {
        const description = error instanceof Error ? error.message : undefined;
        if (stage === 'form') {
          toast.error('Das Startformular konnte nicht geladen werden', { description });
          return;
        }
        setServerError({ definitionId: workflow.definitionId, error });
        toast.error(`„${workflow.name}" konnte nicht gestartet werden`, { description });
      },

      onStateChange: setState,
    });
  }

  const flow = flowRef.current;

  function closeDialog() {
    const current = pendingRef.current;
    if (current) flow.cancel(current.workflow.definitionId);
    setPending(null);
  }

  return {
    /** Startet den Workflow oder öffnet sein Startformular. */
    start: (workflow: StartableWorkflow) => flow.start(workflow),
    /**
     * Ob dieser Workflow gerade lädt, startet oder auf sein ausgefülltes Startformular
     * wartet — der Knopf bleibt so lange gesperrt.
     */
    isBusy: (definitionId: string) => state.busy.has(definitionId),
    /** Die Eigenschaften für `<StartWorkflowDialog />`; die Seite rendert ihn genau einmal. */
    dialog: {
      open: pending !== null,
      onOpenChange: (open: boolean) => {
        if (!open) closeDialog();
      },
      workflowName: pending?.workflow.name ?? '',
      schema: pending?.schema,
      serverError: serverError?.definitionId === pending?.workflow.definitionId ? serverError?.error : undefined,
      busy: pending !== null && state.starting.has(pending.workflow.definitionId),
      onStart: (variables: ProcessVariables) => {
        setServerError(null);
        if (pending) void flow.submit(pending.workflow, variables);
      },
    },
  };
}
