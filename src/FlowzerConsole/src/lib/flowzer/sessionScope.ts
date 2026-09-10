/**
 * Ein Scope ist nur ein kurzlebiger Cache-Schlüssel. Er enthält absichtlich keine
 * Benutzerkennung, E-Mail, Cookie- oder Tokeninformation.
 */
export function createSessionScope(): string {
  return crypto.randomUUID();
}

type PublicPackageScopeCleanup = (sessionScope: string) => void;

let cleanup: PublicPackageScopeCleanup | null = null;

/**
 * Registriert den QueryClient-gebundenen Cleanup der öffentlichen React-Bausteine.
 * Die Registrierung lebt in der Console, damit das veröffentlichte Paket keinen
 * globalen Session- oder Browserzustand kennen muss.
 */
export function registerPublicPackageScopeCleanup(handler: PublicPackageScopeCleanup): () => void {
  cleanup = handler;
  return () => {
    if (cleanup === handler) cleanup = null;
  };
}

/** Entfernt die Daten einer beendeten Sitzung aus den öffentlichen Paket-Query-Keys. */
export function clearPublicPackageScope(sessionScope: string | null): void {
  if (sessionScope) cleanup?.(sessionScope);
}
