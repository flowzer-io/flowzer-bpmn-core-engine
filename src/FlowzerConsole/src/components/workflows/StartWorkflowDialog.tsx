import { useRef, useState } from 'react';

import { FormRenderer, type FormRendererHandle } from '@/components/forms/FormRenderer';
import { Button } from '@/components/ui/Button';
import { Modal } from '@/components/ui/Modal';
import type { ProcessVariables } from '@/lib/api/types';

interface StartWorkflowDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Name des Workflows — er steht im Dialogkopf, damit klar ist, was gestartet wird. */
  workflowName: string;
  /** Form.io-Schema des Startformulars als JSON-String. */
  schema: string | undefined;
  busy?: boolean;
  onStart: (variables: ProcessVariables) => void;
}

/**
 * Das Startformular ausfüllen, bevor der Workflow läuft.
 *
 * Die Pflichtfelder prüft der Renderer, nicht der Server: Form.io kennt bedingt sichtbare
 * Felder, die der Server nicht auswertet — er würde damit gültige Eingaben ablehnen.
 */
export function StartWorkflowDialog({
  open,
  onOpenChange,
  workflowName,
  schema,
  busy = false,
  onStart,
}: StartWorkflowDialogProps) {
  const formRef = useRef<FormRendererHandle>(null);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    if (busy) return;

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
        <FormRenderer ref={formRef} schema={schema} />
        {error && <div className="text-fail mt-2 text-[12.5px]">{error}</div>}
      </div>
    </Modal>
  );
}
