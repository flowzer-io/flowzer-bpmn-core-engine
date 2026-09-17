import { useId, useRef, useState } from 'react';

import { FormRenderer, type FormRendererHandle, type FormRendererProps } from './FormRenderer';
import { Button } from '@/components/ui/Button';
import type { ProcessVariables } from '@/lib/api/types';
import { parseFormPreviewData } from '@/lib/forms/formPreviewData';

const JSON_FIELD_CLASS = 'bg-inset border-border text-text w-full min-w-0 resize-y rounded-[var(--r-sm)] border p-3 font-mono text-xs leading-relaxed';

/** Lokales Testlabor: keine Speicherung und kein Submit-Pfad; nicht mit echten Task-Daten verbinden. */
export function FormAuthoringPreview({ schema, directoryAdapter }: Pick<FormRendererProps, 'schema' | 'directoryAdapter'>) {
  const id = useId();
  const renderer = useRef<FormRendererHandle>(null);
  const [input, setInput] = useState('{}');
  const [applied, setApplied] = useState<ProcessVariables>({});
  const [generation, setGeneration] = useState(0);
  const [ready, setReady] = useState(false);
  const [output, setOutput] = useState('{}');
  const [error, setError] = useState<string | null>(null);
  const [copyStatus, setCopyStatus] = useState('');

  function showData(data: ProcessVariables) {
    setOutput(JSON.stringify(data, null, 2));
    setCopyStatus('');
  }

  function applyInput(text: string) {
    try {
      const data = parseFormPreviewData(text);
      setApplied(data);
      setReady(false);
      setGeneration(value => value + 1);
      setError(null);
    } catch (cause) {
      // Bei Fehlern weder Renderer noch gültige Testeingaben ersetzen.
      setError(cause instanceof Error ? cause.message : 'Die Testdaten konnten nicht übernommen werden.');
    }
  }

  async function copyOutput() {
    try {
      await navigator.clipboard.writeText(output);
      setCopyStatus('JSON-Ausgabe kopiert.');
    } catch {
      setCopyStatus('Kopieren nicht möglich. Bitte die JSON-Ausgabe markieren und manuell kopieren.');
    }
  }

  return <div className="min-w-0 space-y-4">
    <p className="text-muted text-sm">Interaktive Vorschau: Testeingaben werden nicht gespeichert und starten keinen Workflow.</p>
    <details className="border-border rounded-[var(--r-sm)] border p-3">
      <summary className="cursor-pointer text-sm font-semibold">JSON-Eingabe</summary>
      <p id={`${id}-hint`} className="text-muted my-2 text-xs">
        Feldschlüssel und Werte als JSON-Objekt eingeben, ohne „data“-Hülle. Erst „Eingabe übernehmen“ ersetzt die Testwerte des Formulars. Nicht angegebene Felder verwenden ihre Standardwerte.
      </p>
      <label htmlFor={`${id}-input`} className="mb-1 block text-xs font-semibold">JSON-Testdaten</label>
      <textarea id={`${id}-input`} value={input} onChange={event => setInput(event.target.value)}
        rows={7} spellCheck={false} className={JSON_FIELD_CLASS} aria-invalid={Boolean(error)}
        aria-describedby={`${id}-hint${error ? ` ${id}-error` : ''}`} />
      {error && <p id={`${id}-error`} role="alert" className="text-fail mt-2 text-sm">{error}</p>}
      <div className="mt-2 flex flex-wrap gap-2">
        <Button size="sm" onClick={() => applyInput(input)}>Eingabe übernehmen</Button>
        <Button size="sm" variant="ghost" onClick={() => { setInput('{}'); applyInput('{}'); }}>Testdaten zurücksetzen</Button>
      </div>
    </details>

    <FormRenderer key={generation} ref={renderer} schema={schema} initialData={applied}
      directoryAdapter={directoryAdapter} onChange={showData} onReadyChange={isReady => {
        setReady(isReady);
        if (isReady) showData(renderer.current?.getData() ?? {});
      }} />

    <details className="border-border rounded-[var(--r-sm)] border p-3">
      <summary className="cursor-pointer text-sm font-semibold">JSON-Ausgabe</summary>
      <p className="text-muted my-2 text-xs">
        Aktuelle Formularwerte, live aus der Vorschau und ohne API-Hülle. Dies ist noch kein serverseitig validiertes Prozessergebnis; geschützte Variablen, ausgeblendete Felder und Abschlussaktionen werden beim echten Abschluss gesondert geprüft.
      </p>
      <label htmlFor={`${id}-output`} className="mb-1 block text-xs font-semibold">Aktuelle Formularwerte als JSON</label>
      <textarea id={`${id}-output`} readOnly value={ready ? output : ''} rows={9} spellCheck={false}
        className={JSON_FIELD_CLASS} aria-busy={!ready} placeholder="Die Vorschau wird geladen …" />
      <Button size="sm" className="mt-2" disabled={!ready} onClick={() => void copyOutput()}>JSON kopieren</Button>
      {copyStatus && <p role="status" className="text-muted mt-2 text-xs">{copyStatus}</p>}
    </details>
  </div>;
}
