/* eslint-disable react-refresh/only-export-components -- Form.io registriert React-Bruecke und Vertragshilfen gemeinsam. */
import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';

import { componentEditForm } from './componentEditForm';
import { registerSubjectSettings } from './registerSubjectSettings';
import { authoringDirectoryAdapter } from './authoringDirectoryAdapter';
import type { SubjectSelectionPolicy } from './SubjectSelectionSettings';

import { createRoot, type Root } from 'react-dom/client';

import {
  DirectorySubjectPicker,
  type BoundDirectorySubjectAdapter,
  type DirectorySubjectSelection,
} from '@/components/bpmn/properties/DirectorySubjectPicker';
import type { FormDirectorySearchContext, SubjectRefDto } from '@/lib/api/types';


interface FlowzerSubjectSchema {
  type?: string;
  key?: string;
  label?: string;
  multiple?: boolean;
  flowzer?: {
    subjectSelection?: SubjectSelectionPolicy;
    subjectDisplay?: Record<string, boolean>;
  };
}

/** Bindet beim Speichern den hoechsten benoetigten Root-Vertrag und sichere Datagrid-Grenzen. */
export function ensureFlowzerSubjectContract(schema: unknown): unknown {
  if (!schema || typeof schema !== 'object' || Array.isArray(schema)) return schema;
  const root = schema as { components?: unknown; flowzer?: Record<string, unknown> };
  function requiredVersion(value: unknown): number {
    if (!value || typeof value !== 'object') return 1;
    if (Array.isArray(value)) return value.reduce((maximum, item) => Math.max(maximum, requiredVersion(item)), 1);
    const entry = value as Record<string, unknown>;
    if (entry.type === 'datagrid') return 3;
    const own = entry.type === 'flowzerSubject' ? 2 : 1;
    return ['components', 'columns', 'rows'].reduce(
      (maximum, key) => Math.max(maximum, requiredVersion(entry[key])),
      own,
    );
  }
  function bindRepeatPolicy(value: unknown): unknown {
    if (Array.isArray(value)) return value.map(bindRepeatPolicy);
    if (!value || typeof value !== 'object') return value;
    const entry = value as Record<string, unknown>;
    const normalized = Object.fromEntries(Object.entries(entry).map(([key, child]) => [
      key,
      ['components', 'columns', 'rows'].includes(key) ? bindRepeatPolicy(child) : child,
    ]));
    if (entry.type !== 'datagrid') return normalized;
    const validate = entry.validate && typeof entry.validate === 'object' && !Array.isArray(entry.validate)
      ? entry.validate as Record<string, unknown>
      : {};
    const flowzer = entry.flowzer && typeof entry.flowzer === 'object' && !Array.isArray(entry.flowzer)
      ? entry.flowzer as Record<string, unknown>
      : {};
    const repeat = flowzer.repeat && typeof flowzer.repeat === 'object' && !Array.isArray(flowzer.repeat)
      ? flowzer.repeat as Record<string, unknown>
      : {};
    const formioMinimum = Number.isInteger(validate.minLength) && Number(validate.minLength) >= 0
      ? Number(validate.minLength)
      : validate.required === true ? 1 : 0;
    const formioMaximum = Number.isInteger(validate.maxLength) && Number(validate.maxLength) > 0
      ? Number(validate.maxLength)
      : 20;
    return {
      ...normalized,
      flowzer: {
        ...flowzer,
        repeat: {
          minItems: repeat.minItems ?? formioMinimum,
          maxItems: repeat.maxItems ?? formioMaximum,
        },
      },
    };
  }
  const components = bindRepeatPolicy(root.components);
  const hasActions = Array.isArray(root.flowzer?.actions) && root.flowzer.actions.length > 0;
  const version = Math.max(requiredVersion(components), hasActions ? 4 : 1);
  if (version === 1) return schema;
  return { ...root, components, flowzer: { ...(root.flowzer ?? {}), contractVersion: version } };
}

function subjectFromValue(value: unknown): SubjectRefDto[] {
  const values = Array.isArray(value) ? value : value ? [value] : [];
  return values.flatMap((entry) => {
    if (!entry || typeof entry !== 'object') return [];
    const candidate = entry as Partial<SubjectRefDto>;
    if ((candidate.kind !== 'user' && candidate.kind !== 'group') || typeof candidate.id !== 'string') return [];
    return [{ kind: candidate.kind, id: candidate.id }];
  });
}

/** Form.io erhält exakt einen SubjectRef oder ein Array daraus, niemals Anzeigeobjekte. */
export function toSubjectRefValue(
  selected: DirectorySubjectSelection[],
  multiple: boolean,
): SubjectRefDto | SubjectRefDto[] | null {
  const refs = selected.map((entry) => entry.subject);
  return multiple ? refs : refs[0] ?? null;
}

interface FlowzerSubjectBridgeProps {
  initialSelected: DirectorySubjectSelection[];
  pickerProps: Omit<React.ComponentProps<typeof DirectorySubjectPicker>, 'selected' | 'onChange'>;
  onChange: (selected: DirectorySubjectSelection[]) => void;
}

/** Verbindet den kontrollierten Picker mit dem Form.io-Wert. */
function FlowzerSubjectBridge({ initialSelected, pickerProps, onChange }: FlowzerSubjectBridgeProps) {
  return (
    <DirectorySubjectPicker
      {...pickerProps}
      selected={initialSelected}
      onChange={onChange}
    />
  );
}

/** Registriert das einzige Flowzer-spezifische Form.io-Feld. */
// Form.io liefert seine Klassen erst nach dem dynamischen Import; der enge any-Cast ist hier
// die einzige Grenze zur untypisierten Drittanbieter-API.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function registerFlowzerSubjectComponent(Formio: any): void {
  registerSubjectSettings(Formio);
  const Components = Formio.Components;
  if (!Components?.components?.field || Components.components.flowzerSubject) return;

  const BaseField = Components.components.field;

  class FlowzerSubjectComponent extends BaseField {
    static schema(...extend: unknown[]) {
      return BaseField.schema(
        {
          type: 'flowzerSubject',
          label: 'Benutzer- oder Gruppenauswahl',
          key: 'subjects',
          input: true,
          multiple: false,
          flowzer: {
            subjectSelection: {
              allowUsers: true,
              allowGroups: false,
              activeOnly: true,
              includeSubgroups: false,
              allowedUserIds: [],
              userMemberOfGroupIds: [],
              allowedGroupIds: [],
            },
          },
        },
        ...extend,
      );
    }

    static get builderInfo() {
      return {
        title: 'Benutzer-/Gruppenauswahl',
        group: 'basic',
        icon: 'people',
        weight: 25,
        schema: FlowzerSubjectComponent.schema(),
      };
    }

    /** Deutsche, bewusst kleine Konfiguration statt eines undurchsichtigen JSON-Feldes. */
    static editForm() {
      return componentEditForm([
        { type: 'textfield', key: 'label', label: 'Beschriftung', input: true },
        { type: 'checkbox', key: 'multiple', label: 'Mehrere Benutzer oder Gruppen erlauben', input: true },
        { type: 'flowzerSubjectSettings', key: 'flowzer.subjectSelection', label: 'Auswahl und Filter', hideLabel: true, input: true },
        { type: 'selectboxes', key: 'flowzer.subjectDisplay', label: 'Informationen zu Benutzern anzeigen', input: true,
          defaultValue: { name: true, email: true }, values: [
            { label: 'Name', value: 'name' }, { label: 'E-Mail-Adresse', value: 'email' },
            { label: 'Benutzername', value: 'username' }, { label: 'Vorname', value: 'firstName' }, { label: 'Nachname', value: 'lastName' },
          ], description: 'Gruppen zeigen ihren Namen und vollständigen Pfad. Fehlende Profilangaben werden ausgelassen.' },
        { type: 'panel', key: 'advanced', title: 'Erweitert', collapsible: true, collapsed: true, components: [
          { type: 'textfield', key: 'key', label: 'Technischer Schlüssel', input: true },
          { type: 'checkbox', key: 'validate.required', label: 'Pflichtfeld', input: true },
          { type: 'number', key: 'validate.minSelectedCount', label: 'Mindestanzahl bei Mehrfachauswahl', input: true, validate: { min: 0 } },
          { type: 'number', key: 'validate.maxSelectedCount', label: 'Höchstanzahl bei Mehrfachauswahl', input: true, validate: { min: 1 } },
        ] },
      ]);
    }

    private pickerRoot: Root | null = null;
    private authoringAdapter: BoundDirectorySubjectAdapter | undefined;
    private authoringSchema = '';
    private pickerHost: Element | null = null;

    get emptyValue() {
      return this.component.multiple ? [] : null;
    }

    render() {
      return super.render('<div data-flowzer-subject-picker="true"></div>');
    }

    /** Rendert auch nach einem externen Form.io-setValue, etwa beim Laden einer Einreichung. */
    private renderPicker() {
      if (!this.pickerRoot || !this.pickerHost) return;
      const component = this.component as FlowzerSubjectSchema;
      const policy = component.flowzer?.subjectSelection ?? {};
      const context = this.options?.flowzerDirectoryContext as FormDirectorySearchContext | undefined;
      let directoryAdapter = this.options?.flowzerDirectoryAdapter as BoundDirectorySubjectAdapter | undefined;
      const authoringFormId = this.options?.flowzerAuthoringFormId as string | undefined;
      if (!directoryAdapter && authoringFormId) {
        // Die Komponenten-Vorschau testet genau diesen lokalen Feldvertrag, ohne Instanz.
        const schema = JSON.stringify({ flowzer: { contractVersion: 2 }, components: [component] });
        if (schema !== this.authoringSchema) {
          this.authoringSchema = schema;
          this.authoringAdapter = authoringDirectoryAdapter(authoringFormId, schema);
        }
        directoryAdapter = this.authoringAdapter;
      }
      // Aufgabenformulare dürfen ihren Task-Kontext ausschließlich über den
      // gebundenen Host-Adapter erhalten. Der alte Context-Hook bleibt nur für
      // Startformulare und damit außerhalb des verschachtelten Task-Roots aktiv.
      const startFormContext = context?.kind === 'startForm' ? context : undefined;
      const queryClient = this.options?.flowzerQueryClient as QueryClient | undefined;
      // Ohne gebundenen Laufzeit- oder Modellierer-Kontext keine Directory-Suche.
      // Auch deaktivierte Query-Hooks benötigen einen Provider: hier nur einen Hinweis rendern.
      if (!directoryAdapter && (!startFormContext || !queryClient)) {
        this.pickerRoot.render(<p role="note">
          Benutzer-/Gruppenauswahl: Die Verzeichnissuche ist nur im gebundenen Start- oder Aufgabenformular verfügbar.
        </p>);
        return;
      }
      const allowUsers = policy.allowUsers !== false;
      const allowGroups = policy.allowGroups === true;
      const kind = allowUsers && allowGroups ? 'all' : allowGroups ? 'group' : 'user';
      const selected = subjectFromValue(this.dataValue).map((subject) => ({
        subject,
        displayName: subject.id,
        detail: 'Stabile Verzeichnis-ID',
        available: false,
      }));
      const picker = (
        <FlowzerSubjectBridge
          initialSelected={selected}
          pickerProps={{
            definitionId: startFormContext?.definitionId ?? '',
            ...(directoryAdapter
              ? { directoryAdapter }
              : startFormContext ? { directoryContext: startFormContext } : {}),
            fieldKey: component.key,
            kind,
            multiple: component.multiple === true,
            disabled: Boolean(this.options?.readOnly) || (!allowUsers && !allowGroups)
              || (!startFormContext && !directoryAdapter),
            disabledReason: !startFormContext && !directoryAdapter
              ? 'Die Verzeichnissuche ist nur im gebundenen Start- oder Aufgabenformular verfügbar.'
              : undefined,
            label: component.label ?? 'Benutzer oder Gruppe',
            hideLabel: true,
            displayFields: component.flowzer?.subjectDisplay ?? { name: true, email: true },
          }}
          onChange={(next) => {
            this.setValue(toSubjectRefValue(next, component.multiple === true));
          }}
        />
      );
      // createRoot erbt keinen Kontext des Formular-Hosts. Den vorhandenen Cache
      // weiterreichen, nicht je Feld einen neuen QueryClient oder neue Rechte erzeugen.
      this.pickerRoot.render(startFormContext && queryClient
        ? <QueryClientProvider client={queryClient}>{picker}</QueryClientProvider>
        : picker);
    }

    attach(element: HTMLElement) {
      const result = super.attach(element);
      const host = element.querySelector('[data-flowzer-subject-picker="true"]');
      if (!host) return result;

      this.pickerRoot?.unmount();
      this.pickerHost = host;
      this.pickerRoot = createRoot(host);
      this.renderPicker();
      return result;
    }

    setValue(value: unknown, flags?: unknown) {
      const changed = super.setValue(value, flags);
      this.renderPicker();
      return changed;
    }

    destroy(all?: boolean) {
      this.pickerRoot?.unmount();
      this.pickerRoot = null;
      this.pickerHost = null;
      return super.destroy(all);
    }
  }

  Components.addComponent('flowzerSubject', FlowzerSubjectComponent);
}
