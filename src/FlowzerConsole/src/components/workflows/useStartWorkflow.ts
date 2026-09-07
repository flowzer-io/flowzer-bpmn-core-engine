import { useNavigate } from '@tanstack/react-router';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { toast } from 'sonner';

import { definitionsApi } from '@/lib/api/endpoints';
import { queryKeys, useStartInstance } from '@/lib/api/queries';
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

/**
 * Startet einen Workflow aus der Oberfläche — mit Startformular über einen Dialog, ohne
 * Startformular sofort.
 *
 * Die drei Stellen, an denen die Konsole startet (Katalog, Übersicht, Modeler), teilen sich
 * diesen Ablauf samt seiner Rückmeldungen. Vorher stand die Toast-Logik dreimal da und lief
 * auseinander.
 */
export function useStartWorkflow() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const startInstance = useStartInstance();

  const [loadingId, setLoadingId] = useState<string | null>(null);
  const [pending, setPending] = useState<{ workflow: StartableWorkflow; schema: string } | null>(null);

  function run(workflow: StartableWorkflow, variables?: ProcessVariables) {
    startInstance.mutate(
      { definitionId: workflow.definitionId, variables },
      {
        onSuccess: (instance) => {
          // Nur den eigenen Dialog schliessen: Startet nebenher ein Workflow ohne Formular,
          // duerfte dessen Erfolg dem offenen Formular nicht die Eingaben nehmen.
          setPending((current) =>
            current?.workflow.definitionId === workflow.definitionId ? null : current,
          );
          toast.success(`„${workflow.name}" gestartet`, {
            action: {
              label: 'Öffnen',
              onClick: () => void navigate({ to: `/instances/${instance.instanceId}` }),
            },
          });
        },
        onError: (error) =>
          toast.error(`„${workflow.name}" konnte nicht gestartet werden`, {
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    );
  }

  async function start(workflow: StartableWorkflow) {
    // Nur den doppelten Klick auf denselben Workflow abfangen — ein anderer darf nebenher
    // starten, sonst verschluckte die Oberfläche den Klick ohne jede Rückmeldung.
    if (loadingId === workflow.definitionId) return;

    setLoadingId(workflow.definitionId);
    try {
      // Bewusst ohne `staleTime`: Ein Bestandsformular kann sich zwischen zwei Starts geändert
      // haben, und ausgefüllt werden soll die Fassung, die der Start gleich erwartet. Über den
      // Query-Cache läuft der Abruf trotzdem, damit gleichzeitige Klicks sich zusammenlegen.
      const startForm = await queryClient.fetchQuery({
        queryKey: queryKeys.definitionStartForm(workflow.definitionId),
        queryFn: ({ signal }) => definitionsApi.getStartForm(workflow.definitionId, signal),
      });

      const step = startStepFor(startForm);
      if (step.kind === 'start') {
        run(workflow);
        return;
      }

      setPending({ workflow, schema: step.schema });
    } catch (error) {
      toast.error('Das Startformular konnte nicht geladen werden', {
        description: error instanceof Error ? error.message : undefined,
      });
    } finally {
      setLoadingId(null);
    }
  }

  return {
    /** Startet den Workflow oder öffnet sein Startformular. */
    start,
    /** Ob dieser Workflow gerade lädt oder startet — der Knopf bleibt so lange gesperrt. */
    isBusy: (definitionId: string) =>
      loadingId === definitionId ||
      (startInstance.isPending && startInstance.variables?.definitionId === definitionId),
    /** Die Eigenschaften für `<StartWorkflowDialog />`; die Seite rendert ihn genau einmal. */
    dialog: {
      open: pending !== null,
      onOpenChange: (open: boolean) => {
        if (!open) setPending(null);
      },
      workflowName: pending?.workflow.name ?? '',
      schema: pending?.schema,
      busy:
        pending !== null &&
        startInstance.isPending &&
        startInstance.variables?.definitionId === pending.workflow.definitionId,
      onStart: (variables: ProcessVariables) => {
        if (pending) run(pending.workflow, variables);
      },
    },
  };
}
