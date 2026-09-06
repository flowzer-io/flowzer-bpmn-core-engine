import { useState } from 'react';

import { Button } from '@/components/ui/Button';
import { Chip } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { Segmented } from '@/components/ui/Segmented';
import { useForms } from '@/lib/api/queries';
import { nodeTypeIcon, nodeTypeLabel } from '@/lib/bpmnModel';
import { embeddedFormKey, newEmbeddedFormId, parseFormKey, storedFormKey } from '@/lib/formKey';

import type { BpmnEditor, ElementProperties } from '../bpmnEditor';
import { EmbeddedFormDialog } from './EmbeddedFormDialog';
import { IoMappingEditor, Notice, SelectRow, Section, TextRow } from './PropertyFields';
import { WorkflowForms } from './WorkflowForms';

interface BpmnPropertiesProps {
  editor: BpmnEditor | null;
  /** Das ausgewählte Element; `null` steht für die Sicht auf den ganzen Workflow. */
  selectedId: string | null;
  /**
   * Zählt bei jeder Modelländerung hoch. Das Panel liest daraufhin neu — bpmn-js hält den
   * Zustand im Modell, nicht in React, und meldet Änderungen nur als Ereignis.
   */
  revision: number;
  onSelect: (elementId: string) => void;
  readOnly?: boolean;
}

const FORM_SOURCE_OPTIONS = [
  { value: 'stored' as const, label: 'Aus dem Bestand' },
  { value: 'embedded' as const, label: 'In diesem Workflow' },
];

const NEW_EMBEDDED_FORM = '__neu__';

/**
 * Das Eigenschaften-Panel des Modelers.
 *
 * Es zeigt bewusst nur, was Flowzer auswertet, und benennt es in der Sprache der Konsole:
 * Formular, Zuweisung, Frist, Zuordnungen, Auftrag, Bedingungen. Das mitgelieferte
 * Camunda-Panel führte dagegen den vollen Zeebe-Umfang samt Feldern, die diese Engine gar
 * nicht liest — was modelliert werden konnte, lief hinterher nicht.
 */
export function BpmnProperties({ editor, selectedId, revision, onSelect, readOnly = false }: BpmnPropertiesProps) {
  const formsQuery = useForms();
  const [formTab, setFormTab] = useState<{ elementId: string; source: 'stored' | 'embedded' } | null>(null);
  const [editingFormId, setEditingFormId] = useState<string | null>(null);

  // `revision` wird nicht gelesen, sondern loest als Prop das Neuzeichnen aus; erst dadurch
  // stimmen die Werte unten wieder mit dem Modell ueberein.
  void revision;

  const properties = editor && selectedId ? editor.read(selectedId) : null;
  const embeddedForms = editor?.listEmbeddedForms() ?? [];
  const userTasks = editor?.listUserTasks() ?? [];
  const storedFormNames = (formsQuery.data ?? []).map((form) => form.name);
  const editingForm = embeddedForms.find((form) => form.id === editingFormId);

  function saveEmbeddedForm(schema: string) {
    if (!editor || !editingFormId) return;
    editor.saveEmbeddedForm(editingFormId, schema, selectedId ?? undefined);
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {!properties && (
        <>
          <PanelHeader icon="account_tree" title="Workflow" subtitle="Kein Element ausgewählt" />
          <WorkflowForms
            userTasks={userTasks}
            embeddedForms={embeddedForms}
            storedFormNames={storedFormNames}
            onSelectTask={onSelect}
            onEditEmbeddedForm={setEditingFormId}
            onRemoveEmbeddedForm={(formId) => editor?.removeEmbeddedForm(formId)}
            readOnly={readOnly}
          />
          <Section icon="info" title="Hinweis">
            <p className="text-muted m-0 text-[12px] leading-normal">
              Wähle ein Element im Diagramm, um seine Eigenschaften zu bearbeiten.
            </p>
          </Section>
        </>
      )}

      {properties && (
        <>
          <PanelHeader
            icon={nodeTypeIcon(localName(properties.type))}
            title={properties.name.trim() || properties.id}
            subtitle={nodeTypeLabel(localName(properties.type))}
          />

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

          {properties.kind === 'userTask' && (
            <FormSection
              properties={properties}
              editor={editor}
              readOnly={readOnly}
              storedFormNames={storedFormNames}
              embeddedForms={embeddedForms.map((form) => form.id)}
              source={
                formTab?.elementId === properties.id
                  ? formTab.source
                  : parseFormKey(properties.formKey).kind === 'embedded'
                    ? 'embedded'
                    : 'stored'
              }
              onSourceChange={(source) => setFormTab({ elementId: properties.id, source })}
              onEditEmbeddedForm={setEditingFormId}
            />
          )}

          {properties.kind === 'userTask' && (
            <>
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
                  onCommit={(value) =>
                    editor?.setAssignment(properties.id, { ...assignmentOf(properties), assignee: value })
                  }
                />
                <TextRow
                  label="Gruppen"
                  value={properties.candidateGroups}
                  disabled={readOnly}
                  placeholder="einkauf, buchhaltung"
                  hint="Mehrere durch Komma getrennt."
                  onCommit={(value) =>
                    editor?.setAssignment(properties.id, { ...assignmentOf(properties), candidateGroups: value })
                  }
                />
                <TextRow
                  label="Personen"
                  value={properties.candidateUsers}
                  disabled={readOnly}
                  placeholder="anna, bruno"
                  hint="Mehrere durch Komma getrennt."
                  onCommit={(value) =>
                    editor?.setAssignment(properties.id, { ...assignmentOf(properties), candidateUsers: value })
                  }
                />
              </Section>

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
                  onCommit={(value) =>
                    editor?.setSchedule(properties.id, { ...scheduleOf(properties), dueDate: value })
                  }
                />
                <TextRow
                  label="Wiedervorlage"
                  value={properties.followUpDate}
                  disabled={readOnly}
                  placeholder="PT24H"
                  monospace
                  onCommit={(value) =>
                    editor?.setSchedule(properties.id, { ...scheduleOf(properties), followUpDate: value })
                  }
                />
              </Section>
            </>
          )}

          {properties.kind === 'serviceTask' && (
            <Section
              icon="api"
              title="Auftrag"
              hint="Der Auftragstyp verbindet den Schritt mit dem Worker, der ihn ausführt."
            >
              <TextRow
                label="Auftragstyp"
                value={properties.jobType}
                disabled={readOnly}
                placeholder="rechnung-pruefen"
                monospace
                onCommit={(value) => editor?.setJob(properties.id, value, properties.retries)}
              />
              {properties.jobType.trim().length === 0 && (
                <Notice tone="warn">
                  Ohne Auftragstyp findet kein Worker diesen Schritt — die Instanz bliebe hier stehen.
                </Notice>
              )}
              <TextRow
                label="Wiederholungen"
                value={properties.retries}
                disabled={readOnly}
                placeholder="3"
                monospace
                hint="Wie oft ein fehlgeschlagener Auftrag erneut vergeben wird. Leer bedeutet einmalig."
                onCommit={(value) => editor?.setJob(properties.id, properties.jobType, value)}
              />
            </Section>
          )}

          {(properties.kind === 'userTask' || properties.kind === 'serviceTask') && (
            <Section
              icon="data_object"
              title="Zuordnungen"
              hint="Eingang bringt Prozessdaten in den Schritt, Ausgang schreibt sein Ergebnis zurück."
            >
              <IoMappingEditor
                label="Eingang"
                sourceLabel="=antrag.betrag"
                targetLabel="betrag"
                value={properties.inputs}
                disabled={readOnly}
                onChange={(inputs) => editor?.setIoMappings(properties.id, inputs, properties.outputs)}
              />
              <IoMappingEditor
                label="Ausgang"
                sourceLabel="=entscheidung"
                targetLabel="antrag.status"
                value={properties.outputs}
                disabled={readOnly}
                onChange={(outputs) => editor?.setIoMappings(properties.id, properties.inputs, outputs)}
              />
            </Section>
          )}

          {properties.kind === 'gateway' && (
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

                  {!flow.isDefault && (
                    <Button
                      size="sm"
                      variant="ghost"
                      icon="check"
                      className="mt-1.5 px-1"
                      disabled={readOnly}
                      onClick={() => editor?.setDefaultFlow(properties.id, flow.id)}
                    >
                      Als Standardfluss
                    </Button>
                  )}
                  {flow.isDefault && (
                    <Button
                      size="sm"
                      variant="ghost"
                      icon="close"
                      className="mt-1.5 px-1"
                      disabled={readOnly}
                      onClick={() => editor?.setDefaultFlow(properties.id, null)}
                    >
                      Standardfluss aufheben
                    </Button>
                  )}
                </div>
              ))}
            </Section>
          )}

          {properties.uncovered.length > 0 && (
            <Section icon="info" title="Nicht in der Konsole">
              <Notice>
                Dieses Element hat Angaben, die die Engine auswertet, die Konsole aber noch nicht
                bearbeitet: {properties.uncovered.join(', ')}. Sie stehen im BPMN und bleiben beim
                Speichern erhalten — ändern lassen sie sich derzeit nur im Camunda Modeler.
              </Notice>
            </Section>
          )}

          {properties.kind === 'sequenceFlow' && (
            <Section
              icon="call_split"
              title="Bedingung"
              hint={
                properties.conditionApplies
                  ? 'Trifft die Bedingung zu, nimmt die Instanz diesen Weg.'
                  : undefined
              }
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
          )}
        </>
      )}

      <EmbeddedFormDialog
        open={editingFormId !== null}
        onOpenChange={(open) => {
          if (!open) setEditingFormId(null);
        }}
        formId={editingFormId ?? ''}
        schema={editingForm?.schema ?? ''}
        onSave={saveEmbeddedForm}
      />
    </div>
  );
}

function PanelHeader({ icon, title, subtitle }: { icon: string; title: string; subtitle: string }) {
  return (
    <div className="border-border bg-surface-2 flex items-center gap-2.5 border-b px-4 py-3">
      <span className="bg-surface text-accent grid h-8 w-8 flex-none place-items-center rounded-[9px]">
        <Icon name={icon} size={17} />
      </span>
      <div className="min-w-0">
        <div className="truncate text-[13.5px] font-semibold">{title}</div>
        <div className="text-muted text-[11.5px]">{subtitle}</div>
      </div>
    </div>
  );
}

interface FormSectionProps {
  properties: ElementProperties;
  editor: BpmnEditor | null;
  readOnly: boolean;
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
function FormSection({
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
            <Button
              size="sm"
              icon="edit"
              disabled={readOnly}
              onClick={() => onEditEmbeddedForm(selectedEmbeddedId)}
            >
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

function assignmentOf(properties: ElementProperties) {
  return {
    assignee: properties.assignee,
    candidateGroups: properties.candidateGroups,
    candidateUsers: properties.candidateUsers,
  };
}

function scheduleOf(properties: ElementProperties) {
  return { dueDate: properties.dueDate, followUpDate: properties.followUpDate };
}

/** `bpmn:UserTask` → `userTask`, damit die Bezeichner aus `bpmnModel` greifen. */
function localName(type: string): string {
  const name = type.split(':').pop() ?? type;
  return name.charAt(0).toLowerCase() + name.slice(1);
}
