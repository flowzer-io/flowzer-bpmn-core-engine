import { useState } from 'react';

import { Button } from '@/components/ui/Button';
import type { ProcessInstanceInfoDto } from '@/lib/api/types';

import { InstanceMigrationAssistant } from './InstanceMigrationAssistant';

/**
 * Einstieg in den Migrationsassistenten für eine einzelne Instanz.
 *
 * Wie beim Abbruch entscheidet die API über das Recht; der Aufrufer blendet die Aktion
 * nur dort ein, wo sie Aussicht auf Erfolg hat. Der Assistent wird erst beim Öffnen
 * eingehängt, damit die Prüfung nicht ungefragt beim Betrachten der Instanz läuft.
 */
export function MigrateInstanceAction({ instance }: { instance: ProcessInstanceInfoDto }) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button size="sm" icon="upgrade" onClick={() => setOpen(true)}>
        Migrieren …
      </Button>

      {open && (
        <InstanceMigrationAssistant
          open
          onOpenChange={setOpen}
          instanceIds={[instance.instanceId]}
        />
      )}
    </>
  );
}
