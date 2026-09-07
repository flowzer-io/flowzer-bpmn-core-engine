import { useState } from 'react';

import { Icon } from '@/components/ui/Icon';
import { useForms } from '@/lib/api/queries';
import { nodeTypeIcon, nodeTypeLabel } from '@/lib/bpmnModel';
import { parseFormKey } from '@/lib/formKey';

import type { BpmnEditor } from '../bpmnEditor';
import {
  AssignmentSection,
  CallActivitySection,
  FlowSection,
  FormSection,
  GatewaySection,
  GeneralSection,
  JobSection,
  MappingsSection,
  MessageSection,
  MultiInstanceSection,
  ScheduleSection,
  ScriptSection,
  SignalSection,
  TimerSection,
} from './ElementSections';
import { EmbeddedFormDialog } from './EmbeddedFormDialog';
import { Section } from './PropertyFields';
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

/**
 * Das Eigenschaften-Panel des Modelers.
 *
 * Diese Datei entscheidet nur, welche Abschnitte ein Element bekommt; die Abschnitte selbst
 * stehen in `ElementSections.tsx`. Gezeigt wird, was die Engine auswertet, benannt in der
 * Sprache der Konsole. Das mitgelieferte Camunda-Panel führte dagegen den vollen
 * Zeebe-Umfang samt Feldern, die diese Engine gar nicht liest — was modelliert werden
 * konnte, lief hinterher nicht.
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
  const section = properties ? { properties, editor, readOnly } : null;

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

      {properties && section && (
        <>
          <PanelHeader
            icon={nodeTypeIcon(localName(properties.type))}
            title={properties.name.trim() || properties.id}
            subtitle={nodeTypeLabel(localName(properties.type))}
          />

          <GeneralSection {...section} />

          {properties.kind === 'userTask' && (
            <FormSection
              {...section}
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
              <AssignmentSection {...section} />
              <ScheduleSection {...section} />
            </>
          )}

          {properties.timer && <TimerSection {...section} />}
          {properties.message && <MessageSection {...section} />}
          {properties.signalName !== null && <SignalSection {...section} />}
          {properties.calledProcess && <CallActivitySection {...section} />}
          {properties.isScriptTask && <ScriptSection {...section} />}
          {properties.needsJobType && <JobSection {...section} />}

          {(properties.supportsInputMappings || properties.supportsOutputMappings) && (
            <MappingsSection {...section} />
          )}

          {properties.multiInstance && <MultiInstanceSection {...section} />}
          {properties.kind === 'gateway' && <GatewaySection {...section} onSelect={onSelect} />}
          {properties.kind === 'sequenceFlow' && <FlowSection {...section} />}
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

/** `bpmn:UserTask` → `userTask`, damit die Bezeichner aus `bpmnModel` greifen. */
function localName(type: string): string {
  const name = type.split(':').pop() ?? type;
  return name.charAt(0).toLowerCase() + name.slice(1);
}
