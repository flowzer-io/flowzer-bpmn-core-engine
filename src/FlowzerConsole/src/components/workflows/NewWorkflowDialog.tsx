import { useEffect, useState } from 'react';

import { Button } from '@/components/ui/Button';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';

/** Muss zur Grenze in <c>DefinitionController.MaxDefinitionNameLength</c> passen. */
const MAX_NAME_LENGTH = 200;

const FORM_ID = 'neuer-workflow-formular';

interface NewWorkflowDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  busy?: boolean;
  onCreate: (name: string) => void;
}

/**
 * Fragt den Namen, bevor der Workflow angelegt wird.
 *
 * Vorher entstand sofort ein Eintrag „New Definition“, den man erst danach umbenennen
 * konnte — wer den Dialog abbrach, liess einen namenlosen Entwurf im Katalog zurueck.
 */
export function NewWorkflowDialog({ open, onOpenChange, busy = false, onCreate }: NewWorkflowDialogProps) {
  const [name, setName] = useState('');

  // Beim Oeffnen wieder leer beginnen; sonst stuende der Name des letzten Versuchs da.
  useEffect(() => {
    if (open) setName('');
  }, [open]);

  const trimmed = name.trim();
  const tooLong = trimmed.length > MAX_NAME_LENGTH;
  const valid = trimmed.length > 0 && !tooLong;

  function submit() {
    if (!valid || busy) return;
    onCreate(trimmed);
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="Neuer Workflow"
      icon="schema"
      description="Der Name steht im Katalog und in jeder Instanzliste. Er lässt sich später ändern."
      footer={
        <>
          <Button size="sm" onClick={() => onOpenChange(false)} disabled={busy}>
            Abbrechen
          </Button>
          {/* Ueber `form` an das Formular gebunden: Damit legt auch die Eingabetaste an,
              nicht nur der Mausklick. */}
          <Button
            type="submit"
            form={FORM_ID}
            size="sm"
            variant="primary"
            icon="add"
            loading={busy}
            disabled={!valid}
          >
            Anlegen und öffnen
          </Button>
        </>
      }
    >
      <form
        id={FORM_ID}
        className="pb-3"
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <FieldLabel>Name</FieldLabel>
        <TextInput
          autoFocus
          value={name}
          maxLength={MAX_NAME_LENGTH + 1}
          placeholder="z. B. Urlaubsantrag"
          onChange={(event) => setName(event.target.value)}
        />
        {tooLong && (
          <div className="text-fail mt-1.5 text-[12.5px]">
            Höchstens {MAX_NAME_LENGTH} Zeichen.
          </div>
        )}
      </form>
    </Modal>
  );
}
