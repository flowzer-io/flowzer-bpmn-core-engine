import { useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';

import { Button } from '@/components/ui/Button';
import { ConfirmModal } from '@/components/ui/Modal';
import { useDeleteInstance } from '@/lib/api/queries';
import type { ProcessInstanceInfoDto } from '@/lib/api/types';
import { formatVersion, shortId } from '@/lib/format';
import { useCan } from '@/stores/session';

/**
 * Endgültiges Löschen einer beendeten Instanz — eine Betriebsentscheidung.
 *
 * Anders als beim Abbruch wird hier nichts beendet, sondern entfernt: Vorgang, Verlauf,
 * Aufgabendaten und Aufträge sind danach weg und aus keiner Ansicht mehr erreichbar. Die
 * Rückfrage zählt das auf und nennt Workflow, Version und Kennung, damit niemand die falsche
 * Instanz trifft.
 *
 * Die Aktion wird nur der Betriebsrolle angeboten. Das ist bewusst mehr als beim Abbruch, wo
 * die API allein entscheidet: Ein angebotener Knopf, der unwiderruflich fremde Vorgangsdaten
 * entfernt und dann an der Berechtigung scheitert, ist eine Einladung zum Fehlversuch.
 */
export function DeleteInstanceAction({ instance }: { instance: ProcessInstanceInfoDto }) {
  const [confirming, setConfirming] = useState(false);
  const deleteInstance = useDeleteInstance();
  const navigate = useNavigate();
  const mayOperate = useCan()('operator');

  if (!mayOperate) return null;

  return (
    <>
      <Button size="sm" variant="danger" icon="delete" onClick={() => setConfirming(true)}>
        Instanz löschen
      </Button>

      <ConfirmModal
        open={confirming}
        onOpenChange={setConfirming}
        destructive
        busy={deleteInstance.isPending}
        title="Instanz endgültig löschen?"
        description={
          <>
            „{instance.relatedDefinitionName}" {formatVersion(instance.definitionVersion)} · #
            {shortId(instance.instanceId)} wird vollständig entfernt: Verlauf, Aufgabendaten,
            Entwürfe, Aufträge an Worker und die Ereignisspur. Danach ist der Vorgang in keiner
            Ansicht und keinem Bericht mehr auffindbar. Das lässt sich nicht rückgängig machen.
          </>
        }
        confirmLabel="Endgültig löschen"
        confirmIcon="delete"
        onConfirm={() =>
          deleteInstance.mutate(instance.instanceId, {
            onSuccess: () => {
              setConfirming(false);
              toast.success('Instanz gelöscht');
              // Die Detailseite zeigt einen Vorgang, den es nicht mehr gibt; ein Nachladen
              // liefe in einen 404. Deshalb zurück in die Liste.
              void navigate({ to: '/instances' });
            },
            onError: (error) =>
              toast.error('Instanz konnte nicht gelöscht werden', {
                description: error instanceof Error ? error.message : undefined,
              }),
          })
        }
      />
    </>
  );
}
