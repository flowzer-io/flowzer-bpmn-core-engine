import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';

// Alle Stilblätter in fester Reihenfolge; siehe formioStyles.ts.
import './formioStyles';

import { InlineSpinner } from '@/components/ui/States';
import { cn } from '@/lib/cn';
import type { ProcessVariables } from '@/lib/api/types';

export interface FormRendererHandle {
  /** Aktuelle Eingabedaten des Formulars. */
  getData: () => ProcessVariables;
  /** Prüft alle Felder und meldet, ob das Formular gültig ist. */
  validate: () => Promise<boolean>;
}

interface FormRendererProps {
  /** Form.io-Schema als JSON-String (so liefert es die API in `FormDto.formData`). */
  schema: string | undefined;
  initialData?: ProcessVariables;
  readOnly?: boolean;
  onChange?: (data: ProcessVariables) => void;
  className?: string;
}

interface FormioInstance {
  submission: { data: ProcessVariables };
  on: (event: string, callback: (payload: unknown) => void) => void;
  checkValidity: (data: ProcessVariables, dirty: boolean, rowData: unknown) => boolean;
  destroy: () => void;
}

function parseSchema(schema: string | undefined): { value: unknown | null; error: string | null } {
  if (!schema || schema.trim().length === 0) {
    return { value: null, error: 'Für dieses Formular ist noch kein Schema hinterlegt.' };
  }

  try {
    return { value: JSON.parse(schema) as unknown, error: null };
  } catch {
    return { value: null, error: 'Das Formular-Schema ist kein gültiges JSON.' };
  }
}

/**
 * Rendert ein Form.io-Formular.
 *
 * Form.io wird dynamisch nachgeladen: die Bibliothek ist groß und wird nur auf
 * der Aufgaben- und der Formularseite gebraucht. Die eingebauten Absende-Buttons
 * bleiben ausgeblendet — abgeschickt wird über die Aktionsleiste der Konsole,
 * damit „Freigeben“ und „Ablehnen“ als eigene Prozessentscheidungen sichtbar sind.
 */
export const FormRenderer = forwardRef<FormRendererHandle, FormRendererProps>(function FormRenderer(
  { schema, initialData, readOnly = false, onChange, className },
  ref,
) {
  const containerRef = useRef<HTMLDivElement>(null);
  const instanceRef = useRef<FormioInstance | null>(null);
  const onChangeRef = useRef(onChange);
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [error, setError] = useState<string | null>(null);

  onChangeRef.current = onChange;

  useImperativeHandle(
    ref,
    () => ({
      getData: () => instanceRef.current?.submission.data ?? {},
      validate: async () => {
        const instance = instanceRef.current;
        if (!instance) return false;
        return instance.checkValidity(instance.submission.data, true, {});
      },
    }),
    [],
  );

  // Der Erstwert soll das Formular nicht bei jeder Elternaktualisierung neu aufbauen.
  const initialDataKey = JSON.stringify(initialData ?? {});

  useEffect(() => {
    let disposed = false;
    const parsed = parseSchema(schema);

    if (parsed.error) {
      setStatus('error');
      setError(parsed.error);
      return;
    }

    async function create() {
      const container = containerRef.current;
      if (!container) return;

      setStatus('loading');

      try {
        const { Formio } = await import('@formio/js');
        if (disposed) return;

        const form = (await Formio.createForm(container, parsed.value, {
          readOnly,
          noAlerts: true,
          // Der eingebaute Submit-Button würde mit den Prozessaktionen konkurrieren.
          buttonSettings: { showCancel: false, showSubmit: false },
        })) as unknown as FormioInstance;

        if (disposed) {
          form.destroy();
          return;
        }

        instanceRef.current = form;

        if (initialData && Object.keys(initialData).length > 0) {
          form.submission = { data: { ...initialData } };
        }

        form.on('change', () => {
          onChangeRef.current?.(form.submission.data);
        });

        setStatus('ready');
        setError(null);
      } catch (cause) {
        if (disposed) return;
        setStatus('error');
        setError(cause instanceof Error ? cause.message : 'Das Formular konnte nicht geladen werden.');
      }
    }

    void create();

    return () => {
      disposed = true;
      instanceRef.current?.destroy();
      instanceRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [schema, readOnly, initialDataKey]);

  return (
    <div className={cn('formio-surface relative', className)}>
      <div ref={containerRef} />
      {status === 'loading' && (
        <div className="py-8">
          <InlineSpinner label="Formular wird geladen …" />
        </div>
      )}
      {status === 'error' && (
        <div className="border-border text-muted rounded-[var(--r)] border border-dashed px-4 py-6 text-center text-[13.5px]">
          {error}
        </div>
      )}
    </div>
  );
});
