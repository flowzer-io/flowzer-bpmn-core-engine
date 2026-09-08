import { useEffect, useRef, useState } from 'react';

import { FormRenderer, type FormRendererHandle } from '@/components/forms/FormRenderer';
import { FormValidationErrors } from '@/components/forms/FormValidationErrors';
import { Button } from '@/components/ui/Button';
import { Modal } from '@/components/ui/Modal';
import type { FormDirectorySearchContext, ProcessVariables } from '@/lib/api/types';

interface StartWorkflowDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Name des Workflows — er steht im Dialogkopf, damit klar ist, was gestartet wird. */
  workflowName: string;
  /** Form.io-Schema des Startformulars als JSON-String. */
  schema: string | undefined;
  directoryContext?: FormDirectorySearchContext;
  busy?: boolean;
  serverError?: unknown;
  onStart: (variables: ProcessVariables) => void;
}

/**
 * Das Startformular ausfüllen, bevor der Workflow läuft.
 *
 * Der Renderer unterstützt die Eingabe, der Server prüft den gebundenen Vertrag
 * verbindlich. Eine Ablehnung darf weder den Dialog schließen noch Eingaben zurücksetzen.
 */
export function StartWorkflowDialog({
  open,
  onOpenChange,
  workflowName,
  schema,
  directoryContext,
  busy = false,
  serverError,
  onStart,
}: StartWorkflowDialogProps) {
  const formRef = useRef<FormRendererHandle>(null);
  const [error, setError] = useState<string | null>(null);

  // Die Pruefung des Formulars ist asynchron. `busy` kommt erst mit dem naechsten Rendern —
  // ein zweiter Klick in dieser Luecke saehe es noch auf false. Der Ref greift sofort.
  const submitting = useRef(false);

  // Der Dialog bleibt zwischen zwei Starts eingehaengt. Ohne diesen Schritt stuende beim
  // naechsten Oeffnen noch die Meldung des letzten Versuchs da — fuer einen anderen Workflow.
  useEffect(() => {
    if (open) setError(null);
  }, [open]);

  async function submit() {
    if (busy || submitting.current) return;
    submitting.current = true;

    try {
      const renderer = formRef.current;
      if (!renderer) {
        // Der Renderer meldet sich erst, wenn Form.io geladen und das Schema lesbar ist. Ohne
        // ihn zu starten hiesse, den Workflow ohne die Werte loszuschicken, die er braucht.
        setError('Das Startformular ist noch nicht bereit.');
        return;
      }

      if (!(await renderer.validate())) {
        setError('Bitte fülle alle Pflichtfelder aus.');
        return;
      }

      setError(null);
      onStart(renderer.getData());
    } finally {
      // Ab hier haelt der Startablauf selbst die Verriegelung — er verweigert einen zweiten
      // Start desselben Workflows, solange der erste laeuft.
      submitting.current = false;
    }
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={`„${workflowName}" starten`}
      icon="rocket_launch"
      description="Die Angaben aus diesem Formular sind die Startwerte der neuen Instanz."
      footer={
        <>
          <Button size="sm" onClick={() => onOpenChange(false)} disabled={busy}>
            Abbrechen
          </Button>
          <Button size="sm" variant="primary" icon="rocket_launch" loading={busy} onClick={() => void submit()}>
            Starten
          </Button>
        </>
      }
    >
      <div className="pb-3">
        <FormValidationErrors error={serverError} schema={schema} />
        <FormRenderer ref={formRef} schema={schema} directoryContext={directoryContext} />
        {error && <div className="text-fail mt-2 text-[12.5px]">{error}</div>}
      </div>
    </Modal>
  );
}
