import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/Button';
import { Card, CardHeader, EmptyState } from '@/components/ui/Card';
import { Chip } from '@/components/ui/Chip';
import { FieldLabel, SearchInput, TextInput } from '@/components/ui/Field';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { ErrorState, LoadingRows } from '@/components/ui/States';
import { ApiError } from '@/lib/api/client';
import {
  useAiConnections,
  useCreateAiConnection,
  useSetAiConnectionEnabled,
  useUpdateAiConnection,
} from '@/lib/api/queries';
import type {
  AiConnectionDto,
  AiProcessingLocation,
  AiProviderKind,
} from '@/lib/api/types';
import { cn } from '@/lib/cn';
import { formatTimestamp } from '@/lib/format';
import { useCan } from '@/stores/session';

type Selection = string | 'new' | null;

interface EditorState {
  name: string;
  provider: AiProviderKind;
  location: AiProcessingLocation;
  baseAddress: string;
  defaultModel: string;
  secretReference: string;
}

const EMPTY_EDITOR: EditorState = {
  name: '',
  provider: 'OpenAi',
  location: 'Cloud',
  baseAddress: '',
  defaultModel: '',
  secretReference: '',
};

/** Verwaltung sicherer KI-Verbindungsmetadaten ohne Rueckgabe von Secret-Referenzen. */
export function AiConnectionsPage() {
  const mayManage = useCan()('aiConnectionManage');
  const connectionsQuery = useAiConnections({ enabled: mayManage });
  const createConnection = useCreateAiConnection();
  const updateConnection = useUpdateAiConnection();
  const setEnabled = useSetAiConnectionEnabled();
  const [selection, setSelection] = useState<Selection>(null);
  const [search, setSearch] = useState('');
  const [editor, setEditor] = useState<EditorState>(EMPTY_EDITOR);

  const connections = useMemo(() => {
    const term = search.trim().toLocaleLowerCase('de');
    return (connectionsQuery.data ?? [])
      .filter((item) => term.length === 0 || item.name.toLocaleLowerCase('de').includes(term))
      .sort((left, right) => left.name.localeCompare(right.name, 'de'));
  }, [connectionsQuery.data, search]);
  const selected = selection && selection !== 'new'
    ? connectionsQuery.data?.find((item) => item.id === selection)
    : undefined;

  useEffect(() => {
    if (selection === null && connections.length > 0) setSelection(connections[0]!.id);
  }, [connections, selection]);

  useEffect(() => {
    if (selection === 'new') {
      setEditor(EMPTY_EDITOR);
      return;
    }
    if (!selected) return;
    setEditor({
      name: selected.name,
      provider: selected.provider,
      location: selected.location,
      baseAddress: selected.baseAddress ?? '',
      defaultModel: selected.defaultModel,
      // Absichtlich immer leer: Die API liefert die Referenz nicht, und die Console
      // rekonstruiert sie auch nicht aus Namen oder Status.
      secretReference: '',
    });
  }, [selected, selection]);

  if (!mayManage) {
    return (
      <PageContainer>
        <EmptyState
          icon="lock"
          title="Keine Verwaltungsberechtigung"
          description="KI-Verbindungen dürfen nur Personen mit der dafür eingerichteten Verwaltungsrolle ändern."
        />
      </PageContainer>
    );
  }

  function set(patch: Partial<EditorState>) {
    setEditor((current) => ({ ...current, ...patch }));
  }

  function changeProvider(provider: AiProviderKind) {
    set({
      provider,
      ...(provider === 'OpenAiCompatible'
        ? {}
        : { location: 'Cloud' as const, baseAddress: '' }),
    });
  }

  function save() {
    const common = {
      name: editor.name.trim(),
      provider: editor.provider,
      location: editor.location,
      baseAddress: editor.baseAddress.trim() || undefined,
      defaultModel: editor.defaultModel.trim(),
    };
    if (!common.name || !common.defaultModel) {
      toast.error('Name und Standardmodell sind erforderlich.');
      return;
    }

    if (selection === 'new') {
      if (!editor.secretReference.trim()) {
        toast.error('Für eine neue Verbindung ist eine Secret-Referenz erforderlich.');
        return;
      }
      createConnection.mutate(
        { ...common, secretReference: editor.secretReference.trim() },
        {
          onSuccess: (created) => {
            setSelection(created.id);
            toast.success(`Verbindung „${created.name}“ angelegt`);
          },
          onError: (error) => toast.error('Verbindung konnte nicht angelegt werden', { description: message(error) }),
        },
      );
      return;
    }

    if (!selected) return;
    updateConnection.mutate(
      {
        connectionId: selected.id,
        input: {
          ...common,
          expectedRevision: selected.revision,
          secretReference: editor.secretReference.trim() || undefined,
        },
      },
      {
        onSuccess: (updated) => {
          setSelection(updated.id);
          setEditor((current) => ({ ...current, secretReference: '' }));
          toast.success(`Verbindung „${updated.name}“ gespeichert`);
        },
        onError: (error) => toast.error('Verbindung konnte nicht gespeichert werden', { description: message(error) }),
      },
    );
  }

  function toggleEnabled(connection: AiConnectionDto) {
    setEnabled.mutate(
      {
        connectionId: connection.id,
        expectedRevision: connection.revision,
        enabled: !connection.enabled,
      },
      {
        onSuccess: (updated) => toast.success(
          updated.enabled ? 'Verbindung aktiviert' : 'Verbindung deaktiviert',
        ),
        onError: (error) => toast.error('Status konnte nicht geändert werden', { description: message(error) }),
      },
    );
  }

  return (
    <PageContainer>
      <PageHeader
        eyebrow="Administration"
        title="KI-Verbindungen"
        description="Provider, Ziel und Secret-Referenz getrennt verwalten. Geheime Werte bleiben im serverseitigen Secret-Store."
        actions={
          <Button variant="primary" icon="add" onClick={() => setSelection('new')}>
            Verbindung anlegen
          </Button>
        }
      />

      <div className="grid min-h-[520px] gap-4 lg:grid-cols-[minmax(260px,0.75fr)_minmax(420px,1.25fr)]">
        <Card>
          <CardHeader title="Verbindungen" icon="smart_toy" />
          <div className="border-border border-b p-3">
            <SearchInput
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Verbindung suchen"
              aria-label="KI-Verbindung suchen"
            />
          </div>
          {connectionsQuery.isPending ? (
            <LoadingRows />
          ) : connectionsQuery.error ? (
            <ErrorState
              error={connectionsQuery.error}
              onRetry={() => void connectionsQuery.refetch()}
              className="py-10"
            />
          ) : connections.length === 0 ? (
            <EmptyState
              icon="smart_toy"
              title="Noch keine Verbindung"
              description="Lege zuerst nur die Metadaten und eine serverseitige Secret-Referenz an."
            />
          ) : (
            <div className="divide-border divide-y">
              {connections.map((connection) => (
                <button
                  type="button"
                  key={connection.id}
                  onClick={() => setSelection(connection.id)}
                  className={cn(
                    'hover:bg-surface-2 flex w-full items-center gap-3 px-4 py-3 text-left',
                    selection === connection.id && 'bg-surface-2',
                  )}
                >
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-semibold">{connection.name}</span>
                    <span className="text-muted mt-0.5 block truncate text-xs">
                      {providerLabel(connection.provider)} · {locationLabel(connection.location)}
                    </span>
                  </span>
                  <Chip tone={connection.ready ? 'done' : connection.enabled ? 'wait' : 'muted'}>
                    {connection.ready ? 'Bereit' : connection.enabled ? 'Secret fehlt' : 'Inaktiv'}
                  </Chip>
                </button>
              ))}
            </div>
          )}
        </Card>

        <Card>
          <CardHeader
            title={selection === 'new' ? 'Neue Verbindung' : selected?.name ?? 'Verbindung auswählen'}
            icon="edit"
            actions={selected && (
              <Button
                size="sm"
                variant={selected.enabled ? 'danger' : 'secondary'}
                loading={setEnabled.isPending}
                onClick={() => toggleEnabled(selected)}
              >
                {selected.enabled ? 'Deaktivieren' : 'Aktivieren'}
              </Button>
            )}
          />
          {selection === null ? (
            <EmptyState icon="touch_app" title="Verbindung auswählen" />
          ) : (
            <div className="grid gap-5 p-5 sm:grid-cols-2">
              <Field id="ai-name" label="Name">
                <TextInput
                  id="ai-name"
                  value={editor.name}
                  onChange={(event) => set({ name: event.target.value })}
                  placeholder="z. B. OpenAI Produktion"
                />
              </Field>

              <Field id="ai-provider" label="Provider">
                <select
                  id="ai-provider"
                  className={selectClass}
                  value={editor.provider}
                  onChange={(event) => changeProvider(event.target.value as AiProviderKind)}
                >
                  <option value="OpenAi">OpenAI</option>
                  <option value="OpenAiCompatible">OpenAI-kompatibel</option>
                  <option value="Anthropic">Anthropic</option>
                </select>
              </Field>

              <Field id="ai-location" label="Verarbeitung">
                <select
                  id="ai-location"
                  className={selectClass}
                  value={editor.location}
                  disabled={editor.provider !== 'OpenAiCompatible'}
                  onChange={(event) => set({ location: event.target.value as AiProcessingLocation })}
                >
                  <option value="Cloud">Cloud</option>
                  <option value="Local">Lokal</option>
                </select>
              </Field>

              <Field id="ai-model" label="Standardmodell">
                <TextInput
                  id="ai-model"
                  value={editor.defaultModel}
                  onChange={(event) => set({ defaultModel: event.target.value })}
                  placeholder="Modellkennung des Providers"
                  autoComplete="off"
                />
              </Field>

              {editor.provider === 'OpenAiCompatible' && (
                <Field id="ai-endpoint" label="Basisadresse" className="sm:col-span-2">
                  <TextInput
                    id="ai-endpoint"
                    type="url"
                    value={editor.baseAddress}
                    onChange={(event) => set({ baseAddress: event.target.value })}
                    placeholder={editor.location === 'Local'
                      ? 'http://127.0.0.1:11434/v1'
                      : 'https://models.example.org/v1'}
                    autoComplete="off"
                  />
                </Field>
              )}

              <Field id="ai-secret" label="Secret-Referenz" className="sm:col-span-2">
                <TextInput
                  id="ai-secret"
                  value={editor.secretReference}
                  onChange={(event) => set({ secretReference: event.target.value })}
                  placeholder={selection === 'new'
                    ? 'env:FLOWZER_AI_OPENAI'
                    : 'Leer lassen, um die vorhandene Referenz beizubehalten'}
                  autoComplete="off"
                  spellCheck={false}
                />
                <p className="text-muted mt-1.5 text-xs">
                  Nur die Referenz wird gespeichert. Der geheime Wert wird weder geladen noch im Browser angezeigt.
                </p>
              </Field>

              {selected && (
                <div className="text-muted sm:col-span-2 text-xs">
                  Revision {selected.revision} · zuletzt geändert {formatTimestamp(selected.updatedAtUtc)}
                </div>
              )}

              <div className="sm:col-span-2 flex justify-end">
                <Button
                  variant="primary"
                  icon="save"
                  loading={createConnection.isPending || updateConnection.isPending}
                  onClick={save}
                >
                  {selection === 'new' ? 'Anlegen' : 'Speichern'}
                </Button>
              </div>
            </div>
          )}
        </Card>
      </div>
    </PageContainer>
  );
}

function Field({
  id,
  label,
  className,
  children,
}: {
  id: string;
  label: string;
  className?: string;
  children: ReactNode;
}) {
  return (
    <div className={className}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      {children}
    </div>
  );
}

const selectClass = cn(
  'bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5',
  'text-[13.5px] outline-none focus:border-accent disabled:opacity-55',
);

function providerLabel(provider: AiProviderKind): string {
  if (provider === 'OpenAi') return 'OpenAI';
  if (provider === 'Anthropic') return 'Anthropic';
  return 'OpenAI-kompatibel';
}

function locationLabel(location: AiProcessingLocation): string {
  return location === 'Cloud' ? 'Cloud' : 'Lokal';
}

function message(error: unknown): string {
  if (error instanceof ApiError && error.status === 409) {
    return 'Die Verbindung wurde zwischenzeitlich geändert. Lade die Liste neu und wiederhole die Änderung.';
  }
  return error instanceof Error ? error.message : 'Unbekannter Fehler.';
}
