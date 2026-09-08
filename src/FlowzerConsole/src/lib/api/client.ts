import { getRuntimeConfig } from '@/lib/config/runtime';

import type { ApiStatusResult } from './types';

/**
 * Basis-URL der Flowzer-API.
 *
 * Im Dev-Betrieb zeigt der Standardwert auf den Vite-Proxy (`/api`), damit weder
 * CORS noch abweichende Origins eine Rolle spielen. Im Container kommt der Wert
 * aus der Laufzeitkonfiguration; ein gebautes Bundle soll nicht je Umgebung neu
 * gebaut werden müssen.
 */
export let API_BASE_URL: string = (import.meta.env.VITE_FLOWZER_API_URL ?? '/api').replace(/\/+$/, '');

const DEVELOPMENT_USER_ID = 'd266f2b6-e96e-4d4a-9c20-c8e541394df0';
const DEVELOPMENT_USER_HEADER = 'X-Flowzer-UserId';

/** Übernimmt die geladene Laufzeitkonfiguration. Wird einmal beim Start gerufen. */
export function applyRuntimeConfig(): void {
  API_BASE_URL = getRuntimeConfig().apiBaseUrl;
}

export class ApiError extends Error {
  readonly status: number;
  readonly url: string;
  readonly body: unknown;

  constructor(message: string, options: { status: number; url: string; body?: unknown }) {
    super(message);
    this.name = 'ApiError';
    this.status = options.status;
    this.url = options.url;
    this.body = options.body;
  }
}

let csrf: { requestToken: string; headerName: string } | null = null;
let csrfRequest: Promise<{ requestToken: string; headerName: string }> | null = null;

/** Setzt den nur im Speicher gehaltenen CSRF-Token zurück, etwa nach einer 401-Antwort. */
export function clearCsrfToken(): void {
  csrf = null;
  csrfRequest = null;
}

/** Meldet eine abgelaufene BFF-Sitzung an den Session-Store. */
let unauthorizedHandler: () => void = () => {};

export function setUnauthorizedHandler(handler: () => void): void {
  unauthorizedHandler = handler;
}

/** Antwortheader, mit dem die API eine Ablehnung einordnet. */
export const ACCESS_DENIED_HEADER = 'X-Flowzer-Access-Denied';

let accessDeniedHandler: (denied: boolean) => void = () => {};

/** Hinterlegt, wohin der Client meldet, dass dieses Konto nicht freigeschaltet ist. */
export function setAccessDeniedHandler(handler: (denied: boolean) => void): void {
  accessDeniedHandler = handler;
}

async function getCsrfToken(): Promise<{ requestToken: string; headerName: string }> {
  if (csrf) return csrf;
  if (csrfRequest) return csrfRequest;

  csrfRequest = (async () => {
    const response = await fetch('/bff/csrf', { credentials: 'same-origin' });
    const parsed = await readBody(response, false);
    if (response.status === 401) {
      clearCsrfToken();
      unauthorizedHandler();
    }
    if (!response.ok || !isCsrf(parsed)) {
      throw new ApiError('Der CSRF-Schutz konnte nicht geladen werden.', {
        status: response.status,
        url: '/bff/csrf',
        body: parsed,
      });
    }

    csrf = parsed;
    return parsed;
  })();

  try {
    return await csrfRequest;
  } finally {
    csrfRequest = null;
  }
}

function isCsrf(value: unknown): value is { requestToken: string; headerName: string } {
  if (!value || typeof value !== 'object') return false;
  const record = value as Record<string, unknown>;
  return typeof record.requestToken === 'string' && typeof record.headerName === 'string' && record.headerName.length > 0;
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'PATCH';
  /** JSON-Body. Wird automatisch serialisiert. */
  body?: unknown;
  /** Roher Body (z. B. BPMN-XML). Hat Vorrang vor `body`. */
  rawBody?: string;
  contentType?: string;
  query?: Record<string, string | number | boolean | undefined | null>;
  signal?: AbortSignal;
  /** Antwort als Text statt JSON lesen. */
  asText?: boolean;
}

function buildUrl(path: string, query?: RequestOptions['query']): string {
  const url = `${API_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`;
  if (!query) return url;

  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue;
    params.append(key, String(value));
  }

  const serialized = params.toString();
  return serialized ? `${url}?${serialized}` : url;
}

async function readBody(response: Response, asText: boolean): Promise<unknown> {
  if (response.status === 204) return null;

  const text = await response.text();
  if (text.length === 0) return null;
  if (asText) return unwrapJsonString(text);

  try {
    return JSON.parse(text) as unknown;
  } catch {
    // Einige Endpunkte (z. B. das BPMN-XML) antworten mit Text ohne JSON-Content-Type.
    return text;
  }
}

/**
 * Schutz vor JSON-verpacktem Text.
 *
 * Ältere API-Stände geben `Ok(xml)` zurück; bei `Accept: application/json` macht
 * die Content-Negotiation daraus ein JSON-String-Literal ("<?xml …\" …"). Ohne
 * Auspacken sieht ein XML-Parser ein führendes Anführungszeichen und bricht ab.
 */
function unwrapJsonString(text: string): string {
  if (!text.startsWith('"')) return text;

  try {
    const parsed = JSON.parse(text) as unknown;
    return typeof parsed === 'string' ? parsed : text;
  } catch {
    return text;
  }
}

function extractErrorMessage(body: unknown, fallback: string): string {
  if (typeof body === 'string' && body.trim().length > 0) return body;

  if (body !== null && typeof body === 'object') {
    const record = body as Record<string, unknown>;
    if (typeof record.errorMessage === 'string' && record.errorMessage.length > 0) {
      return record.errorMessage;
    }
    if (typeof record.title === 'string' && record.title.length > 0) {
      return record.title;
    }
    if (typeof record.detail === 'string' && record.detail.length > 0) {
      return record.detail;
    }
  }

  return fallback;
}

/** Führt einen Request gegen die Flowzer-API aus und wirft bei Fehlern eine `ApiError`. */
export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, rawBody, contentType, query, signal, asText = false } = options;

  // Text-Antworten (BPMN-XML) dürfen nicht durch den JSON-Formatter laufen —
  // sonst kommt ein JSON-String-Literal statt des Dokuments zurück.
  const headers: Record<string, string> = {
    Accept: asText ? 'application/xml, text/plain' : 'application/json, text/plain',
  };

  // Nur Vites Entwicklungsbuild zusammen mit dem explizit deaktivierten BFF darf den
  // technischen Header verwenden. Die API akzeptiert ihn ebenfalls nur in Development;
  // produktive Browser senden weder diesen Header noch ein Bearer-Token.
  if (import.meta.env.DEV && !getRuntimeConfig().bffEnabled) {
    headers[DEVELOPMENT_USER_HEADER] = DEVELOPMENT_USER_ID;
  }

  if (
    getRuntimeConfig().bffEnabled &&
    (method === 'POST' || method === 'PUT' || method === 'PATCH' || method === 'DELETE')
  ) {
    const csrfToken = await getCsrfToken();
    headers[csrfToken.headerName] = csrfToken.requestToken;
  }

  let payload: BodyInit | undefined;
  if (rawBody !== undefined) {
    payload = rawBody;
    headers['Content-Type'] = contentType ?? 'text/plain';
  } else if (body !== undefined) {
    payload = JSON.stringify(body);
    headers['Content-Type'] = contentType ?? 'application/json';
  }

  const url = buildUrl(path, query);
  let response: Response;

  try {
    response = await fetch(url, { method, headers, body: payload, signal, credentials: 'same-origin' });
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === 'AbortError') throw cause;
    throw new ApiError('Die Flowzer-API ist nicht erreichbar.', { status: 0, url, body: cause });
  }

  const parsed = await readBody(response, asText);

  if (response.status === 401 && getRuntimeConfig().bffEnabled) {
    clearCsrfToken();
    unauthorizedHandler();
  }

  // Die API ordnet jede Ablehnung ein: `application` heisst, dass dieses Konto Flowzer
  // gar nicht benutzen darf, `capability` nur, dass diese eine Handlung fehlt. Ohne die
  // Unterscheidung muesste die Oberflaeche jede Ablehnung als Zugangsverlust anzeigen.
  if (response.status === 403) {
    accessDeniedHandler(response.headers.get(ACCESS_DENIED_HEADER) !== 'capability');
  } else if (response.ok) {
    accessDeniedHandler(false);
  }

  if (!response.ok) {
    throw new ApiError(extractErrorMessage(parsed, `${response.status} ${response.statusText}`), {
      status: response.status,
      url,
      body: parsed,
    });
  }

  return parsed as T;
}

/**
 * Ruft einen Endpunkt auf, der in `ApiStatusResult<T>` verpackt antwortet, und
 * entpackt das Ergebnis. Ein `successful: false` wird wie ein HTTP-Fehler behandelt.
 */
export async function requestStatusResult<T>(path: string, options: RequestOptions = {}): Promise<T> {
  return unwrapStatusResult(await request<ApiStatusResult<T>>(path, options), path);
}

/**
 * Wie `requestStatusResult`, laesst aber eine leere Antwort zu und gibt dafuer `null` zurueck.
 *
 * Fuer Endpunkte, bei denen „es gibt nichts" eine gueltige Auskunft ist und kein Fehler — das
 * Startformular eines Workflows etwa antwortet mit 204, wenn der Workflow ohne Eingabe startet.
 */
export async function requestOptionalStatusResult<T>(
  path: string,
  options: RequestOptions = {},
): Promise<T | null> {
  const envelope = await request<ApiStatusResult<T> | null>(path, options);
  if (envelope === null) return null;

  return unwrapStatusResult(envelope, path);
}

function unwrapStatusResult<T>(envelope: ApiStatusResult<T>, path: string): T {
  if (!envelope || typeof envelope !== 'object') {
    throw new ApiError('Unerwartete Antwort der Flowzer-API.', { status: 200, url: path, body: envelope });
  }

  if (envelope.successful === false) {
    throw new ApiError(envelope.errorMessage ?? 'Die Anfrage wurde von der API abgelehnt.', {
      status: 200,
      url: path,
      body: envelope,
    });
  }

  return envelope.result as T;
}

/** Wie `requestStatusResult`, gibt bei fehlendem Ergebnis aber `void` zurück. */
export async function requestStatus(path: string, options: RequestOptions = {}): Promise<void> {
  await requestStatusResult<unknown>(path, options);
}
