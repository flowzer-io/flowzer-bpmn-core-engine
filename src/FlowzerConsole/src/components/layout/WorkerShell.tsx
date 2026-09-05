import type { ReactNode } from 'react';

import { TasksPage } from '@/pages/TasksPage';
import { Icon } from '@/components/ui/Icon';
import { useUserTasks } from '@/lib/api/queries';
import { useAppearance, useResolvedTheme } from '@/stores/appearance';
import { useSession } from '@/stores/session';

import { LogoMark, LogoWordmark } from './Logo';

interface WorkerShellProps {
  onOpenUserMenu: () => void;
  children?: ReactNode;
}

/**
 * Reduzierte Oberfläche für Sachbearbeitende: nur die eigene Aufgabenliste,
 * keine Navigation, keine Betriebsdaten.
 */
export function WorkerShell({ onOpenUserMenu, children }: WorkerShellProps) {
  const user = useSession((state) => state.user);
  const toggleTheme = useAppearance((state) => state.toggleTheme);
  const theme = useResolvedTheme();
  const tasksQuery = useUserTasks();

  const openCount = tasksQuery.data?.length ?? 0;

  return (
    <div className="flex h-screen flex-col">
      <header className="border-border bg-surface flex h-[60px] flex-none items-center gap-3.5 border-b px-6">
        <LogoMark />
        <LogoWordmark className="text-xl" />
        <span className="bg-border ml-1 h-[22px] w-px" />
        <span className="text-muted text-sm font-semibold">Meine Aufgaben</span>

        <span className="flex-1" />

        <span className="bg-surface-2 inline-flex items-center gap-2 rounded-[20px] px-3 py-1.5 text-[13px] font-semibold">
          <span className="bg-accent h-2 w-2 rounded-full" />
          {openCount} offen
        </span>

        <button
          type="button"
          onClick={toggleTheme}
          title="Ansicht wechseln"
          aria-label="Zwischen heller und dunkler Ansicht wechseln"
          className="text-muted hover:bg-surface-2 grid h-[38px] w-[38px] cursor-pointer place-items-center rounded-[var(--r-sm)] border-none bg-transparent"
        >
          <Icon name={theme === 'dark' ? 'light_mode' : 'dark_mode'} size={21} />
        </button>

        <button
          type="button"
          onClick={onOpenUserMenu}
          title="Benutzermenü"
          className="bg-surface-2 border-border hover:border-border-strong flex cursor-pointer items-center gap-2 rounded-[var(--r-sm)] border py-1 pr-2 pl-1"
        >
          <span className="from-accent to-accent-2 grid h-7 w-7 place-items-center rounded-full bg-gradient-to-br text-xs font-bold text-white">
            {user?.initials ?? '?'}
          </span>
          <span className="text-[13px] font-semibold whitespace-nowrap">{user?.name ?? 'Unbekannt'}</span>
          <Icon name="unfold_more" size={18} className="text-muted" />
        </button>
      </header>

      <div className="flex min-h-0 flex-1">
        <TasksPage variant="worker" />
      </div>

      {children}
    </div>
  );
}
