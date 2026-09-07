import { Button } from '@/components/ui/Button';
import { Chip } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { describeFormKey, parseFormKey } from '@/lib/formKey';

import type { EmbeddedForm, UserTaskReference } from '../bpmnEditor';
import { Notice, Section } from './PropertyFields';

/** Ein Formular, wie es im Workflow vorkommt — mit den Aufgaben, die es benutzen. */
interface FormUsage {
  key: string;
  label: string;
  origin: 'embedded' | 'stored';
  /** Das Formular ist zwar verwiesen, aber weder im Workflow noch im Bestand auffindbar. */
  missing: boolean;
  tasks: UserTaskReference[];
}

interface WorkflowFormsProps {
  userTasks: UserTaskReference[];
  embeddedForms: EmbeddedForm[];
  /** Namen der Formulare im Bestand — für die Prüfung, ob ein Verweis ins Leere zeigt. */
  storedFormNames: string[];
  onSelectTask: (elementId: string) => void;
  onEditEmbeddedForm: (formId: string) => void;
  onRemoveEmbeddedForm: (formId: string) => void;
  readOnly: boolean;
}

/**
 * Übersicht, welche Formulare dieser Workflow benutzt (Aufgabe „Formulare im Workflow").
 *
 * Sie beantwortet drei Fragen auf einen Blick: Welche Formulare hängen an diesem Workflow,
 * welche Aufgabe benutzt welches, und wo zeigt ein Verweis ins Leere. Letzteres ist der
 * eigentliche Grund für die Liste — ein Formular, das erst beim Bearbeiten der Aufgabe
 * fehlt, fällt sonst niemandem vor dem Deploy auf.
 */
export function WorkflowForms({
  userTasks,
  embeddedForms,
  storedFormNames,
  onSelectTask,
  onEditEmbeddedForm,
  onRemoveEmbeddedForm,
  readOnly,
}: WorkflowFormsProps) {
  const usages = collectUsages(userTasks, embeddedForms, storedFormNames);
  const withoutForm = userTasks.filter((task) => parseFormKey(task.formKey).kind === 'none');
  const unusedForms = embeddedForms.filter(
    (form) =>
      !userTasks.some((task) => {
        const reference = parseFormKey(task.formKey);
        return reference.kind === 'embedded' && reference.formId === form.id;
      }),
  );

  return (
    <Section
      icon="description"
      title="Formulare in diesem Workflow"
      hint={
        userTasks.length === 0
          ? 'Der Workflow hat noch keine menschliche Aufgabe.'
          : 'Jede menschliche Aufgabe zeigt über ihren Form-Key auf ein Formular.'
      }
    >
      {usages.map((usage) => (
        <div key={usage.key} className="border-border rounded-[var(--r-sm)] border p-2.5">
          <div className="flex items-start gap-2">
            <Icon
              name="description"
              size={16}
              className={usage.missing ? 'text-fail mt-px' : 'text-accent mt-px'}
            />
            <div className="min-w-0 flex-1">
              <div className="truncate text-[13px] font-semibold">{usage.label}</div>
              <div className="mt-1 flex flex-wrap items-center gap-1.5">
                <Chip tone={usage.origin === 'embedded' ? 'accent' : 'muted'}>
                  {usage.origin === 'embedded' ? 'Im Workflow' : 'Aus dem Bestand'}
                </Chip>
                {usage.missing && <Chip tone="fail">Nicht gefunden</Chip>}
              </div>
            </div>
            {usage.origin === 'embedded' && !usage.missing && (
              <Button
                size="sm"
                variant="ghost"
                icon="edit"
                className="px-1.5"
                disabled={readOnly}
                onClick={() => onEditEmbeddedForm(usage.key)}
              >
                <span className="sr-only">{usage.label} bearbeiten</span>
              </Button>
            )}
          </div>

          <ul className="m-0 mt-2 flex list-none flex-col gap-1 p-0">
            {usage.tasks.map((task) => (
              <li key={task.id}>
                <button
                  type="button"
                  onClick={() => onSelectTask(task.id)}
                  className="text-muted hover:text-accent flex w-full cursor-pointer items-center gap-1.5 border-none bg-transparent p-0 text-left text-[12px]"
                >
                  <Icon name="person" size={13} className="flex-none" />
                  <span className="truncate">{task.name}</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      ))}

      {usages.length === 0 && userTasks.length > 0 && (
        <Notice tone="warn">Keine der Aufgaben verweist bisher auf ein Formular.</Notice>
      )}

      {withoutForm.length > 0 && usages.length > 0 && (
        <Notice tone="warn">
          Ohne Formular: {withoutForm.map((task) => task.name).join(', ')}. Der Workflow lässt sich erst
          speichern, wenn jede menschliche Aufgabe ein Formular hat.
        </Notice>
      )}

      {unusedForms.length > 0 && (
        <div>
          <p className="text-muted m-0 mb-1.5 text-[12px]">
            Im Workflow gespeichert, aber von keiner Aufgabe benutzt:
          </p>
          <div className="flex flex-col gap-1.5">
            {unusedForms.map((form) => (
              <div key={form.id} className="flex items-center gap-1.5">
                <span className="text-muted min-w-0 flex-1 truncate font-mono text-[12px]">{form.id}</span>
                <Button
                  size="sm"
                  variant="ghost"
                  icon="edit"
                  className="px-1.5"
                  disabled={readOnly}
                  onClick={() => onEditEmbeddedForm(form.id)}
                >
                  <span className="sr-only">{form.id} bearbeiten</span>
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  icon="delete"
                  className="px-1.5"
                  disabled={readOnly}
                  onClick={() => onRemoveEmbeddedForm(form.id)}
                >
                  <span className="sr-only">{form.id} aus dem Workflow entfernen</span>
                </Button>
              </div>
            ))}
          </div>
        </div>
      )}
    </Section>
  );
}

/** Fasst die Aufgaben nach dem Formular zusammen, auf das sie zeigen. */
function collectUsages(
  userTasks: UserTaskReference[],
  embeddedForms: EmbeddedForm[],
  storedFormNames: string[],
): FormUsage[] {
  const byKey = new Map<string, FormUsage>();

  for (const task of userTasks) {
    const reference = parseFormKey(task.formKey);
    if (reference.kind === 'none') continue;

    // Gespeicherte Formulare werden ueber den ganzen Schluessel gruppiert: „Urlaubsantrag"
    // und „Urlaubsantrag:1.0" sind zwei verschiedene Ziele, auch wenn der Name derselbe ist.
    const key = reference.kind === 'embedded' ? reference.formId : (task.formKey ?? '').trim();
    const existing = byKey.get(key);

    if (existing) {
      existing.tasks.push(task);
      continue;
    }

    const missing =
      reference.kind === 'embedded'
        ? !embeddedForms.some((form) => form.id === reference.formId)
        : !storedFormNames.some((name) => name.toLowerCase() === reference.name.toLowerCase());

    byKey.set(key, {
      key,
      label: describeFormKey(task.formKey),
      origin: reference.kind,
      missing,
      tasks: [task],
    });
  }

  return [...byKey.values()].sort((a, b) => a.label.localeCompare(b.label, 'de'));
}
