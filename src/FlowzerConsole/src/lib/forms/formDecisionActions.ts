export type FormDecisionActionVariant = 'primary' | 'secondary' | 'danger';
export type FormDecisionActionValue = string | number | boolean | null;

export interface FormDecisionActionAssignment {
  field: string;
  value: FormDecisionActionValue;
}

export interface FormDecisionActionDraft {
  id: string;
  label: string;
  variant: FormDecisionActionVariant;
  set: FormDecisionActionAssignment[];
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isScalar(value: unknown): value is FormDecisionActionValue {
  return value === null || ['string', 'number', 'boolean'].includes(typeof value);
}

export interface FormDecisionActionsInspection {
  actions: FormDecisionActionDraft[];
  hasUnsupportedFragments: boolean;
}

function hasOnlyKeys(value: Record<string, unknown>, allowed: string[]) {
  return Object.keys(value).every((key) => allowed.includes(key));
}

/**
 * Prüft, ob die Root-Aktionen durch den begrenzten visuellen Editor verlustfrei
 * dargestellt werden können. Unbekannte Fragmente werden niemals umgedeutet.
 */
export function inspectFormDecisionActions(schema: unknown): FormDecisionActionsInspection {
  if (!isObject(schema) || !isObject(schema.flowzer)) {
    return { actions: [], hasUnsupportedFragments: false };
  }
  if (schema.flowzer.actions === undefined) {
    return { actions: [], hasUnsupportedFragments: false };
  }
  if (!Array.isArray(schema.flowzer.actions)) {
    return { actions: [], hasUnsupportedFragments: true };
  }

  const actions: FormDecisionActionDraft[] = [];
  for (const candidate of schema.flowzer.actions) {
    if (!isObject(candidate)
        || !hasOnlyKeys(candidate, ['id', 'label', 'variant', 'set'])
        || typeof candidate.id !== 'string'
        || typeof candidate.label !== 'string'
        || !['primary', 'secondary', 'danger'].includes(String(candidate.variant))
        || !Array.isArray(candidate.set)) {
      return { actions: [], hasUnsupportedFragments: true };
    }
    const assignments: FormDecisionActionAssignment[] = [];
    for (const entry of candidate.set) {
      if (!isObject(entry)
          || !hasOnlyKeys(entry, ['field', 'value'])
          || typeof entry.field !== 'string'
          || !Object.prototype.hasOwnProperty.call(entry, 'value')
          || !isScalar(entry.value)) {
        return { actions: [], hasUnsupportedFragments: true };
      }
      assignments.push({ field: entry.field, value: entry.value });
    }
    actions.push({
      id: candidate.id,
      label: candidate.label,
      variant: candidate.variant as FormDecisionActionVariant,
      set: assignments,
    });
  }
  return { actions, hasUnsupportedFragments: false };
}

/** Liest nur den begrenzten editierbaren Teil; unbekannte Fragmente werden nicht umgedeutet. */
export function readFormDecisionActions(schema: unknown): FormDecisionActionDraft[] {
  return inspectFormDecisionActions(schema).actions;
}

/** Schreibt ausschließlich Root-Aktionen und bewahrt alle übrigen Form.io-/Flowzer-Werte. */
export function writeFormDecisionActions(
  schema: unknown,
  actions: FormDecisionActionDraft[],
): unknown {
  if (!isObject(schema)) return schema;
  const hasFlowzer = isObject(schema.flowzer);
  if (!hasFlowzer && actions.length === 0) return schema;
  const flowzer = hasFlowzer ? schema.flowzer as Record<string, unknown> : {};
  const rest = { ...flowzer };
  delete rest.actions;
  return {
    ...schema,
    flowzer: actions.length > 0 ? { ...rest, actions } : rest,
  };
}
