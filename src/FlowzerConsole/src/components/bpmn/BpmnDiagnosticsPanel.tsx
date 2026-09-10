import { Icon } from '@/components/ui/Icon';
import { toneSurface } from '@/components/ui/Chip';
import type { BpmnDiagnostic } from '@/lib/modeling/diagnostics';

interface BpmnDiagnosticsPanelProps {
  diagnostics: readonly BpmnDiagnostic[];
  contractVersion?: string;
  onSelectElement?: (elementId: string) => void;
}

/** Dauerhafte, hostneutrale Anzeige für elementbezogene Modellprüfungen. */
export function BpmnDiagnosticsPanel({ diagnostics, contractVersion, onSelectElement }: BpmnDiagnosticsPanelProps) {
  if (diagnostics.length === 0) return null;

  const errors = diagnostics.filter((diagnostic) => diagnostic.severity === 'error');
  const tone = errors.length > 0 ? 'fail' : 'wait';

  return (
    <section
      aria-labelledby="bpmn-diagnostics-title"
      aria-live="polite"
      className="mx-[22px] mt-3 rounded-[var(--r)] border px-4 py-3"
      style={{ background: toneSurface(tone, 10), borderColor: `color-mix(in oklab, var(--${tone}) 34%, transparent)` }}
    >
      <div id="bpmn-diagnostics-title" className="flex items-center gap-2 text-[13.5px] font-semibold" style={{ color: `var(--${tone})` }}>
        <Icon name={errors.length > 0 ? 'error' : 'info'} size={18} />
        Modellprüfung
        {contractVersion && <span className="text-faint ml-auto font-mono text-[11px]">Vertrag v{contractVersion}</span>}
      </div>
      <ul className="text-muted mt-2 flex list-disc flex-col gap-1 pl-5 text-[12.5px]">
        {diagnostics.map((diagnostic, index) => (
          <li key={`${diagnostic.code}-${diagnostic.elementId ?? ''}-${index}`}>
            {diagnostic.elementId && onSelectElement ? (
              <button
                type="button"
                className="text-accent cursor-pointer border-none bg-transparent p-0 text-left font-semibold underline"
                aria-label={`Befund an Element ${diagnostic.elementId} anwählen`}
                onClick={() => onSelectElement(diagnostic.elementId!)}
              >
                {diagnostic.message}
              </button>
            ) : (
              diagnostic.message
            )}
            {diagnostic.propertyPath && <span className="text-faint font-mono"> ({diagnostic.propertyPath})</span>}
            <span className="text-faint ml-1 font-mono">[{diagnostic.code}]</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
