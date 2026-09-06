import { Button } from '@/components/ui/Button';
import { Icon } from '@/components/ui/Icon';
import { useSession } from '@/stores/session';

/**
 * Was zu sehen ist, solange niemand angemeldet ist. Ohne diese Seite liefe die
 * Konsole in eine Reihe abgelehnter Aufrufe und zeigte nur Fehlermeldungen.
 */
export function SignInGate({ status }: { status: 'unknown' | 'anonymous' | 'signed-in' }) {
  const signIn = useSession((state) => state.signIn);

  if (status === 'unknown') {
    return (
      <div className="grid h-screen place-items-center">
        <p className="text-muted text-sm">Anmeldung wird geprüft …</p>
      </div>
    );
  }

  return (
    <div className="grid h-screen place-items-center px-6">
      <div className="bg-surface border-border shadow-pop w-full max-w-[420px] rounded-[var(--r-lg)] border p-8 text-center">
        <span className="from-accent to-accent-2 mx-auto mb-5 grid h-14 w-14 place-items-center rounded-full bg-gradient-to-br text-accent-ink">
          <Icon name="account_circle" size={30} />
        </span>
        <h1 className="mb-2 text-[20px] font-semibold">Flowzer Console</h1>
        <p className="text-muted mb-6 text-[14px] leading-relaxed">
          Melden Sie sich mit Ihrem Firmenkonto an, um Prozesse, Instanzen und Aufgaben zu sehen.
        </p>
        <Button onClick={() => void signIn()} className="w-full justify-center">
          Anmelden
        </Button>
      </div>
    </div>
  );
}
