import { afterEach, describe, expect, it, vi } from 'vitest';
import { EmbedActionChannel, EmbedActionError, isHostHandshake } from './EmbedChannel';
class Port {
  messages: Record<string, unknown>[] = [];
  listeners = new Set<(event: MessageEvent) => void>();
  start = vi.fn(); close = vi.fn();
  postMessage(message: Record<string, unknown>) { this.messages.push(message); }
  addEventListener(_type: string, listener: (event: MessageEvent) => void) { this.listeners.add(listener); }
  removeEventListener(_type: string, listener: (event: MessageEvent) => void) { this.listeners.delete(listener); }
  respond(body: Record<string, unknown>) { this.listeners.forEach(callback => callback(new MessageEvent('message', { data: body }))); }
}
afterEach(() => vi.useRealTimers());
describe('Opaque Formular-Hostkanal', () => {
  // Testzweck: Prototyp-Namen sind unbekannte Codes und liefern keine fremden
  // Funktionen oder technischen Texte als sichtbare Fehlermeldungen.
  it('normalisiert geerbte Fehlercode-Namen vor dem Lookup', () => {
    for (const code of ['constructor', 'toString', '__proto__']) {
      const error = new EmbedActionError(code);
      expect(error.code).toBe('flowzer.connection_failed');
      expect(error.message).toContain('derzeit nicht verfügbar');
    }
  });
  // Testzweck: Sichere wiederholte Feldpfade dürfen nicht verloren gehen; nur
  // einfache Kennungen bzw. deklarierte Zeilen-/Kindpfade gelangen zur Fehleranzeige.
  it('erhält sichere Zeilenfeldmeldungen und verwirft freie Pfade', () => {
    const error = new EmbedActionError('flowzer.validation_failed', {
      answer: ['Bitte ausfüllen.'], 'requests[0].answer': ['Diese Zeile benötigt eine Antwort.'],
      'requests[1]': ['Diese Zeile ist ungültig.'], 'requests[0].answer.secret': ['nicht zulässig'],
      'requests[-1].answer': ['nicht zulässig'], 'requests[no].answer': ['nicht zulässig'],
      '../answer': ['nicht zulässig'], 'requests[0]\\answer': ['nicht zulässig'],
    });
    expect(Object.keys(error.fieldMessages)).toEqual(['answer', 'requests[0].answer', 'requests[1]']);
    expect(error.fieldMessages['requests[0].answer']).toEqual(['Diese Zeile benötigt eine Antwort.']);
  });
  // Testzweck: Ein korrelierter, aber unvollständiger oder malformer Envelope
  // darf niemals als erfolgreicher Save oder Abschluss gelten.
  it('weist Antworten ohne bestätigten Ergebnisvertrag zurück', async () => {
    for (const response of [{}, { error: 'failed' }, { error: {} }, { result: undefined }]) {
      const port = new Port(); const channel = new EmbedActionChannel(port as unknown as MessagePort, 'session');
      const result = channel.request('task.complete', {}); const sent = port.messages[0]!;
      port.respond({ kind: 'response', id: sent.id, sessionId: 'session', ...response });
      await expect(result).rejects.toBeInstanceOf(EmbedActionError); channel.close();
    }
  });

  // Testzweck: Nur der tatsächliche Parent mit exakter freigegebener Origin und passender Instanznonce darf verbinden.
  it('bindet Parent, Origin und Sitzung ohne freie Ports oder Tokens', () => {
    const parent = {} as Window;
    const event = { source: parent, origin: 'https://host.example.test', data: { kind: 'flowzer.embed.connect', version: 1, sessionId: 'session' }, ports: [] } as unknown as MessageEvent;
    expect(isHostHandshake(event, parent, event.origin, 'session')).toBe(true);
    for (const change of [{ origin: 'https://other.test' }, { source: {} }, { data: { ...event.data, sessionId: 'other' } }, { ports: [{}] }])
      expect(isHostHandshake({ ...event, ...change } as MessageEvent, parent, event.origin, 'session')).toBe(false);
  });
  // Testzweck: Antworten anderer RPCs oder Sitzungen dürfen keine Aktion bestätigen.
  it('vermittelt korrelierte Aktionen ausschließlich auf dem übertragenen Port', async () => {
    const port = new Port(); const channel = new EmbedActionChannel(port as unknown as MessagePort, 'session');
    const result = channel.request('draft.save', { data: { answer: '' }, expectedRevision: 1 });
    const sent = port.messages[0]!;
    expect(sent.operation).toBe('draft.save');
    port.respond({ kind: 'response', id: sent.id, sessionId: 'other', result: { revision: 2 } });
    port.respond({ kind: 'response', id: sent.id, sessionId: 'session', result: { revision: 2 } });
    await expect(result).resolves.toEqual({ revision: 2 }); channel.close();
  });
  // Testzweck: Eine tatsächliche Benutzerabmeldung/Formularschließung beendet offene Aktionen, nicht ein Zugangstoken-Timer.
  it('beendet offene Aufrufe beim expliziten Schließen', async () => {
    const channel = new EmbedActionChannel(new Port() as unknown as MessagePort, 'session');
    const result = channel.request('draft.save', {}); channel.close();
    await expect(result).rejects.toThrow('beendet');
  });
  // Testzweck: Nach 46 Minuten ohne laufende Aktion bleibt derselbe Kanal nutzbar; kein Bearbeitungsdeadline.
  it('bleibt über die Einlösefrist hinaus nutzbar', async () => {
    vi.useFakeTimers(); const port = new Port(); const channel = new EmbedActionChannel(port as unknown as MessagePort, 'session');
    await vi.advanceTimersByTimeAsync(46 * 60 * 1000);
    const result = channel.request('draft.save', {}); const sent = port.messages[0]!;
    port.respond({ kind: 'response', id: sent.id, sessionId: 'session', result: { revision: 1 } });
    await expect(result).resolves.toEqual({ revision: 1 }); channel.close();
  });
  // Testzweck: Ein einzelner Transporttimeout ist ein Speicherfehler, keine Neuladung oder Aufhebung der Formularinstanz.
  it('begrenzt einen ausbleibenden Aufruf ohne den Kanal zu zerstören', async () => {
    vi.useFakeTimers(); const port = new Port(); const channel = new EmbedActionChannel(port as unknown as MessagePort, 'session');
    const failed = channel.request('draft.save', {}); const assertion = expect(failed).rejects.toThrow(/verbindung/i);
    await vi.advanceTimersByTimeAsync(30_000); await assertion;
    const later = channel.request('draft.save', {}); const sent = port.messages.at(-1)!;
    port.respond({ kind: 'response', id: sent.id, sessionId: 'session', result: {} });
    await expect(later).resolves.toEqual({}); channel.close();
  });
});
