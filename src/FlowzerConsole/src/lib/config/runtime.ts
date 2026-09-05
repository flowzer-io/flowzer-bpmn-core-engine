/**
 * Laufzeitkonfiguration der Konsole.
 *
 * Ein gebautes SPA-Bundle ist unveränderlich; Adresse der API und des Identity
 * Providers dürfen aber nicht beim Bauen feststehen, sonst braucht jede Umgebung
 * ein eigenes Image. Der Container schreibt deshalb beim Start `config.json`, und
 * die Anwendung liest sie, bevor sie das erste Mal zeichnet. Im Entwicklungsbetrieb
 * ohne diese Datei greifen die `VITE_`-Werte.
 */
export interface RuntimeConfig {
  /** Basisadresse der Flowzer-API, ohne abschließenden Schrägstrich. */
  apiBaseUrl: string;
  /** OIDC-Issuer, z. B. `https://auth.example/realms/MaassIT`. Leer = ohne Anmeldung. */
  oidcAuthority: string;
  oidcClientId: string;
  /** Zusätzliche Scopes über `openid profile email` hinaus. */
  oidcScopes: string[];
}

const FALLBACK: RuntimeConfig = {
  apiBaseUrl: (import.meta.env.VITE_FLOWZER_API_URL ?? '/api').replace(/\/+$/, ''),
  oidcAuthority: (import.meta.env.VITE_FLOWZER_OIDC_AUTHORITY ?? '').trim(),
  oidcClientId: (import.meta.env.VITE_FLOWZER_OIDC_CLIENT_ID ?? '').trim(),
  oidcScopes: splitScopes(import.meta.env.VITE_FLOWZER_OIDC_SCOPES),
};

let current: RuntimeConfig = FALLBACK;

export function getRuntimeConfig(): RuntimeConfig {
  return current;
}

/** Wahr, sobald ein Identity Provider konfiguriert ist. */
export function isAuthenticationConfigured(): boolean {
  return current.oidcAuthority.length > 0 && current.oidcClientId.length > 0;
}

/**
 * Lädt `config.json` neben dem Bundle. Fehlt sie oder ist sie unlesbar, bleibt es
 * bei den Bauwerten: Der Entwicklungsbetrieb soll ohne zusätzliche Datei laufen.
 */
export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  try {
    const response = await fetch(`${import.meta.env.BASE_URL}config.json`, { cache: 'no-store' });
    if (!response.ok) return current;

    const raw = (await response.json()) as Partial<RuntimeConfig> & { oidcScopes?: string[] | string };
    current = {
      apiBaseUrl: (raw.apiBaseUrl ?? FALLBACK.apiBaseUrl).replace(/\/+$/, ''),
      oidcAuthority: (raw.oidcAuthority ?? FALLBACK.oidcAuthority).trim(),
      oidcClientId: (raw.oidcClientId ?? FALLBACK.oidcClientId).trim(),
      oidcScopes: Array.isArray(raw.oidcScopes) ? raw.oidcScopes : splitScopes(raw.oidcScopes),
    };
  } catch {
    // Eine fehlende Datei ist der Normalfall im Entwicklungsbetrieb, kein Fehler.
  }

  return current;
}

function splitScopes(value: string | undefined): string[] {
  return (value ?? '')
    .split(/[\s,]+/)
    .map((scope) => scope.trim())
    .filter(Boolean);
}
