import { errorMessage, FlowzerApiError } from './errors.js';
import type {
  FlowzerAuth,
  FlowzerCallOptions,
  FlowzerClientOptions,
  FlowzerCompletionOptions,
} from './types.js';

interface ApiEnvelope<T> {
  successful?: boolean;
  errorMessage?: string | null;
  result?: T | null;
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE';

interface RequestOptions extends FlowzerCallOptions {
  method?: Method;
  body?: unknown;
  query?: Record<string, string | number | null | undefined>;
  idempotencyKey?: string | undefined;
}

function validHeaderValue(value: string) {
  return value.length > 0 && !value.includes('\r') && !value.includes('\n');
}

function validateIdempotencyKey(value: string) {
  if (value.length < 1 || value.length > 200 || !/^[\x21-\x7E]+$/.test(value)) {
    throw new TypeError('idempotencyKey must contain 1-200 visible ASCII characters.');
  }
}

/** Zustandsloser HTTP-Transport; Authentisierung wird für jeden Aufruf vom Host bezogen. */
export class FlowzerTransport {
  private readonly baseUrl: string;
  private readonly fetch: typeof globalThis.fetch;
  private readonly auth: FlowzerAuth | undefined;
  private readonly onUnauthorized: (() => void) | undefined;

  constructor(options: FlowzerClientOptions) {
    if (!options.baseUrl || options.baseUrl.trim().length === 0) {
      throw new TypeError('baseUrl is required.');
    }
    let baseUrlEnd = options.baseUrl.length;
    while (baseUrlEnd > 0 && options.baseUrl[baseUrlEnd - 1] === '/') baseUrlEnd -= 1;
    this.baseUrl = options.baseUrl.slice(0, baseUrlEnd);
    this.fetch = options.fetch ?? globalThis.fetch;
    if (typeof this.fetch !== 'function') throw new TypeError('A fetch implementation is required.');
    this.auth = options.auth;
    this.onUnauthorized = options.onUnauthorized;
  }

  async status<T>(path: string, options: RequestOptions = {}): Promise<T> {
    const body = await this.request<ApiEnvelope<T>>(path, options);
    if (!body || typeof body !== 'object' || Array.isArray(body)) {
      throw new FlowzerApiError('Unexpected Flowzer API response.', {
        status: 200,
        url: this.url(path, options.query),
        body,
      });
    }
    if (body.successful === false) {
      throw new FlowzerApiError(body.errorMessage ?? 'The Flowzer API rejected the request.', {
        status: 200,
        url: this.url(path, options.query),
        body,
      });
    }
    return body.result as T;
  }

  async statusResult<T>(path: string, options: RequestOptions = {}): Promise<T> {
    const result = await this.status<T | null | undefined>(path, options);
    if (result === null || result === undefined) {
      throw new FlowzerApiError('The Flowzer API response contains no result.', {
        status: 200,
        url: this.url(path, options.query),
        body: result,
      });
    }
    return result;
  }

  async statusVoid(path: string, options: RequestOptions = {}): Promise<void> {
    await this.status<unknown>(path, options);
  }

  private async request<T>(path: string, options: RequestOptions): Promise<T> {
    const method = options.method ?? 'GET';
    const headers = new Headers({ Accept: 'application/json, application/problem+json' });
    const credentials: RequestCredentials = this.auth?.kind === 'cookie' ? 'include' : 'omit';

    if (this.auth?.kind === 'bearer') {
      const token = await this.auth.getAccessToken();
      if (!validHeaderValue(token)) throw new TypeError('getAccessToken returned an invalid token.');
      headers.set('Authorization', `Bearer ${token}`);
    } else if (this.auth?.kind === 'cookie' && method !== 'GET') {
      const csrf = await this.auth.getCsrfToken();
      if (!/^[A-Za-z0-9-]+$/.test(csrf.headerName) || !validHeaderValue(csrf.requestToken)) {
        throw new TypeError('getCsrfToken returned an invalid header.');
      }
      headers.set(csrf.headerName, csrf.requestToken);
    }

    if (options.idempotencyKey !== undefined) {
      validateIdempotencyKey(options.idempotencyKey);
      headers.set('Idempotency-Key', options.idempotencyKey);
    }

    let requestBody: string | undefined;
    if (options.body !== undefined) {
      requestBody = JSON.stringify(options.body);
      headers.set('Content-Type', 'application/json');
    }

    const url = this.url(path, options.query);
    let response: Response;
    try {
      response = await this.fetch(url, {
        method,
        headers,
        credentials,
        ...(requestBody === undefined ? {} : { body: requestBody }),
        ...(options.signal === undefined ? {} : { signal: options.signal }),
      });
    } catch (cause) {
      if (cause instanceof Error && cause.name === 'AbortError') throw cause;
      throw new FlowzerApiError('The Flowzer API is unreachable.', { status: 0, url, body: cause });
    }

    const parsed = await readBody(response);
    if (response.status === 401) this.onUnauthorized?.();
    if (!response.ok) {
      throw new FlowzerApiError(
        errorMessage(parsed, `${response.status} ${response.statusText}`),
        { status: response.status, url, body: parsed },
      );
    }
    return parsed as T;
  }

  private url(path: string, query?: RequestOptions['query']) {
    const normalizedPath = path.startsWith('/') ? path : `/${path}`;
    const url = `${this.baseUrl}${normalizedPath}`;
    if (!query) return url;
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null) params.set(key, String(value));
    }
    const serialized = params.toString();
    return serialized ? `${url}?${serialized}` : url;
  }
}

async function readBody(response: Response): Promise<unknown> {
  if (response.status === 204) return null;
  const text = await response.text();
  if (text.length === 0) return null;
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return text;
  }
}

export function completionOptions(
  options: FlowzerCompletionOptions | undefined,
): Pick<RequestOptions, 'signal' | 'idempotencyKey'> {
  if (!options || options.idempotencyKey === undefined) {
    throw new TypeError('An explicit idempotencyKey is required for task completion.');
  }
  return {
    ...(options.signal === undefined ? {} : { signal: options.signal }),
    idempotencyKey: options.idempotencyKey,
  };
}
