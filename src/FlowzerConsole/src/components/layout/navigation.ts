/** Die Hauptnavigation der Konsole — Reihenfolge und Icons wie im Design. */
import type { FlowzerCapability } from '@/lib/auth/roles';

export interface NavItem {
  key: string;
  label: string;
  icon: string;
  path: string;
  /** Weitere Pfade, bei denen dieser Eintrag aktiv erscheinen soll. */
  matches?: string[];
  /**
   * Fähigkeit, die dieser Bereich verlangt. Ohne Angabe genügt der Zugang: Lesen
   * darf die API jeder Zugelassene, erst Schreiben und der Betrieb sind Rollen.
   */
  requires?: FlowzerCapability;
}

export const NAV_ITEMS: readonly NavItem[] = [
  { key: 'dashboard', label: 'Dashboard', icon: 'space_dashboard', path: '/' },
  { key: 'tasks', label: 'Meine Aufgaben', icon: 'inbox', path: '/tasks' },
  { key: 'workflows', label: 'Workflows', icon: 'schema', path: '/workflows', matches: ['/modeler'] },
  { key: 'instances', label: 'Instanzen', icon: 'play_circle', path: '/instances' },
  { key: 'forms', label: 'Formulare', icon: 'description', path: '/forms' },
  { key: 'operations', label: 'Betrieb', icon: 'monitoring', path: '/operations', requires: 'operator' },
] as const;

export function activeNavKey(pathname: string): string {
  if (pathname === '/') return 'dashboard';
  if (pathname.startsWith('/tasks')) return 'tasks';

  const match = NAV_ITEMS.find(
    (item) =>
      item.path !== '/' &&
      (pathname.startsWith(item.path) || item.matches?.some((prefix) => pathname.startsWith(prefix))),
  );

  return match?.key ?? 'dashboard';
}

export const PAGE_TITLES: Record<string, string> = {
  dashboard: 'Dashboard',
  workflows: 'Workflows',
  instances: 'Instanzen',
  forms: 'Formulare',
  operations: 'Betrieb & Diagnose',
  tasks: 'Meine Aufgaben',
};

/**
 * Behält nur die Bereiche, die diese Person auch benutzen kann. Ein Eintrag, der zu
 * einer Ablehnung führt, gehört nicht in die Navigation.
 */
export function visibleNavItems(can: (capability: FlowzerCapability) => boolean): NavItem[] {
  return NAV_ITEMS.filter((item) => !item.requires || can(item.requires));
}
