import { Link, useRouterState } from '@tanstack/react-router';

import { Icon } from '@/components/ui/Icon';
import { cn, mix } from '@/lib/cn';
import { useAppearance } from '@/stores/appearance';
import { describeRole, useSession, useCan } from '@/stores/session';

import { LogoMark, LogoWordmark } from './Logo';
import { activeNavKey, visibleNavItems } from './navigation';

interface SidebarProps {
  onOpenUserMenu: () => void;
}

export function Sidebar({ onOpenUserMenu }: SidebarProps) {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const sidebar = useAppearance((state) => state.sidebar);
  const toggleSidebar = useAppearance((state) => state.toggleSidebar);
  const user = useSession((state) => state.user);
  const can = useCan();
  const navItems = visibleNavItems(can);

  const expanded = sidebar === 'full';
  const currentKey = activeNavKey(pathname);

  return (
    <aside
      className={cn(
        'bg-surface border-border sticky top-0 z-[5] flex h-screen flex-none flex-col border-r',
        'transition-[width] duration-200',
      )}
      style={{ width: 'var(--sidebar-w)' }}
    >
      <div className="border-border flex h-[60px] items-center gap-[11px] border-b px-5">
        <LogoMark />
        {expanded && <LogoWordmark />}
        <button
          type="button"
          onClick={toggleSidebar}
          title={expanded ? 'Navigation einklappen' : 'Navigation ausklappen'}
          aria-label={expanded ? 'Navigation einklappen' : 'Navigation ausklappen'}
          className={cn(
            'text-faint hover:bg-surface-2 hover:text-text ml-auto grid h-8 w-8 cursor-pointer',
            'place-items-center rounded-[var(--r-sm)] border-none bg-transparent',
            !expanded && 'ml-0',
          )}
        >
          <Icon name={expanded ? 'left_panel_close' : 'left_panel_open'} size={19} />
        </button>
      </div>

      <nav className="flex flex-1 flex-col gap-[3px] overflow-auto p-3 pt-3.5">
        {expanded && (
          <div className="text-faint px-3 pt-1.5 pb-2 font-mono text-[10.5px] tracking-[0.14em] uppercase">
            Navigation
          </div>
        )}

        {navItems.map((item) => {
          const active = item.key === currentKey;
          return (
            <Link
              key={item.key}
              to={item.path}
              title={item.label}
              className={cn(
                'flex w-full cursor-pointer items-center gap-3 rounded-[var(--r-sm)] px-3 py-2.5',
                'text-[14.5px] transition-colors duration-150',
                active ? 'text-accent font-semibold' : 'text-muted hover:bg-surface-2 font-medium',
                !expanded && 'justify-center px-0',
              )}
              style={active ? { background: mix('--accent', 12) } : undefined}
            >
              <Icon name={item.icon} size={21} />
              {expanded && <span>{item.label}</span>}
            </Link>
          );
        })}
      </nav>

      <div className="border-border flex flex-col gap-0.5 border-t p-3">
        <button
          type="button"
          onClick={onOpenUserMenu}
          className={cn(
            'hover:bg-surface-2 flex cursor-pointer items-center gap-[11px] rounded-[var(--r-sm)]',
            'border-none bg-transparent px-2.5 py-2 text-left',
            !expanded && 'justify-center px-0',
          )}
        >
          <span className="from-accent to-accent-2 grid h-[34px] w-[34px] shrink-0 place-items-center rounded-full bg-gradient-to-br text-[13px] font-bold tracking-[0.02em] text-accent-ink">
            {user?.initials ?? '?'}
          </span>
          {expanded && (
            <span className="min-w-0">
              <span className="block truncate text-[13.5px] font-semibold">{user?.name ?? 'Unbekannt'}</span>
              <span className="text-muted block text-xs">{describeRole(user)}</span>
            </span>
          )}
        </button>
      </div>
    </aside>
  );
}
