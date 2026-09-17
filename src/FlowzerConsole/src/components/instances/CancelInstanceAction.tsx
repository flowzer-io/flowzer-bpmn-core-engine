import { useState } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/Button';
import { ConfirmModal } from '@/components/ui/Modal';
import { useCancelInstance } from '@/lib/api/queries';
import type { ProcessInstanceInfoDto } from '@/lib/api/types';
import { formatVersion, shortId } from '@/lib/format';

/**
 * Bewusster Abbruch einer laufenden Instanz — eine Betriebsentscheidung.
 *
 * Die Rückfrage nennt Workflow, Version und Kennung: Wer mehrere Instanzen nebeneinander
 * offen hat, soll vor dem unwiderruflichen Schritt sehen, welche er trifft. Ob jemand
 * abbrechen darf, entscheidet die API; der Aufrufer blendet die Aktion nur dort ein, wo
 * sie Aussicht auf Erfolg hat.
 */
export function CancelInstanceAction({ instance }: { instance: ProcessInstanceInfoDto }) {
  const [confirming, setConfirming] = useState(false);
  const cancelInstance = useCancelInstance();

  return (
    <>
      <Button size="sm" variant="danger" icon="cancel" onClick={() => setConfirming(true)}>
        Instanz abbrechen
      </Button>

      <ConfirmModal
        open={confirming}
        onOpenChange={setConfirming}
        destructive
        busy={cancelInstance.isPending}
        title="Instanz abbrechen?"
        description={
          <>
            „{instance.relatedDefinitionName}" {formatVersion(instance.definitionVersion)} · #
            {shortId(instance.instanceId)} wird beendet. Offene Aufgaben, Timer und Aufträge an
            Worker entfallen; bereits ausgeführte Schritte werden nicht zurückgenommen. Das lässt
            sich nicht rückgängig machen.
          </>
        }
        confirmLabel="Instanz abbrechen"
        confirmIcon="cancel"
        dismissLabel="Instanz weiterlaufen lassen"
        onConfirm={() =>
          cancelInstance.mutate(instance.instanceId, {
            onSuccess: () => {
              setConfirming(false);
              toast.success('Instanz abgebrochen');
            },
            onError: (error) =>
              toast.error('Instanz konnte nicht abgebrochen werden', {
                description: error instanceof Error ? error.message : undefined,
              }),
          })
        }
      />
    </>
  );
}
