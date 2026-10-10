import type { EmbedSnapshot } from './EmbeddedTaskForm';
import type { EmbedStartSnapshot } from './EmbeddedStartForm';

function object(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function guid(value: unknown): value is string { return typeof value === 'string' && value !== '00000000-0000-0000-0000-000000000000'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value); }
function formHost(value: Record<string, unknown>): boolean { return typeof value.hostOrigin === 'string'
  && /^https:\/\/[A-Za-z0-9.-]+(?::\d{1,5})?$/.test(value.hostOrigin) && object(value.form) && typeof value.form.formData === 'string'; }

/** Geschlossene Zwecke statt frei vom Fragment gewählter API-Pfade. */
export function parseEmbedEntry(fragment: string): { purpose: 'task' | 'start'; secret: string } {
  const start = fragment.startsWith('start.'); const secret = start ? fragment.slice(6) : fragment;
  if (!/^[A-Za-z0-9_-]{43}$/.test(secret)) throw new Error('link');
  return { purpose: start ? 'start' : 'task', secret };
}

/** Isolierter Startvertrag ohne Human-Task-Attrappe oder ungeprüften Zusatzkontext. */
export function validStartSnapshot(value: unknown): value is EmbedStartSnapshot {
  return object(value) && formHost(value) && guid(value.definitionId) && typeof value.relatedDefinitionId === 'string'
    && value.relatedDefinitionId.length > 0 && value.relatedDefinitionId.length <= 512
    && Object.keys(value).length === 4 && ['definitionId', 'relatedDefinitionId', 'hostOrigin', 'form'].every(key => Object.hasOwn(value, key));
}

/** Der bestehende Aufgabenvertrag bleibt getrennt und verlangt beide Revisionen. */
export function validTaskSnapshot(value: unknown): value is EmbedSnapshot {
  return object(value) && formHost(value) && guid(value.userTaskId)
    && Number.isSafeInteger(value.taskRevision) && Number(value.taskRevision) >= 0
    && object(value.context) && object(value.draft) && value.draft.userTaskId === value.userTaskId
    && Number.isSafeInteger(value.draft.revision) && Number(value.draft.revision) >= 0 && object(value.draft.data);
}
