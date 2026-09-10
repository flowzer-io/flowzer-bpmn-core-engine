import { useEffect, useMemo, useRef, useState } from 'react';
import { toast } from 'sonner';

import { FormBuilder, type FormBuilderHandle } from '@/components/forms/FormBuilder';
import { Button } from '@/components/ui/Button';
import { Card, EmptyState } from '@/components/ui/Card';
import { ConfirmModal } from '@/components/ui/Modal';
import { SearchInput, TextInput } from '@/components/ui/Field';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { ErrorState, InlineSpinner, Skeleton } from '@/components/ui/States';
import {
  useCreateFormSection,
  useDiscardFormSectionDraft,
  useFormSectionDraft,
  useFormSectionVersions,
  useFormSections,
  usePublishFormSectionDraft,
  useRenameFormSection,
  useSaveFormSectionDraft,
} from '@/lib/api/queries';
import { ApiError } from '@/lib/api/client';
import { cn } from '@/lib/cn';
import { useCan } from '@/stores/session';

/** Pflegeoberfläche für hostneutrale, versionierte Formularabschnitte. */
export function FormSectionsPage() {
  const mayPublish = useCan()('modeler');
  const sectionsQuery = useFormSections({ enabled: mayPublish });
  const createSection = useCreateFormSection();
  const renameSection = useRenameFormSection();
  const saveDraft = useSaveFormSectionDraft();
  const discardDraft = useDiscardFormSectionDraft();
  const publishDraft = usePublishFormSectionDraft();
  const builderRef = useRef<FormBuilderHandle>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [name, setName] = useState('');
  const [newName, setNewName] = useState('');
  const [editorSchema, setEditorSchema] = useState<string>();
  const [dirty, setDirty] = useState(false);
  const [ready, setReady] = useState(false);
  const [generation, setGeneration] = useState(0);
  const [conflict, setConflict] = useState(false);
  const [confirmPublish, setConfirmPublish] = useState(false);
  const [confirmDiscard, setConfirmDiscard] = useState(false);

  const sections = useMemo(() => {
    const term = search.trim().toLocaleLowerCase('de');
    return (sectionsQuery.data ?? [])
      .filter((section) => term.length === 0 || section.name.toLocaleLowerCase('de').includes(term))
      .sort((a, b) => a.name.localeCompare(b.name, 'de'));
  }, [search, sectionsQuery.data]);
  const draftQuery = useFormSectionDraft(selectedId ?? undefined);
  const versionsQuery = useFormSectionVersions(selectedId ?? undefined);
  const selected = sectionsQuery.data?.find((section) => section.sectionId === selectedId);

  useEffect(() => {
    if (!selectedId && sections.length > 0) setSelectedId(sections[0]!.sectionId);
  }, [selectedId, sections]);

  useEffect(() => {
    if (selectedId && !sections.some((section) => section.sectionId === selectedId)) {
      setSelectedId(sections[0]?.sectionId ?? null);
    }
  }, [selectedId, sections]);

  useEffect(() => {
    setEditorSchema(undefined);
    setDirty(false);
    setConflict(false);
    setReady(false);
    setGeneration((value) => value + 1);
  }, [selectedId]);

  // Form.io besitzt waehrend der Bearbeitung den lokalen Stand. Ein Hintergrund-
  // Refetch darf keine ungespeicherten Eingaben durch eine neue Query-Antwort ersetzen.
  useEffect(() => {
    if (editorSchema === undefined && draftQuery.data)
      setEditorSchema(draftQuery.data.sectionData);
  }, [draftQuery.data, editorSchema]);

  useEffect(() => {
    setName(selected?.name ?? '');
  }, [selected?.name, selectedId]);

  function create() {
    const trimmed = newName.trim();
    if (!trimmed) return;
    createSection.mutate(trimmed, {
      onSuccess: (created) => {
        setNewName('');
        setSelectedId(created.sectionId);
        toast.success(`Abschnitt „${created.name}" angelegt`);
      },
      onError: (error) => toast.error('Abschnitt konnte nicht angelegt werden', { description: message(error) }),
    });
  }

  function rename() {
    if (!selectedId) return;
    const trimmed = name.trim();
    if (!trimmed || trimmed === selected?.name) return;
    renameSection.mutate({ sectionId: selectedId, name: trimmed }, {
      onSuccess: () => toast.success('Name gespeichert'),
      onError: (error) => toast.error('Name konnte nicht gespeichert werden', { description: message(error) }),
    });
  }

  function readSchema(): string | null {
    try {
      const schema = builderRef.current?.getSchema() ?? editorSchema;
      if (!schema) return null;
      setEditorSchema(schema);
      return schema;
    } catch (error) {
      toast.error('Schema konnte nicht gelesen werden', { description: message(error) });
      return null;
    }
  }

  function save() {
    if (!selectedId) return;
    const sectionData = readSchema();
    if (!sectionData) return;
    saveDraft.mutate({
      sectionId: selectedId,
      draft: { expectedRevision: draftQuery.data?.revision ?? 0, sectionData },
    }, {
      onSuccess: (saved) => {
        setDirty(false);
        setConflict(false);
        toast.success(`Entwurf Revision ${saved.revision} gespeichert`);
      },
      onError: (error) => {
        setConflict(error instanceof ApiError && error.status === 409);
        toast.error('Speichern fehlgeschlagen', { description: message(error) });
      },
    });
  }

  function reloadDraft() {
    void draftQuery.refetch().then((result) => {
      if (!result.data) return;
      setEditorSchema(result.data.sectionData);
      setDirty(false);
      setConflict(false);
      setGeneration((value) => value + 1);
    });
  }

  if (!mayPublish) {
    return <PageContainer><EmptyState icon="lock" title="Modelliererrolle erforderlich" description="Abschnitte dürfen nur von Modellierenden gepflegt werden." /></PageContainer>;
  }

  return (
    <PageContainer>
      <PageHeader
        title="Formularabschnitte"
        description="Versionierte, wiederverwendbare Bausteine für deklarative Formulare."
        actions={
          <div className="flex w-full gap-2 sm:w-auto">
            <TextInput
              value={newName}
              onChange={(event) => setNewName(event.target.value)}
              onKeyDown={(event) => event.key === 'Enter' && create()}
              placeholder="Name des neuen Abschnitts"
              aria-label="Name des neuen Abschnitts"
            />
            <Button variant="primary" icon="add" loading={createSection.isPending} disabled={!newName.trim()} onClick={create}>Anlegen</Button>
          </div>
        }
      />

      <div className="grid items-start gap-[22px] lg:grid-cols-[300px_minmax(0,1fr)]">
        <div className="flex flex-col gap-2">
          <SearchInput value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Abschnitte filtern …" />
          {sectionsQuery.isPending && Array.from({ length: 3 }, (_, index) => <Skeleton key={index} className="h-[58px]" />)}
          {sectionsQuery.error && <ErrorState error={sectionsQuery.error} onRetry={() => void sectionsQuery.refetch()} />}
          {!sectionsQuery.isPending && sections.length === 0 && <Card><EmptyState icon="view_agenda" title="Noch keine Abschnitte" description="Lege den ersten wiederverwendbaren Formularabschnitt an." /></Card>}
          {sections.map((section) => (
            <button
              key={section.sectionId}
              type="button"
              onClick={() => setSelectedId(section.sectionId)}
              className={cn('bg-surface border-border flex w-full cursor-pointer items-center rounded-[var(--r)] border px-3 py-3 text-left transition-colors hover:border-accent', selectedId === section.sectionId && 'border-accent bg-accent/10')}
            >
              <span className="min-w-0 flex-1 truncate text-sm font-semibold">{section.name}</span>
              <span className="text-muted ml-2 font-mono text-[10px]">Abschnitt</span>
            </button>
          ))}
        </div>

        <Card>
          <div className="border-border bg-surface-2 flex flex-wrap items-center gap-2.5 border-b px-[22px] py-3.5">
            <div className="min-w-0 flex-1">
              <div className="truncate text-[15px] font-semibold">{selected?.name ?? 'Kein Abschnitt ausgewählt'}</div>
              <div className="text-muted text-xs">{draftQuery.data?.hasDraft ? `Entwurf · Revision ${draftQuery.data.revision}` : draftQuery.data?.basedOnVersion ? `Basis · v${draftQuery.data.basedOnVersion.major}.${draftQuery.data.basedOnVersion.minor}` : 'Noch nicht veröffentlicht'}</div>
            </div>
            {selected && <TextInput value={name} onChange={(event) => setName(event.target.value)} onBlur={rename} onKeyDown={(event) => event.key === 'Enter' && rename()} aria-label="Abschnitt umbenennen" className="max-w-[220px]" />}
            {selected && draftQuery.data?.hasDraft && !dirty && <Button size="sm" variant="primary" icon="upload" onClick={() => setConfirmPublish(true)}>Veröffentlichen</Button>}
            {selected && draftQuery.data?.hasDraft && !dirty && <Button size="sm" variant="secondary" onClick={() => setConfirmDiscard(true)}>Verwerfen</Button>}
            {selected && <Button size="sm" variant="primary" icon="save" loading={saveDraft.isPending} disabled={!ready || !dirty || conflict} onClick={save}>Entwurf speichern</Button>}
          </div>
          <div className="p-4">
            {selectedId && (
              <div className="border-border mb-4 flex flex-wrap items-center gap-2 border-b pb-3" aria-label="Veröffentlichte Abschnittsversionen">
                <span className="text-muted text-xs font-semibold">Veröffentlichte Fassungen</span>
                {versionsQuery.isPending && <InlineSpinner label="Versionen werden geladen …" />}
                {!versionsQuery.isPending && (versionsQuery.data ?? []).length === 0 && (
                  <span className="text-muted text-xs">Noch keine</span>
                )}
                {(versionsQuery.data ?? []).map((version) => (
                  <span key={version.id} className="border-border bg-surface-2 rounded-full border px-2 py-0.5 font-mono text-xs">
                    v{version.version.major}.{version.version.minor}
                  </span>
                ))}
              </div>
            )}
            {!selectedId && <EmptyState icon="view_agenda" title="Abschnitt auswählen" description="Wähle links einen Abschnitt aus oder lege einen neuen an." />}
            {selectedId && draftQuery.isPending && <InlineSpinner label="Abschnitt wird geladen …" />}
            {selectedId && draftQuery.error && <ErrorState error={draftQuery.error} onRetry={() => void draftQuery.refetch()} />}
            {conflict && <div className="border-warn text-warn mb-3 flex items-center justify-between rounded-[var(--r)] border px-3 py-2 text-sm"><span>Der Entwurf wurde zwischenzeitlich geändert.</span><Button size="sm" onClick={reloadDraft}>Serverstand laden</Button></div>}
            {selectedId && editorSchema && !draftQuery.isPending && !draftQuery.error && <FormBuilder key={`${selectedId}-${generation}`} ref={builderRef} schema={editorSchema} contractScope="section" onReadyChange={setReady} onChange={() => setDirty(true)} />}
          </div>
        </Card>
      </div>

      <ConfirmModal open={confirmPublish} onOpenChange={setConfirmPublish} busy={publishDraft.isPending} title="Abschnitt veröffentlichen?" description="Die geprüfte Fassung wird unveränderlich und kann anschließend in Formularen referenziert werden." confirmLabel="Version veröffentlichen" confirmIcon="upload" onConfirm={() => { if (!selectedId || !draftQuery.data) return; publishDraft.mutate({ sectionId: selectedId, expectedRevision: draftQuery.data.revision }, { onSuccess: (published) => { setConfirmPublish(false); setDirty(false); toast.success(`Version v${published.version.major}.${published.version.minor} veröffentlicht`); }, onError: (error) => { setConfirmPublish(false); setConflict(error instanceof ApiError && error.status === 409); toast.error('Veröffentlichung fehlgeschlagen', { description: message(error) }); } }); }} />
      <ConfirmModal open={confirmDiscard} onOpenChange={setConfirmDiscard} busy={discardDraft.isPending} destructive title="Entwurf verwerfen?" description="Die veröffentlichte Fassung bleibt erhalten." confirmLabel="Entwurf verwerfen" confirmIcon="delete" onConfirm={() => { if (!selectedId || !draftQuery.data) return; discardDraft.mutate({ sectionId: selectedId, expectedRevision: draftQuery.data.revision }, { onSuccess: () => { setConfirmDiscard(false); reloadDraft(); toast.success('Entwurf verworfen'); }, onError: (error) => { setConfirmDiscard(false); setConflict(error instanceof ApiError && error.status === 409); toast.error('Entwurf konnte nicht verworfen werden', { description: message(error) }); } }); }} />
    </PageContainer>
  );
}

function message(error: unknown): string | undefined {
  return error instanceof Error ? error.message : undefined;
}
