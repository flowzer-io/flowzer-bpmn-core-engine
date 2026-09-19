import { useState } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/Button';
import { Modal } from '@/components/ui/Modal';
import { InlineSpinner } from '@/components/ui/States';
import { ApiError } from '@/lib/api/client';
import type { MigrateInstancesInput } from '@/lib/api/queries';
import { useInstanceMigrationPreview, useMigrateInstances } from '@/lib/api/queries';
import type { InstanceMigrationFindingDto, MigrationFlowNodeDto } from '@/lib/api/types';
import { formatVersion, shortId } from '@/lib/format';
import type { MigrationMappingChoice } from '@/lib/instanceMigration';
import {
  instanceCountLabel,
  migrationFindingText,
  migrationFlowNodeMapping,
} from '@/lib/instanceMigration';

import { MigrationInstanceRow } from './MigrationInstanceRow';
import { MigrationNodeMapping } from './MigrationNodeMapping';
import { MigrationRetryNotice } from './MigrationRetryNotice';
import { MigrationSummary } from './MigrationSummary';

interface InstanceMigrationAssistantProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Alle ausgewählten Instanzen — auch die, die sich am Ende nicht migrieren lassen. */
  instanceIds: string[];
  /** Läuft nach einer Migration, damit die Liste ihre Auswahl aufheben kann. */
  onMigrated?: () => void;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Unbekannter Fehler.';
}

function findingList(findings: InstanceMigrationFindingDto[]): string[] {
  return findings.map(migrationFindingText);
}

/**
 * Hebt laufende Instanzen auf die aktuell deployte Workflow-Version.
 *
 * Der Dialog prüft zuerst folgenlos und zeigt je Instanz, was die Migration bedeutet —
 * verworfene Entwürfe eingeschlossen. Erst danach und erst nach ausdrücklicher
 * Bestätigung wird migriert, denn zurücknehmen lässt sich das nicht. Nicht migrierbare
 * Instanzen bleiben unverändert auf ihrer Version; sie werden gar nicht erst gesendet.
 */
export function InstanceMigrationAssistant({
  open,
  onOpenChange,
  instanceIds,
  onMigrated,
}: InstanceMigrationAssistantProps) {
  const [acknowledged, setAcknowledged] = useState(false);
  const [choices, setChoices] = useState<MigrationMappingChoice[]>([]);
  const flowNodeMapping = migrationFlowNodeMapping(choices);
  const previewQuery = useInstanceMigrationPreview(open ? instanceIds : undefined, flowNodeMapping);
  const migrate = useMigrateInstances();

  // Scheitert eine Prüfung, gilt auch der zuvor gezeigte Stand nicht mehr: Dann steht hier
  // nur noch der neue Anlauf und keine Liste, die eine überholte Auskunft weiterbehauptet.
  const preview = previewQuery.error ? undefined : previewQuery.data;
  const result = migrate.data;
  const migratableIds = (preview?.instances ?? [])
    .filter((entry) => entry.migratable)
    .map((entry) => entry.instanceId);

  // Deployt jemand zwischen Prüfung und Migration, antwortet die API mit 409. Dann ist
  // die geprüfte Zielversion überholt und der Dialog darf sie nicht erneut anbieten.
  const conflict = migrate.error instanceof ApiError && migrate.error.status === 409;
  const canStart = Boolean(preview) && migratableIds.length > 0 && !result && !conflict;

  function handleOpenChange(next: boolean) {
    if (!next) {
      setAcknowledged(false);
      setChoices([]);
      migrate.reset();
    }
    onOpenChange(next);
  }

  /**
   * Eine getroffene Wahl bleibt auch geleert stehen: Der nächste Trockenlauf fordert den
   * Knoten dann wieder ein, und die Zeile springt zwischendurch nicht aus dem Block.
   * Dieselbe Wahl noch einmal ist keine Änderung und löst deshalb keine neue Prüfung aus.
   */
  function chooseTarget(source: MigrationFlowNodeDto, targetId: string) {
    if (choices.some((entry) => entry.source.id === source.id && entry.targetId === targetId)) return;

    // Die Zuordnung verschiebt, welche Instanzen migriert werden. Die Zustimmung galt der
    // vorigen Menge und muss deshalb erneut gegeben werden.
    setAcknowledged(false);
    setChoices((previous) => [
      ...previous.filter((entry) => entry.source.id !== source.id),
      { source, targetId },
    ]);
  }

  function recheck() {
    setAcknowledged(false);
    migrate.reset();
    void previewQuery.refetch();
  }

  function start() {
    if (!preview) return;
    const input: MigrateInstancesInput = {
      instanceIds: migratableIds,
      targetDefinitionId: preview.targetDefinitionId,
    };
    if (Object.keys(flowNodeMapping).length > 0) input.flowNodeMapping = flowNodeMapping;

    migrate.mutate(input, {
      onSuccess: (data) => {
        const migrated = data.instances.filter((entry) => entry.migrated).length;
        if (migrated === data.instances.length) {
          toast.success(`${instanceCountLabel(migrated)} migriert`);
        } else {
          toast.warning(`${migrated} von ${instanceCountLabel(data.instances.length)} migriert`);
        }
        onMigrated?.();
      },
      // Den Versionskonflikt erklärt der Dialog selbst; ein Toast dazu wäre doppelt.
      onError: (error) => {
        if (error instanceof ApiError && error.status === 409) return;
        toast.error('Migration fehlgeschlagen', { description: errorMessage(error) });
      },
    });
  }

  return (
    <Modal
      open={open}
      onOpenChange={handleOpenChange}
      icon="upgrade"
      title="Instanzen migrieren"
      className="w-[min(680px,calc(100vw-32px))]"
      description={
        preview ? (
          <>
            „{preview.relatedDefinitionName}“ · {formatVersion(preview.sourceVersion)} →{' '}
            {formatVersion(preview.targetVersion)}
          </>
        ) : (
          'Laufende Instanzen auf die aktuell deployte Workflow-Version heben.'
        )
      }
      footer={
        <>
          {canStart && (
            <Button size="sm" onClick={() => handleOpenChange(false)} disabled={migrate.isPending}>
              Abbrechen
            </Button>
          )}
          {canStart ? (
            <Button
              size="sm"
              variant="primary"
              icon="upgrade"
              loading={migrate.isPending}
              // Während eine geänderte Zuordnung geprüft wird, steht die Liste noch auf dem
              // alten Stand. Migriert würde sonst nach einer Auskunft, die gerade veraltet.
              disabled={!acknowledged || previewQuery.isFetching}
              onClick={start}
            >
              {instanceCountLabel(migratableIds.length)} migrieren
            </Button>
          ) : (
            <Button size="sm" onClick={() => handleOpenChange(false)}>
              Schließen
            </Button>
          )}
        </>
      }
    >
      <div className="pb-3">
        {previewQuery.isPending && <InlineSpinner label="Migration wird geprüft …" />}

        {previewQuery.error && (
          <MigrationRetryNotice
            icon="error"
            title="Migration konnte nicht geprüft werden"
            description={errorMessage(previewQuery.error)}
            onRetry={recheck}
          />
        )}

        {conflict && (
          <MigrationRetryNotice
            icon="warning"
            title="Inzwischen wurde eine andere Version deployt."
            description="Die geprüfte Zielversion gilt nicht mehr. Prüfe die Auswahl erneut, bevor du migrierst."
            onRetry={recheck}
          />
        )}

        {preview && !conflict && !result && (
          <MigrationNodeMapping
            mapping={preview.mapping}
            choices={choices}
            onChoose={chooseTarget}
            disabled={migrate.isPending}
          />
        )}

        {preview && !conflict && (
          <ul className="m-0 flex list-none flex-col gap-2 p-0">
            {result
              ? result.instances.map((entry) => (
                  <MigrationInstanceRow
                    key={entry.instanceId}
                    title={`#${shortId(entry.instanceId)}`}
                    ok={entry.migrated}
                    label={entry.migrated ? 'Migriert' : 'Nicht migriert'}
                    problems={findingList(entry.problems)}
                  />
                ))
              : preview.instances.map((entry) => (
                  <MigrationInstanceRow
                    key={entry.instanceId}
                    title={`#${shortId(entry.instanceId)}`}
                    ok={entry.migratable}
                    label={entry.migratable ? 'Migrierbar' : 'Nicht migrierbar'}
                    problems={findingList(entry.problems)}
                    notices={findingList(entry.notices)}
                  />
                ))}
          </ul>
        )}

        {preview && !conflict && !result && (
          <MigrationSummary
            sourceVersion={preview.sourceVersion}
            targetVersion={preview.targetVersion}
            total={preview.instances.length}
            migratable={migratableIds.length}
            acknowledged={acknowledged}
            onAcknowledgedChange={setAcknowledged}
          />
        )}
      </div>
    </Modal>
  );
}
