import { useState } from 'react';

import { Button } from '@/components/ui/Button';
import type { ProcessInstanceInfoDto } from '@/lib/api/types';

import { InstanceModificationDialog } from './InstanceModificationDialog';

/**
 * Einstieg in den Eingriffsdialog für eine laufende Instanz.
 *
 * Wie bei Abbruch und Migration entscheidet die API über das Recht; der Aufrufer blendet
 * die Aktion nur dort ein, wo sie Aussicht auf Erfolg hat. Der Dialog wird erst beim
 * Öffnen eingehängt, damit der Trockenlauf nicht ungefragt beim Betrachten der Instanz läuft.
 */
export function ModifyInstanceAction({ instance }: { instance: ProcessInstanceInfoDto }) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button size="sm" icon="edit" onClick={() => setOpen(true)}>
        Instanz anpassen …
      </Button>

      {open && <InstanceModificationDialog open onOpenChange={setOpen} instance={instance} />}
    </>
  );
}
