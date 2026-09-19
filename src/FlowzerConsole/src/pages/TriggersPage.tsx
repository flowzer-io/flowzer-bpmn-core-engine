import { useMemo, useState, type ReactNode } from 'react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/Button';
import { Card, CardHeader, EmptyState, SectionLabel } from '@/components/ui/Card';
import { Chip } from '@/components/ui/Chip';
import { FieldLabel, SearchInput, TextInput } from '@/components/ui/Field';
import { ConfirmModal, Modal } from '@/components/ui/Modal';
import { PageContainer, PageHeader } from '@/components/ui/PageHeader';
import { ErrorState, LoadingRows } from '@/components/ui/States';
import { API_BASE_URL } from '@/lib/api/client';
import {
  useCreateInboundTrigger,
  useDefinitions,
  useDeleteInboundTrigger,
  useInboundTriggers,
  useRotateInboundTriggerSecret,
  useUpdateInboundTrigger,
} from '@/lib/api/queries';
import type {
  InboundTriggerDto,
  InboundTriggerKind,
  InboundTriggerSecretDto,
  InboundTriggerVariablesMode,
} from '@/lib/api/types';
import { cn } from '@/lib/cn';
import { formatNumber, formatTimestamp } from '@/lib/format';
import { useCan } from '@/stores/session';

/**
 * Bearbeitungsstand eines Auslösers.
 *
 * Die Art steht nur beim Anlegen zur Wahl: `PUT` ignoriert sie, und ein Feld, das
 * beim Speichern wirkungslos bleibt, verspricht der bedienenden Person etwas Falsches.
 */
interface EditorState {
  triggerId: string | null;
  name: string;
  kind: InboundTriggerKind;
  definitionId: string;
  messageName: string;
  correlationKeyPath: string;
  variablesMode: InboundTriggerVariablesMode;
  /** Kommagetrennte Liste der erlaubten obersten Felder. */
  allowedFields: string;
  enabled: boolean;
}

/**
 * Ein neuer Auslöser beginnt datensparsam: Feldmodus mit leerer Liste heißt „gar keine
 * Variablen". Wer den ganzen Inhalt übernehmen will, wählt das ausdrücklich — nicht
 * umgekehrt.
 */
const EMPTY_EDITOR: EditorState = {
  triggerId: null,
  name: '',
  kind: 'start',
  definitionId: '',
  messageName: '',
  correlationKeyPath: '',
  variablesMode: 'fields',
  allowedFields: '',
  enabled: true,
};

/** Einmalig angezeigtes Geheimnis — es lebt nur in diesem Zustand, nie im Query-Cache. */
interface SecretReveal {
  title: string;
  triggerName: string;
  key: string;
  secret: string;
}

type Confirmation =
  | { kind: 'disable'; trigger: InboundTriggerDto }
  | { kind: 'delete'; trigger: InboundTriggerDto }
  | { kind: 'rotate'; trigger: InboundTriggerDto };

/** Verwaltung eingehender Webhook-Auslöser mit einmaliger Anzeige des Geheimnisses. */
export function TriggersPage() {
  const mayManage = useCan()('operator');
  const triggersQuery = useInboundTriggers({ enabled: mayManage });
  const definitionsQuery = useDefinitions({ enabled: mayManage });
  const createTrigger = useCreateInboundTrigger();
  const updateTrigger = useUpdateInboundTrigger();
  const rotateSecret = useRotateInboundTriggerSecret();
  const deleteTrigger = useDeleteInboundTrigger();

  const [search, setSearch] = useState('');
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [reveal, setReveal] = useState<SecretReveal | null>(null);
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null);

  const definitionName = useMemo(() => {
    const byId = new Map<string, string>();
    for (const definition of definitionsQuery.data ?? []) {
      byId.set(definition.definitionId, definition.name);
    }
    return byId;
  }, [definitionsQuery.data]);

  const triggers = useMemo(() => {
    const term = search.trim().toLocaleLowerCase('de');
    return (triggersQuery.data ?? [])
      .filter((item) =>
        term.length === 0 ||
        item.name.toLocaleLowerCase('de').includes(term) ||
        item.key.toLocaleLowerCase('de').includes(term))
      .sort((left, right) => left.name.localeCompare(right.name, 'de'));
  }, [search, triggersQuery.data]);

  if (!mayManage) {
    return (
      <PageContainer>
        <EmptyState
          icon="lock"
          title="Keine Betriebsberechtigung"
          description="Eingehende Auslöser dürfen nur Personen mit der Betriebsrolle einsehen und ändern."
        />
      </PageContainer>
    );
  }

  function set(patch: Partial<EditorState>) {
    setEditor((current) => (current ? { ...current, ...patch } : current));
  }

  function save() {
    if (!editor) return;

    const name = editor.name.trim();
    if (!name) {
      toast.error('Ein Name ist erforderlich.');
      return;
    }

    const target = editor.kind === 'start'
      ? { definitionId: editor.definitionId.trim() || undefined }
      : {
        messageName: editor.messageName.trim() || undefined,
        correlationKeyPath: editor.correlationKeyPath.trim() || undefined,
      };

    if (editor.kind === 'start' && !target.definitionId) {
      toast.error('Bitte einen Workflow auswählen.');
      return;
    }
    if (editor.kind === 'message' && (!target.messageName || !target.correlationKeyPath)) {
      toast.error('Nachrichtenname und Korrelationspfad sind erforderlich.');
      return;
    }

    // Eine leere Liste ist gültig und bedeutet „keine Variablen" — der datensparsame
    // Standard. Ein Zwang zu mindestens einem Feld nähme genau diese Wahl weg.
    const allowedFields = editor.variablesMode === 'fields' ? splitFields(editor.allowedFields) : [];

    if (editor.triggerId === null) {
      createTrigger.mutate(
        { name, kind: editor.kind, ...target, variablesMode: editor.variablesMode, allowedFields },
        {
          onSuccess: (created: InboundTriggerSecretDto) => {
            setEditor(null);
            showSecret('Auslöser angelegt', created);
            toast.success(`Auslöser „${created.trigger.name}“ angelegt`);
          },
          onError: (error: unknown) => toast.error('Auslöser konnte nicht angelegt werden', {
            description: message(error),
          }),
        },
      );
      return;
    }

    updateTrigger.mutate(
      {
        triggerId: editor.triggerId,
        input: {
          name,
          enabled: editor.enabled,
          ...target,
          variablesMode: editor.variablesMode,
          allowedFields,
        },
      },
      {
        onSuccess: (updated: InboundTriggerDto) => {
          setEditor(null);
          toast.success(`Auslöser „${updated.name}“ gespeichert`);
        },
        onError: (error: unknown) => toast.error('Auslöser konnte nicht gespeichert werden', {
          description: message(error),
        }),
      },
    );
  }

  function showSecret(title: string, result: InboundTriggerSecretDto) {
    setReveal({
      title,
      triggerName: result.trigger.name,
      key: result.trigger.key,
      secret: result.secret,
    });
  }

  function confirmDisable(trigger: InboundTriggerDto) {
    updateTrigger.mutate(
      {
        triggerId: trigger.id,
        input: {
          name: trigger.name,
          enabled: false,
          definitionId: trigger.definitionId ?? undefined,
          messageName: trigger.messageName ?? undefined,
          correlationKeyPath: trigger.correlationKeyPath ?? undefined,
          variablesMode: trigger.variablesMode,
          allowedFields: trigger.allowedFields,
        },
      },
      {
        onSuccess: () => {
          setConfirmation(null);
          toast.success(`Auslöser „${trigger.name}“ deaktiviert`);
        },
        onError: (error: unknown) => toast.error('Auslöser konnte nicht deaktiviert werden', {
          description: message(error),
        }),
      },
    );
  }

  function enable(trigger: InboundTriggerDto) {
    updateTrigger.mutate(
      {
        triggerId: trigger.id,
        input: {
          name: trigger.name,
          enabled: true,
          definitionId: trigger.definitionId ?? undefined,
          messageName: trigger.messageName ?? undefined,
          correlationKeyPath: trigger.correlationKeyPath ?? undefined,
          variablesMode: trigger.variablesMode,
          allowedFields: trigger.allowedFields,
        },
      },
      {
        onSuccess: () => toast.success(`Auslöser „${trigger.name}“ aktiviert`),
        onError: (error: unknown) => toast.error('Auslöser konnte nicht aktiviert werden', {
          description: message(error),
        }),
      },
    );
  }

  function confirmRotate(trigger: InboundTriggerDto) {
    rotateSecret.mutate(trigger.id, {
      onSuccess: (rotated: InboundTriggerSecretDto) => {
        setConfirmation(null);
        showSecret('Geheimnis erneuert', rotated);
      },
      onError: (error: unknown) => toast.error('Geheimnis konnte nicht erneuert werden', {
        description: message(error),
      }),
    });
  }

  function confirmDelete(trigger: InboundTriggerDto) {
    deleteTrigger.mutate(trigger.id, {
      onSuccess: () => {
        setConfirmation(null);
        toast.success(`Auslöser „${trigger.name}“ gelöscht`);
      },
      onError: (error: unknown) => toast.error('Auslöser konnte nicht gelöscht werden', {
        description: message(error),
      }),
    });
  }

  return (
    <PageContainer>
      <PageHeader
        eyebrow="Betrieb"
        title="Auslöser"
        description="Eingehende Webhooks starten Workflows oder stellen Nachrichten zu. Jeder Aufruf wird mit HMAC-SHA256 signiert; das Geheimnis wird nur einmal angezeigt."
        actions={
          <Button variant="primary" icon="add" onClick={() => setEditor({ ...EMPTY_EDITOR })}>
            Auslöser anlegen
          </Button>
        }
      />

      <Card>
        <CardHeader title="Eingehende Auslöser" icon="bolt" />
        <div className="border-border border-b p-3">
          <SearchInput
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Auslöser suchen"
            aria-label="Auslöser suchen"
          />
        </div>

        {triggersQuery.isPending ? (
          <LoadingRows />
        ) : triggersQuery.error ? (
          <ErrorState
            error={triggersQuery.error}
            onRetry={() => void triggersQuery.refetch()}
            className="py-10"
          />
        ) : triggers.length === 0 ? (
          <EmptyState
            icon="bolt"
            title="Noch kein Auslöser"
            description="Lege einen Auslöser an, um einen Workflow oder eine Nachricht von außen anzustoßen."
          />
        ) : (
          <div className="divide-border divide-y">
            {triggers.map((trigger) => (
              <TriggerRow
                key={trigger.id}
                trigger={trigger}
                targetLabel={targetLabel(trigger, definitionName)}
                busy={updateTrigger.isPending || rotateSecret.isPending || deleteTrigger.isPending}
                onEdit={() => setEditor(toEditor(trigger))}
                onRotate={() => setConfirmation({ kind: 'rotate', trigger })}
                onToggle={() => (trigger.enabled
                  ? setConfirmation({ kind: 'disable', trigger })
                  : enable(trigger))}
                onDelete={() => setConfirmation({ kind: 'delete', trigger })}
              />
            ))}
          </div>
        )}
      </Card>

      <Modal
        open={editor !== null}
        onOpenChange={(open) => !open && setEditor(null)}
        title={editor?.triggerId === null ? 'Auslöser anlegen' : 'Auslöser bearbeiten'}
        description={editor?.triggerId === null
          ? 'Nach dem Anlegen werden Schlüssel und Geheimnis genau einmal angezeigt.'
          : 'Die Art eines Auslösers steht fest und lässt sich nachträglich nicht ändern.'}
        icon="bolt"
        className="w-[min(640px,calc(100vw-32px))]"
        footer={
          <>
            <Button size="sm" onClick={() => setEditor(null)}>Abbrechen</Button>
            <Button
              size="sm"
              variant="primary"
              icon="save"
              loading={createTrigger.isPending || updateTrigger.isPending}
              onClick={save}
            >
              {editor?.triggerId === null ? 'Anlegen' : 'Speichern'}
            </Button>
          </>
        }
      >
        {editor && (
          <div className="grid gap-5 py-2 sm:grid-cols-2">
            <Labelled id="trigger-name" label="Name">
              <TextInput
                id="trigger-name"
                value={editor.name}
                onChange={(event) => set({ name: event.target.value })}
                placeholder="z. B. Bestellung aus dem Shop"
              />
            </Labelled>

            {editor.triggerId === null ? (
              <Labelled id="trigger-kind" label="Art">
                <select
                  id="trigger-kind"
                  className={selectClass}
                  value={editor.kind}
                  onChange={(event) => set({ kind: event.target.value as InboundTriggerKind })}
                >
                  <option value="start">Workflow starten</option>
                  <option value="message">Nachricht zustellen</option>
                </select>
              </Labelled>
            ) : (
              <div>
                <FieldLabel>Art</FieldLabel>
                <p className="py-2.5 text-[13.5px]">
                  {kindLabel(editor.kind)}
                  <span className="text-muted"> · nicht änderbar</span>
                </p>
              </div>
            )}

            {editor.kind === 'start' ? (
              <Labelled id="trigger-definition" label="Workflow" className="sm:col-span-2">
                <select
                  id="trigger-definition"
                  className={selectClass}
                  value={editor.definitionId}
                  onChange={(event) => set({ definitionId: event.target.value })}
                >
                  <option value="">Bitte auswählen …</option>
                  {(definitionsQuery.data ?? []).map((definition) => (
                    <option key={definition.definitionId} value={definition.definitionId}>
                      {definition.name}
                    </option>
                  ))}
                </select>
              </Labelled>
            ) : (
              <>
                <Labelled id="trigger-message" label="Nachrichtenname">
                  <TextInput
                    id="trigger-message"
                    value={editor.messageName}
                    onChange={(event) => set({ messageName: event.target.value })}
                    placeholder="z. B. Zahlungseingang"
                    autoComplete="off"
                  />
                </Labelled>
                <Labelled id="trigger-correlation" label="Korrelationspfad">
                  <TextInput
                    id="trigger-correlation"
                    value={editor.correlationKeyPath}
                    onChange={(event) => set({ correlationKeyPath: event.target.value })}
                    placeholder="z. B. order.id"
                    autoComplete="off"
                    spellCheck={false}
                  />
                </Labelled>
              </>
            )}

            <Labelled id="trigger-variables" label="Variablen">
              <select
                id="trigger-variables"
                className={selectClass}
                value={editor.variablesMode}
                onChange={(event) =>
                  set({ variablesMode: event.target.value as InboundTriggerVariablesMode })}
              >
                <option value="body">Ganzen Inhalt übernehmen</option>
                <option value="fields">Nur erlaubte Felder übernehmen</option>
              </select>
            </Labelled>

            {editor.variablesMode === 'fields' && (
              <Labelled id="trigger-fields" label="Erlaubte Felder">
                <TextInput
                  id="trigger-fields"
                  value={editor.allowedFields}
                  onChange={(event) => set({ allowedFields: event.target.value })}
                  placeholder="order, customer"
                  autoComplete="off"
                  spellCheck={false}
                />
                <p className="text-muted mt-1.5 text-xs">
                  Kommagetrennte oberste Felder des Inhalts. Alles andere wird verworfen; leer
                  heißt, dass der Workflow keine Variablen aus dem Aufruf bekommt.
                </p>
              </Labelled>
            )}

            {editor.triggerId !== null && (
              <label className="flex cursor-pointer items-center gap-2 text-[13.5px] sm:col-span-2">
                <input
                  type="checkbox"
                  checked={editor.enabled}
                  onChange={(event) => set({ enabled: event.target.checked })}
                />
                Aktiv — der Auslöser nimmt Aufrufe entgegen
              </label>
            )}
          </div>
        )}
      </Modal>

      <Modal
        open={reveal !== null}
        onOpenChange={(open) => !open && setReveal(null)}
        title={reveal?.title ?? ''}
        description={`Schlüssel und Geheimnis von „${reveal?.triggerName ?? ''}“ werden genau einmal angezeigt. Das Geheimnis ist danach nicht mehr abrufbar — bewahre es jetzt sicher auf.`}
        icon="lock"
        className="w-[min(680px,calc(100vw-32px))]"
        footer={<Button size="sm" variant="primary" onClick={() => setReveal(null)}>Fertig</Button>}
      >
        {reveal && (
          <div className="grid gap-4 py-2">
            <CopyBlock label="Aufrufadresse" value={`POST ${triggerUrl(reveal.key)}`} copy={triggerUrl(reveal.key)} />
            <CopyBlock label="Schlüssel" value={reveal.key} copy={reveal.key} />
            <CopyBlock label="Geheimnis" value={reveal.secret} copy={reveal.secret} />
            <CopyBlock
              label="curl-Beispiel"
              value={curlExample(triggerUrl(reveal.key), reveal.secret)}
              copy={curlExample(triggerUrl(reveal.key), reveal.secret)}
            />
          </div>
        )}
      </Modal>

      <ConfirmModal
        open={confirmation?.kind === 'disable'}
        onOpenChange={(open) => !open && setConfirmation(null)}
        title="Auslöser deaktivieren?"
        description={`„${confirmation?.trigger.name ?? ''}“ nimmt danach keine Aufrufe mehr entgegen. Die Adresse bleibt bestehen und antwortet mit einer Ablehnung.`}
        confirmLabel="Deaktivieren"
        busy={updateTrigger.isPending}
        onConfirm={() => confirmation?.kind === 'disable' && confirmDisable(confirmation.trigger)}
      />

      <ConfirmModal
        open={confirmation?.kind === 'rotate'}
        onOpenChange={(open) => !open && setConfirmation(null)}
        title="Geheimnis erneuern?"
        description={`Das bisherige Geheimnis von „${confirmation?.trigger.name ?? ''}“ verliert sofort seine Gültigkeit. Bestehende Aufrufer müssen das neue Geheimnis übernehmen.`}
        confirmLabel="Erneuern"
        confirmIcon="refresh"
        busy={rotateSecret.isPending}
        onConfirm={() => confirmation?.kind === 'rotate' && confirmRotate(confirmation.trigger)}
      />

      <ConfirmModal
        open={confirmation?.kind === 'delete'}
        onOpenChange={(open) => !open && setConfirmation(null)}
        title="Auslöser löschen?"
        description={`„${confirmation?.trigger.name ?? ''}“ wird endgültig entfernt.`}
        confirmLabel="Löschen"
        confirmIcon="delete"
        destructive
        busy={deleteTrigger.isPending}
        onConfirm={() => confirmation?.kind === 'delete' && confirmDelete(confirmation.trigger)}
      >
        <p className="text-[13.5px]">
          Die Adresse <span className="font-mono">POST /trigger/{confirmation?.trigger.key ?? ''}</span>{' '}
          ist danach nicht mehr erreichbar. Aufrufer, die sie noch verwenden, laufen ins Leere.
        </p>
      </ConfirmModal>
    </PageContainer>
  );
}

interface TriggerRowProps {
  trigger: InboundTriggerDto;
  targetLabel: string;
  busy: boolean;
  onEdit: () => void;
  onRotate: () => void;
  onToggle: () => void;
  onDelete: () => void;
}

function TriggerRow({ trigger, targetLabel, busy, onEdit, onRotate, onToggle, onDelete }: TriggerRowProps) {
  const address = `POST ${triggerUrl(trigger.key)}`;

  return (
    <div className="grid gap-3 px-4 py-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-semibold">{trigger.name}</span>
        <Chip tone="accent">{kindLabel(trigger.kind)}</Chip>
        <Chip tone={trigger.enabled ? 'done' : 'muted'}>{trigger.enabled ? 'Aktiv' : 'Inaktiv'}</Chip>
        <span className="flex-1" />
        <Button size="sm" icon="edit" onClick={onEdit} disabled={busy}>Bearbeiten</Button>
        <Button size="sm" icon="refresh" onClick={onRotate} disabled={busy}>Geheimnis erneuern</Button>
        <Button size="sm" onClick={onToggle} disabled={busy}>
          {trigger.enabled ? 'Deaktivieren' : 'Aktivieren'}
        </Button>
        <Button size="sm" variant="danger" icon="delete" onClick={onDelete} disabled={busy}>
          Löschen
        </Button>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Detail label="Ziel">{targetLabel}</Detail>
        <Detail label="Schlüssel">
          <span className="flex items-center gap-1.5">
            <span className="font-mono text-[12.5px] break-all">{trigger.key}</span>
            <CopyButton value={trigger.key} label={`Schlüssel kopieren: ${trigger.name}`} />
          </span>
        </Detail>
        <Detail label="Nutzung">{usageLabel(trigger)}</Detail>
        <Detail label="Letzter Fehler">
          {trigger.lastFailureReason
            ? `${failureLabel(trigger.lastFailureReason)} · ${formatTimestamp(trigger.lastFailureAt)}`
            : 'Kein Fehler seit dem Anlegen'}
        </Detail>
      </div>

      <div className="bg-surface-2 flex items-center gap-2 rounded-[var(--r-sm)] px-3 py-2">
        <span className="min-w-0 flex-1 font-mono text-[12.5px] break-all">{address}</span>
        <CopyButton value={triggerUrl(trigger.key)} label={`Aufrufadresse kopieren: ${trigger.name}`} />
      </div>
    </div>
  );
}

function Detail({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <SectionLabel>{label}</SectionLabel>
      <div className="mt-1 text-[13px]">{children}</div>
    </div>
  );
}

function Labelled({
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

function CopyBlock({ label, value, copy }: { label: string; value: string; copy: string }) {
  return (
    <div>
      <div className="mb-1.5 flex items-center gap-2">
        <SectionLabel className="flex-1">{label}</SectionLabel>
        <CopyButton value={copy} label={`${label} kopieren`} />
      </div>
      <pre className="bg-surface-2 border-border overflow-auto rounded-[var(--r-sm)] border p-3 font-mono text-[12.5px] whitespace-pre-wrap">
        {value}
      </pre>
    </div>
  );
}

function CopyButton({ value, label }: { value: string; label: string }) {
  return (
    <Button size="sm" variant="ghost" icon="content_copy" aria-label={label} onClick={() => void copyToClipboard(value, label)} />
  );
}

/** Legt einen Wert in die Zwischenablage; ohne Berechtigung bleibt es bei einem Hinweis. */
async function copyToClipboard(value: string, label: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value);
    toast.success('In die Zwischenablage kopiert');
  } catch {
    toast.error(`„${label}“ war nicht möglich. Bitte den Wert von Hand markieren.`);
  }
}

/**
 * Vollständige Aufrufadresse des Auslösers.
 *
 * Die Basisadresse der API kann relativ sein (`/api` im Entwicklungsbetrieb). Für ein
 * kopierbares curl-Beispiel muss sie absolut werden, sonst taugt der Schnipsel nur im Browser.
 */
function triggerUrl(key: string): string {
  const path = `${API_BASE_URL}/trigger/${key}`;
  try {
    return new URL(path, window.location.origin).toString();
  } catch {
    return path;
  }
}

/** Fertiger Shell-Schnipsel samt Signaturberechnung. */
function curlExample(url: string, secret: string): string {
  return [
    '# Zeitstempel und Signatur bilden',
    'BODY=\'{"order":{"id":"4711"}}\'',
    'TS=$(date +%s)',
    `SECRET='${secret}'`,
    'SIG=$(printf \'%s.%s\' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -r | cut -d\' \' -f1)',
    `curl -X POST '${url}' \\`,
    '  -H \'Content-Type: application/json\' \\',
    '  -H "X-Flowzer-Timestamp: $TS" \\',
    '  -H "X-Flowzer-Signature: sha256=$SIG" \\',
    '  -d "$BODY"',
  ].join('\n');
}

function toEditor(trigger: InboundTriggerDto): EditorState {
  return {
    triggerId: trigger.id,
    name: trigger.name,
    kind: trigger.kind,
    definitionId: trigger.definitionId ?? '',
    messageName: trigger.messageName ?? '',
    correlationKeyPath: trigger.correlationKeyPath ?? '',
    variablesMode: trigger.variablesMode,
    allowedFields: trigger.allowedFields.join(', '),
    enabled: trigger.enabled,
  };
}

function splitFields(value: string): string[] {
  return value
    .split(',')
    .map((field) => field.trim())
    .filter((field) => field.length > 0);
}

function kindLabel(kind: InboundTriggerKind): string {
  return kind === 'start' ? 'Workflow starten' : 'Nachricht zustellen';
}

function targetLabel(trigger: InboundTriggerDto, definitionName: Map<string, string>): string {
  if (trigger.kind === 'start') {
    const id = trigger.definitionId ?? '';
    // Der Katalog kann noch laden oder den Workflow nicht mehr kennen — dann bleibt
    // wenigstens die Kennung stehen, statt eine leere Spalte zu zeigen.
    return definitionName.get(id) ?? (id || '—');
  }
  const parts = [trigger.messageName, trigger.correlationKeyPath].filter(Boolean);
  return parts.length > 0 ? parts.join(' · ') : '—';
}

function usageLabel(trigger: InboundTriggerDto): string {
  if (trigger.useCount === 0) return 'Noch nicht aufgerufen';
  return `${formatNumber(trigger.useCount)} Aufrufe · zuletzt ${formatTimestamp(trigger.lastUsedAt)}`;
}

/** Übersetzt die kurzen technischen Gründe der API in verständliches Deutsch. */
function failureLabel(reason: string): string {
  switch (reason) {
    case 'disabled':
      return 'Auslöser war deaktiviert';
    case 'timestamp':
      return 'Zeitstempel zu alt oder zu weit in der Zukunft';
    case 'signature':
      return 'Signatur passte nicht zum Geheimnis';
    case 'payload':
      return 'Inhalt war nicht verwertbar';
    case 'correlation-key':
      return 'Korrelationswert im Inhalt nicht gefunden';
    case 'not-deployed':
      return 'Workflow ist nicht veröffentlicht';
    default:
      return reason;
  }
}

const selectClass = cn(
  'bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5',
  'text-[13.5px] outline-none focus:border-accent disabled:opacity-55',
);

function message(error: unknown): string {
  return error instanceof Error ? error.message : 'Unbekannter Fehler.';
}
