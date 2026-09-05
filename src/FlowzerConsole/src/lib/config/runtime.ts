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
  /**
   * Audience der API im Access-Token. Unter ihr stehen die Clientrollen. Ohne diese
   * Angabe würden Rollen fremder Clients mitgelesen, und jemand mit `operator` auf
   * einer anderen Anwendung sähe hier die vollständige Konsole.
   */
  oidcAudience: string;
  /** Zusätzliche Scopes über `openid profile email` hinaus. */
  oidcScopes: string[];
}

export class RuntimeConfigError extends Error {}

const FALLBACK: RuntimeConfig = {
  apiBaseUrl: (import.meta.env.VITE_FLOWZER_API_URL ?? '/api').replace(/\/+$/, ''),
  oidcAuthority: (import.meta.env.VITE_FLOWZER_OIDC_AUTHORITY ?? '').trim(),
  oidcClientId: (import.meta.env.VITE_FLOWZER_OIDC_CLIENT_ID ?? '').trim(),
  oidcAudience: (import.meta.env.VITE_FLOWZER_OIDC_AUDIENCE ?? '').trim(),
  oidcScopes: splitScopes(import.meta.env.VITE_FLOWZER_OIDC_SCOPES),
};

let current: RuntimeConfig = FALLBACK;

export function getRuntimeConfig(): RuntimeConfig {
  return current;
}

/** Wahr, sobald ein Identity Provider vollständig konfiguriert ist. */
export function isAuthenticationConfigured(): boolean {
  return current.oidcAuthority.length > 0 && current.oidcClientId.length > 0;
}

/**
 * Lädt `config.json` neben dem Bundle. Fehlt sie oder ist sie unlesbar, bleibt es
 * bei den Bauwerten: Der Entwicklungsbetrieb soll ohne zusätzliche Datei laufen.
 *
 * Wirft, wenn die Konfiguration halb ist. Eine Anwendung, die die Anmeldung wegen
 * einer vergessenen Client-Id überspringt, zeigt sonst die volle Oberfläche, während
 * die API jeden Aufruf ablehnt — der schlechteste aller Zustände.
 */
export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  try {
    const response = await fetch(`${import.meta.env.BASE_URL}config.json`, { cache: 'no-store' });
    if (response.ok) {
      const raw = (await response.json()) as Partial<RuntimeConfig> & { oidcScopes?: string[] | string };
      current = {
        apiBaseUrl: (raw.apiBaseUrl ?? FALLBACK.apiBaseUrl).replace(/\/+$/, ''),
        oidcAuthority: (raw.oidcAuthority ?? FALLBACK.oidcAuthority).trim(),
        oidcClientId: (raw.oidcClientId ?? FALLBACK.oidcClientId).trim(),
        oidcAudience: (raw.oidcAudience ?? FALLBACK.oidcAudience).trim(),
        oidcScopes: Array.isArray(raw.oidcScopes) ? raw.oidcScopes : splitScopes(raw.oidcScopes),
      };
    }
  } catch {
    // Eine fehlende Datei ist der Normalfall im Entwicklungsbetrieb, kein Fehler.
  }

  validate(current);
  return current;
}

function validate(config: RuntimeConfig): void {
  const hasAuthority = config.oidcAuthority.length > 0;
  const hasClientId = config.oidcClientId.length > 0;

  if (hasAuthority !== hasClientId) {
    throw new RuntimeConfigError(
      'Die Anmeldung ist unvollständig konfiguriert: Es müssen Authority und Client-Id gesetzt sein oder keines von beidem.',
    );
  }

  // Das Zugangstoken geht als Bearer an diese Adresse. Ein absolutes Ziel müsste
  // ausdrücklich vertrauenswürdig sein; ein relativer Pfad bleibt beim eigenen Origin.
  if (config.apiBaseUrl.length > 0 && !config.apiBaseUrl.startsWith('/')) {
    const target = tryParse(config.apiBaseUrl);
    if (!target || target.protocol !== 'https:' || target.origin === 'null') {
      throw new RuntimeConfigError(
        `Die API-Adresse "${config.apiBaseUrl}" ist weder ein relativer Pfad noch eine https-Adresse.`,
      );
    }
  }
}

function tryParse(value: string): URL | null {
  try {
    return new URL(value);
  } catch {
    return null;
  }
}

function splitScopes(value: string | undefined): string[] {
  return (value ?? '')
    .split(/[\s,]+/)
    .map((scope) => scope.trim())
    .filter(Boolean);
}
