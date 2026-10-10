import { useMemo, useRef, useState } from 'react';
import { FormRenderer, type FormRendererHandle } from '@/components/forms/FormRenderer';
import { FormValidationErrors } from '@/components/forms/FormValidationErrors';
import { InlineSpinner } from '@/components/ui/States';
import type { BoundDirectorySubjectAdapter } from '@/components/bpmn/properties/DirectorySubjectPicker';
import type { FormDto, ProcessVariables } from '@/lib/api/types';
import { getClientFormActions } from '@/lib/forms/formContractClient';
import { EmbedActionError, type EmbedActionChannel } from './EmbedChannel';

/** Ein persönlicher, bereits eingelöster Snapshot; kein Token und keine Mutationsfreigabe. */
export interface EmbedSnapshot {
  userTaskId: string; hostOrigin: string; taskRevision: number; form: FormDto; context: ProcessVariables;
  draft: { userTaskId: string; revision: number; data: ProcessVariables };
}

/** Bestehender Renderer mit manuellen Draft-/Abschlussaktionen auf dem gebundenen Hostkanal. */
export function EmbeddedTaskForm({ snapshot, channel }: { snapshot: EmbedSnapshot; channel: EmbedActionChannel }) {
  const renderer = useRef<FormRendererHandle>(null);
  const busy = useRef(false); const readyRef = useRef(false); const savedDataKey = useRef<string | null>(null);
  const draftRevision = useRef(snapshot.draft.revision);
  const completionAttempt = useRef<{ expectedTaskRevision: number; actionId?: string; data: ProcessVariables; idempotencyKey: string } | null>(null);
  // Dieser Erstwert bleibt während aller Saves/Token-Erneuerungen konstant. Ein
  // aktualisierter Serverentwurf darf niemals einen neuen Form.io-Renderer erzeugen.
  const [initialData] = useState(() => ({ ...snapshot.context, ...snapshot.draft.data }));
  const [ready, setReady] = useState(false); const [pending, setPending] = useState(false); const [completed, setCompleted] = useState(false);
  const [completing, setCompleting] = useState(false); const [completionUncertain, setCompletionUncertain] = useState(false);
  const [status, setStatus] = useState<'saved' | 'dirty' | 'saving' | 'error'>(snapshot.draft.revision > 0 ? 'saved' : 'dirty');
  const [error, setError] = useState<Error | null>(null);
  const formActions = useMemo(() => getClientFormActions(snapshot.form.formData ?? '{}'), [snapshot.form.formData]);
  const directory = useMemo<BoundDirectorySubjectAdapter>(() => ({
    cacheKey: ['embed', snapshot.userTaskId],
    search: (fieldKey, options) => channel.request('directory.search', { fieldKey, query: options.query, kind: options.kind }, options.signal),
    resolve: (fieldKey, subjects, signal) => channel.request('directory.resolve', { fieldKey, subjects }, signal),
  }), [channel, snapshot.userTaskId]);

  async function save() {
    if (busy.current || !readyRef.current || completed || completionUncertain) return;
    busy.current = true; setPending(true); setStatus('saving'); setError(null);
    try {
      const data = structuredClone(renderer.current?.getData() ?? {});
      const dataKey = JSON.stringify(data);
      const saved = await channel.request<EmbedSnapshot['draft']>('draft.save', { expectedRevision: draftRevision.current,
        expectedTaskRevision: snapshot.taskRevision, data });
      if (saved.userTaskId !== snapshot.userTaskId || !Number.isSafeInteger(saved.revision) || saved.revision <= draftRevision.current)
        throw new EmbedActionError('flowzer.connection_failed');
      draftRevision.current = saved.revision;
      savedDataKey.current = dataKey;
      // Form.io/Directory senden auch verspätete Change-Signale ohne Wertänderung.
      // Nur der tatsächliche Inhalt entscheidet, nicht die Anzahl solcher Events.
      setStatus(JSON.stringify(renderer.current?.getData() ?? {}) === dataKey ? 'saved' : 'dirty');
    } catch (cause) { setStatus('error'); setError(cause instanceof EmbedActionError ? cause : new EmbedActionError('flowzer.connection_failed')); }
    finally { busy.current = false; setPending(false); }
  }
  async function complete(actionId?: string) {
    if (busy.current || !readyRef.current || completed) return;
    busy.current = true; setPending(true); setCompleting(true); setError(null);
    try {
      if (!completionAttempt.current) {
        const action = formActions.find(item => item.id === actionId);
        if (!await renderer.current?.validate(action?.assignments)) {
          setError(new Error('Bitte fülle die erforderlichen Felder aus.')); return;
        }
        // Der Renderer mutiert seinen Datenbaum. Unklare Wiederholungen müssen
        // dennoch exakt denselben Auftrag senden, nicht nur denselben Schlüssel.
        completionAttempt.current = { expectedTaskRevision: snapshot.taskRevision, actionId,
          data: structuredClone(renderer.current?.getData() ?? {}), idempotencyKey: crypto.randomUUID() };
      }
      const result = await channel.request<{ completed: boolean }>('task.complete', completionAttempt.current);
      if (result?.completed !== true) throw new EmbedActionError('flowzer.connection_failed');
      setCompleted(true); setCompletionUncertain(false);
    } catch (cause) {
      const failure = cause instanceof EmbedActionError ? cause : new EmbedActionError('flowzer.connection_failed');
      const uncertain = failure.code === 'flowzer.connection_failed';
      // Nur ein ausdrücklich bekannter fachlicher Fehler gibt einen neuen Auftrag
      // frei. Bei unklarem Ausgang bleiben Eingaben und ursprünglicher Auftrag fest.
      if (!uncertain) completionAttempt.current = null;
      setCompletionUncertain(uncertain); setError(failure);
    } finally { busy.current = false; setPending(false); setCompleting(false); }
  }
  if (completed) return <p role="status">Aufgabe abgeschlossen.</p>;
  return <main className="flowzer-embed-form">
    <fieldset className="flowzer-embed-fields" disabled={completing || completionUncertain} inert={completing || completionUncertain}>
    <FormRenderer ref={renderer} schema={snapshot.form.formData ?? undefined} initialData={initialData} directoryAdapter={directory}
      onReadyChange={value => {
        if (value && !readyRef.current && snapshot.draft.revision > 0 && savedDataKey.current === null)
          savedDataKey.current = JSON.stringify(renderer.current?.getData() ?? {});
        readyRef.current = value; setReady(value);
      }}
      onChange={data => { if (readyRef.current && !busy.current)
        setStatus(JSON.stringify(data) === savedDataKey.current ? 'saved' : 'dirty'); }} />
    </fieldset>
    {error && <><p role="alert">{error.message}</p><FormValidationErrors error={null} schema={snapshot.form.formData ?? undefined}
      safeFieldMessages={error instanceof EmbedActionError ? error.fieldMessages : undefined} /></>}
    <footer>
      <p role="status">{status === 'saving' ? 'Speicherung läuft …' : status === 'saved' ? 'Zwischenstand gespeichert' : status === 'dirty' ? 'Ungespeicherte Änderungen' : 'Zwischenstand konnte nicht gespeichert werden'}</p>
      {completionUncertain && <p role="note">Der Abschluss ist noch unklar. Wiederhole ausschließlich den ursprünglichen Auftrag; deine Eingaben bleiben bis zur Klärung erhalten.</p>}
      {pending && <InlineSpinner label="Aktion wird verarbeitet …" />}
      <button type="button" disabled={!ready || pending || completionUncertain} onClick={() => void save()}>Zwischenstand speichern</button>
      {completionUncertain ? <button type="button" disabled={pending} onClick={() => void complete()}>Abschluss erneut bestätigen</button> : formActions.length ? formActions.map(action => <button type="button" key={action.id} disabled={!ready || pending}
        onClick={() => void complete(action.id)}>{action.label}</button>)
        : <button type="button" disabled={!ready || pending} onClick={() => void complete()}>Absenden</button>}
    </footer>
  </main>;
}
