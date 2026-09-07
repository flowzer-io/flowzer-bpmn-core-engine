import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';

import { FormBuilder, type FormBuilderHandle } from '@/components/forms/FormBuilder';
import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/Icon';

interface EmbeddedFormDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Kennung des Formulars im Workflow — sie steht im Kopf, damit klar ist, was bearbeitet wird. */
  formId: string;
  schema: string;
  onSave: (schema: string) => void;
}

const EMPTY_SCHEMA = JSON.stringify({ display: 'form', components: [] }, null, 2);

/**
 * Bearbeitet ein Formular, das im Workflow selbst liegt.
 *
 * Es ist derselbe Editor wie unter „Formulare"; gespeichert wird aber nicht im Bestand,
 * sondern ins Diagramm — erst „Speichern" auf der Modellierungsseite schreibt es weg.
 * Deshalb heißt die Schaltfläche hier „Übernehmen".
 *
 * Bewusst eine eigene Flaeche statt des Dialogs aus `ui/Modal`: Form.io oeffnet zum
 * Bearbeiten eines Feldes einen eigenen Dialog am Ende des Dokuments. Fuer Radix ist das
 * ein Klick ausserhalb — der erste Klick auf ein Feld schloss den umgebenden Dialog und
 * liess den Form.io-Dialog wirkungslos stehen. Ohne Fokusfalle und ohne Aussenklick-Erkennung
 * arbeitet der Editor hier genauso wie auf der Formularseite.
 */
export function EmbeddedFormDialog({ open, onOpenChange, formId, schema, onSave }: EmbeddedFormDialogProps) {
  const builderRef = useRef<FormBuilderHandle>(null);
  const [builderReady, setBuilderReady] = useState(false);

  useEffect(() => {
    if (!open) return;

    // Escape schliesst nur, solange kein Form.io-Dialog darueber liegt — sonst verschwaende
    // die Taste beides auf einmal.
    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape') return;
      if (document.querySelector('.formio-dialog')) return;
      onOpenChange(false);
    }

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [open, onOpenChange]);

  if (!open) return null;

  function handleApply() {
    const builder = builderRef.current;
    if (!builder) return;
    onSave(builder.getSchema());
    onOpenChange(false);
  }

  return createPortal(
    <div
      role="dialog"
      aria-label="Formular in diesem Workflow"
      // Ueber der 100 von diagram-js: Das Kontextmenue am ausgewaehlten Element liegt sonst
      // vor der Formularflaeche.
      className="bg-surface fixed inset-0 z-[120] flex flex-col"
    >
      <div className="border-border bg-surface-2 flex flex-none items-center gap-3 border-b px-[22px] py-3.5">
        <span className="bg-surface text-accent grid h-9 w-9 flex-none place-items-center rounded-[10px]">
          <Icon name="description" size={20} />
        </span>
        <div className="min-w-0 flex-1">
          <div className="font-display text-[16px] font-semibold">Formular in diesem Workflow</div>
          <div className="text-muted text-[12.5px]">
            Wird mit dem Workflow gespeichert und versioniert. Kennung:{' '}
            <span className="font-mono">{formId}</span>
          </div>
        </div>
        <Button size="sm" onClick={() => onOpenChange(false)}>
          Abbrechen
        </Button>
        <Button size="sm" variant="primary" icon="check" disabled={!builderReady} onClick={handleApply}>
          Übernehmen
        </Button>
      </div>

      <div className="min-h-0 flex-1 overflow-auto p-[22px]">
        {/* Der Builder wird pro Formular neu aufgebaut: Form.io haelt sein Schema intern und
            uebernaehme einen Wechsel der Kennung sonst nicht. */}
        <FormBuilder
          key={formId}
          ref={builderRef}
          schema={schema || EMPTY_SCHEMA}
          onReadyChange={setBuilderReady}
        />
      </div>
    </div>,
    document.body,
  );
}
