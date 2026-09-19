import type { VersionDto } from '@/lib/api/types';
import { formatVersion, shortId } from '@/lib/format';

/**
 * Die Filterregeln der Instanzliste: Workflow, davon abhängig die Version, dazu die Suche.
 *
 * Sie stehen hier ohne React, weil sie Betriebswissen tragen — etwa dass Instanzen ohne
 * auffindbare Definition wählbar bleiben müssen — und sich so einzeln prüfen lassen.
 */

/** Auswahlwert „keine Einschränkung“; gilt für beide Listen. */
export const ANY_FILTER = 'all';

/** Sammelschlüssel der Instanzen, deren gebundene Definition nicht mehr vorliegt. */
export const UNKNOWN_VERSION = 'unknown';

export interface InstanceFilter {
  workflowId: string;
  versionKey: string;
}

export const NO_INSTANCE_FILTER: InstanceFilter = { workflowId: ANY_FILTER, versionKey: ANY_FILTER };

export interface FilterOption {
  value: string;
  label: string;
  count: number;
}

/** Nur die Felder, die die Regeln lesen — der Rest der Instanz spielt hier keine Rolle. */
export interface FilterableInstance {
  instanceId: string;
  relatedDefinitionId: string;
  relatedDefinitionName: string;
  definitionVersion?: VersionDto | null;
}

/** Stabiler Schlüssel einer gebundenen Version; ohne Definition der Sammelschlüssel. */
export function versionKeyOf(instance: FilterableInstance): string {
  const version = instance.definitionVersion;
  return version ? `${version.major}.${version.minor}` : UNKNOWN_VERSION;
}

/** Die Workflows, zu denen es tatsächlich Instanzen gibt — alphabetisch nach Anzeigename. */
export function workflowFilterOptions(instances: readonly FilterableInstance[]): FilterOption[] {
  const byId = new Map<string, FilterOption>();

  for (const instance of instances) {
    const known = byId.get(instance.relatedDefinitionId);
    if (known) known.count += 1;
    else
      byId.set(instance.relatedDefinitionId, {
        value: instance.relatedDefinitionId,
        label: instance.relatedDefinitionName,
        count: 1,
      });
  }

  return [...byId.values()].sort((left, right) => left.label.localeCompare(right.label, 'de'));
}

/**
 * Die Versionen eines Workflows, neueste zuerst.
 *
 * Ohne gewählten Workflow bleibt die Liste leer: Dieselbe Versionsnummer bedeutet bei zwei
 * Workflows zwei verschiedene Stände, eine gemeinsame Liste wäre also irreführend.
 */
export function versionFilterOptions(
  instances: readonly FilterableInstance[],
  workflowId: string,
): FilterOption[] {
  if (workflowId === ANY_FILTER) return [];

  const byKey = new Map<string, FilterOption>();

  for (const instance of instances) {
    if (instance.relatedDefinitionId !== workflowId) continue;

    const key = versionKeyOf(instance);
    const known = byKey.get(key);
    if (known) known.count += 1;
    else
      byKey.set(key, {
        value: key,
        label: key === UNKNOWN_VERSION ? 'v? · unbekannt' : formatVersion(instance.definitionVersion),
        count: 1,
      });
  }

  return [...byKey.values()].sort(compareVersionOptions);
}

function compareVersionOptions(left: FilterOption, right: FilterOption): number {
  // Die verwaisten Instanzen haben keine Nummer, die sich einsortieren ließe. Sie stehen
  // am Ende, wo sie nicht die Reihenfolge der echten Versionen stören.
  if (left.value === UNKNOWN_VERSION) return right.value === UNKNOWN_VERSION ? 0 : 1;
  if (right.value === UNKNOWN_VERSION) return -1;

  const [leftMajor, leftMinor] = parseVersionKey(left.value);
  const [rightMajor, rightMinor] = parseVersionKey(right.value);
  return rightMajor - leftMajor || rightMinor - leftMinor;
}

function parseVersionKey(key: string): [number, number] {
  const parts = key.split('.');
  return [Number(parts[0] ?? 0), Number(parts[1] ?? 0)];
}

/**
 * Bringt eine Auswahl mit dem aktuellen Bestand in Einklang.
 *
 * Wechselt der Workflow oder verschwindet eine Version beim Neuladen, bliebe sonst eine
 * Einschränkung aktiv, die in keiner Auswahlliste mehr steht — die Liste wirkte grundlos leer.
 */
export function normalizeInstanceFilter(
  filter: InstanceFilter,
  instances: readonly FilterableInstance[],
): InstanceFilter {
  const workflowId = workflowFilterOptions(instances).some((option) => option.value === filter.workflowId)
    ? filter.workflowId
    : ANY_FILTER;

  const versionKey = versionFilterOptions(instances, workflowId).some(
    (option) => option.value === filter.versionKey,
  )
    ? filter.versionKey
    : ANY_FILTER;

  return { workflowId, versionKey };
}

/** Trifft die Instanz alle aktiven Einschränkungen — Workflow, Version und Suchbegriff? */
export function matchesInstanceFilter(
  instance: FilterableInstance,
  filter: InstanceFilter,
  search: string,
): boolean {
  if (filter.workflowId !== ANY_FILTER && instance.relatedDefinitionId !== filter.workflowId) return false;
  if (filter.versionKey !== ANY_FILTER && versionKeyOf(instance) !== filter.versionKey) return false;

  const term = search.trim().toLowerCase();
  if (term.length === 0) return true;

  return (
    instance.relatedDefinitionName.toLowerCase().includes(term) ||
    instance.instanceId.toLowerCase().includes(term) ||
    // Gesucht wird nach der Kennung, die in der Liste steht — nicht nach der ganzen Guid.
    shortId(instance.instanceId).toLowerCase().includes(term)
  );
}
