import { createRoot } from 'react-dom/client';
import { Formio } from '@formio/js';
import { InlineSpinner } from '@/components/ui/States';
import { initializeOpaqueFormioRuntime, type OpaqueFormioRuntime } from '@/components/forms/opaqueFormioRuntime';
import { EmbeddedTaskForm, type EmbedSnapshot } from './EmbeddedTaskForm';
import { EmbedActionChannel, isHostHandshake } from './EmbedChannel';
import '@/styles/app.css';
import './embed.css';

function object(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function validSnapshot(value: unknown): value is EmbedSnapshot {
  if (!object(value) || typeof value.userTaskId !== 'string' || typeof value.hostOrigin !== 'string'
      || !/^https:\/\/[A-Za-z0-9.-]+(?::\d{1,5})?$/.test(value.hostOrigin)
      || !Number.isSafeInteger(value.taskRevision) || Number(value.taskRevision) < 0
      || !object(value.form) || typeof value.form.formData !== 'string'
      || !object(value.context) || !object(value.draft) || value.draft.userTaskId !== value.userTaskId
      || !Number.isSafeInteger(value.draft.revision) || Number(value.draft.revision) < 0 || !object(value.draft.data)) return false;
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.userTaskId);
}

/** Ein einziger anonymer Read-only-Abruf. Keine Cookies, Authheader, Retry oder freie Ziel-URL. */
async function redeem(): Promise<EmbedSnapshot> {
  let secret = location.hash.slice(1);
  if (!/^[A-Za-z0-9_-]{43}$/.test(secret)) throw new Error('link');
  // Fragmentnavigation bleibt im selben Dokument und nimmt den Secret-Einstieg
  // sofort aus der sichtbaren URL/History, bevor irgendwelche Eingaben entstehen.
  location.replace('#');
  const request = fetch(new URL('/form-embed/redeem', location.href), {
    method: 'POST', credentials: 'omit', redirect: 'error', cache: 'no-store', referrerPolicy: 'no-referrer',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ secret }), signal: AbortSignal.timeout(15_000),
  });
  secret = '';
  const response = await request;
  if (!response.ok || !response.body) throw new Error('redeem');
  const reader = response.body.getReader(); const decoder = new TextDecoder(); let text = ''; let bytes = 0;
  try {
    while (true) {
      const part = await reader.read(); if (part.done) break;
      bytes += part.value.byteLength; if (bytes > 3 * 1024 * 1024) throw new Error('budget');
      text += decoder.decode(part.value, { stream: true });
    }
    text += decoder.decode();
  } finally { await reader.cancel(); reader.releaseLock(); }
  const body: unknown = JSON.parse(text);
  if (!object(body) || body.successful !== true || !validSnapshot(body.result)) throw new Error('snapshot');
  return body.result;
}

/** Nur der erwartete Parent darf genau einen frischen privaten Port erhalten. */
function connect(snapshot: EmbedSnapshot): Promise<EmbedActionChannel> {
  const sessionId = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    const cleanup = () => { clearTimeout(timer); window.removeEventListener('message', receive); };
    const receive = (event: MessageEvent) => {
      if (!isHostHandshake(event, parent, snapshot.hostOrigin, sessionId)) return;
      cleanup(); const pair = new MessageChannel();
      const channel = new EmbedActionChannel(pair.port1, sessionId);
      try { parent.postMessage({ kind: 'flowzer.embed.connected', version: 1, sessionId }, snapshot.hostOrigin, [pair.port2]); resolve(channel); }
      catch { channel.close(); reject(new Error('host')); }
    };
    const timer = setTimeout(() => { cleanup(); reject(new Error('host')); }, 15_000);
    window.addEventListener('message', receive);
    parent.postMessage({ kind: 'flowzer.embed.ready', version: 1, sessionId }, snapshot.hostOrigin);
  });
}

/** Separater Produktions-Einstieg: kein AppRouter, BFF, Login, Session-Store oder TT-Feldrenderer. */
async function start() {
  const root = createRoot(document.getElementById('root')!);
  try {
    if (parent === window) throw new Error('parent');
    initializeOpaqueFormioRuntime(Formio as unknown as OpaqueFormioRuntime);
    root.render(<InlineSpinner label="Formular wird geladen …" />);
    const snapshot = await redeem(); const channel = await connect(snapshot);
    window.addEventListener('pagehide', () => channel.close(), { once: true });
    root.render(<EmbeddedTaskForm snapshot={snapshot} channel={channel} />);
  } catch {
    // Keine Secret-/Response-/Token-/URL-Inhalte im sichtbaren Fehler oder Log.
    root.render(<p role="alert">Das Formular konnte nicht sicher geöffnet werden. Bitte öffne es erneut über TickyTask.</p>);
  }
}
void start();
