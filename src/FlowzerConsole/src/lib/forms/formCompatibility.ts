import type { FormCompatibilityItemDto } from '@/lib/api/types';

/** Gruppiert nur problematische Eintraege; kompatible Versionen erzeugen keinen Warnstatus. */
export function incompatibleCountByForm(
  inventory: readonly FormCompatibilityItemDto[],
): Map<string, number> {
  const result = new Map<string, number>();
  for (const item of inventory) {
    if (!item.compatible) result.set(item.formId, (result.get(item.formId) ?? 0) + 1);
  }
  return result;
}

/** Uebersetzt bekannte kanonische Codes, ohne untrusted Serverdetails wiederzugeben. */
export function describeCompatibilityIssue(code: string | null | undefined): string {
  const messages: Record<string, string> = {
    'schema.script': 'enthält nicht unterstütztes Custom-JavaScript',
    'condition.script': 'enthält eine nicht unterstützte Script-Bedingung',
    'selection.dynamic_source': 'verwendet eine nicht freigegebene dynamische Datenquelle',
    'schema.field_type': 'enthält einen noch nicht unterstützten Feldtyp',
    'validation.unsupported': 'enthält eine nicht unterstützte Validierungsregel',
  };
  return code ? (messages[code] ?? `benötigt Migration (${safeCode(code)})`) : 'benötigt Migration';
}

function safeCode(code: string): string {
  return /^[a-z0-9_.-]{1,80}$/i.test(code) ? code : 'unbekannter Code';
}
