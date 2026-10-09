import { createRoot } from 'react-dom/client';
import { Formio } from '@formio/js';
import { InlineSpinner } from '@/components/ui/States';
import { initializeOpaqueFormioRuntime, type OpaqueFormioRuntime } from '@/components/forms/opaqueFormioRuntime';
import { EmbeddedTaskForm, type EmbedSnapshot } from './EmbeddedTaskForm';
import { EmbeddedStartForm, type EmbedStartSnapshot } from './EmbeddedStartForm';
import { parseEmbedEntry, validStartSnapshot, validTaskSnapshot } from './EmbedSnapshot';
import { EmbedActionChannel, isHostHandshake } from './EmbedChannel';
import '@/styles/app.css';
import './embed.css';

function object(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value); }
/** Ein einziger anonymer Read-only-Abruf. Keine Cookies, Authheader, Retry oder freie Ziel-URL. */
async function redeem(): Promise<{ purpose: 'task'; snapshot: EmbedSnapshot } | { purpose: 'start'; snapshot: EmbedStartSnapshot }> {
  let entry = parseEmbedEntry(location.hash.slice(1));
  const purpose = entry.purpose;
  // Fragmentnavigation bleibt im selben Dokument und nimmt den Secret-Einstieg
  // sofort aus der sichtbaren URL/History, bevor irgendwelche Eingaben entstehen.
  location.replace('#');
  const request = fetch(new URL(purpose === 'start' ? '/form-embed/start/redeem' : '/form-embed/redeem', location.href), {
    method: 'POST', credentials: 'omit', redirect: 'error', cache: 'no-store', referrerPolicy: 'no-referrer',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ secret: entry.secret }), signal: AbortSignal.timeout(15_000),
  });
  entry = { purpose, secret: '' };
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
  if (!object(body) || body.successful !== true) throw new Error('snapshot');
  if (purpose === 'start' && validStartSnapshot(body.result)) return { purpose, snapshot: body.result };
  if (purpose === 'task' && validTaskSnapshot(body.result)) return { purpose, snapshot: body.result };
  throw new Error('snapshot');
}

/** Nur der erwartete Parent darf genau einen frischen privaten Port erhalten. */
function connect(snapshot: { hostOrigin: string }): Promise<EmbedActionChannel> {
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
    const entry = await redeem(); const channel = await connect(entry.snapshot);
    window.addEventListener('pagehide', () => channel.close(), { once: true });
    root.render(entry.purpose === 'start' ? <EmbeddedStartForm snapshot={entry.snapshot} channel={channel} />
      : <EmbeddedTaskForm snapshot={entry.snapshot} channel={channel} />);
  } catch {
    // Keine Secret-/Response-/Token-/URL-Inhalte im sichtbaren Fehler oder Log.
    root.render(<p role="alert">Das Formular konnte nicht sicher geöffnet werden. Bitte öffne es erneut über die Hostanwendung.</p>);
  }
}
void start();
