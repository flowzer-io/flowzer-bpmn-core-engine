import { Link, useRouterState } from '@tanstack/react-router';

import { Icon } from '@/components/ui/Icon';
import { cn } from '@/lib/cn';
import { useCompactLayout } from '@/lib/useCompactLayout';
import { useAppearance, useResolvedTheme } from '@/stores/appearance';
import { useBreadcrumbStore, type Crumb } from '@/stores/breadcrumbs';
import { describeRole, useSession } from '@/stores/session';

import { activeNavKey, PAGE_TITLES } from './navigation';
import { NotificationsMenu } from './NotificationsMenu';

interface TopbarProps {
  onOpenPalette: () => void;
  onOpenUserMenu: () => void;
}

export function Topbar({ onOpenPalette, onOpenUserMenu }: TopbarProps) {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const registeredTrail = useBreadcrumbStore((state) => state.trail);
  const toggleTheme = useAppearance((state) => state.toggleTheme);
  const theme = useResolvedTheme();
  const user = useSession((state) => state.user);
  const compact = useCompactLayout();

  const trail: Crumb[] = registeredTrail ?? [{ label: PAGE_TITLES[activeNavKey(pathname)] ?? 'Flowzer' }];

  return (
    <header
      className={cn(
        'border-border relative z-[4] flex h-[60px] flex-none items-center gap-3 border-b px-4 md:gap-4 md:px-[22px]',
        'backdrop-blur-[10px]',
      )}
      style={{ background: 'color-mix(in oklab, var(--surface) 78%, transparent)' }}
    >
      <nav
        aria-label="Brotkrumen"
        className="flex min-w-0 flex-1 items-center gap-[7px] overflow-hidden md:flex-[0_1_auto]"
      >
        {trail.map((crumb, index) => (
          <span key={`${crumb.label}-${index}`} className="flex min-w-0 items-center gap-[7px]">
            {index > 0 && <Icon name="chevron_right" size={17} className="text-faint" />}
            {crumb.to ? (
              <Link
                to={crumb.to}
                className="font-display hover:text-accent text-muted shrink-0 text-base font-semibold whitespace-nowrap"
              >
                {crumb.label}
              </Link>
            ) : (
              <span className="font-display min-w-0 truncate text-[17px] font-semibold tracking-[-0.01em]">
                {crumb.label}
              </span>
            )}
          </span>
        ))}
      </nav>

      {/*
        * Die Suche bleibt dem grossen Schirm vorbehalten: Auf 375 px blieb von ihr ein
        * Lupensymbol uebrig, und die Tastenkombination gibt es auf einem Telefon nicht.
        */}
      {!compact && (
      <div className="flex min-w-0 flex-1 justify-center">
        <button
          type="button"
          onClick={onOpenPalette}
          className={cn(
            'bg-surface-2 border-border hover:border-border-strong text-faint flex w-[min(440px,100%)]',
            'cursor-text items-center gap-2.5 rounded-[var(--r-sm)] border px-3 py-2 text-left',
          )}
        >
          <Icon name="search" size={18} />
          <span className="truncate text-[13.5px]">Workflows, Instanzen, Aufgaben …</span>
          <span className="border-border-strong ml-auto shrink-0 rounded-[5px] border px-1.5 font-mono text-[11px]">
            ⌘K
          </span>
        </button>
      </div>
      )}

      <div className="flex flex-none items-center gap-[5px]">
        <NotificationsMenu />

        <button
          type="button"
          onClick={toggleTheme}
          title="Ansicht wechseln"
          aria-label="Zwischen heller und dunkler Ansicht wechseln"
          className="text-muted hover:bg-surface-2 grid h-[38px] w-[38px] cursor-pointer place-items-center rounded-[var(--r-sm)] border-none bg-transparent"
        >
          <Icon name={theme === 'dark' ? 'light_mode' : 'dark_mode'} size={21} />
        </button>

        <span className="bg-border mx-[5px] h-6 w-px" />

        <button
          type="button"
          onClick={onOpenUserMenu}
          title="Benutzermenü"
          className="bg-surface-2 border-border hover:border-border-strong flex cursor-pointer items-center gap-2 rounded-[var(--r-sm)] border py-1 pr-2 pl-1"
        >
          <span className="from-accent to-accent-2 grid h-7 w-7 place-items-center rounded-full bg-gradient-to-br text-xs font-bold text-accent-ink">
            {user?.initials ?? '?'}
          </span>
          <span className="text-text text-[13px] font-semibold whitespace-nowrap max-md:hidden">
            {describeRole(user)}
          </span>
          <Icon name="unfold_more" size={18} className="text-muted max-md:hidden" />
        </button>
      </div>
    </header>
  );
}
