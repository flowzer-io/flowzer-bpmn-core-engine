import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';

// Alle Stilblätter in fester Reihenfolge; siehe formioStyles.ts.
import './formioStyles';

import {
  ensureFlowzerSubjectContract,
  registerFlowzerSubjectComponent,
} from './FlowzerSubjectComponent';
import { FormDecisionActionsEditor } from './FormDecisionActionsEditor';

import { InlineSpinner } from '@/components/ui/States';
import { cn } from '@/lib/cn';
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
  { schema, onChange, onReadyChange, className },
  ref,
) {
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
        return JSON.stringify(
          ensureFlowzerSubjectContract(writeFormDecisionActions(
            builder.form ?? builder.schema ?? EMPTY_SCHEMA,
            actionsRef.current,
          )),
          null,
          2,
        );
      },
    }),
    [],
  );

  useEffect(() => {
    let disposed = false;

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

      setStatus('loading');

      try {
        const { Formio } = await import('@formio/js');
        registerFlowzerSubjectComponent(Formio);
        if (disposed) return;

        const builder = (await Formio.builder(container, parsed, {
          noDefaultSubmitButton: true,
        })) as unknown as { instance: BuilderInstance } & BuilderInstance;

        if (disposed) {
          builder.destroy?.();
          return;
        }

        const instance = builder.instance ?? builder;
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
      builderRef.current?.destroy();
      builderRef.current = null;
    };
  }, [schema]);

  return (
    <div className={cn('formio-builder formio-surface relative', className)}>
      <div ref={containerRef} />
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
