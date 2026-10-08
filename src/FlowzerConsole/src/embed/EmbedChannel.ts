/** Der Hostkanal transportiert nur Formularaktionen, niemals Tokens oder Berechtigungen. */
export type EmbedOperation = 'draft.save' | 'task.complete' | 'directory.search' | 'directory.resolve';
interface PendingCall { resolve: (value: unknown) => void; reject: (reason: unknown) => void; timer: ReturnType<typeof setTimeout>; cleanup: () => void; }
function object(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value); }

/** Fachfehler des authentifizierten Hosts; technische Remote-Texte werden nicht übernommen. */
export class EmbedActionError extends Error {
  readonly code: string;
  readonly fieldMessages: Record<string, string[]>;
  constructor(code: string, fields?: unknown) {
    const messages: Record<string, string> = {
      'flowzer.revision_conflict': 'Ein neuerer Zwischenstand liegt vor. Deine Eingaben bleiben erhalten; öffne das Formular bewusst neu, um den Serverstand zu laden.',
      'flowzer.access_denied': 'Die Aufgabe wurde beendet oder du darfst sie nicht mehr bearbeiten.',
      'flowzer.validation_failed': 'Bitte prüfe die Formulareingaben und die markierten Felder.',
      'flowzer.request_too_large': 'Der Formularinhalt ist zu groß.',
    };
    super((Object.hasOwn(messages, code) ? messages[code] : undefined) ?? 'Die Formularverbindung ist derzeit nicht verfügbar. Deine Eingaben bleiben erhalten.');
    this.name = 'EmbedActionError'; this.code = Object.hasOwn(messages, code) ? code : 'flowzer.connection_failed';
    // Dieser Kanal kommt ausschließlich vom authentifizierten Parent. Feldmeldungen
    // sind dessen expliziter sicherer API-Vertrag, niemals freie Remote-detail-Texte.
    this.fieldMessages = Object.create(null) as Record<string, string[]>;
    if (object(fields)) for (const [field, values] of Object.entries(fields).slice(0, 100)) {
      if (field.length <= 128 && /^[A-Za-z][A-Za-z0-9_]*$/.test(field) && Array.isArray(values))
        this.fieldMessages[field] = values.filter((value): value is string => typeof value === 'string' && value.length <= 256).slice(0, 8);
    }
  }
}

/** Requestkorrelation auf einem privaten MessagePort; keine globale window-Mutationsschnittstelle. */
export class EmbedActionChannel {
  private readonly pending = new Map<string, PendingCall>();
  private closed = false;
  private readonly port: MessagePort;
  private readonly sessionId: string;
  constructor(port: MessagePort, sessionId: string) {
    this.port = port; this.sessionId = sessionId;
    port.addEventListener('message', this.receive); port.start();
  }
  request<T>(operation: EmbedOperation, payload: unknown, signal?: AbortSignal): Promise<T> {
    if (signal?.aborted) return Promise.reject(new DOMException('Abgebrochen', 'AbortError'));
    if (this.closed) return Promise.reject(new Error('Die Formularverbindung wurde beendet.'));
    if (this.pending.size >= 16) return Promise.reject(new EmbedActionError('flowzer.connection_failed'));
    const id = crypto.randomUUID();
    return new Promise<T>((resolve, reject) => {
      const abort = () => { this.finish(id); reject(new DOMException('Abgebrochen', 'AbortError')); };
      const timer = setTimeout(() => { this.finish(id); reject(new EmbedActionError('flowzer.connection_failed')); }, 30_000);
      this.pending.set(id, { resolve: value => resolve(value as T), reject, timer, cleanup: () => signal?.removeEventListener('abort', abort) });
      signal?.addEventListener('abort', abort, { once: true });
      try { this.port.postMessage({ kind: 'request', sessionId: this.sessionId, id, operation, payload }); }
      catch { this.finish(id); reject(new EmbedActionError('flowzer.connection_failed')); }
    });
  }
  close(): void {
    if (this.closed) return;
    this.closed = true; this.port.removeEventListener('message', this.receive); this.port.close();
    for (const [id, pending] of this.pending) { this.finish(id); pending.reject(new Error('Die Formularverbindung wurde beendet.')); }
  }
  private finish(id: string): void {
    const pending = this.pending.get(id); if (!pending) return;
    clearTimeout(pending.timer); pending.cleanup(); this.pending.delete(id);
  }
  private receive = (event: MessageEvent): void => {
    const data: unknown = event.data;
    if (!object(data) || data.kind !== 'response' || data.sessionId !== this.sessionId || typeof data.id !== 'string') return;
    const pending = this.pending.get(data.id); if (!pending) return;
    this.finish(data.id);
    // Korrelationsdaten allein sind keine fachliche Erfolgsbestätigung. Ein
    // fehlendes Ergebnis oder malformer Fehler darf niemals Eingaben entfernen.
    if (data.error !== undefined && data.error !== null) {
      pending.reject(object(data.error) && typeof data.error.code === 'string'
        ? new EmbedActionError(data.error.code, data.error.fieldMessages)
        : new EmbedActionError('flowzer.connection_failed'));
    } else if (Object.hasOwn(data, 'result') && data.result !== undefined) pending.resolve(data.result);
    else pending.reject(new EmbedActionError('flowzer.connection_failed'));
  };
}

/** Die Nonce bindet einen Frame-Ladevorgang, ersetzt aber niemals die Host-Backendautorisierung. */
export function isHostHandshake(event: MessageEvent, parent: Window, origin: string, sessionId: string): boolean {
  const data: unknown = event.data;
  return event.source === parent && event.origin === origin && event.ports.length === 0
    && object(data) && data.kind === 'flowzer.embed.connect' && data.version === 1 && data.sessionId === sessionId;
}
