import { Link, useRouterState } from '@tanstack/react-router';

import { Icon } from '@/components/ui/Icon';
import { cn } from '@/lib/cn';
import { useUserTasks } from '@/lib/api/queries';

import { activeNavKey } from './navigation';

/**
 * Die Navigation auf dem Telefon.
 *
 * Bewusst nur drei Bereiche statt der fünf aus der Seitenleiste: eigene Aufgaben
 * bearbeiten, Instanzen ansehen, Workflows lesen. Der Modeler, der Formularbau und
 * der Betrieb bleiben am großen Bildschirm — sie unterwegs zu bedienen wäre nicht
 * bequem, sondern nur fehleranfällig. Die Seiten sind über einen Verweis weiterhin
 * erreichbar; sie werden hier nur nicht angeboten.
 */
const TABS = [
  { key: 'tasks', label: 'Aufgaben', icon: 'inbox', path: '/tasks' },
  { key: 'instances', label: 'Instanzen', icon: 'play_circle', path: '/instances' },
  { key: 'workflows', label: 'Workflows', icon: 'schema', path: '/workflows' },
] as const;

export function MobileTabBar() {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const tasksQuery = useUserTasks();
  const currentKey = activeNavKey(pathname);
  const openTasks = tasksQuery.data?.length ?? 0;

  return (
    <nav
      aria-label="Hauptbereiche"
      className={cn(
        'border-border bg-surface fixed inset-x-0 bottom-0 z-20 grid grid-cols-3 border-t md:hidden',
        // Auf Geräten mit Gestenleiste sitzt der Rand des Bildschirms unter der Leiste.
        'pb-[env(safe-area-inset-bottom)]',
      )}
    >
      {TABS.map((tab) => {
        const active = currentKey === tab.key;
        return (
          <Link
            key={tab.key}
            to={tab.path}
            aria-current={active ? 'page' : undefined}
            className={cn(
              'relative flex min-h-[58px] flex-col items-center justify-center gap-0.5 text-[11px] font-semibold',
              active ? 'text-accent' : 'text-faint',
            )}
          >
            <span className="relative">
              <Icon name={tab.icon} size={23} />
              {tab.key === 'tasks' && openTasks > 0 && (
                <span
                  className="absolute -top-1 -right-2.5 grid h-[17px] min-w-[17px] place-items-center rounded-full px-1 text-[10.5px] font-bold text-white"
                  style={{ background: 'var(--fail)' }}
                >
                  {openTasks}
                </span>
              )}
            </span>
            {tab.label}
          </Link>
        );
      })}
    </nav>
  );
}
