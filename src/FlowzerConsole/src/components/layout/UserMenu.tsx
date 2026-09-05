import * as Dialog from '@radix-ui/react-dialog';

import { Chip } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { cn, mix } from '@/lib/cn';
import { ACCENTS, useAppearance, type Accent, type Density } from '@/stores/appearance';
import { useSession } from '@/stores/session';

interface UserMenuProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const ACCENT_LABELS: Record<Accent, string> = {
  iris: 'Iris',
  teal: 'Petrol',
  emerald: 'Smaragd',
  amber: 'Bernstein',
  rose: 'Rosé',
};

const DENSITIES: { value: Density; label: string }[] = [
  { value: 'comfortable', label: 'Komfortabel' },
  { value: 'compact', label: 'Kompakt' },
];

const ROLE_LABELS: Record<string, string> = {
  access: 'Zugang',
  modeler: 'Modellieren',
  operator: 'Betrieb',
  worker: 'Service-Tasks',
};

/**
 * Benutzer- und Darstellungsmenü. Zeigt die angemeldete Person mit den Rollen aus
 * ihrem Token; sie bestimmen, was die Konsole anbietet und was die API zulässt.
 */
export function UserMenu({ open, onOpenChange }: UserMenuProps) {
  const user = useSession((state) => state.user);
  const signOut = useSession((state) => state.signOut);
  const { accent, setAccent, density, setDensity, theme, setTheme } = useAppearance();
  const taskFocus = useAppearance((state) => state.taskFocus);
  const setTaskFocus = useAppearance((state) => state.setTaskFocus);

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-[60]" />
        <Dialog.Content
          aria-describedby={undefined}
          className="bg-surface border-border shadow-pop animate-fade-in-fast fixed top-14 right-[18px] z-[61] w-[318px] overflow-hidden rounded-[var(--r-lg)] border"
        >
          <Dialog.Title className="text-faint px-4 pt-3.5 pb-2 font-mono text-[10.5px] tracking-[0.1em] uppercase">
            Angemeldet
          </Dialog.Title>

          <div className="flex items-center gap-3 px-4 pb-3">
            <span className="from-accent to-accent-2 grid h-[38px] w-[38px] shrink-0 place-items-center rounded-full bg-gradient-to-br text-[13px] font-bold text-white">
              {user?.initials ?? '?'}
            </span>
            <span className="min-w-0 flex-1">
              <span className="block truncate text-[13.5px] font-semibold">{user?.name ?? 'Unbekannt'}</span>
              {user?.email && <span className="text-muted block truncate text-xs">{user.email}</span>}
            </span>
          </div>

          <div className="flex flex-wrap gap-1.5 px-4 pb-3">
            {[...(user?.roles ?? [])].sort().map((role) => (
              <Chip key={role} tone={role === 'access' ? 'muted' : 'accent'}>
                {ROLE_LABELS[role] ?? role}
              </Chip>
            ))}
            {(user?.roles.size ?? 0) === 0 && <Chip tone="wait">Keine Rolle zugewiesen</Chip>}
          </div>

          <div className="border-border space-y-3.5 border-t px-4 py-3.5">
            <div>
              <div className="text-faint mb-2 font-mono text-[10.5px] tracking-[0.1em] uppercase">Akzent</div>
              <div className="flex gap-2">
                {ACCENTS.map((option) => (
                  <button
                    key={option}
                    type="button"
                    title={ACCENT_LABELS[option]}
                    aria-label={`Akzentfarbe ${ACCENT_LABELS[option]}`}
                    aria-pressed={accent === option}
                    onClick={() => setAccent(option)}
                    data-accent={option}
                    className={cn(
                      'h-6 w-6 cursor-pointer rounded-full border-2 transition-transform',
                      accent === option ? 'border-text scale-110' : 'border-transparent',
                    )}
                    style={{ background: 'linear-gradient(135deg, var(--accent), var(--accent-2))' }}
                  />
                ))}
              </div>
            </div>

            <div>
              <div className="text-faint mb-2 font-mono text-[10.5px] tracking-[0.1em] uppercase">Dichte</div>
              <div className="flex gap-2">
                {DENSITIES.map((option) => (
                  <button
                    key={option.value}
                    type="button"
                    onClick={() => setDensity(option.value)}
                    className={cn(
                      'flex-1 cursor-pointer rounded-[var(--r-sm)] border px-2 py-1.5 text-[12.5px] font-semibold',
                      density === option.value
                        ? 'border-accent text-accent'
                        : 'border-border text-muted hover:border-border-strong',
                    )}
                    style={density === option.value ? { background: mix('--accent', 10) } : undefined}
                  >
                    {option.label}
                  </button>
                ))}
              </div>
            </div>

            <div>
              <div className="text-faint mb-2 font-mono text-[10.5px] tracking-[0.1em] uppercase">Umfang</div>
              <div className="flex gap-2">
                {[
                  { value: false, label: 'Volle Konsole' },
                  { value: true, label: 'Nur Aufgaben' },
                ].map((option) => (
                  <button
                    key={String(option.value)}
                    type="button"
                    onClick={() => setTaskFocus(option.value)}
                    className={cn(
                      'flex-1 cursor-pointer rounded-[var(--r-sm)] border px-2 py-1.5 text-[12.5px] font-semibold',
                      taskFocus === option.value
                        ? 'border-accent text-accent'
                        : 'border-border text-muted hover:border-border-strong',
                    )}
                    style={taskFocus === option.value ? { background: mix('--accent', 10) } : undefined}
                  >
                    {option.label}
                  </button>
                ))}
              </div>
            </div>

            <div>
              <div className="text-faint mb-2 font-mono text-[10.5px] tracking-[0.1em] uppercase">Darstellung</div>
              <div className="flex gap-2">
                {(['light', 'dark', 'system'] as const).map((option) => (
                  <button
                    key={option}
                    type="button"
                    onClick={() => setTheme(option)}
                    className={cn(
                      'flex-1 cursor-pointer rounded-[var(--r-sm)] border px-2 py-1.5 text-[12.5px] font-semibold',
                      theme === option
                        ? 'border-accent text-accent'
                        : 'border-border text-muted hover:border-border-strong',
                    )}
                    style={theme === option ? { background: mix('--accent', 10) } : undefined}
                  >
                    {option === 'light' ? 'Hell' : option === 'dark' ? 'Dunkel' : 'System'}
                  </button>
                ))}
              </div>
            </div>
          </div>

          <div className="border-border border-t px-2 py-2">
            <button
              type="button"
              onClick={() => {
                onOpenChange(false);
                void signOut();
              }}
              className={cn(
                'hover:bg-surface-2 flex w-full cursor-pointer items-center gap-2.5 rounded-[var(--r-sm)]',
                'border-none px-3 py-2.5 text-left text-[13.5px] font-semibold',
              )}
            >
              <Icon name="logout" size={19} className="text-muted" />
              Abmelden
            </button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
