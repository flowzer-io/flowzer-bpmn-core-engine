import { ApiError, clearCsrfToken, request } from '@/lib/api/client';

/** Datensparsame Antwort des serverseitigen BFF. */
export interface BffSession {
  id: string;
  name: string;
  email?: string;
  capabilities: string[];
}

/** CSRF-Vertrag des BFF; der Token wird absichtlich nur im JavaScript-Speicher gehalten. */
export interface BffCsrf {
  requestToken: string;
  headerName: string;
}

export class BffError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'BffError';
    this.status = status;
  }
}

/** Erlaubt nur lokale absolute Pfade als Rücksprungziel nach dem Login. */
export function sanitiseReturnTo(target?: string): string {
  if (!target || !target.startsWith('/') || target.startsWith('//') || target.startsWith('/\\')) return '/';

  try {
    const resolved = new URL(target, window.location.origin);
    return resolved.origin === window.location.origin ? `${resolved.pathname}${resolved.search}${resolved.hash}` : '/';
  } catch {
    return '/';
  }
}

export function buildLoginUrl(returnTo?: string): string {
  return `/bff/login?returnTo=${encodeURIComponent(sanitiseReturnTo(returnTo))}`;
}

/** Navigiert den Browser zum serverseitigen OIDC-Code-Flow. */
export function signIn(returnTo = `${window.location.pathname}${window.location.search}${window.location.hash}`): void {
  window.location.assign(buildLoginUrl(returnTo));
}

export async function fetchSession(): Promise<BffSession | null> {
  const response = await fetch('/bff/session', { credentials: 'same-origin' });
  if (response.status === 401) return null;
  if (!response.ok) throw new BffError('Die BFF-Sitzung konnte nicht geladen werden.', response.status);

  const value = (await response.json()) as unknown;
  if (!isSession(value)) throw new BffError('Der BFF lieferte eine ungültige Sitzung.', response.status);
  return value;
}

export async function logout(): Promise<void> {
  try {
    await request<void>('/bff/logout', { method: 'POST' });
  } catch (error) {
    if (!(error instanceof ApiError) || error.status !== 401) {
      throw error;
    }
  } finally {
    clearCsrfToken();
  }
}

function isSession(value: unknown): value is BffSession {
  if (!value || typeof value !== 'object') return false;
  const record = value as Record<string, unknown>;
  return (
    typeof record.id === 'string' &&
    typeof record.name === 'string' &&
    (record.email === undefined || typeof record.email === 'string') &&
    Array.isArray(record.capabilities) &&
    record.capabilities.every((capability) => typeof capability === 'string')
  );
}
