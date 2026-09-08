/**
 * Kleine, nebenwirkungsfreie Browser-Vorprüfung für den veröffentlichten Flowzer-
 * Formularvertrag. Directory-Zustand und benannte Berechnungen bleiben bewusst
 * serverautoritativ; diese Funktion ist niemals eine Berechtigungsgrenze.
 */

export type FormErrorMap = Record<string, string[]>;

export type ClientFormValidationResult =
  | { authority: 'client'; outcome: 'accepted'; output: Record<string, unknown> }
  | { authority: 'client'; outcome: 'rejected'; errors: FormErrorMap }
  | { authority: 'server' };

export class ClientFormContractError extends Error {
  readonly code: string;

  constructor(code: string) {
    super(`Unsupported form contract: ${code}.`);
    this.name = 'ClientFormContractError';
    this.code = code;
  }
}

type JsonObject = Record<string, unknown>;

interface ClientCondition {
  when: string;
  eq: unknown;
  show: boolean;
}

interface ClientField {
  key: string;
  type: string;
  schema: JsonObject;
  readOnly: boolean;
  conditions: ClientCondition[];
  calculated: boolean;
}

interface ClientContract {
  profile: 'flowzer.forms/1' | 'flowzer.forms/2' | 'flowzer.forms/3';
  fields: ClientField[];
  repeatGroupKeys: Set<string>;
  ignoredKeys: Set<string>;
  rules: unknown[];
  requiresServer: boolean;
}

const FIELD_TYPES = new Set([
  'textfield', 'textarea', 'email', 'url', 'phoneNumber', 'password', 'number',
  'currency', 'checkbox', 'radio', 'select', 'datetime', 'time', 'hidden',
  'flowzerSubject',
]);
const LAYOUT_TYPES = new Set(['panel', 'fieldset', 'columns', 'table', 'tabs', 'well']);
const SCRIPT_KEYS = ['calculateValue', 'customDefaultValue', 'customConditional', 'logic'] as const;

export function validateClientFormContract(
  schemaJson: string,
  input: Record<string, unknown>,
  context: Record<string, unknown> = {},
): ClientFormValidationResult {
  const contract = compile(schemaJson);
  if (contract.requiresServer) return { authority: 'server' };

  const errors: FormErrorMap = {};
  const output: Record<string, unknown> = {};
  const fieldsByKey = new Map(contract.fields.map((field) => [field.key, field]));
  for (const key of Object.keys(input)) {
    if (key === 'UserId' || contract.ignoredKeys.has(key)) continue;
    if (!fieldsByKey.has(key)) addError(errors, safeKey(key) ? key : '', 'field.undeclared');
  }

  for (const field of contract.fields) {
    if (field.calculated) continue;
    const present = Object.prototype.hasOwnProperty.call(input, field.key);
    const value = input[field.key];
    if (field.readOnly) {
      if (present && !deepEqual(value, context[field.key])) addError(errors, field.key, 'field.read_only');
      continue;
    }
    if (!field.conditions.every((condition) => visible(condition, input, context, fieldsByKey))) {
      if (!empty(value)) addError(errors, field.key, 'field.inactive');
      continue;
    }
    validateField(field, value, errors);
    if (present && !errors[field.key]) output[field.key] = empty(value) && !Array.isArray(value) ? null : value;
  }

  validateDateRules(contract.rules, input, context, fieldsByKey, errors);
  return Object.keys(errors).length > 0
    ? { authority: 'client', outcome: 'rejected', errors }
    : { authority: 'client', outcome: 'accepted', output };
}

export function inspectClientFormContract(schemaJson: string) {
  const contract = compile(schemaJson);
  return { profile: contract.profile, requiresServer: contract.requiresServer } as const;
}

function compile(schemaJson: string): ClientContract {
  if (schemaJson.length > 1_048_576) fail('schema.size_limit');
  let parsed: unknown;
  try {
    parsed = JSON.parse(schemaJson);
  } catch {
    fail('schema.json');
  }
  if (!isObject(parsed)) fail('schema.object');
  if (SCRIPT_KEYS.some((key) => active(parsed[key]))) fail('schema.script');
  const flowzer = object(parsed.flowzer);
  const contractVersion = flowzer.contractVersion ?? 1;
  if (contractVersion !== 1 && contractVersion !== 2 && contractVersion !== 3) fail('schema.version');

  const contract: ClientContract = {
    profile: contractVersion === 3
      ? 'flowzer.forms/3'
      : contractVersion === 2 ? 'flowzer.forms/2' : 'flowzer.forms/1',
    fields: [],
    repeatGroupKeys: new Set(),
    ignoredKeys: new Set(),
    rules: Array.isArray(flowzer.rules) ? flowzer.rules : [],
    requiresServer: false,
  };
  visit(parsed.components, contract, [], false, contractVersion);
  const keys = [
    ...contract.fields.map((field) => field.key),
    ...contract.repeatGroupKeys,
    ...contract.ignoredKeys,
  ];
  if (new Set(keys).size !== keys.length) fail('schema.duplicate_key');
  return contract;
}

function visit(
  components: unknown,
  contract: ClientContract,
  inheritedConditions: ClientCondition[],
  inheritedReadOnly: boolean,
  version: number,
) {
  if (components === undefined) return;
  if (!Array.isArray(components)) fail('schema.components');
  for (const componentValue of components) {
    if (!isObject(componentValue)) fail('schema.component');
    if (SCRIPT_KEYS.some((key) => active(componentValue[key]))) fail('schema.script');
    const component = componentValue;
    validateHelpText(component, version);
    const type = typeof component.type === 'string' ? component.type : '';
    const conditional = object(component.conditional);
    if (active(conditional.json)) fail('condition.script');
    const conditions = [...inheritedConditions];
    if (typeof conditional.when === 'string' && conditional.when.length > 0) {
      if (typeof conditional.show !== 'boolean' || !safeKey(conditional.when)
          || conditional.eq === undefined || isObject(conditional.eq) || Array.isArray(conditional.eq)) {
        fail('condition.unsupported');
      }
      conditions.push({ when: conditional.when, eq: conditional.eq, show: conditional.show });
    }
    const readOnly = inheritedReadOnly || component.disabled === true
      || object(component.flowzer).access === 'context';

    if (type !== 'datagrid' && active(object(component.flowzer).repeat)) fail('repeat.unexpected');
    if (LAYOUT_TYPES.has(type)) {
      visit(component.components, contract, conditions, readOnly, version);
      visitNestedLayouts(component.columns, contract, conditions, readOnly, version);
      visitNestedLayouts(component.rows, contract, conditions, readOnly, version);
      continue;
    }
    if (type === 'datagrid') {
      if (version !== 3) fail('schema.version');
      const key = typeof component.key === 'string' ? component.key : '';
      if (!safeKey(key)) fail('schema.key');
      validateRepeatGroup(component, version);
      if (contract.repeatGroupKeys.has(key)) fail('schema.duplicate_key');
      contract.repeatGroupKeys.add(key);
      contract.requiresServer = true;
      continue;
    }
    if (type === 'button' || type === 'content' || type === 'htmlelement') {
      if (typeof component.key === 'string' && component.key.length > 0) contract.ignoredKeys.add(component.key);
      continue;
    }
    if (!FIELD_TYPES.has(type)) fail('schema.field_type');
    if (type === 'flowzerSubject' && version < 2) fail('schema.version');
    if (type === 'select' && typeof component.dataSrc === 'string'
        && component.dataSrc !== '' && component.dataSrc !== 'values') {
      fail('selection.dynamic_source');
    }
    if (typeof component.key !== 'string' || !safeKey(component.key)) fail('schema.key');
    const calculated = active(object(component.flowzer).calculation);
    if (type === 'flowzerSubject' || calculated) contract.requiresServer = true;
    contract.fields.push({
      key: component.key,
      type,
      schema: component,
      readOnly,
      conditions,
      calculated,
    });
  }
}

function validateRepeatGroup(component: JsonObject, version: number) {
  if (component.input === false) fail('repeat.input');
  if (component.multiple === true) fail('repeat.multiple');
  const flowzer = object(component.flowzer);
  if (component.flowzer !== undefined && !isObject(component.flowzer)) fail('repeat.policy_object');
  if (Object.entries(flowzer).some(([key, value]) => !['repeat', 'helpText', 'access'].includes(key) && active(value))) {
    fail('repeat.policy_unknown');
  }
  if (flowzer.access !== undefined && flowzer.access !== null
      && !['input', 'context', ''].includes(String(flowzer.access))) {
    fail('field.access');
  }
  const repeatValue = flowzer.repeat;
  if (repeatValue !== undefined && !isObject(repeatValue)) fail('repeat.policy_object');
  const repeat = object(repeatValue);
  if (Object.keys(repeat).some((key) => key !== 'minItems' && key !== 'maxItems')) {
    fail('repeat.policy_unknown');
  }
  const minimum = repeat.minItems ?? 0;
  const maximum = repeat.maxItems ?? 20;
  if (typeof minimum !== 'number' || typeof maximum !== 'number'
      || !Number.isInteger(minimum) || !Number.isInteger(maximum)
      || minimum < 0 || maximum < 1 || maximum > 50 || minimum > maximum) {
    fail('repeat.range');
  }
  const validate = object(component.validate);
  const allowedValidation = new Set(['required', 'minLength', 'maxLength', 'customMessage']);
  if (Object.entries(validate).some(([key, value]) => !allowedValidation.has(key) && active(value))) {
    fail('validation.unsupported');
  }
  if (validate.required !== undefined && typeof validate.required !== 'boolean') fail('schema.boolean');
  if ((validate.minLength !== undefined && validate.minLength !== minimum)
      || (typeof validate.maxLength === 'number' && validate.maxLength > 0 && validate.maxLength !== maximum)
      || (validate.required === true && minimum < 1)) {
    fail('repeat.formio_mismatch');
  }
  if (!Array.isArray(component.components)) fail('repeat.components');
  const keys = new Set<string>();
  const conditions: ClientCondition[] = [];
  for (const childValue of component.components) {
    if (!isObject(childValue)) fail('schema.component');
    const child = childValue;
    if (SCRIPT_KEYS.some((key) => active(child[key]))) fail('schema.script');
    validateHelpText(child, version);
    const type = typeof child.type === 'string' ? child.type : '';
    if (type === 'datagrid') fail('repeat.nested');
    if (!FIELD_TYPES.has(type) || type === 'flowzerSubject') fail('repeat.field_type');
    if (child.input === false) fail('repeat.input');
    if (child.multiple === true) fail('repeat.multiple_field');
    if (child.disabled === true || object(child.flowzer).access === 'context') {
      fail('repeat.read_only_field');
    }
    if (active(object(child.flowzer).calculation)) fail('repeat.calculation');
    const key = typeof child.key === 'string' ? child.key : '';
    if (!safeKey(key)) fail('schema.key');
    if (keys.has(key)) fail('schema.duplicate_key');
    keys.add(key);
    const conditional = object(child.conditional);
    if (active(conditional.json)) fail('condition.script');
    if (typeof conditional.when === 'string' && conditional.when.length > 0) {
      if (typeof conditional.show !== 'boolean' || !safeKey(conditional.when)
          || conditional.eq === undefined || isObject(conditional.eq) || Array.isArray(conditional.eq)) {
        fail('condition.unsupported');
      }
      conditions.push({ when: conditional.when, eq: conditional.eq, show: conditional.show });
    }
  }
  if (conditions.some((condition) => !keys.has(condition.when))) fail('condition.unknown_field');
}

function validateHelpText(component: JsonObject, version: number) {
  if (version !== 3) return;
  for (const value of [component.description, object(component.flowzer).helpText]) {
    if (value === undefined || value === null) continue;
    if (typeof value !== 'string') fail('help.type');
    if (value.length > 2_000) fail('help.length');
    if (value.includes('<') || value.includes('>')
        || [...value].some((character) => {
          const code = character.charCodeAt(0);
          return (code < 32 && code !== 9 && code !== 10 && code !== 13) || code === 127;
        })) {
      fail('help.plain_text');
    }
  }
}

function visitNestedLayouts(
  value: unknown,
  contract: ClientContract,
  conditions: ClientCondition[],
  readOnly: boolean,
  version: number,
) {
  if (value === undefined) return;
  if (!Array.isArray(value)) fail('schema.layout');
  for (const child of value) {
    if (Array.isArray(child)) visitNestedLayouts(child, contract, conditions, readOnly, version);
    else if (isObject(child)) {
      visit(child.components, contract, conditions, readOnly, version);
      visitNestedLayouts(child.columns, contract, conditions, readOnly, version);
      visitNestedLayouts(child.rows, contract, conditions, readOnly, version);
    } else fail('schema.layout');
  }
}

function validateField(field: ClientField, value: unknown, errors: FormErrorMap) {
  const validate = object(field.schema.validate);
  const multiple = field.schema.multiple === true;
  if (Array.isArray(value) && !multiple) {
    addError(errors, field.key, 'type.scalar');
    return;
  }
  if (multiple && Array.isArray(value) && value.length === 0 && positiveNumber(validate.minSelectedCount)) {
    addError(errors, field.key, 'selection.min');
  }
  if (empty(value) || (field.type === 'checkbox' && value === false && validate.required === true)) {
    if (validate.required === true) addError(errors, field.key, 'required');
    return;
  }
  if (multiple) {
    if (!Array.isArray(value)) {
      addError(errors, field.key, 'type.array');
      return;
    }
    if (typeof validate.maxSelectedCount === 'number' && value.length > validate.maxSelectedCount) {
      addError(errors, field.key, 'selection.max');
    }
    if (typeof validate.minSelectedCount === 'number' && value.length < validate.minSelectedCount) {
      addError(errors, field.key, 'selection.min');
    }
    for (const item of value) validateScalar(field, item, validate, errors);
    return;
  }
  validateScalar(field, value, validate, errors);
}

function validateScalar(field: ClientField, value: unknown, validate: JsonObject, errors: FormErrorMap) {
  if (field.type === 'number' || field.type === 'currency') {
    if (typeof value !== 'number' || !Number.isFinite(value)) {
      addError(errors, field.key, 'type.number');
      return;
    }
    if (typeof validate.min === 'number' && value < validate.min) addError(errors, field.key, 'number.min');
    if (typeof validate.max === 'number' && value > validate.max) addError(errors, field.key, 'number.max');
    return;
  }
  if (field.type === 'checkbox') {
    if (typeof value !== 'boolean') addError(errors, field.key, 'type.boolean');
    return;
  }
  if (field.type === 'select' || field.type === 'radio') {
    const values = field.type === 'radio'
      ? field.schema.values
      : object(field.schema.data).values;
    if (!Array.isArray(values)
        || !values.some((item) => isObject(item) && deepEqual(item.value, value))) {
      addError(errors, field.key, 'selection.invalid');
    }
    return;
  }
  if (field.type === 'hidden' && (typeof value === 'number' || typeof value === 'boolean')) return;
  if (typeof value !== 'string') {
    addError(errors, field.key, 'type.string');
    return;
  }
  if (typeof validate.minLength === 'number' && value.length < validate.minLength) {
    addError(errors, field.key, 'text.min_length');
  }
  if ((typeof validate.maxLength === 'number' && value.length > validate.maxLength) || value.length > 131_072) {
    addError(errors, field.key, 'text.max_length');
  }
  if (field.type === 'datetime' && !parseDate(value)) addError(errors, field.key, 'date.invalid');
}

function validateDateRules(
  rules: unknown[],
  input: JsonObject,
  context: JsonObject,
  fields: Map<string, ClientField>,
  errors: FormErrorMap,
) {
  for (const candidate of rules) {
    if (!isObject(candidate) || candidate.kind !== 'dateOrder'
        || typeof candidate.start !== 'string' || typeof candidate.end !== 'string') continue;
    const from = parseDate(effectiveValue(candidate.start, input, context, fields));
    const until = parseDate(effectiveValue(candidate.end, input, context, fields));
    if (from === null || until === null) continue;
    if (from > until || (from === until && candidate.allowEqual !== true)) {
      addError(errors, candidate.end, 'date.order');
    }
  }
}

function visible(
  condition: ClientCondition,
  input: JsonObject,
  context: JsonObject,
  fields: Map<string, ClientField>,
) {
  return (conditionText(effectiveValue(condition.when, input, context, fields))
    === conditionText(condition.eq)) === condition.show;
}

function effectiveValue(key: string, input: JsonObject, context: JsonObject, fields: Map<string, ClientField>) {
  return fields.get(key)?.readOnly ? context[key] : input[key];
}

function conditionText(value: unknown) {
  if (value === true) return 'true';
  if (value === false) return 'false';
  if (value === null || value === undefined) return '';
  return String(value);
}

function parseDate(value: unknown): number | null {
  if (typeof value !== 'string') return null;
  const match = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(Z|[+-]\d{2}:\d{2})?)?$/.exec(value);
  if (!match) return null;
  const [, yearText, monthText, dayText, hourText = '00', minuteText = '00', secondText = '00', zone] = match;
  const year = Number(yearText);
  const month = Number(monthText);
  const day = Number(dayText);
  const hour = Number(hourText);
  const minute = Number(minuteText);
  const second = Number(secondText);
  const lastDay = month >= 1 && month <= 12
    ? new Date(Date.UTC(year, month, 0)).getUTCDate()
    : 0;
  if (day < 1 || day > lastDay || hour > 23 || minute > 59 || second > 59) return null;
  // Der Server behandelt eine fehlende Zone mit DateTimeStyles.AssumeUniversal.
  const normalized = value.length === 10 ? `${value}T00:00:00Z` : zone ? value : `${value}Z`;
  const timestamp = Date.parse(normalized);
  return Number.isNaN(timestamp) ? null : timestamp;
}

function empty(value: unknown) {
  return value === undefined || value === null
    || (typeof value === 'string' && value.trim().length === 0)
    || (Array.isArray(value) && value.length === 0);
}

function active(value: unknown): boolean {
  if (value === undefined || value === null || value === false) return false;
  if (typeof value === 'string') return value.trim().length > 0;
  if (typeof value === 'number') return value !== 0;
  if (Array.isArray(value)) return value.length > 0;
  if (isObject(value)) return Object.keys(value).length > 0;
  return true;
}

function object(value: unknown): JsonObject {
  return isObject(value) ? value : {};
}

function isObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function deepEqual(left: unknown, right: unknown) {
  return JSON.stringify(left) === JSON.stringify(right);
}

function positiveNumber(value: unknown) {
  return typeof value === 'number' && value > 0;
}

function safeKey(key: string) {
  return key.length > 0 && key.length <= 128
    && /^[A-Za-z][A-Za-z0-9_]*$/.test(key)
    && key !== 'UserId' && key !== 'constructor' && key !== 'prototype';
}

function addError(errors: FormErrorMap, key: string, code: string) {
  const values = errors[key] ?? (errors[key] = []);
  if (!values.includes(code)) values.push(code);
}

function fail(code: string): never {
  throw new ClientFormContractError(code);
}
