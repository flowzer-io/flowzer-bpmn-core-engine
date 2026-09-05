import { useEffect, useState } from 'react';

import { completeSignIn } from '@/lib/auth/oidc';

/**
 * Nimmt die Rückleitung des Identity Providers entgegen und führt dorthin weiter,
 * wo die Person hinwollte. Ein Fehler wird angezeigt statt verschluckt: Sonst
 * bliebe eine leere Seite zurück, und niemand wüsste warum.
 */
export function AuthenticationCallbackPage() {
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    completeSignIn()
      .then((target) => {
        if (!cancelled) window.location.replace(target);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause.message : String(cause));
      });

    return () => {
      cancelled = true;
    };
  }, []);

  if (error) {
    return (
      <div className="grid h-screen place-items-center px-6">
        <div className="max-w-[460px] text-center">
          <h1 className="mb-2 text-[18px] font-semibold">Anmeldung fehlgeschlagen</h1>
          <p className="text-muted mb-5 text-[14px] leading-relaxed">{error}</p>
          <a className="text-accent text-[14px] font-semibold" href="/">
            Zurück zur Startseite
          </a>
        </div>
      </div>
    );
  }

  return (
    <div className="grid h-screen place-items-center">
      <p className="text-muted text-sm">Anmeldung wird abgeschlossen …</p>
    </div>
  );
}

/** Rückleitung nach dem Abmelden. */
export function SignedOutPage() {
  return (
    <div className="grid h-screen place-items-center px-6">
      <div className="max-w-[420px] text-center">
        <h1 className="mb-2 text-[18px] font-semibold">Abgemeldet</h1>
        <p className="text-muted mb-5 text-[14px]">Sie sind von Flowzer abgemeldet.</p>
        <a className="text-accent text-[14px] font-semibold" href="/">
          Erneut anmelden
        </a>
      </div>
    </div>
  );
}
