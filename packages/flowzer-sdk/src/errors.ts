import type { FlowzerProblemDetails } from './types.js';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readFieldErrors(value: unknown): Record<string, string[]> {
  if (!isRecord(value)) return {};
  const result: Record<string, string[]> = {};
  for (const [key, candidate] of Object.entries(value)) {
    if (Array.isArray(candidate) && candidate.every((entry) => typeof entry === 'string')) {
      result[key] = [...candidate];
    }
  }
  return result;
}

export class FlowzerApiError extends Error {
  readonly status: number;
  readonly url: string;
  readonly body: unknown;
  readonly problem: FlowzerProblemDetails | null;
  readonly fieldErrors: Record<string, string[]>;
  readonly traceId: string | null;

  constructor(message: string, options: { status: number; url: string; body?: unknown }) {
    super(message);
    this.name = 'FlowzerApiError';
    this.status = options.status;
    this.url = options.url;
    this.body = options.body;
    this.problem = isRecord(options.body) ? options.body as FlowzerProblemDetails : null;
    this.fieldErrors = readFieldErrors(this.problem?.errors);
    this.traceId = typeof this.problem?.traceId === 'string' ? this.problem.traceId : null;
  }
}

export function errorMessage(body: unknown, fallback: string): string {
  if (typeof body === 'string' && body.trim().length > 0) return body;
  if (!isRecord(body)) return fallback;
  for (const key of ['errorMessage', 'detail', 'title'] as const) {
    const value = body[key];
    if (typeof value === 'string' && value.trim().length > 0) return value;
  }
  return fallback;
}
