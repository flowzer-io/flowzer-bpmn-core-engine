import { QueryClientContext } from '@tanstack/react-query';
import { forwardRef, useContext, useEffect, useImperativeHandle, useRef, useState } from 'react';

// Alle Stilblätter in fester Reihenfolge; siehe formioStyles.ts.
import './formioStyles';

import {
  ensureFlowzerSubjectContract,
  registerFlowzerSubjectComponent,
} from './FlowzerSubjectComponent';
import { FormDecisionActionsEditor } from './FormDecisionActionsEditor';
import { FORM_RENDERER_TEXTS_DE } from './formioLanguage';
import { registerFormSectionComponent } from './FormSectionComponent';
import { registerFormLibraryComponent } from './FormLibraryComponent';

import { InlineSpinner } from '@/components/ui/States';
import { cn } from '@/lib/cn';
import { browserFormLanguage } from '@/lib/locale';
import {
  inspectFormDecisionActions,
  writeFormDecisionActions,
  type FormDecisionActionDraft,
} from '@/lib/forms/formDecisionActions';

export interface FormBuilderHandle {
  /** Das aktuelle Schema als JSON-String — genau so erwartet es `FormDto.formData`. */
  getSchema: () => string;
}

interface FormBuilderProps {
  formId?: string;
  schema: string | undefined;
  onChange?: () => void;
  /**
   * Meldet, ob der Editor sein Schema herausgeben kann. Erst dann darf eine Oberflaeche
   * das Speichern anbieten — vorher waere der Knopf eine Zusage, die ins Leere geht.
   */
  onReadyChange?: (ready: boolean) => void;
  className?: string;
}

interface BuilderInstance {
  form: unknown;
  addNewComponent?: (element: HTMLElement) => void;
  schema: unknown;
  on: (event: string, callback: () => void) => void;
  destroy: () => void;
}

const EMPTY_SCHEMA = { display: 'form', components: [] };

/**
 * Form.io-Builder für die Formularpflege.
 *
 * Das Schema wird bewusst als JSON-String durchgereicht, weil die API es in
 * `FormDto.formData` ebenfalls als String speichert — so gibt es keine
 * verlustbehaftete Zwischenrepräsentation.
 */
export const FormBuilder = forwardRef<FormBuilderHandle, FormBuilderProps>(function FormBuilder(
  { schema, formId, onChange, onReadyChange, className },
  ref,
) {
  const queryClient = useContext(QueryClientContext);
  const containerRef = useRef<HTMLDivElement>(null);
  const builderRef = useRef<BuilderInstance | null>(null);
  const onChangeRef = useRef(onChange);
  const onReadyChangeRef = useRef(onReadyChange);
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [error, setError] = useState<string | null>(null);
  const [actions, setActions] = useState<FormDecisionActionDraft[]>([]);
  const actionsRef = useRef<FormDecisionActionDraft[]>([]);

  onChangeRef.current = onChange;
  onReadyChangeRef.current = onReadyChange;

  useImperativeHandle(
    ref,
    () => ({
      getSchema: () => {
        const builder = builderRef.current;
        if (!builder) throw new Error('Der Formular-Editor ist noch nicht bereit.');
        const current = builder.form ?? builder.schema ?? EMPTY_SCHEMA;
        return JSON.stringify(
          ensureFlowzerSubjectContract(writeFormDecisionActions(current, actionsRef.current)),
          null,
          2,
        );
      },
    }),
    [],
  );

  useEffect(() => {
    let disposed = false;
    let ownedInstance: BuilderInstance | null = null;
    // Jede asynchrone Generation besitzt ihr eigenes DOM. Ein verspätetes destroy()
    // darf nur den alten Host leeren, niemals die bereits sichtbare Nachfolgeversion.
    const host = document.createElement('div');

    let parsed: unknown = EMPTY_SCHEMA;
    if (schema && schema.trim().length > 0) {
      try {
        parsed = JSON.parse(schema);
      } catch {
        setStatus('error');
        setError('Das gespeicherte Formular-Schema ist kein gültiges JSON und kann nicht bearbeitet werden.');
        return;
      }
    }
    const actionInspection = inspectFormDecisionActions(parsed);
    actionsRef.current = actionInspection.actions;
    setActions(actionInspection.actions);
    if (actionInspection.hasUnsupportedFragments) {
      setStatus('error');
      setError(
        'Die gespeicherten Entscheidungsaktionen enthalten unbekannte oder unvollständige Werte. '
        + 'Der visuelle Editor wurde gesperrt, damit diese Daten nicht stillschweigend verloren gehen.',
      );
      onReadyChangeRef.current?.(false);
      return;
    }

    async function create() {
      const container = containerRef.current;
      if (!container) return;

      container.appendChild(host);
      setStatus('loading');

      try {
        const { Formio } = await import('@formio/js');
        registerFlowzerSubjectComponent(Formio);
        registerFormLibraryComponent(Formio);
        registerFormSectionComponent(Formio);
        if (disposed) return;

        const builder = (await Formio.builder(host, parsed, {
          noDefaultSubmitButton: true,
          keyboardBuilder: true,
          // Wie im Renderer: Sprache nach dem Browser, die Vorschau spricht dieselbe wie das Formular.
          language: browserFormLanguage(),
          i18n: { de: { ...FORM_RENDERER_TEXTS_DE, searchFields: 'Komponenten suchen', dragAndDropComponent: 'Komponente hierher ziehen oder in der Palette anklicken',
            Basic: 'Felder', Advanced: 'Weitere Felder', Layout: 'Bereiche & Layout', Data: 'Daten', Premium: 'Weitere Komponenten',
            'Text Field': 'Textfeld', 'Text Area': 'Mehrzeiliger Text', Number: 'Zahl', Password: 'Passwort', Checkbox: 'Ja / Nein',
            'Select Boxes': 'Mehrfachauswahl', Select: 'Auswahlliste', Radio: 'Einfachauswahl', Button: 'Schaltfläche',
            component: '– Einstellungen', help: 'Hilfe', save: 'Übernehmen', cancel: 'Abbrechen', remove: 'Entfernen',
            preview: 'Vorschau', showPreview: 'Vorschau anzeigen', hidePreview: 'Vorschau ausblenden' } },
          flowzerAuthoringFormId: formId,
          flowzerQueryClient: queryClient,
        })) as unknown as { instance: BuilderInstance } & BuilderInstance;

        const instance = builder.instance ?? builder;
        if (disposed) {
          instance.destroy?.();
          return;
        }

        ownedInstance = instance;
        builderRef.current = instance;

        instance.on('change', () => onChangeRef.current?.());
        instance.on('saveComponent', () => onChangeRef.current?.());
        instance.on('removeComponent', () => onChangeRef.current?.());

        setStatus('ready');
        setError(null);
        onReadyChangeRef.current?.(true);
      } catch (cause) {
        if (disposed) return;
        setStatus('error');
        setError(cause instanceof Error ? cause.message : 'Der Formular-Editor konnte nicht geladen werden.');
      }
    }

    void create();

    return () => {
      disposed = true;
      onReadyChangeRef.current?.(false);
      ownedInstance?.destroy();
      if (builderRef.current === ownedInstance) builderRef.current = null;
      host.remove();
    };
  }, [schema, formId, queryClient]);

  return (
    <div className={cn('formio-builder formio-surface relative', className)}>
      <div ref={containerRef} onClick={(event) => {
        // Derselbe Herstellerpfad wie bei Enter: auch per Klick/Touch ohne Drag-and-drop einfügen.
        const paletteItem = (event.target as HTMLElement).closest<HTMLElement>('[ref="sidebar-component"]');
        if (paletteItem && containerRef.current?.contains(paletteItem)) builderRef.current?.addNewComponent?.(paletteItem);
      }} />
      {status === 'loading' && (
        <div className="py-8">
          <InlineSpinner label="Formular-Editor wird geladen …" />
        </div>
      )}
      {status === 'error' && (
        <div className="border-border text-fail rounded-[var(--r)] border border-dashed px-4 py-6 text-center text-[13.5px]">
          {error}
        </div>
      )}
      {status === 'ready' && (
        <FormDecisionActionsEditor
          actions={actions}
          onChange={(next) => {
            actionsRef.current = next;
            setActions(next);
            onChangeRef.current?.();
          }}
        />
      )}
    </div>
  );
});
