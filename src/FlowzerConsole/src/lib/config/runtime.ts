/**
 * Laufzeitkonfiguration der Konsole.
 *
 * Das SPA-Bundle bleibt unverändert. Der Server liefert nur unkritische
 * Bereitstellungswerte; insbesondere gelangen weder OIDC-Clientdaten noch
 * Rollennamen in den Browser. Die eigentliche Anmeldung läuft ausschließlich
 * über den same-origin BFF.
 */
export interface RuntimeConfig {
  /** Basisadresse der Flowzer-API, ohne abschließenden Schrägstrich. */
  apiBaseUrl: string;
  /** Akzentfarbe der Oberfläche. Unbekannte Werte fallen auf `iris` zurück. */
  accent: Accent;
  /** Aktiviert die serverseitige BFF-Sitzung für diese Bereitstellung. */
  bffEnabled: boolean;
}

export const ACCENTS = ['iris', 'teal', 'emerald', 'amber', 'rose'] as const;
export type Accent = (typeof ACCENTS)[number];

const DEFAULT_ACCENT: Accent = 'iris';

function toAccent(value: unknown): Accent {
  return typeof value === 'string' && (ACCENTS as readonly string[]).includes(value)
    ? (value as Accent)
    : DEFAULT_ACCENT;
}

function toBoolean(value: unknown, fallback: boolean): boolean {
  if (typeof value === 'boolean') return value;
  if (typeof value === 'string') {
    if (value.trim().toLowerCase() === 'true') return true;
    if (value.trim().toLowerCase() === 'false') return false;
  }
  return fallback;
}

export class RuntimeConfigError extends Error {}

const FALLBACK: RuntimeConfig = {
  apiBaseUrl: (import.meta.env.VITE_FLOWZER_API_URL ?? '/api').replace(/\/+$/, ''),
  accent: toAccent(import.meta.env.VITE_FLOWZER_ACCENT),
  bffEnabled: toBoolean(import.meta.env.VITE_FLOWZER_BFF_ENABLED, false),
};

let current: RuntimeConfig = FALLBACK;

export function getRuntimeConfig(): RuntimeConfig {
  return current;
}

/** Lädt `config.json` neben dem Bundle und validiert den same-origin API-Pfad. */
export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  let response: Response | null = null;
  try {
    response = await fetch(`${import.meta.env.BASE_URL}config.json`, { cache: 'no-store' });
  } catch {
    // Eine fehlende Datei ist der Normalfall im Entwicklungsbetrieb.
  }

  if (response?.ok && isJson(response)) {
    let raw: { apiBaseUrl?: unknown; accent?: unknown; bffEnabled?: unknown };

    try {
      raw = await response.json();
    } catch (cause) {
      throw new RuntimeConfigError(
        `Die Datei config.json ist vorhanden, aber kein gültiges JSON: ${
          cause instanceof Error ? cause.message : String(cause)
        }`,
      );
    }

    current = {
      apiBaseUrl:
        typeof raw.apiBaseUrl === 'string' ? raw.apiBaseUrl.replace(/\/+$/, '') : FALLBACK.apiBaseUrl,
      accent: raw.accent === undefined ? FALLBACK.accent : toAccent(raw.accent),
      bffEnabled: toBoolean(raw.bffEnabled, FALLBACK.bffEnabled),
    };
  }

  validate(current);
  return current;
}

function validate(config: RuntimeConfig): void {
  // Ein führender Schrägstrich genügt nicht: `//fremde.example` ist
  // protokollrelativ und würde API-Cookies an einen fremden Origin senden.
  if (config.apiBaseUrl.length > 0) {
    const resolved = tryParse(config.apiBaseUrl, window.location.origin);
    if (!resolved || resolved.origin !== window.location.origin) {
      throw new RuntimeConfigError(
        `Die API-Adresse "${config.apiBaseUrl}" liegt nicht im selben Origin wie die Oberfläche. ` +
          'Im Container leitet nginx die API-Pfade weiter; dort genügt "/".',
      );
    }
  }
}

function isJson(response: Response): boolean {
  return (response.headers.get('content-type') ?? '').toLowerCase().includes('json');
}

function tryParse(value: string, base?: string): URL | null {
  try {
    return new URL(value, base);
  } catch {
    return null;
  }
}
