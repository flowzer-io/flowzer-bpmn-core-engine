import { useNavigate } from '@tanstack/react-router';
import { useEffect, useMemo, useState } from 'react';
import { toast } from 'sonner';

import { BpmnThumbnail } from '@/components/bpmn/BpmnThumbnail';
import { Button } from '@/components/ui/Button';
import { Card, EmptyState } from '@/components/ui/Card';
import { Chip, Dot, toneSurface, type Tone } from '@/components/ui/Chip';
import { SearchInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { ConfirmModal } from '@/components/ui/Modal';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { Segmented } from '@/components/ui/Segmented';
import { ErrorState, Skeleton } from '@/components/ui/States';
import { FolderDelegationDialog } from '@/components/workflows/FolderDelegationDialog';
import { FolderDialog } from '@/components/workflows/FolderDialog';
import { FolderTree, WORKFLOW_DRAG_TYPE } from '@/components/workflows/FolderTree';
import { MoveWorkflowDialog } from '@/components/workflows/MoveWorkflowDialog';
import { NewWorkflowDialog } from '@/components/workflows/NewWorkflowDialog';
import {
  useCreateDefinition,
  useCreateFolder,
  useDefinitions,
  useDeleteDefinition,
  useDeleteFolder,
  useFolders,
  useInstances,
  useMoveDefinition,
  useStartInstance,
  useUpdateFolder,
  useUpdateFolderAssignments,
} from '@/lib/api/queries';
import type {
  ExtendedBpmnMetaDefinitionDto,
  FolderAssignmentDto,
  VersionDto,
  WorkflowFolderDto,
} from '@/lib/api/types';
import { instanceBucket } from '@/lib/api/normalize';
import { ancestorIds, folderPath, pathLabel, sortByPath } from '@/lib/folderTree';
import { formatRelative, parseApiDate } from '@/lib/format';
import { iconForLabel } from '@/lib/taskView';
import { useCan } from '@/stores/session';

type SortKey = 'updated' | 'name' | 'active';

const SORT_OPTIONS = [
  { value: 'updated' as const, label: 'Zuletzt geändert' },
  { value: 'name' as const, label: 'Name' },
  { value: 'active' as const, label: 'Aktivität' },
];

type DeployState = 'deployed' | 'outdated' | 'draft';

const DEPLOY_LABEL: Record<DeployState, string> = {
  deployed: 'Aktiv',
  outdated: 'Neue Version',
  draft: 'Entwurf',
};

const DEPLOY_TONE: Record<DeployState, Tone> = {
  deployed: 'done',
  outdated: 'wait',
  draft: 'muted',
};

function versionLabel(version: VersionDto | null | undefined): string {
  return version ? `v${version.major}.${version.minor}` : '—';
}

function deployState(definition: ExtendedBpmnMetaDefinitionDto): DeployState {
  if (!definition.deployedId || !definition.deployedVersion) return 'draft';

  const latest = definition.latestVersion;
  if (!latest) return 'deployed';

  const isNewer =
    latest.major > definition.deployedVersion.major ||
    (latest.major === definition.deployedVersion.major && latest.minor > definition.deployedVersion.minor);

  return isNewer ? 'outdated' : 'deployed';
}

export function WorkflowsPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState<SortKey>('updated');

  const mayPublish = useCan()('modeler');
  const definitionsQuery = useDefinitions();
  const foldersQuery = useFolders();
  const instancesQuery = useInstances();
  const createDefinition = useCreateDefinition();
  const deleteDefinition = useDeleteDefinition();
  const moveDefinition = useMoveDefinition();
  const startInstance = useStartInstance();
  const createFolder = useCreateFolder();
  const updateFolder = useUpdateFolder();
  const deleteFolder = useDeleteFolder();
  const updateAssignments = useUpdateFolderAssignments();

  const folders = useMemo(() => foldersQuery.data ?? [], [foldersQuery.data]);

  const [selectedFolderId, setSelectedFolderId] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());

  const [creating, setCreating] = useState(false);
  const [folderDialog, setFolderDialog] = useState<{ folder: WorkflowFolderDto | null } | null>(null);
  const [delegating, setDelegating] = useState<WorkflowFolderDto | null>(null);
  const [pendingDelete, setPendingDelete] = useState<ExtendedBpmnMetaDefinitionDto | null>(null);
  const [pendingFolderDelete, setPendingFolderDelete] = useState<WorkflowFolderDto | null>(null);
  const [moving, setMoving] = useState<ExtendedBpmnMetaDefinitionDto | null>(null);

  const selectedFolder = folders.find((folder) => folder.id === selectedFolderId) ?? null;

  // Ein Ordner, den es nicht mehr gibt — geloescht, oder die Rechte haben sich geaendert —
  // darf nicht als leere Ansicht stehen bleiben.
  useEffect(() => {
    if (selectedFolderId && !foldersQuery.isPending && !selectedFolder) {
      setSelectedFolderId(null);
    }
  }, [selectedFolderId, selectedFolder, foldersQuery.isPending]);

  const mayEditHere = selectedFolder ? selectedFolder.mayEdit : mayPublish;
  const mayDelegateHere = selectedFolder ? selectedFolder.mayDelegate : mayPublish;

  /** Anzahl laufender Instanzen je Definition — für die Fußzeile der Karten. */
  const activeByDefinition = useMemo(() => {
    const counts = new Map<string, number>();
    for (const instance of instancesQuery.data ?? []) {
      if (instanceBucket(instance.state) !== 'active') continue;
      counts.set(instance.relatedDefinitionId, (counts.get(instance.relatedDefinitionId) ?? 0) + 1);
    }
    return counts;
  }, [instancesQuery.data]);

  const term = search.trim().toLowerCase();

  const definitions = useMemo(() => {
    const filtered = (definitionsQuery.data ?? []).filter((definition) => {
      // Gesucht wird ueber den ganzen Katalog, nicht nur im offenen Ordner: Wer den Namen
      // kennt, weiss meist nicht mehr, wo der Workflow abgelegt wurde — genau dann sucht man.
      if (term.length > 0) {
        return (
          definition.name.toLowerCase().includes(term) ||
          definition.definitionId.toLowerCase().includes(term) ||
          (definition.description ?? '').toLowerCase().includes(term)
        );
      }

      return (definition.folderId ?? null) === selectedFolderId;
    });

    return [...filtered].sort((a, b) => {
      if (sort === 'name') return a.name.localeCompare(b.name, 'de');
      if (sort === 'active') {
        return (activeByDefinition.get(b.definitionId) ?? 0) - (activeByDefinition.get(a.definitionId) ?? 0);
      }
      const aDate = parseApiDate(a.latestVersionDateTime)?.getTime() ?? 0;
      const bDate = parseApiDate(b.latestVersionDateTime)?.getTime() ?? 0;
      return bDate - aDate;
    });
  }, [definitionsQuery.data, term, selectedFolderId, sort, activeByDefinition]);

  const rootWorkflowCount = useMemo(
    () => (definitionsQuery.data ?? []).filter((definition) => !definition.folderId).length,
    [definitionsQuery.data],
  );

  const totalActive = useMemo(
    () => [...activeByDefinition.values()].reduce((sum, value) => sum + value, 0),
    [activeByDefinition],
  );

  const path = folderPath(selectedFolderId, folders);

  function selectFolder(folderId: string | null) {
    setSelectedFolderId(folderId);
    // Beim Sprung in einen Ordner den Weg dorthin aufklappen — sonst waere der gewaehlte
    // Ordner ausgewaehlt, aber im Baum nicht zu sehen.
    if (folderId) {
      setExpanded(new Set([...expanded, ...ancestorIds(folderId, folders)]));
    }
  }

  function toggleFolder(folderId: string) {
    const next = new Set(expanded);
    if (!next.delete(folderId)) next.add(folderId);
    setExpanded(next);
  }

  function moveWorkflow(definitionId: string, folderId: string | null, name?: string) {
    moveDefinition.mutate(
      { definitionId, folderId },
      {
        onSuccess: () => {
          setMoving(null);
          const ziel = folderId ? (folders.find((folder) => folder.id === folderId)?.name ?? 'Ordner') : 'oberste Ebene';
          toast.success(`„${name ?? definitionId}" liegt jetzt in ${ziel}`);
        },
        onError: (error) =>
          toast.error('Verschieben fehlgeschlagen', {
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    );
  }

  return (
    <PageContainer>
      <PageHeader
        title="Workflows"
        description={
          definitionsQuery.isPending
            ? 'Katalog wird geladen …'
            : `${definitions.length} Prozessdefinition${definitions.length === 1 ? '' : 'en'} · ${totalActive} laufende Instanz${totalActive === 1 ? '' : 'en'}`
        }
        actions={
          // Anlegen fuehrt in den Modeler. Der ist auf einem Telefon nicht zu bedienen,
          // also wird er dort erst gar nicht angeboten — Workflows sind unterwegs zum
          // Nachschlagen da.
          <div className="flex items-center gap-2 max-md:hidden">
            <Button
              icon="create_new_folder"
              disabled={!mayDelegateHere}
              title={
                mayDelegateHere
                  ? undefined
                  : 'Unterordner anzulegen setzt die Fachverantwortung für diesen Ordner voraus.'
              }
              onClick={() => setFolderDialog({ folder: null })}
            >
              Ordner
            </Button>
            <Button
              variant="primary"
              icon="add"
              disabled={!mayEditHere}
              title={
                mayEditHere
                  ? undefined
                  : 'In diesem Ordner dürfen Sie keine Workflows anlegen.'
              }
              onClick={() => setCreating(true)}
            >
              Neuer Workflow
            </Button>
          </div>
        }
      />

      {path.length > 0 && (
        <nav aria-label="Ordnerpfad" className="text-muted mb-3 flex items-center gap-1 text-[13px]">
          <button
            type="button"
            className="hover:text-text cursor-pointer border-none bg-transparent p-0 text-inherit"
            onClick={() => selectFolder(null)}
          >
            Alle
          </button>
          {path.map((folder, index) => (
            <span key={folder.id} className="flex items-center gap-1">
              <Icon name="chevron_right" size={15} className="text-faint" />
              {index === path.length - 1 ? (
                <span className="text-text font-semibold">{folder.name}</span>
              ) : (
                <button
                  type="button"
                  className="hover:text-text cursor-pointer border-none bg-transparent p-0 text-inherit"
                  onClick={() => selectFolder(folder.id)}
                >
                  {folder.name}
                </button>
              )}
            </span>
          ))}
        </nav>
      )}

      {selectedFolder && (
        <ResponsibilityBar
          folder={selectedFolder}
          onEdit={() => setFolderDialog({ folder: selectedFolder })}
          onDelegate={() => setDelegating(selectedFolder)}
          onDelete={() => setPendingFolderDelete(selectedFolder)}
        />
      )}

      <div className="mb-5 flex items-center gap-2.5">
        <SearchInput
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Alle Ordner durchsuchen …"
          wrapperClassName="max-w-[340px] flex-1 py-2"
        />
        <span className="flex-1" />
        <Segmented options={SORT_OPTIONS} value={sort} onChange={setSort} aria-label="Sortierung" />
      </div>

      <div className="flex items-start gap-5">
        <aside className="border-border bg-inset w-[218px] flex-none rounded-[var(--r)] border p-2 max-md:hidden">
          {foldersQuery.isPending ? (
            <Skeleton className="h-[180px] rounded-[var(--r-sm)]" />
          ) : (
            <FolderTree
              folders={folders}
              selectedId={selectedFolderId}
              expandedIds={expanded}
              rootWorkflowCount={rootWorkflowCount}
              onSelect={selectFolder}
              onToggle={toggleFolder}
              onDropWorkflow={(definitionId, folderId) => moveWorkflow(definitionId, folderId)}
            />
          )}
        </aside>

        <div className="min-w-0 flex-1">
          {/* Auf dem Telefon hat eine Baumspalte keinen Platz; der Ordner wird dort
              ausgewaehlt statt durchgeblaettert. */}
          {folders.length > 0 && (
            <select
              className="bg-surface border-border text-text mb-4 w-full rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] md:hidden"
              value={selectedFolderId ?? ''}
              onChange={(event) => selectFolder(event.target.value || null)}
            >
              <option value="">Alle Workflows ({rootWorkflowCount})</option>
              {sortByPath(folders).map((folder) => (
                <option key={folder.id} value={folder.id}>
                  {pathLabel(folder.id, folders)} ({folder.workflowCount})
                </option>
              ))}
            </select>
          )}

          {definitionsQuery.error && (
            <Card>
              <ErrorState error={definitionsQuery.error} onRetry={() => void definitionsQuery.refetch()} />
            </Card>
          )}

          {definitionsQuery.isPending && (
            <div className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(304px,1fr))]">
              {Array.from({ length: 6 }, (_, index) => (
                <Skeleton key={index} className="h-[260px] rounded-[var(--r-lg)]" />
              ))}
            </div>
          )}

          {!definitionsQuery.isPending && !definitionsQuery.error && definitions.length === 0 && (
            <Card>
              <EmptyState
                icon={search ? 'search_off' : 'schema'}
                title={
                  search
                    ? 'Kein Workflow gefunden'
                    : selectedFolder
                      ? `„${selectedFolder.name}“ ist leer`
                      : 'Noch keine Workflows'
                }
                description={
                  search
                    ? 'Die Suche geht über alle Ordner. Passe den Suchbegriff an oder lege einen neuen Workflow an.'
                    : mayEditHere
                      ? 'Lege einen Workflow an, modelliere ihn im Editor und deploye ihn anschließend.'
                      : 'Hier liegt noch nichts. Anlegen darf, wer für diesen Ordner zuständig ist.'
                }
              />
            </Card>
          )}

          <div className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(304px,1fr))]">
            {definitions.map((definition) => {
              const state = deployState(definition);
              const activeCount = activeByDefinition.get(definition.definitionId) ?? 0;
              const canStart = Boolean(definition.deployedId);
              const homeFolder = folders.find((folder) => folder.id === definition.folderId) ?? null;
              const mayEditThis = definition.folderId ? (homeFolder?.mayEdit ?? false) : mayPublish;

              return (
                <Card
                  key={definition.definitionId}
                  className="hover:border-accent hover:shadow-pop group cursor-pointer p-0 transition-[border-color,box-shadow,transform] duration-150 hover:-translate-y-0.5"
                  onClick={() => void navigate({ to: `/workflows/${encodeURIComponent(definition.definitionId)}` })}
                  // Ziehen ist der schnelle Weg ins andere Fach; der Knopf „Verschieben"
                  // daneben ist derselbe Vorgang fuer alle, die keine Maus benutzen.
                  draggable={mayEditThis}
                  onDragStart={(event) => {
                    event.dataTransfer.setData(WORKFLOW_DRAG_TYPE, definition.definitionId);
                    event.dataTransfer.effectAllowed = 'move';
                  }}
                >
                  <div className="relative h-[120px]">
                    <BpmnThumbnail versionGuid={definition.deployedId ?? null} className="h-full w-full" />
                    <span className="absolute top-2.5 right-2.5">
                      <Chip tone={DEPLOY_TONE[state]}>{DEPLOY_LABEL[state]}</Chip>
                    </span>
                  </div>

                  <div className="px-[17px] py-[15px]">
                    <div className="flex items-center gap-2.5">
                      <span
                        className="text-accent grid h-8 w-8 flex-none place-items-center rounded-[9px]"
                        style={{ background: toneSurface('accent', 11) }}
                      >
                        <Icon name={iconForLabel(`${definition.name} ${definition.description ?? ''}`)} size={19} />
                      </span>
                      <div className="font-display min-w-0 truncate text-[15.5px] font-semibold">
                        {definition.name}
                      </div>
                    </div>

                    <div className="text-muted mt-2.5 min-h-[39px] text-[13px] leading-normal">
                      {definition.description?.trim() || (
                        <span className="text-faint">Keine Beschreibung hinterlegt.</span>
                      )}
                    </div>

                    <div className="border-border mt-3.5 flex items-center gap-2.5 border-t pt-3.5">
                      <span className="bg-surface-2 text-muted rounded-md px-1.5 py-0.5 font-mono text-[11.5px] font-semibold">
                        {versionLabel(definition.latestVersion)}
                      </span>
                      {/* Bei einer Suche ueber alle Ordner ist der Fundort die eigentliche
                          Auskunft — ohne ihn weiss man nicht, wo der Treffer liegt. */}
                      {term.length > 0 && (
                        <span className="text-faint inline-flex min-w-0 items-center gap-1 text-[12px]">
                          <Icon name={homeFolder ? 'folder' : 'home_storage'} size={13} className="flex-none" />
                          <span className="truncate">{homeFolder?.name ?? 'Oberste Ebene'}</span>
                        </span>
                      )}
                      <span className="flex-1" />
                      {activeCount > 0 && (
                        <span className="text-muted inline-flex items-center gap-1.5 text-[12.5px]">
                          <Dot tone="run" size={6} />
                          {activeCount} aktiv
                        </span>
                      )}
                      <span className="text-faint text-xs whitespace-nowrap">
                        {formatRelative(definition.latestVersionDateTime)}
                      </span>
                    </div>

                    <div className="mt-3 flex gap-2 opacity-0 transition-opacity duration-150 group-hover:opacity-100 focus-within:opacity-100">
                      <Button
                        size="sm"
                        icon="rocket_launch"
                        className="flex-1"
                        disabled={!canStart || startInstance.isPending}
                        title={canStart ? undefined : 'Der Workflow muss zuerst deployt werden.'}
                        onClick={(event) => {
                          event.stopPropagation();
                          startInstance.mutate(definition.definitionId, {
                            onSuccess: (instance) =>
                              toast.success(`„${definition.name}" gestartet`, {
                                action: {
                                  label: 'Öffnen',
                                  onClick: () => void navigate({ to: `/instances/${instance.instanceId}` }),
                                },
                              }),
                            onError: (error) =>
                              toast.error('Start fehlgeschlagen', {
                                description: error instanceof Error ? error.message : undefined,
                              }),
                          });
                        }}
                      >
                        Starten
                      </Button>
                      <Button
                        size="sm"
                        icon="edit"
                        className="flex-1"
                        onClick={(event) => {
                          event.stopPropagation();
                          void navigate({ to: `/workflows/${encodeURIComponent(definition.definitionId)}` });
                        }}
                      >
                        Bearbeiten
                      </Button>
                      {mayEditThis && (
                        <Button
                          size="sm"
                          icon="drive_file_move"
                          title={`„${definition.name}" verschieben`}
                          className="w-[38px] px-0"
                          onClick={(event) => {
                            event.stopPropagation();
                            setMoving(definition);
                          }}
                        >
                          <span className="sr-only">{definition.name} verschieben</span>
                        </Button>
                      )}
                      {mayEditThis && (
                        <Button
                          size="sm"
                          variant="danger"
                          icon="delete"
                          title={`„${definition.name}" löschen`}
                          className="w-[38px] px-0"
                          onClick={(event) => {
                            event.stopPropagation();
                            setPendingDelete(definition);
                          }}
                        >
                          {/* Der Name gehoert in die Beschriftung: In einer Kachelwand gibt es
                              viele gleich aussehende Loeschknoepfe. */}
                          <span className="sr-only">{definition.name} löschen</span>
                        </Button>
                      )}
                    </div>
                  </div>
                </Card>
              );
            })}
          </div>
        </div>
      </div>

      <NewWorkflowDialog
        open={creating}
        onOpenChange={setCreating}
        busy={createDefinition.isPending}
        onCreate={(name) =>
          createDefinition.mutate(
            { name, folderId: selectedFolderId },
            {
              onSuccess: (meta) => {
                setCreating(false);
                toast.success(`„${meta.name}" angelegt`);
                void navigate({ to: `/workflows/${encodeURIComponent(meta.definitionId)}` });
              },
              onError: (error) =>
                toast.error('Workflow konnte nicht angelegt werden', {
                  description: error instanceof Error ? error.message : undefined,
                }),
            },
          )
        }
      />

      <FolderDialog
        open={folderDialog !== null}
        onOpenChange={(open) => {
          if (!open) setFolderDialog(null);
        }}
        folder={folderDialog?.folder ?? null}
        defaultParentId={selectedFolderId}
        folders={folders}
        busy={createFolder.isPending || updateFolder.isPending}
        onSubmit={(request) => {
          const bestehend = folderDialog?.folder;
          if (bestehend) {
            updateFolder.mutate(
              { id: bestehend.id, folder: request },
              {
                onSuccess: () => {
                  setFolderDialog(null);
                  toast.success(`„${request.name}" gespeichert`);
                },
                onError: (error) =>
                  toast.error('Ordner konnte nicht geändert werden', {
                    description: error instanceof Error ? error.message : undefined,
                  }),
              },
            );
            return;
          }

          createFolder.mutate(request, {
            onSuccess: (folder) => {
              setFolderDialog(null);
              toast.success(`Ordner „${folder.name}" angelegt`);
              selectFolder(folder.id);
            },
            onError: (error) =>
              toast.error('Ordner konnte nicht angelegt werden', {
                description: error instanceof Error ? error.message : undefined,
              }),
          });
        }}
      />

      <FolderDelegationDialog
        open={delegating !== null}
        onOpenChange={(open) => {
          if (!open) setDelegating(null);
        }}
        folder={delegating}
        busy={updateAssignments.isPending}
        onSubmit={(assignments: FolderAssignmentDto[]) => {
          const ziel = delegating;
          if (!ziel) return;
          updateAssignments.mutate(
            { id: ziel.id, assignments },
            {
              onSuccess: () => {
                setDelegating(null);
                toast.success(`Zuständigkeiten für „${ziel.name}" gespeichert`);
              },
              onError: (error) =>
                toast.error('Zuständigkeiten konnten nicht gespeichert werden', {
                  description: error instanceof Error ? error.message : undefined,
                }),
            },
          );
        }}
      />

      <MoveWorkflowDialog
        open={moving !== null}
        onOpenChange={(open) => {
          if (!open) setMoving(null);
        }}
        workflowName={moving?.name ?? ''}
        currentFolderId={moving?.folderId ?? null}
        folders={folders}
        mayUseRoot={mayPublish}
        busy={moveDefinition.isPending}
        onSubmit={(folderId) => {
          if (!moving) return;
          moveWorkflow(moving.definitionId, folderId, moving.name);
        }}
      />

      <ConfirmModal
        open={pendingDelete !== null}
        onOpenChange={(open) => {
          if (!open) setPendingDelete(null);
        }}
        destructive
        busy={deleteDefinition.isPending}
        title={`„${pendingDelete?.name ?? ''}" löschen?`}
        description="Alle Versionen dieses Workflows, ihre Diagramme und die bereits beendeten Instanzen werden entfernt. Das lässt sich nicht rückgängig machen. Laufende Instanzen verhindern das Löschen."
        confirmLabel="Endgültig löschen"
        confirmIcon="delete"
        onConfirm={() => {
          const target = pendingDelete;
          if (!target) return;
          deleteDefinition.mutate(target.definitionId, {
            onSuccess: () => {
              setPendingDelete(null);
              toast.success(`„${target.name}" gelöscht`);
            },
            onError: (error) =>
              toast.error('Workflow konnte nicht gelöscht werden', {
                description: error instanceof Error ? error.message : undefined,
              }),
          });
        }}
      />

      <ConfirmModal
        open={pendingFolderDelete !== null}
        onOpenChange={(open) => {
          if (!open) setPendingFolderDelete(null);
        }}
        destructive
        busy={deleteFolder.isPending}
        title={`Ordner „${pendingFolderDelete?.name ?? ''}" löschen?`}
        description="Nur leere Ordner lassen sich löschen. Unterordner und Workflows müssen vorher verschoben oder einzeln gelöscht werden."
        confirmLabel="Ordner löschen"
        confirmIcon="delete"
        onConfirm={() => {
          const target = pendingFolderDelete;
          if (!target) return;
          deleteFolder.mutate(target.id, {
            onSuccess: () => {
              setPendingFolderDelete(null);
              if (selectedFolderId === target.id) setSelectedFolderId(target.parentId ?? null);
              toast.success(`Ordner „${target.name}" gelöscht`);
            },
            onError: (error) =>
              toast.error('Ordner konnte nicht gelöscht werden', {
                description: error instanceof Error ? error.message : undefined,
              }),
          });
        }}
      />
    </PageContainer>
  );
}

interface ResponsibilityBarProps {
  folder: WorkflowFolderDto;
  onEdit: () => void;
  onDelegate: () => void;
  onDelete: () => void;
}

/**
 * Die Zuständigkeitszeile über der Kachelwand.
 *
 * Sie beantwortet die Frage, die ein Ordner sonst offen lässt: Wen muss ich fragen? Wer
 * hier nichts darf, sieht statt der Schaltflächen den Hinweis, wer zuständig ist — eine
 * Ansicht, die eine Handlung anbietet und danach eine Ablehnung liefert, ist schlechter
 * als eine, die sie gar nicht erst anbietet.
 */
function ResponsibilityBar({ folder, onEdit, onDelegate, onDelete }: ResponsibilityBarProps) {
  const alle = [...folder.assignments, ...folder.inheritedAssignments];
  const verantwortlich = alle.filter((assignment) => assignment.role === 'steward');
  const bearbeiter = alle.filter((assignment) => assignment.role === 'editor');

  return (
    <div className="border-border bg-inset mb-4 flex flex-wrap items-center gap-2.5 rounded-[var(--r)] border px-3.5 py-2.5">
      <SubjectGroup icon="shield_person" label="Fachverantwortung" assignments={verantwortlich} />
      {verantwortlich.length > 0 && bearbeiter.length > 0 && (
        <span className="bg-border-strong h-4 w-px" aria-hidden />
      )}
      <SubjectGroup icon="edit_note" label="Bearbeiten" assignments={bearbeiter} />

      {alle.length === 0 && (
        <span className="text-faint text-[12px]">
          Niemand eingetragen — dieser Ordner bleibt der Rolle fürs Modellieren vorbehalten.
        </span>
      )}

      <span className="flex-1" />

      {folder.mayDelegate ? (
        <div className="flex items-center gap-2">
          <Button size="sm" icon="manage_accounts" onClick={onDelegate}>
            Zuständigkeiten
          </Button>
          <Button size="sm" icon="edit" onClick={onEdit}>
            Ordner
          </Button>
          <Button size="sm" variant="danger" icon="delete" className="w-[34px] px-0" onClick={onDelete}>
            <span className="sr-only">Ordner {folder.name} löschen</span>
          </Button>
        </div>
      ) : (
        <span className="text-faint inline-flex items-center gap-1.5 text-[12px]">
          <Icon name="lock" size={14} />
          {folder.mayEdit ? 'Sie dürfen hier bearbeiten, aber nicht delegieren.' : 'Nur ansehen und starten.'}
        </span>
      )}
    </div>
  );
}

function SubjectGroup({
  icon,
  label,
  assignments,
}: {
  icon: string;
  label: string;
  assignments: FolderAssignmentDto[];
}) {
  if (assignments.length === 0) return null;

  return (
    <>
      <span className="text-muted inline-flex items-center gap-1.5 text-[11.5px]">
        <Icon name={icon} size={15} className="text-faint" />
        {label}
      </span>
      {assignments.map((assignment) => (
        <span
          key={`${assignment.subjectKind}-${assignment.subject}`}
          className="bg-surface border-border inline-flex items-center gap-1.5 rounded-full border py-0.5 pr-2.5 pl-1 text-[11.5px] font-semibold whitespace-nowrap"
        >
          <span
            className={
              assignment.subjectKind === 'group'
                ? 'bg-surface-2 text-muted grid h-[18px] w-[18px] place-items-center rounded-full'
                : 'bg-accent text-accent-ink grid h-[18px] w-[18px] place-items-center rounded-full text-[9px] font-bold'
            }
          >
            {assignment.subjectKind === 'group' ? (
              <Icon name="group" size={12} />
            ) : (
              (assignment.displayName ?? assignment.subject)
                .split(/[\s.@_-]+/)
                .filter(Boolean)
                .slice(0, 2)
                .map((teil) => teil[0]?.toUpperCase() ?? '')
                .join('')
            )}
          </span>
          {assignment.displayName ?? assignment.subject}
        </span>
      ))}
    </>
  );
}
