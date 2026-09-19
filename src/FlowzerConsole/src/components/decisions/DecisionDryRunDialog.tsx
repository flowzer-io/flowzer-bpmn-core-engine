import { useState } from 'react';

import { Button } from '@/components/ui/Button';
import { FieldLabel } from '@/components/ui/Field';
import { Modal } from '@/components/ui/Modal';
import { useEvaluateDecision } from '@/lib/api/queries';
import type { DecisionEvaluationResult, DecisionSummary } from '@/lib/api/types';
import { cn } from '@/lib/cn';

interface DecisionDryRunDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  decisionDefinitionId: string;
  /** Die Entscheidungen der Datei; eine davon wird ausgewertet. */
  decisions: readonly DecisionSummary[];
}

/**
 * Der Trockenlauf: eine Entscheidung mit selbst eingegebenen Variablen auswerten, ohne eine
 * Instanz zu starten.
 *
 * Die Variablen bleiben roher JSON-Text und werden erst beim Auswerten gelesen. Ein Editor,
 * der bei jedem Tastendruck parst, meldete jede halbfertige Eingabe als Fehler.
 */
export function DecisionDryRunDialog({
  open,
  onOpenChange,
  decisionDefinitionId,
  decisions,
}: DecisionDryRunDialogProps) {
  const evaluate = useEvaluateDecision();
  const [decisionId, setDecisionId] = useState(decisions[0]?.decisionId ?? '');
  const [variablesText, setVariablesText] = useState('{\n  \n}');
  const [inputError, setInputError] = useState<string | null>(null);
  const [result, setResult] = useState<DecisionEvaluationResult | null>(null);
  const [failure, setFailure] = useState<string | null>(null);

  const selected = decisions.some((decision) => decision.decisionId === decisionId)
    ? decisionId
    : decisions[0]?.decisionId ?? '';

  function run() {
    setResult(null);
    setFailure(null);

    const variables = readVariables(variablesText);
    if ('error' in variables) {
      setInputError(variables.error);
      return;
    }
    setInputError(null);

    evaluate.mutate(
      { decisionDefinitionId, decisionId: selected, variables: variables.value },
      {
        onSuccess: setResult,
        onError: (error) =>
          setFailure(error instanceof Error ? error.message : 'Der Trockenlauf ist fehlgeschlagen.'),
      },
    );
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      icon="play_arrow"
      title="Trockenlauf"
      description="Wertet die Entscheidung einmalig mit den eingegebenen Variablen aus. Es wird keine Instanz gestartet und nichts gespeichert."
      className="w-[min(680px,calc(100vw-32px))]"
      footer={
        <>
          <Button size="sm" onClick={() => onOpenChange(false)}>
            Schließen
          </Button>
          <Button
            size="sm"
            variant="primary"
            icon="play_arrow"
            loading={evaluate.isPending}
            disabled={selected.length === 0}
            onClick={run}
          >
            Auswerten
          </Button>
        </>
      }
    >
      <div className="grid gap-4 py-2">
        <div>
          <FieldLabel htmlFor="dry-run-decision">Entscheidung</FieldLabel>
          <select
            id="dry-run-decision"
            value={selected}
            onChange={(event) => setDecisionId(event.target.value)}
            className="bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] outline-none focus:border-accent"
          >
            {decisions.length === 0 && <option value="">— keine Entscheidung in dieser Datei —</option>}
            {decisions.map((decision) => (
              <option key={decision.decisionId} value={decision.decisionId}>
                {decision.name ? `${decision.name} · ${decision.decisionId}` : decision.decisionId}
              </option>
            ))}
          </select>
        </div>

        <div>
          <FieldLabel htmlFor="dry-run-variables">Variablen (JSON)</FieldLabel>
          <textarea
            id="dry-run-variables"
            rows={9}
            spellCheck={false}
            value={variablesText}
            onChange={(event) => setVariablesText(event.target.value)}
            className={cn(
              'bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5',
              'font-mono text-[12.5px] outline-none focus:border-accent',
              inputError && 'border-fail',
            )}
          />
          {inputError ? (
            <p role="alert" className="text-fail mt-1.5 text-xs">{inputError}</p>
          ) : (
            <p className="text-muted mt-1.5 text-xs">
              Ein JSON-Objekt, dessen Felder die Entscheidung als Eingaben liest, etwa{' '}
              <code className="font-mono">{'{ "season": "Fall", "guestCount": 4 }'}</code>.
            </p>
          )}
        </div>

        {failure && (
          <div role="alert" className="border-fail text-fail rounded-[var(--r)] border px-4 py-3 text-sm">
            {failure}
          </div>
        )}

        {result && <DryRunResult result={result} />}
      </div>
    </Modal>
  );
}

function DryRunResult({ result }: { result: DecisionEvaluationResult }) {
  const required = Object.entries(result.requiredResults);

  return (
    <div className="border-border rounded-[var(--r)] border">
      <div className="border-border bg-surface-2 border-b px-4 py-2 text-[13px] font-semibold">
        Ergebnis von {result.decisionId}
      </div>
      <div className="grid gap-3 p-4">
        <Value label="Wert" value={result.value} />
        <MatchedRules rules={result.matchedRules} />

        {required.length > 0 && (
          <div>
            <FieldLabel>Zwischenergebnisse</FieldLabel>
            <div className="border-border divide-border divide-y rounded-[var(--r-sm)] border">
              {required.map(([key, intermediate]) => (
                <div key={key} className="grid gap-2 p-3">
                  <div className="text-[13px] font-semibold">{intermediate.decisionId || key}</div>
                  <Value label="Wert" value={intermediate.value} />
                  <MatchedRules rules={intermediate.matchedRules} />
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function Value({ label, value }: { label: string; value: unknown }) {
  return (
    <div>
      <FieldLabel>{label}</FieldLabel>
      <pre className="bg-surface-2 m-0 overflow-auto rounded-[var(--r-sm)] p-3 font-mono text-[12.5px]">
        {JSON.stringify(value, null, 2) ?? 'null'}
      </pre>
    </div>
  );
}

function MatchedRules({ rules }: { rules: readonly string[] }) {
  return (
    <div>
      <FieldLabel>Getroffene Regeln</FieldLabel>
      {rules.length === 0 ? (
        <p className="text-muted m-0 text-[13px]">Keine Regel hat gegriffen.</p>
      ) : (
        <ul className="m-0 flex list-none flex-wrap gap-1.5 p-0">
          {rules.map((rule) => (
            <li
              key={rule}
              className="border-border bg-surface-2 rounded-[var(--r-sm)] border px-2 py-0.5 font-mono text-[12px]"
            >
              {rule}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * Liest die Variablen aus dem Textfeld. Ein JSON-Array oder eine nackte Zahl wäre zwar
 * gültiges JSON, aber keine Variablenbelegung — das meldet der Dialog getrennt vom
 * Syntaxfehler, weil es ein anderer Fehler ist.
 */
function readVariables(text: string): { value: Record<string, unknown> } | { error: string } {
  const trimmed = text.trim();
  if (trimmed.length === 0) return { value: {} };

  let parsed: unknown;
  try {
    parsed = JSON.parse(trimmed);
  } catch (cause) {
    return { error: `Das ist kein gültiges JSON: ${cause instanceof Error ? cause.message : 'unbekannter Fehler'}` };
  }

  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return { error: 'Die Variablen müssen ein JSON-Objekt sein, etwa { "betrag": 100 }.' };
  }

  return { value: parsed as Record<string, unknown> };
}
