import { useMemo, useRef, useState } from 'react';
import { FormRenderer, type FormRendererHandle } from '@/components/forms/FormRenderer';
import { FormValidationErrors } from '@/components/forms/FormValidationErrors';
import { InlineSpinner } from '@/components/ui/States';
import type { BoundDirectorySubjectAdapter } from '@/components/bpmn/properties/DirectorySubjectPicker';
import type { FormDto, ProcessVariables } from '@/lib/api/types';
import { EmbedActionError, type EmbedActionChannel } from './EmbedChannel';

/** Persönlicher Startsnapshot ohne Aufgabe, Instanz oder persistierten Entwurf. */
export interface EmbedStartSnapshot {
  definitionId: string; relatedDefinitionId: string; hostOrigin: string; form: FormDto;
}

/** Bestehender Renderer als getrennte Startansicht; Aktionen vermittelt allein der Host. */
export function EmbeddedStartForm({ snapshot, channel }: { snapshot: EmbedStartSnapshot; channel: EmbedActionChannel }) {
  const renderer = useRef<FormRendererHandle>(null);
  const busy = useRef(false); const readyRef = useRef(false);
  const attempt = useRef<{ data: ProcessVariables; idempotencyKey: string } | null>(null);
  const [initialData] = useState<ProcessVariables>({});
  const [ready, setReady] = useState(false); const [pending, setPending] = useState(false);
  const [uncertain, setUncertain] = useState(false); const [obsolete, setObsolete] = useState(false); const [started, setStarted] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const directory = useMemo<BoundDirectorySubjectAdapter>(() => ({
    cacheKey: ['embed-start', snapshot.relatedDefinitionId, snapshot.definitionId],
    search: (fieldKey, options) => channel.request('directory.search', { fieldKey, query: options.query, kind: options.kind }, options.signal),
    resolve: (fieldKey, subjects, signal) => channel.request('directory.resolve', { fieldKey, subjects }, signal),
  }), [channel, snapshot.relatedDefinitionId, snapshot.definitionId]);

  async function start() {
    if (busy.current || !readyRef.current || started || obsolete) return;
    busy.current = true; setPending(true); setError(null);
    try {
      if (!attempt.current) {
        if (!await renderer.current?.validate()) { setError(new Error('Bitte fülle die erforderlichen Felder aus.')); return; }
        // Der Host leitet Definition/Version/Benutzer/Ticket aus seiner eigenen
        // geöffneten Ansicht ab, niemals aus einem manipulierbaren Frame-Payload.
        // Ein unklarer Ausgang bleibt an genau diese geklonten Originaldaten gebunden.
        attempt.current = { data: structuredClone(renderer.current?.getData() ?? {}), idempotencyKey: crypto.randomUUID() };
      }
      const result = await channel.request<{ started: boolean }>('workflow.start', attempt.current);
      if (result?.started !== true) throw new EmbedActionError('flowzer.connection_failed');
      setStarted(true); setUncertain(false);
    } catch (cause) {
      const failure = cause instanceof EmbedActionError ? cause : new EmbedActionError('flowzer.connection_failed');
      // Ein späterer Vorprüfungs-/Zugangsfehler sagt nichts über einen früheren
      // unklaren Start aus. Diese Ungewissheit endet nur mit gebundenem Erfolg.
      const unknown = uncertain || failure.code === 'flowzer.connection_failed';
      if (!unknown) attempt.current = null;
      // Nur ein erstmaliger nachweislicher Vor-Anlage-Versionskonflikt
      // stoppt diese veraltete Fassung. Ein früherer Unknown bleibt gebunden.
      setObsolete(!unknown && failure.code === 'flowzer.definition_changed');
      setUncertain(unknown);
      // Im sticky-Unknown keine spätere Neuauswahl-/Korrekturanweisung zeigen:
      // ihr Nein-Beleg fehlt für den früheren Versand weiterhin.
      setError(unknown ? new EmbedActionError('flowzer.connection_failed') : failure);
    } finally { busy.current = false; setPending(false); }
  }
  if (started) return <p role="status">Workflow gestartet.</p>;
  return <main className="flowzer-embed-form">
    <fieldset className="flowzer-embed-fields" disabled={pending || uncertain || obsolete} inert={pending || uncertain || obsolete}>
      <FormRenderer ref={renderer} schema={snapshot.form.formData ?? undefined} initialData={initialData} directoryAdapter={directory}
        onReadyChange={value => { readyRef.current = value; setReady(value); }} />
    </fieldset>
    {error && <><p role="alert">{error.message}</p><FormValidationErrors error={null} schema={snapshot.form.formData ?? undefined}
      safeFieldMessages={error instanceof EmbedActionError ? error.fieldMessages : undefined} /></>}
    <footer>
      {uncertain && <p role="note">Der Start ist noch unklar. Bestätige ausschließlich den ursprünglichen Auftrag; deine Eingaben bleiben bis zur Klärung erhalten.</p>}
      {pending && <InlineSpinner label="Start wird verarbeitet …" />}
      <button type="button" disabled={!ready || pending || obsolete} onClick={() => void start()}>
        {uncertain ? 'Ursprünglichen Start erneut bestätigen' : 'Workflow starten'}
      </button>
    </footer>
  </main>;
}
