import { ApiError } from '@/lib/api/client';

export type BpmnDiagnosticSeverity = 'error' | 'warning' | 'info';

/** Hostneutrale, stabile Darstellung eines BPMN-Validierungsbefunds. */
export interface BpmnDiagnostic {
  readonly code: string;
  readonly severity: BpmnDiagnosticSeverity;
  readonly message: string;
  readonly elementId?: string;
  readonly propertyPath?: string;
  readonly source: 'client' | 'server';
  readonly traceId?: string;
}

interface ProblemDetailsBody {
  readonly code?: unknown;
  readonly issues?: unknown;
  readonly traceId?: unknown;
}

interface ProblemIssue {
  readonly code?: unknown;
  readonly severity?: unknown;
  readonly message?: unknown;
  readonly elementId?: unknown;
  readonly propertyPath?: unknown;
}

/**
 * Liest ausschließlich den versionierten BPMN-Problemvertrag.
 * Unbekannte/alte Fehler werden bewusst nicht geraten: Der aufrufende Mutationspfad
 * kann sie weiterhin als normalen Toast anzeigen.
 */
export function normalizeBpmnDiagnostics(error: unknown): BpmnDiagnostic[] {
  const body = error instanceof ApiError ? error.body : error;
  if (!isRecord(body) || body.code !== 'bpmn.model.invalid' || !Array.isArray(body.issues)) return [];

  return body.issues.flatMap((entry) => {
    if (!isRecord(entry)) return [];
    const issue = entry as ProblemIssue;
    if (!isNonEmptyString(issue.code) || !isSeverity(issue.severity) || !isNonEmptyString(issue.message)) return [];

    const elementId = optionalString(issue.elementId);
    const propertyPath = optionalString(issue.propertyPath);
    return [{
      code: issue.code,
      severity: issue.severity,
      message: issue.message,
      ...(elementId ? { elementId } : {}),
      ...(propertyPath ? { propertyPath } : {}),
      source: 'server' as const,
      ...(isNonEmptyString(body.traceId) ? { traceId: body.traceId } : {}),
    }];
  });
}

function isRecord(value: unknown): value is ProblemDetailsBody & Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function optionalString(value: unknown): string | undefined {
  return isNonEmptyString(value) ? value : undefined;
}

function isSeverity(value: unknown): value is BpmnDiagnosticSeverity {
  return value === 'error' || value === 'warning' || value === 'info';
}
