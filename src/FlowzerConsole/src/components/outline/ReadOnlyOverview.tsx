import { readOnlyOverview } from '@/lib/outline/readOnlyOverview';

/** Kein Exportpfad: unbekannte BPMN-Semantik wird angezeigt, niemals beim Speichern verworfen. */
export function ReadOnlyOverview({ xml, onOpen }: { xml: string; onOpen: (id: string) => void }) {
  const nodes = readOnlyOverview(xml);
  if (nodes.length === 0) return null;
  const labels = new Map(nodes.map(node => [node.id, node.name || node.id]));
  return <section aria-label="Schreibgeschützte Prozessübersicht" className="border-border rounded-[var(--r)] border p-4">
    <h2 className="font-semibold">Prozessübersicht</h2>
    <p className="text-muted mb-4 text-sm">
      Diesen Workflow bearbeitest du im Diagramm. Hier siehst du seine Schritte und Verbindungen,
      ohne dass beim Ansichtswechsel BPMN-Einstellungen verloren gehen. Die Liste ist keine Ausführungsreihenfolge.
    </p>
    <ul className="space-y-3">
      {nodes.map(node => <li key={node.id} className="border-border border-t pt-3">
        <button type="button" className="text-accent text-left font-semibold underline" onClick={() => onOpen(node.id)}>
          {node.name || node.id}
        </button>
        <span className="text-muted ml-2 text-xs">{node.type}</span>
        {node.next.length > 0 && <div className="text-muted text-sm">
          Weiter zu: {node.next.map(id => labels.get(id) ?? id).join(', ')}
        </div>}
      </li>)}
    </ul>
  </section>;
}
