import { useEffect, useMemo, useRef, useState } from 'react';
import { toast } from 'sonner';

import { FormBuilder, type FormBuilderHandle } from '@/components/forms/FormBuilder';
import { FormSectionPicker } from '@/components/forms/FormSectionPicker';
import { FormRenderer } from '@/components/forms/FormRenderer';
import { Button } from '@/components/ui/Button';
import { Card, EmptyState } from '@/components/ui/Card';
import { ConfirmModal } from '@/components/ui/Modal';
import { Chip, toneSurface } from '@/components/ui/Chip';
import { SearchInput, TextInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { Segmented } from '@/components/ui/Segmented';
import { ErrorState, InlineSpinner, Skeleton } from '@/components/ui/States';
import {
  useDeleteForm,
  useDiscardFormAuthoringDraft,
  useForm,
  useFormAuthoringDraft,
  useFormAuthoringPreview,
  useFormCompatibilityInventory,
  useForms,
  usePublishFormAuthoringDraft,
  useSaveFormAuthoringDraft,
  useSaveFormMeta,
} from '@/lib/api/queries';
import { ApiError } from '@/lib/api/client';
import { cn } from '@/lib/cn';
import { describeCompatibilityIssue, incompatibleCountByForm } from '@/lib/forms/formCompatibility';
import { iconForLabel } from '@/lib/taskView';
import { appendSectionReference } from '@/lib/forms/sectionReferences';
import type { FormSectionVersionSummaryDto } from '@/lib/api/types';
import { useCan } from '@/stores/session';

type Mode = 'preview' | 'edit';
type InventoryFilter = 'all' | 'migration';

const MODE_OPTIONS = [
  { value: 'preview' as const, label: 'Vorschau' },
  { value: 'edit' as const, label: 'Felder' },
];

export function FormsPage() {
  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [mode, setMode] = useState<Mode>('preview');
  const [creating, setCreating] = useState(false);
  const [newName, setNewName] = useState('');
  const [inventoryFilter, setInventoryFilter] = useState<InventoryFilter>('all');
  const [editorSchema, setEditorSchema] = useState<string | undefined>();
  const [dirty, setDirty] = useState(false);
  const [builderReady, setBuilderReady] = useState(false);
  const [draftConflict, setDraftConflict] = useState(false);
  const [editorGeneration, setEditorGeneration] = useState(0);
  const builderRef = useRef<FormBuilderHandle>(null);
  // Formulare darf jeder Zugelassene lesen; anlegen und aendern verlangt die Modelliererrolle.
  const mayPublish = useCan()('modeler');

  const formsQuery = useForms();
  const saveDraft = useSaveFormAuthoringDraft();
  const discardDraft = useDiscardFormAuthoringDraft();
  const publishDraft = usePublishFormAuthoringDraft();
  const saveMeta = useSaveFormMeta();
  const deleteForm = useDeleteForm();
  const compatibilityQuery = useFormCompatibilityInventory(mayPublish);
  const [pendingDelete, setPendingDelete] = useState<{ formId: string; name: string } | null>(null);
  const [pendingPublish, setPendingPublish] = useState(false);
  const [pendingDiscard, setPendingDiscard] = useState(false);

  const incompatibleByForm = useMemo(() => {
    return incompatibleCountByForm(compatibilityQuery.data ?? []);
  }, [compatibilityQuery.data]);

  const forms = useMemo(() => {
    const term = search.trim().toLowerCase();
    return (formsQuery.data ?? [])
      .filter((form) => term.length === 0 || form.name.toLowerCase().includes(term))
      .filter((form) => inventoryFilter === 'all' || incompatibleByForm.has(form.formId))
      .sort((a, b) => a.name.localeCompare(b.name, 'de'));
  }, [formsQuery.data, incompatibleByForm, inventoryFilter, search]);

  // Beim ersten Laden das erste Formular auswählen, damit die Vorschau nicht leer bleibt.
  useEffect(() => {
    if (!selectedId && forms.length > 0) setSelectedId(forms[0]!.formId);
  }, [forms, selectedId]);

  // Ein Filter darf rechts keinen unsichtbaren, nicht mehr zur Liste gehoerenden
  // Datensatz stehen lassen. Bei leerem Ergebnis wird auch die Detailauswahl geleert.
  useEffect(() => {
    if (selectedId && !forms.some((form) => form.formId === selectedId)) {
      setSelectedId(forms[0]?.formId ?? null);
    }
  }, [forms, selectedId]);

  const selected = forms.find((form) => form.formId === selectedId);
  const draftQuery = useFormAuthoringDraft(mayPublish ? selectedId ?? undefined : undefined);
  const publishedQuery = useForm(mayPublish ? undefined : selectedId ?? undefined);
  const sourceData = useMemo(
    () => mayPublish
      ? draftQuery.data
      : publishedQuery.data
        ? {
            formData: publishedQuery.data.formData ?? '{}',
            basedOnVersion: publishedQuery.data.version,
            hasDraft: false,
            revision: 0,
          }
        : undefined,
    [draftQuery.data, mayPublish, publishedQuery.data],
  );
  const sourcePending = mayPublish ? draftQuery.isPending : publishedQuery.isPending;
  const sourceError = mayPublish ? draftQuery.error : publishedQuery.error;
  const previewQuery = useFormAuthoringPreview(
    mayPublish ? selectedId ?? undefined : undefined,
    editorSchema,
    mayPublish && mode === 'preview',
  );
  const selectedCompatibility = useMemo(
    () => (compatibilityQuery.data ?? []).filter((item) => item.formId === selectedId),
    [compatibilityQuery.data, selectedId],
  );
  const selectedIssues = selectedCompatibility.filter((item) => !item.compatible);

  useEffect(() => {
    setEditorSchema(undefined);
    setDirty(false);
    setBuilderReady(false);
    setDraftConflict(false);
    setEditorGeneration((generation) => generation + 1);
  }, [selectedId]);

  useEffect(() => {
    if (sourceData && editorSchema === undefined) {
      setEditorSchema(sourceData.formData);
    }
  }, [sourceData, editorSchema]);

  function handleCreate() {
    const name = newName.trim();
    if (!name) return;

    const formId = crypto.randomUUID();

    saveMeta.mutate(
      { formId, name },
      {
        onSuccess: () => {
          // Neu angelegte Formulare beginnen als Entwurf. Erst die ausdrueckliche
          // Veroeffentlichung erzeugt eine fuer Deployments sichtbare Version.
          saveDraft.mutate(
            {
              formId,
              draft: {
                expectedRevision: 0,
                formData: JSON.stringify({ display: 'form', components: [] }, null, 2),
              },
            },
            {
              onSuccess: () => {
                toast.success(`Entwurf „${name}" angelegt`);
                setCreating(false);
                setNewName('');
                setSelectedId(formId);
                setMode('edit');
              },
              onError: (error) =>
                toast.error('Erster Entwurf konnte nicht gespeichert werden', {
                  description: error instanceof Error ? error.message : undefined,
                }),
            },
          );
        },
        onError: (error) =>
          toast.error('Formular konnte nicht angelegt werden', {
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    );
  }

  function readEditorSchema(): string | null {
    if (!builderRef.current) return editorSchema ?? null;
    try {
      const schema = builderRef.current.getSchema();
      setEditorSchema(schema);
      return schema;
    } catch (error) {
      toast.error('Das Schema konnte nicht gelesen werden', {
        description: error instanceof Error ? error.message : undefined,
      });
      return null;
    }
  }

  function handleSaveSchema() {
    if (!selectedId) return;
    const schema = readEditorSchema();
    if (schema === null) return;

    saveDraft.mutate(
      {
        formId: selectedId,
        draft: { expectedRevision: draftQuery.data?.revision ?? 0, formData: schema },
      },
      {
        onSuccess: (saved) => {
          setDirty(false);
          setDraftConflict(false);
          toast.success(`Entwurf Revision ${saved.revision} gespeichert`);
        },
        onError: (error) => {
          setDraftConflict(error instanceof ApiError && error.status === 409);
          toast.error('Speichern fehlgeschlagen', {
            description: error instanceof Error ? error.message : undefined,
          });
        },
      },
    );
  }

  async function adoptServerDraft() {
    const result = await draftQuery.refetch();
    if (!result.data) return;
    setEditorSchema(result.data.formData);
    setDirty(false);
    setDraftConflict(false);
    setEditorGeneration((generation) => generation + 1);
  }

  function handleModeChange(next: Mode) {
    if (mode === 'edit' && next === 'preview' && readEditorSchema() === null) return;
    setMode(next);
  }

  function insertSection(version: FormSectionVersionSummaryDto, name: string) {
    const current = readEditorSchema();
    if (!current) return;
    try {
      const next = appendSectionReference(current, {
        sectionId: version.sectionId,
        version: version.version,
        label: name,
      });
      setEditorSchema(next);
      setDirty(next !== draftQuery.data?.formData);
      setEditorGeneration((generation) => generation + 1);
      toast.success(`Abschnitt „${name}" v${version.version.major}.${version.version.minor} eingefügt`);
    } catch (error) {
      toast.error('Abschnitt konnte nicht eingefügt werden', {
        description: error instanceof Error ? error.message : undefined,
      });
    }
  }

  return (
    <PageContainer>
      <PageHeader
        title="Formulare"
        description="Formulare, die Nutzende beim Bearbeiten von Aufgaben ausfüllen."
        actions={
          <Button variant="primary" icon="add" onClick={() => setCreating((open) => !open)} disabled={!mayPublish}>
            Neues Formular
          </Button>
        }
      />

      {creating && (
        <Card className="mb-4 flex items-center gap-2.5 p-3.5">
          <TextInput
            autoFocus
            value={newName}
            onChange={(event) => setNewName(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') handleCreate();
              if (event.key === 'Escape') setCreating(false);
            }}
            placeholder="Name des Formulars — er dient zugleich als Form-Key im BPMN"
            className="flex-1"
          />
          <Button
            variant="primary"
            loading={saveMeta.isPending || saveDraft.isPending}
            onClick={handleCreate}
          >
            Anlegen
          </Button>
          <Button variant="ghost" onClick={() => setCreating(false)}>
            Abbrechen
          </Button>
        </Card>
      )}

      <div className="grid items-start gap-[22px] lg:grid-cols-[300px_minmax(0,1fr)]">
        <div className="flex flex-col gap-2">
          <SearchInput
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Formular filtern …"
            wrapperClassName="py-2 mb-1"
          />

          {mayPublish && (
            <Segmented
              options={[
                { value: 'all', label: 'Alle', count: formsQuery.data?.length ?? 0 },
                { value: 'migration', label: 'Migration', count: incompatibleByForm.size },
              ]}
              value={inventoryFilter}
              onChange={setInventoryFilter}
              aria-label="Formularbestand filtern"
              className="mb-1"
            />
          )}

          {formsQuery.isPending &&
            Array.from({ length: 4 }, (_, index) => <Skeleton key={index} className="h-[60px]" />)}

          {formsQuery.error && (
            <ErrorState error={formsQuery.error} onRetry={() => void formsQuery.refetch()} />
          )}

          {!formsQuery.isPending && forms.length === 0 && (
            <Card>
              <EmptyState
                icon="description"
                title={search ? 'Kein Treffer' : 'Noch keine Formulare'}
                description={
                  search
                    ? 'Passe den Suchbegriff an.'
                    : 'Lege ein Formular an und verweise im BPMN über den Form-Key darauf.'
                }
              />
            </Card>
          )}

          {forms.map((form) => {
            const active = form.formId === selectedId;
            return (
              <button
                key={form.formId}
                type="button"
                onClick={() => setSelectedId(form.formId)}
                className={cn(
                  'flex w-full cursor-pointer items-center gap-3 rounded-[var(--r)] border px-3 py-3 text-left',
                  'transition-[background-color,border-color] duration-150',
                  active ? 'border-accent' : 'bg-surface border-border hover:border-accent',
                )}
                style={active ? { background: toneSurface('accent', 8) } : undefined}
              >
                <span
                  className={cn(
                    'bg-surface-2 grid h-9 w-9 flex-none place-items-center rounded-[9px]',
                    active ? 'text-accent' : 'text-muted',
                  )}
                >
                  <Icon name={iconForLabel(form.name)} size={19} />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-semibold">{form.name}</span>
                  <span className="text-muted mt-px block font-mono text-xs">
                    Form-Key: {form.name}
                  </span>
                </span>
                {(incompatibleByForm.get(form.formId) ?? 0) > 0 && (
                  <Chip tone="wait">Migration</Chip>
                )}
              </button>
            );
          })}
        </div>

        <Card>
          <div className="border-border bg-surface-2 flex items-center justify-between gap-3 border-b px-[22px] py-3.5">
            <div className="flex min-w-0 items-center gap-2.5">
              <Icon name="description" size={19} className="text-accent" />
              <div className="min-w-0">
                <div className="truncate text-[15px] font-semibold">
                  {selected?.name ?? 'Kein Formular ausgewählt'}
                </div>
                <div className="text-muted text-xs">
                  {sourceData?.hasDraft
                    ? `Entwurf · Revision ${sourceData.revision}`
                    : sourceData?.basedOnVersion
                      ? `Veröffentlicht v${sourceData.basedOnVersion.major}.${sourceData.basedOnVersion.minor}`
                      : 'Noch nicht veröffentlicht'}
                </div>
              </div>
            </div>

            <div className="flex flex-wrap items-center justify-end gap-2.5">
              {mayPublish && mode === 'edit' && selectedId && (
                <Button
                  variant="primary"
                  size="sm"
                  icon="save"
                  loading={saveDraft.isPending}
                  disabled={!builderReady || !dirty || draftConflict}
                  onClick={handleSaveSchema}
                >
                  Entwurf speichern
                </Button>
              )}
              {mayPublish && draftQuery.data?.hasDraft && !dirty && !draftConflict && (
                <Button
                  variant="primary"
                  size="sm"
                  icon="upload"
                  onClick={() => setPendingPublish(true)}
                >
                  Veröffentlichen
                </Button>
              )}
              {mayPublish && draftQuery.data?.hasDraft && !dirty && (
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={publishDraft.isPending}
                  onClick={() => setPendingDiscard(true)}
                >
                  Entwurf verwerfen
                </Button>
              )}
              {mayPublish && (
                <Segmented options={MODE_OPTIONS} value={mode} onChange={handleModeChange} aria-label="Ansicht" />
              )}
              {mayPublish && selected && (
                <Button
                  size="sm"
                  variant="danger"
                  icon="delete"
                  title={`„${selected.name}" löschen`}
                  className="w-[38px] px-0"
                  onClick={() => setPendingDelete({ formId: selected.formId, name: selected.name })}
                >
                  {/* Der Name gehoert in die Beschriftung: Der Knopf steht neben einer
                      Formularvorschau, die selbst keinen Namen traegt. */}
                  <span className="sr-only">{selected.name} löschen</span>
                </Button>
              )}
            </div>
          </div>

          <div className={cn(mode === 'preview' ? 'max-w-[640px] px-[30px] py-[26px]' : 'p-4')}>
            {!selectedId && (
              <EmptyState
                icon="ads_click"
                title="Formular wählen"
                description="Wähle links ein Formular, um es anzusehen oder zu bearbeiten."
              />
            )}

            {selectedId && sourcePending && <InlineSpinner />}

            {selectedId && sourceError && (
              <ErrorState
                error={sourceError}
                title="Formular konnte nicht geladen werden"
                onRetry={() => void (mayPublish ? draftQuery.refetch() : publishedQuery.refetch())}
              />
            )}

            {selectedId && draftConflict && (
              <div className="border-warn text-warn mb-4 flex items-center justify-between gap-3 rounded-[var(--r)] border px-4 py-3 text-sm">
                <span>Der Entwurf wurde zwischenzeitlich geändert. Deine lokale Fassung bleibt erhalten.</span>
                <Button size="sm" variant="secondary" onClick={() => void adoptServerDraft()}>
                  Serverstand laden
                </Button>
              </div>
            )}

            {mayPublish && selectedId && selectedIssues.length > 0 && (
              <div className="border-warn mb-4 rounded-[var(--r)] border px-4 py-3 text-sm">
                <div className="text-warn mb-1 font-semibold">Bestand vor erneutem Veröffentlichen prüfen</div>
                <ul className="text-muted list-disc space-y-1 pl-5">
                  {selectedIssues.map((item) => (
                    <li key={`${item.source}-${item.publishedFormId ?? item.draftRevision ?? 'current'}`}>
                      {item.source === 'draft'
                        ? `Entwurf Revision ${item.draftRevision ?? 0}`
                        : `Version ${item.version ? `${item.version.major}.${item.version.minor}` : 'unbekannt'}`}
                      {' '}{describeCompatibilityIssue(item.issueCode)}.
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {selectedId && sourceData && mode === 'preview' && mayPublish && previewQuery.isPending && (
              <InlineSpinner />
            )}

            {selectedId && sourceData && mode === 'preview' && mayPublish && previewQuery.error && (
              <ErrorState
                error={previewQuery.error}
                title="Vorschau konnte nicht erzeugt werden"
                onRetry={() => void previewQuery.refetch()}
              />
            )}

            {selectedId && sourceData && mode === 'preview' && mayPublish && previewQuery.data && (
              <FormRenderer schema={previewQuery.data.formData} />
            )}

            {selectedId && sourceData && mode === 'preview' && !mayPublish && (
              <FormRenderer schema={editorSchema} />
            )}

            {mayPublish && selectedId && draftQuery.data && mode === 'edit' && (
              <>
                <FormSectionPicker onInsert={insertSection} />
                <FormBuilder
                  key={`edit-${selectedId}-${editorGeneration}`}
                  ref={builderRef}
                  schema={editorSchema}
                  onReadyChange={setBuilderReady}
                  // Form.io besitzt waehrend der Bearbeitung den aktuellen Zustand. Ein
                  // Zurueckschreiben bei jedem Event wuerde den Builder ueber sein schema-Prop
                  // zerstoeren und neu aufbauen; gelesen wird erst bei Save/Preview/Insert.
                  onChange={() => setDirty(true)}
                />
              </>
            )}
          </div>
        </Card>
      </div>
      <ConfirmModal
        open={pendingPublish}
        onOpenChange={setPendingPublish}
        busy={publishDraft.isPending}
        title={`„${selected?.name ?? ''}" veröffentlichen?`}
        description="Der aktuelle Entwurf wird verbindlich geprüft und als neue unveränderliche Version veröffentlicht. Laufende Deployments behalten ihre bisher gebundene Version."
        confirmLabel="Version veröffentlichen"
        confirmIcon="upload"
        onConfirm={() => {
          if (!selectedId || !draftQuery.data?.hasDraft) return;
          publishDraft.mutate(
            { formId: selectedId, expectedRevision: draftQuery.data.revision },
            {
              onSuccess: (published) => {
                setPendingPublish(false);
                setDirty(false);
                setDraftConflict(false);
                const version = published.version;
                toast.success(version ? `Version v${version.major}.${version.minor} veröffentlicht` : 'Formular veröffentlicht');
              },
              onError: (error) => {
                setPendingPublish(false);
                setDraftConflict(error instanceof ApiError && error.status === 409);
                toast.error('Veröffentlichung fehlgeschlagen', {
                  description: error instanceof Error ? error.message : undefined,
                });
              },
            },
          );
        }}
      />
      <ConfirmModal
        open={pendingDiscard}
        onOpenChange={setPendingDiscard}
        destructive
        busy={discardDraft.isPending}
        title="Entwurf verwerfen?"
        description="Die letzte veröffentlichte Fassung bleibt erhalten. Der aktuelle Autorenentwurf kann danach nicht wiederhergestellt werden."
        confirmLabel="Entwurf verwerfen"
        confirmIcon="delete"
        onConfirm={() => {
          if (!selectedId || !draftQuery.data?.hasDraft) return;
          discardDraft.mutate(
            { formId: selectedId, expectedRevision: draftQuery.data.revision },
            {
              onSuccess: async () => {
                setPendingDiscard(false);
                setEditorSchema(undefined);
                setDirty(false);
                setDraftConflict(false);
                const refreshed = await draftQuery.refetch();
                if (refreshed.data) setEditorSchema(refreshed.data.formData);
                setEditorGeneration((generation) => generation + 1);
                toast.success('Entwurf verworfen');
              },
              onError: (error) => {
                setPendingDiscard(false);
                setDraftConflict(error instanceof ApiError && error.status === 409);
                toast.error('Entwurf konnte nicht verworfen werden', {
                  description: error instanceof Error ? error.message : undefined,
                });
              },
            },
          );
        }}
      />
      <ConfirmModal
        open={pendingDelete !== null}
        onOpenChange={(open) => {
          if (!open) setPendingDelete(null);
        }}
        destructive
        busy={deleteForm.isPending}
        title={`„${pendingDelete?.name ?? ''}" löschen?`}
        description="Alle Versionen dieses Formulars werden entfernt. Das lässt sich nicht rückgängig machen. Benutzt ein deployter Workflow das Formular, verhindert das das Löschen."
        confirmLabel="Endgültig löschen"
        confirmIcon="delete"
        onConfirm={() => {
          const target = pendingDelete;
          if (!target) return;
          deleteForm.mutate(target.formId, {
            onSuccess: () => {
              setPendingDelete(null);
              if (selectedId === target.formId) setSelectedId(null);
              toast.success(`„${target.name}" gelöscht`);
            },
            onError: (error) =>
              toast.error('Formular konnte nicht gelöscht werden', {
                description: error instanceof Error ? error.message : undefined,
              }),
          });
        }}
      />
    </PageContainer>
  );
}
