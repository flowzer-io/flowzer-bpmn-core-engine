/**
 * Die Abschnitte des Eigenschaften-Panels, je einer für eine Sache, die die Engine an einem
 * Element auswertet.
 *
 * Jedes Feld schickt nur seine eigene Änderung; die übrigen Werte holt sich der Adapter aus
 * dem Modell. Gäbe ein Feld den ganzen Stand aus seinem letzten Bild mit, machte ein Klick
 * aus dem Feld heraus auf einen Schalter derselben Gruppe die Eingabe wieder zunichte.
 */

import { Button } from '@/components/ui/Button';
import { Chip } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { Segmented } from '@/components/ui/Segmented';
import { embeddedFormKey, newEmbeddedFormId, parseFormKey, storedFormKey } from '@/lib/formKey';

import type {
  BpmnEditor,
  CalledProcess,
  ElementProperties,
  MessageReference,
  ScriptDefinition,
  TimerKind,
} from '../bpmnEditor';
import { CheckRow, IoMappingEditor, Notice, SelectRow, Section, TextRow } from './PropertyFields';

export interface SectionProps {
  properties: ElementProperties;
  editor: BpmnEditor | null;
  readOnly: boolean;
}

const FORM_SOURCE_OPTIONS = [
  { value: 'stored' as const, label: 'Aus dem Bestand' },
  { value: 'embedded' as const, label: 'In diesem Workflow' },
];

const NEW_EMBEDDED_FORM = '__neu__';

const SCRIPT_MODE_OPTIONS = [
  { value: 'script' as const, label: 'Als Skript' },
  { value: 'job' as const, label: 'Als Auftrag' },
];

const TIMER_KIND_OPTIONS = [
  { value: 'duration' as const, label: 'Dauer' },
  { value: 'date' as const, label: 'Zeitpunkt' },
  { value: 'cycle' as const, label: 'Zyklus' },
];

/** Was in das Feld gehört — je Art der Zeitangabe eine andere Schreibweise. */
const TIMER_HINTS: Record<TimerKind, { placeholder: string; hint: string }> = {
  duration: {
    placeholder: 'PT48H',
    hint: 'ISO-8601-Dauer ab dem Erreichen des Schritts, etwa PT48H oder P3D.',
  },
  date: {
    placeholder: '2026-10-01T10:00:00Z',
    hint: 'Fester Zeitpunkt als ISO-8601-Datum mit Zeitzone.',
  },
  cycle: {
    placeholder: 'R3/PT1H',
    hint: 'Wiederholung als ISO-8601-Zyklus, etwa R3/PT1H, oder ein Cron-Ausdruck.',
  },
};

export function GeneralSection({ properties, editor, readOnly }: SectionProps) {
  return (
    <Section icon="edit" title="Allgemein">
      <TextRow
        label="Name"
        value={properties.name}
        disabled={readOnly}
        placeholder="Wie heißt dieser Schritt?"
        onCommit={(value) => editor?.setName(properties.id, value)}
      />
      <div>
        <div className="text-muted mb-1 font-mono text-[10.5px] font-medium tracking-[0.06em] uppercase">
          Kennung
        </div>
        <div className="text-faint font-mono text-[12px] break-all">{properties.id}</div>
      </div>
    </Section>
  );
}

interface FormSectionProps extends SectionProps {
  storedFormNames: string[];
  embeddedForms: string[];
  source: 'stored' | 'embedded';
  onSourceChange: (source: 'stored' | 'embedded') => void;
  onEditEmbeddedForm: (formId: string) => void;
}

/**
 * Der Formularverweis einer menschlichen Aufgabe.
 *
 * Beide Herkünfte stehen gleichberechtigt nebeneinander: ein Formular aus dem Bestand, das
 * mehrere Workflows teilen, oder eines im Workflow selbst, das mit ihm versioniert wird.
 */
export function FormSection({
  properties,
  editor,
  readOnly,
  storedFormNames,
  embeddedForms,
  source,
  onSourceChange,
  onEditEmbeddedForm,
}: FormSectionProps) {
  const reference = parseFormKey(properties.formKey);
  const selectedName = reference.kind === 'stored' ? reference.name : '';
  const selectedEmbeddedId = reference.kind === 'embedded' ? reference.formId : '';

  // Ein Verweis auf ein inzwischen umbenanntes oder geloeschtes Formular darf nicht still aus
  // der Auswahl verschwinden — sonst sieht die Aufgabe unbelegt aus und wird stumm ueberschrieben.
  const nameOptions = storedFormNames.includes(selectedName)
    ? storedFormNames
    : [selectedName, ...storedFormNames].filter((name) => name.length > 0);

  function createEmbeddedForm() {
    if (!editor) return;
    const formId = newEmbeddedFormId(properties.name || properties.id);
    // Die Aufgabe bestimmt, in welchem Prozess das Formular entsteht — in einer Kollaboration
    // gehoert es in den Pool der Aufgabe und nicht in den erstbesten.
    editor.saveEmbeddedForm(formId, JSON.stringify({ display: 'form', components: [] }, null, 2), properties.id);
    editor.setFormKey(properties.id, embeddedFormKey(formId));
    onEditEmbeddedForm(formId);
  }

  return (
    <Section icon="description" title="Formular">
      <Segmented
        options={FORM_SOURCE_OPTIONS}
        value={source}
        onChange={onSourceChange}
        aria-label="Herkunft des Formulars"
      />

      {source === 'stored' && (
        <>
          <SelectRow
            label="Formular"
            value={selectedName}
            disabled={readOnly}
            options={[
              { value: '', label: '— kein Formular —' },
              ...nameOptions.map((name) => ({ value: name, label: name })),
            ]}
            onChange={(name) => {
              if (name === '') {
                editor?.setFormKey(properties.id, null);
                return;
              }
              // Die festgehaltene Version gehoert zu dem Formular, an dem sie gesetzt wurde.
              // Beim Wechsel auf ein anderes Formular waere sie geraten — und die Aufgabe
              // liefe in „Version 1.0 of form … was not found".
              const keptVersion = reference.kind === 'stored' && reference.name === name ? reference.version : null;
              editor?.setFormKey(properties.id, storedFormKey(name, keptVersion));
            }}
          />
          <TextRow
            label="Version"
            value={reference.kind === 'stored' ? (reference.version ?? '') : ''}
            disabled={readOnly || selectedName.length === 0}
            placeholder="neueste"
            monospace
            hint="Leer lassen, damit die Aufgabe immer die neueste Fassung zeigt."
            onCommit={(version) => editor?.setFormKey(properties.id, storedFormKey(selectedName, version))}
          />
          {selectedName.length > 0 && !storedFormNames.includes(selectedName) && (
            <Notice tone="warn">
              Im Bestand gibt es kein Formular „{selectedName}". Die Aufgabe ließe sich später nicht
              bearbeiten.
            </Notice>
          )}
        </>
      )}

      {source === 'embedded' && (
        <>
          <SelectRow
            label="Formular im Workflow"
            value={selectedEmbeddedId}
            disabled={readOnly}
            options={[
              { value: '', label: '— kein Formular —' },
              ...embeddedForms.map((formId) => ({ value: formId, label: formId })),
              { value: NEW_EMBEDDED_FORM, label: 'Neues Formular anlegen …' },
            ]}
            onChange={(value) => {
              if (value === NEW_EMBEDDED_FORM) {
                createEmbeddedForm();
                return;
              }
              editor?.setFormKey(properties.id, value === '' ? null : embeddedFormKey(value));
            }}
            hint="Ein bereits im Workflow vorhandenes Formular oder ein neues."
          />

          {selectedEmbeddedId.length > 0 && embeddedForms.includes(selectedEmbeddedId) && (
            <Button size="sm" icon="edit" disabled={readOnly} onClick={() => onEditEmbeddedForm(selectedEmbeddedId)}>
              Felder bearbeiten
            </Button>
          )}

          {selectedEmbeddedId.length > 0 && !embeddedForms.includes(selectedEmbeddedId) && (
            <Notice tone="warn">
              Der Workflow enthält kein Formular „{selectedEmbeddedId}". Lege eines an oder wähle ein
              vorhandenes.
            </Notice>
          )}
        </>
      )}

      {reference.kind === 'none' && properties.externalFormReference.length > 0 && (
        <Notice tone="warn">
          Das Formular „{properties.externalFormReference}" steht in{' '}
          <span className="font-mono">zeebe:externalReference</span>, das diese Engine nicht liest. Wähle
          es oben erneut aus — dann steht es wieder als Form-Key im Diagramm.
        </Notice>
      )}

      {reference.kind === 'none' && properties.externalFormReference.length === 0 && (
        <Notice tone="warn">
          Ohne Formular lässt sich der Workflow nicht speichern — jede menschliche Aufgabe braucht eines.
        </Notice>
      )}
    </Section>
  );
}

export function AssignmentSection({ properties, editor, readOnly }: SectionProps) {
  return (
    <Section
      icon="person"
      title="Zuweisung"
      hint="Leer heißt: Die Aufgabe steht allen offen, die den Workflow bedienen dürfen."
    >
      <TextRow
        label="Zugewiesen an"
        value={properties.assignee}
        disabled={readOnly}
        placeholder="Benutzername oder E-Mail"
        onCommit={(value) => editor?.setAssignment(properties.id, { assignee: value })}
      />
      <TextRow
        label="Gruppen"
        value={properties.candidateGroups}
        disabled={readOnly}
        placeholder="einkauf, buchhaltung"
        hint="Mehrere durch Komma getrennt."
        onCommit={(value) => editor?.setAssignment(properties.id, { candidateGroups: value })}
      />
      <TextRow
        label="Personen"
        value={properties.candidateUsers}
        disabled={readOnly}
        placeholder="anna, bruno"
        hint="Mehrere durch Komma getrennt."
        onCommit={(value) => editor?.setAssignment(properties.id, { candidateUsers: value })}
      />
    </Section>
  );
}

export function ScheduleSection({ properties, editor, readOnly }: SectionProps) {
  return (
    <Section
      icon="schedule"
      title="Frist"
      hint="Zeitpunkt (2026-10-01T10:00:00Z), Dauer ab Start (PT48H) oder FEEL-Ausdruck (=…)."
    >
      <TextRow
        label="Fällig"
        value={properties.dueDate}
        disabled={readOnly}
        placeholder="PT48H"
        monospace
        onCommit={(value) => editor?.setSchedule(properties.id, { dueDate: value })}
      />
      <TextRow
        label="Wiedervorlage"
        value={properties.followUpDate}
        disabled={readOnly}
        placeholder="PT24H"
        monospace
        onCommit={(value) => editor?.setSchedule(properties.id, { followUpDate: value })}
      />
    </Section>
  );
}

/** Der Auftrag an einen externen Worker — an Service-Tasks und an sendenden Ereignissen. */
export function JobSection({ properties, editor, readOnly }: SectionProps) {
  return (
    <Section
      icon="api"
      title="Auftrag"
      hint={
        properties.kind === 'serviceTask'
          ? 'Der Auftragstyp verbindet den Schritt mit dem Worker, der ihn ausführt.'
          : 'Diesen Schritt führt ein Worker aus — bei einem sendenden Ereignis verschickt er die Nachricht.'
      }
    >
      <TextRow
        label="Auftragstyp"
        value={properties.jobType}
        disabled={readOnly}
        placeholder="rechnung-pruefen"
        monospace
        onCommit={(value) => editor?.setJob(properties.id, { type: value })}
      />
      {properties.jobType.trim().length === 0 && (
        <Notice tone="warn">
          Ohne Auftragstyp findet kein Worker diesen Schritt — die Instanz bliebe hier stehen.
        </Notice>
      )}
      {/* Wiederholungen liest die Engine nur am Service-Task. An einem sendenden Ereignis oder
          einer Skript-Aufgabe waere das Feld eine Angabe ohne Wirkung. */}
      {properties.kind === 'serviceTask' && (
        <TextRow
          label="Wiederholungen"
          value={properties.retries}
          disabled={readOnly}
          placeholder="3"
          monospace
          hint="Wie oft ein fehlgeschlagener Auftrag erneut vergeben wird. Leer bedeutet einmalig."
          onCommit={(value) => editor?.setJob(properties.id, { retries: value })}
        />
      )}
    </Section>
  );
}

export function MappingsSection({ properties, editor, readOnly }: SectionProps) {
  return (
    <Section
      icon="data_object"
      title="Zuordnungen"
      hint="Eingang bringt Prozessdaten in den Schritt, Ausgang schreibt sein Ergebnis zurück."
    >
      {properties.supportsInputMappings && (
        <IoMappingEditor
          label="Eingang"
          sourceLabel="=antrag.betrag"
          targetLabel="betrag"
          value={properties.inputs}
          disabled={readOnly}
          onChange={(inputs) => editor?.setIoMappings(properties.id, inputs, properties.outputs)}
        />
      )}
      <IoMappingEditor
        label="Ausgang"
        sourceLabel="=entscheidung"
        targetLabel="antrag.status"
        value={properties.outputs}
        disabled={readOnly}
        onChange={(outputs) => editor?.setIoMappings(properties.id, properties.inputs, outputs)}
      />
    </Section>
  );
}

interface GatewaySectionProps extends SectionProps {
  onSelect: (elementId: string) => void;
}

export function GatewaySection({ properties, editor, readOnly, onSelect }: GatewaySectionProps) {
  return (
    <Section
      icon="call_split"
      title="Bedingungen"
      hint="Die Engine prüft die Bedingungen der Reihe nach. Greift keine, läuft der Standardfluss."
    >
      {properties.outgoing.length === 0 && (
        <Notice tone="warn">Dieses Tor hat noch keinen ausgehenden Fluss.</Notice>
      )}

      {properties.outgoing.map((flow) => (
        <div key={flow.id} className="border-border rounded-[var(--r-sm)] border p-2.5">
          <div className="mb-2 flex items-center gap-2">
            <Icon name="arrow_forward" size={14} className="text-faint flex-none" />
            <button
              type="button"
              onClick={() => onSelect(flow.id)}
              className="text-text hover:text-accent min-w-0 flex-1 cursor-pointer truncate border-none bg-transparent p-0 text-left text-[12.5px] font-semibold"
            >
              {flow.name.trim() || flow.targetLabel}
            </button>
            {flow.isDefault && <Chip tone="accent">Standard</Chip>}
          </div>

          <TextRow
            label="Bedingung"
            value={flow.condition}
            disabled={readOnly || flow.isDefault}
            placeholder={'=betrag < 1000'}
            monospace
            onCommit={(value) => editor?.setCondition(flow.id, value)}
          />

          <Button
            size="sm"
            variant="ghost"
            icon={flow.isDefault ? 'close' : 'check'}
            className="mt-1.5 px-1"
            disabled={readOnly}
            onClick={() => editor?.setDefaultFlow(properties.id, flow.isDefault ? null : flow.id)}
          >
            {flow.isDefault ? 'Standardfluss aufheben' : 'Als Standardfluss'}
          </Button>
        </div>
      ))}
    </Section>
  );
}

export function FlowSection({ properties, editor, readOnly }: SectionProps) {
  return (
    <Section
      icon="call_split"
      title="Bedingung"
      hint={properties.conditionApplies ? 'Trifft die Bedingung zu, nimmt die Instanz diesen Weg.' : undefined}
    >
      {!properties.conditionApplies && (
        <Notice>
          Bedingungen wertet die Engine nur an Toren und an Aufgaben mit mehreren Ausgängen aus.
        </Notice>
      )}
      {properties.conditionApplies && properties.isDefaultFlow && (
        <Notice>Dieser Fluss ist der Standardfluss und braucht deshalb keine Bedingung.</Notice>
      )}
      {properties.conditionApplies && !properties.isDefaultFlow && (
        <TextRow
          label="Bedingung"
          value={properties.condition}
          disabled={readOnly}
          placeholder={'=betrag < 1000'}
          monospace
          onCommit={(value) => editor?.setCondition(properties.id, value)}
        />
      )}
    </Section>
  );
}

/**
 * Die Zeitangabe eines Timers. Die Art bestimmt, was im Feld stehen darf — deshalb steht sie
 * davor und nicht als Erklärung daneben.
 */
export function TimerSection({ properties, editor, readOnly }: SectionProps) {
  const timer = properties.timer;
  if (!timer) return null;

  const { placeholder, hint } = TIMER_HINTS[timer.kind];

  return (
    <Section icon="schedule" title="Zeitangabe">
      <Segmented
        options={TIMER_KIND_OPTIONS}
        value={timer.kind}
        disabled={readOnly}
        onChange={(kind) => editor?.setTimer(properties.id, { kind })}
        aria-label="Art der Zeitangabe"
      />
      <TextRow
        label="Wert"
        value={timer.expression}
        disabled={readOnly}
        placeholder={placeholder}
        monospace
        hint={hint}
        onCommit={(expression) => editor?.setTimer(properties.id, { expression })}
      />
      {timer.expression.trim().length === 0 && (
        <Notice tone="warn">
          Ohne Zeitangabe lässt sich der Workflow nicht speichern — die Engine wüsste nicht, wie lange
          sie wartet.
        </Notice>
      )}
    </Section>
  );
}

export function MessageSection({ properties, editor, readOnly }: SectionProps) {
  const current: MessageReference = properties.message ?? { name: '', correlationKey: '' };

  return (
    <Section icon="mail" title="Nachricht" hint="Die Instanz wartet, bis eine Nachricht dieses Namens eintrifft.">
      <TextRow
        label="Name"
        value={current.name}
        disabled={readOnly}
        placeholder="Antrag eingegangen"
        onCommit={(name) => editor?.setMessage(properties.id, { name })}
      />
      {current.name.trim().length === 0 && (
        <Notice tone="warn">
          Ohne Namen lässt sich der Workflow nicht speichern — die Nachricht wäre nicht zuzuordnen.
        </Notice>
      )}
      <TextRow
        label="Korrelationsschlüssel"
        value={current.correlationKey}
        disabled={readOnly}
        placeholder="=antragsnummer"
        monospace
        hint="Bestimmt, welche laufende Instanz die Nachricht bekommt. Leer heißt: Sie startet eine neue."
        onCommit={(correlationKey) => editor?.setMessage(properties.id, { correlationKey })}
      />
    </Section>
  );
}

export function SignalSection({ properties, editor, readOnly }: SectionProps) {
  const name = properties.signalName ?? '';

  return (
    <Section icon="sensors" title="Signal" hint="Ein Signal erreicht alle Instanzen, die darauf warten.">
      <TextRow
        label="Name"
        value={name}
        disabled={readOnly}
        placeholder="Freigabe erteilt"
        onCommit={(value) => editor?.setSignal(properties.id, value)}
      />
      {name.trim().length === 0 && (
        <Notice tone="warn">Ohne Namen lässt sich der Workflow nicht speichern.</Notice>
      )}
    </Section>
  );
}

export function CallActivitySection({ properties, editor, readOnly }: SectionProps) {
  const current: CalledProcess = properties.calledProcess ?? {
    processId: '',
    propagateAllChildVariables: true,
    propagateAllParentVariables: true,
  };

  return (
    <Section
      icon="call_merge"
      title="Aufgerufener Prozess"
      hint="Dieser Schritt startet einen anderen Workflow und wartet auf sein Ende."
    >
      <TextRow
        label="Prozesskennung"
        value={current.processId}
        disabled={readOnly}
        placeholder="Process_Urlaubsantrag"
        monospace
        onCommit={(processId) => editor?.setCalledProcess(properties.id, { processId })}
      />
      {current.processId.trim().length === 0 && (
        <Notice tone="warn">Ohne Prozesskennung lässt sich der Workflow nicht speichern.</Notice>
      )}
      {/* Ohne Prozesskennung gibt es nichts weiterzugeben — und die Erweiterung, an der die
          Schalter haengen, steht dann bewusst gar nicht im Diagramm. */}
      <CheckRow
        label="Daten in den aufgerufenen Prozess geben"
        checked={current.propagateAllParentVariables}
        disabled={readOnly || current.processId.trim().length === 0}
        onChange={(propagateAllParentVariables) =>
          editor?.setCalledProcess(properties.id, { propagateAllParentVariables })
        }
      />
      <CheckRow
        label="Ergebnis zurück in diesen Prozess übernehmen"
        checked={current.propagateAllChildVariables}
        disabled={readOnly || current.processId.trim().length === 0}
        onChange={(propagateAllChildVariables) =>
          editor?.setCalledProcess(properties.id, { propagateAllChildVariables })
        }
      />
    </Section>
  );
}

/**
 * Eine Skript-Aufgabe läuft entweder als FEEL-Ausdruck in der Engine oder als Auftrag an einen
 * Worker. Beides zugleich gäbe es im Modell nicht — die Engine nimmt das Skript, sobald eines
 * da ist. Deshalb ist es ein Umschalter und keine zwei nebeneinanderstehenden Felder.
 */
export function ScriptSection({ properties, editor, readOnly }: SectionProps) {
  const current: ScriptDefinition = properties.script ?? { expression: '', resultVariable: '' };

  return (
    <Section icon="code" title="Skript">
      <Segmented
        options={SCRIPT_MODE_OPTIONS}
        value={properties.script ? 'script' : 'job'}
        disabled={readOnly}
        onChange={(mode) => editor?.setScriptMode(properties.id, mode)}
        aria-label="Wie der Schritt ausgeführt wird"
      />

      {properties.script && (
        <>
          <TextRow
            label="Ausdruck"
            value={current.expression}
            disabled={readOnly}
            placeholder="=betrag * 1.19"
            monospace
            onCommit={(expression) => editor?.setScript(properties.id, { expression })}
          />
          {current.expression.trim().length === 0 && (
            <Notice tone="warn">Ohne Ausdruck hat der Schritt nichts zu berechnen.</Notice>
          )}
          <TextRow
            label="Ergebnisvariable"
            value={current.resultVariable}
            disabled={readOnly}
            placeholder="bruttobetrag"
            monospace
            hint="Unter diesem Namen steht das Ergebnis danach im Prozess."
            onCommit={(resultVariable) => editor?.setScript(properties.id, { resultVariable })}
          />
        </>
      )}
    </Section>
  );
}

/** Mehrfachausführung: derselbe Schritt einmal je Eintrag einer Liste. */
export function MultiInstanceSection({ properties, editor, readOnly }: SectionProps) {
  const loop = properties.multiInstance;
  if (!loop) return null;

  const write = (patch: Partial<Omit<typeof loop, 'isSequential'>>) =>
    editor?.setMultiInstance(properties.id, patch);

  return (
    <Section
      icon="inventory_2"
      title="Mehrfachausführung"
      hint={
        loop.isSequential
          ? 'Der Schritt läuft nacheinander einmal je Eintrag.'
          : 'Der Schritt läuft für alle Einträge gleichzeitig.'
      }
    >
      <TextRow
        label="Liste"
        value={loop.inputCollection}
        disabled={readOnly}
        placeholder="=positionen"
        monospace
        onCommit={(value) => write({ inputCollection: value })}
      />
      {loop.inputCollection.trim().length === 0 && (
        <Notice tone="warn">
          Ohne Liste lässt sich der Workflow nicht speichern — die Engine wüsste nicht, worüber sie
          läuft.
        </Notice>
      )}
      <TextRow
        label="Eintrag"
        value={loop.inputElement}
        disabled={readOnly}
        placeholder="position"
        monospace
        hint="Unter diesem Namen steht der einzelne Eintrag im Schritt."
        onCommit={(value) => write({ inputElement: value })}
      />
      <TextRow
        label="Ergebnisliste"
        value={loop.outputCollection}
        disabled={readOnly}
        placeholder="ergebnisse"
        monospace
        onCommit={(value) => write({ outputCollection: value })}
      />
      <TextRow
        label="Ergebnis je Eintrag"
        value={loop.outputElement}
        disabled={readOnly}
        placeholder="=ergebnis"
        monospace
        onCommit={(value) => write({ outputElement: value })}
      />
      <TextRow
        label="Abbruchbedingung"
        value={loop.completionCondition}
        disabled={readOnly}
        placeholder="=anzahl > 3"
        monospace
        hint="Trifft sie zu, bricht die Engine die übrigen Durchläufe ab."
        onCommit={(value) => write({ completionCondition: value })}
      />
    </Section>
  );
}
