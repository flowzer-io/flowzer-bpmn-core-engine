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
  /**
   * Akzentfarbe der Oberfläche. Sie gehört zum Erscheinungsbild des Unternehmens und
   * ist deshalb eine Einstellung der Bereitstellung, keine persönliche Vorliebe: Alle
   * sehen dieselbe. Unbekannte Werte fallen still auf `iris` zurück — eine falsch
   * geschriebene Farbe soll die Oberfläche nicht am Starten hindern.
   */
  accent: Accent;
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
  /**
   * Namen der Rollen, wie der Betrieb sie im Identity Provider vergeben hat. Die API
   * wertet sie konfigurierbar aus; heißt eine Rolle dort `flowzer.modeler`, muss die
   * Oberfläche denselben Namen kennen, sonst zeigt sie trotz Berechtigung die
   * reduzierte Ansicht.
   */
  roleNames: RoleNames;
}

export const ACCENTS = ['iris', 'teal', 'emerald', 'amber', 'rose'] as const;
export type Accent = (typeof ACCENTS)[number];

const DEFAULT_ACCENT: Accent = 'iris';

function toAccent(value: unknown): Accent {
  return typeof value === 'string' && (ACCENTS as readonly string[]).includes(value)
    ? (value as Accent)
    : DEFAULT_ACCENT;
}

export interface RoleNames {
  access: string;
  modeler: string;
  operator: string;
  worker: string;
}

const DEFAULT_ROLE_NAMES: RoleNames = {
  access: 'access',
  modeler: 'modeler',
  operator: 'operator',
  worker: 'worker',
};

export class RuntimeConfigError extends Error {}

const FALLBACK: RuntimeConfig = {
  apiBaseUrl: (import.meta.env.VITE_FLOWZER_API_URL ?? '/api').replace(/\/+$/, ''),
  accent: toAccent(import.meta.env.VITE_FLOWZER_ACCENT),
  oidcAuthority: (import.meta.env.VITE_FLOWZER_OIDC_AUTHORITY ?? '').trim(),
  oidcClientId: (import.meta.env.VITE_FLOWZER_OIDC_CLIENT_ID ?? '').trim(),
  oidcAudience: (import.meta.env.VITE_FLOWZER_OIDC_AUDIENCE ?? '').trim(),
  oidcScopes: splitScopes(import.meta.env.VITE_FLOWZER_OIDC_SCOPES),
  roleNames: DEFAULT_ROLE_NAMES,
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
  let response: Response | null = null;
  try {
    response = await fetch(`${import.meta.env.BASE_URL}config.json`, { cache: 'no-store' });
  } catch {
    // Eine fehlende Datei ist der Normalfall im Entwicklungsbetrieb, kein Fehler.
  }

  if (response?.ok) {
    let raw: Partial<RuntimeConfig> & {
      oidcScopes?: string[] | string;
      roleNames?: Partial<RoleNames>;
    };

    try {
      raw = await response.json();
    } catch (cause) {
      // Bewusst nicht wie eine fehlende Datei behandelt: Die Datei ist da, der Betrieb
      // wollte also konfigurieren. Still auf die Bauwerte zurueckzufallen hiesse, im
      // Container mit der Entwicklungsadresse und ohne Anmeldung zu starten — und das
      // faellt erst auf, wenn jemand vor einer leeren Oberflaeche sitzt.
      throw new RuntimeConfigError(
        `Die Datei config.json ist vorhanden, aber kein gültiges JSON: ${
          cause instanceof Error ? cause.message : String(cause)
        }`,
      );
    }

    current = {
      apiBaseUrl: (raw.apiBaseUrl ?? FALLBACK.apiBaseUrl).replace(/\/+$/, ''),
      accent: raw.accent === undefined ? FALLBACK.accent : toAccent(raw.accent),
      oidcAuthority: (raw.oidcAuthority ?? FALLBACK.oidcAuthority).trim(),
      oidcClientId: (raw.oidcClientId ?? FALLBACK.oidcClientId).trim(),
      oidcAudience: (raw.oidcAudience ?? FALLBACK.oidcAudience).trim(),
      oidcScopes: Array.isArray(raw.oidcScopes) ? raw.oidcScopes : splitScopes(raw.oidcScopes),
      roleNames: { ...DEFAULT_ROLE_NAMES, ...(raw.roleNames ?? {}) },
    };
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

  // Die API muss im selben Origin liegen. Zwei Gründe: Das Zugangstoken geht als
  // Bearer an diese Adresse, und die Einordnung einer Ablehnung kommt aus dem Header
  // `X-Flowzer-Access-Denied`, den ein Browser über Origin-Grenzen hinweg nur mit
  // `Access-Control-Expose-Headers` herausgibt. Ohne ihn erschiene jede fehlende
  // Einzelberechtigung als kompletter Zugangsverlust.
  //
  // Ein führender Schrägstrich genügt als Prüfung nicht: `//fremde.example` ist
  // protokollrelativ und landet bei einem fremden Origin.
  if (config.apiBaseUrl.length > 0) {
    const resolved = tryParse(config.apiBaseUrl, window.location.origin);
    if (!resolved || resolved.origin !== window.location.origin) {
      throw new RuntimeConfigError(
        `Die API-Adresse "${config.apiBaseUrl}" liegt nicht im selben Origin wie die Oberfläche. ` +
          'Im Container leitet das mitgelieferte nginx die API-Pfade weiter; dort genügt "/".',
      );
    }
  }
}

function tryParse(value: string, base?: string): URL | null {
  try {
    return new URL(value, base);
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
