import { useState } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/Card';
import { Modal } from '@/components/ui/Modal';
import { InlineSpinner } from '@/components/ui/States';
import { ApiError } from '@/lib/api/client';
import { useInstanceMigrationPreview, useMigrateInstances } from '@/lib/api/queries';
import type { InstanceMigrationFindingDto } from '@/lib/api/types';
import { formatVersion, shortId } from '@/lib/format';
import { migrationFindingText } from '@/lib/instanceMigration';

import { MigrationInstanceRow } from './MigrationInstanceRow';

interface InstanceMigrationAssistantProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Alle ausgewählten Instanzen — auch die, die sich am Ende nicht migrieren lassen. */
  instanceIds: string[];
  /** Läuft nach einer Migration, damit die Liste ihre Auswahl aufheben kann. */
  onMigrated?: () => void;
}

/** „3 Instanzen“ — die Einzahl gehört zum deutschen Text dazu. */
function countLabel(count: number): string {
  return `${count} ${count === 1 ? 'Instanz' : 'Instanzen'}`;
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
  const previewQuery = useInstanceMigrationPreview(open ? instanceIds : undefined);
  const migrate = useMigrateInstances();

  const preview = previewQuery.data;
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
      migrate.reset();
    }
    onOpenChange(next);
  }

  function recheck() {
    setAcknowledged(false);
    migrate.reset();
    void previewQuery.refetch();
  }

  function start() {
    if (!preview) return;
    migrate.mutate(
      { instanceIds: migratableIds, targetDefinitionId: preview.targetDefinitionId },
      {
        onSuccess: (data) => {
          const migrated = data.instances.filter((entry) => entry.migrated).length;
          if (migrated === data.instances.length) toast.success(`${countLabel(migrated)} migriert`);
          else toast.warning(`${migrated} von ${countLabel(data.instances.length)} migriert`);
          onMigrated?.();
        },
        // Den Versionskonflikt erklärt der Dialog selbst; ein Toast dazu wäre doppelt.
        onError: (error) => {
          if (error instanceof ApiError && error.status === 409) return;
          toast.error('Migration fehlgeschlagen', { description: errorMessage(error) });
        },
      },
    );
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
              disabled={!acknowledged}
              onClick={start}
            >
              {countLabel(migratableIds.length)} migrieren
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
          <RetryNotice
            icon="error"
            title="Migration konnte nicht geprüft werden"
            description={errorMessage(previewQuery.error)}
            onRetry={recheck}
          />
        )}

        {conflict && (
          <RetryNotice
            icon="warning"
            title="Inzwischen wurde eine andere Version deployt."
            description="Die geprüfte Zielversion gilt nicht mehr. Prüfe die Auswahl erneut, bevor du migrierst."
            onRetry={recheck}
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
          <div className="border-border mt-4 border-t pt-3.5">
            {migratableIds.length === 0 ? (
              <p className="text-muted m-0 text-[13.5px]">
                Keine der ausgewählten Instanzen lässt sich auf {formatVersion(preview.targetVersion)}{' '}
                heben. Es wird nichts geändert.
              </p>
            ) : (
              <MigrationConfirmation
                summary={`${migratableIds.length} von ${countLabel(preview.instances.length)} werden migriert. Nicht migrierbare bleiben unverändert auf ${formatVersion(preview.sourceVersion)}.`}
                acknowledged={acknowledged}
                onAcknowledgedChange={setAcknowledged}
              />
            )}
          </div>
        )}
      </div>
    </Modal>
  );
}

/** Fehlermeldung samt neuem Anlauf — die Prüfung ist folgenlos und darf wiederholt werden. */
function RetryNotice({
  icon,
  title,
  description,
  onRetry,
}: {
  icon: string;
  title: string;
  description: string;
  onRetry: () => void;
}) {
  return (
    <EmptyState
      icon={icon}
      title={title}
      description={description}
      action={
        <Button icon="refresh" onClick={onRetry}>
          Erneut prüfen
        </Button>
      }
    />
  );
}

/** Zusammenfassung und die Bestätigung, ohne die der Assistent nicht migriert. */
function MigrationConfirmation({
  summary,
  acknowledged,
  onAcknowledgedChange,
}: {
  summary: string;
  acknowledged: boolean;
  onAcknowledgedChange: (acknowledged: boolean) => void;
}) {
  return (
    <>
      <p className="m-0 text-[13.5px]">{summary}</p>
      <label className="mt-3 flex cursor-pointer items-start gap-2.5 text-[13.5px]">
        <input
          type="checkbox"
          checked={acknowledged}
          onChange={(event) => onAcknowledgedChange(event.target.checked)}
          className="accent-accent mt-0.5 h-4 w-4 cursor-pointer"
        />
        Mir ist bewusst, dass sich die Migration nicht rückgängig machen lässt.
      </label>
    </>
  );
}
