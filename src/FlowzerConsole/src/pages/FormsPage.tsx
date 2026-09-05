import { useEffect, useMemo, useRef, useState } from 'react';
import { toast } from 'sonner';

import { FormBuilder, type FormBuilderHandle } from '@/components/forms/FormBuilder';
import { FormRenderer } from '@/components/forms/FormRenderer';
import { Button } from '@/components/ui/Button';
import { Card, EmptyState } from '@/components/ui/Card';
import { toneSurface } from '@/components/ui/Chip';
import { SearchInput, TextInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { Segmented } from '@/components/ui/Segmented';
import { ErrorState, InlineSpinner, Skeleton } from '@/components/ui/States';
import { useForm, useForms, useSaveForm, useSaveFormMeta } from '@/lib/api/queries';
import { cn } from '@/lib/cn';
import { iconForLabel } from '@/lib/taskView';

type Mode = 'preview' | 'edit';

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
  const builderRef = useRef<FormBuilderHandle>(null);

  const formsQuery = useForms();
  const saveForm = useSaveForm();
  const saveMeta = useSaveFormMeta();

  const forms = useMemo(() => {
    const term = search.trim().toLowerCase();
    return (formsQuery.data ?? [])
      .filter((form) => term.length === 0 || form.name.toLowerCase().includes(term))
      .sort((a, b) => a.name.localeCompare(b.name, 'de'));
  }, [formsQuery.data, search]);

  // Beim ersten Laden das erste Formular auswählen, damit die Vorschau nicht leer bleibt.
  useEffect(() => {
    if (!selectedId && forms.length > 0) setSelectedId(forms[0]!.formId);
  }, [forms, selectedId]);

  const selected = forms.find((form) => form.formId === selectedId);
  const formQuery = useForm(selectedId ?? undefined);

  function handleCreate() {
    const name = newName.trim();
    if (!name) return;

    const formId = crypto.randomUUID();

    saveMeta.mutate(
      { formId, name },
      {
        onSuccess: () => {
          // Ein neues Formular braucht sofort eine erste Version, sonst liefert
          // `GET /form/{id}/latest` einen 404.
          saveForm.mutate(
            {
              formId,
              formData: JSON.stringify({ display: 'form', components: [] }, null, 2),
              version: { major: 0, minor: 1 },
            },
            {
              onSuccess: () => {
                toast.success(`Formular „${name}" angelegt`);
                setCreating(false);
                setNewName('');
                setSelectedId(formId);
                setMode('edit');
              },
              onError: (error) =>
                toast.error('Erste Version konnte nicht gespeichert werden', {
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

  function handleSaveSchema() {
    if (!selectedId) return;

    let schema: string;
    try {
      schema = builderRef.current!.getSchema();
    } catch (error) {
      toast.error('Das Schema konnte nicht gelesen werden', {
        description: error instanceof Error ? error.message : undefined,
      });
      return;
    }

    const current = formQuery.data?.version;
    const nextVersion = current ? { major: current.major, minor: current.minor + 1 } : { major: 0, minor: 1 };

    saveForm.mutate(
      { formId: selectedId, formData: schema, version: nextVersion },
      {
        onSuccess: (saved) =>
          toast.success(`Version v${saved.version.major}.${saved.version.minor} gespeichert`),
        onError: (error) =>
          toast.error('Speichern fehlgeschlagen', {
            description: error instanceof Error ? error.message : undefined,
          }),
      },
    );
  }

  return (
    <PageContainer>
      <PageHeader
        title="Formulare"
        description="Formulare, die Nutzende beim Bearbeiten von Aufgaben ausfüllen."
        actions={
          <Button variant="primary" icon="add" onClick={() => setCreating((open) => !open)}>
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
            loading={saveMeta.isPending || saveForm.isPending}
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
                  {formQuery.data?.version
                    ? `Version v${formQuery.data.version.major}.${formQuery.data.version.minor}`
                    : 'Live-Vorschau'}
                </div>
              </div>
            </div>

            <div className="flex items-center gap-2.5">
              {mode === 'edit' && selectedId && (
                <Button
                  variant="primary"
                  size="sm"
                  icon="save"
                  loading={saveForm.isPending}
                  onClick={handleSaveSchema}
                >
                  Speichern
                </Button>
              )}
              <Segmented options={MODE_OPTIONS} value={mode} onChange={setMode} aria-label="Ansicht" />
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

            {selectedId && formQuery.isPending && <InlineSpinner />}

            {selectedId && formQuery.error && (
              <ErrorState
                error={formQuery.error}
                title="Formular konnte nicht geladen werden"
                onRetry={() => void formQuery.refetch()}
              />
            )}

            {selectedId && formQuery.data && mode === 'preview' && (
              <FormRenderer key={`preview-${selectedId}`} schema={formQuery.data.formData ?? undefined} />
            )}

            {selectedId && formQuery.data && mode === 'edit' && (
              <FormBuilder key={`edit-${selectedId}`} ref={builderRef} schema={formQuery.data.formData ?? undefined} />
            )}
          </div>
        </Card>
      </div>
    </PageContainer>
  );
}
