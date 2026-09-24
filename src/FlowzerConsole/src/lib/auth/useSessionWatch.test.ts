import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { BffSession } from '@/lib/auth/bff';
import { fetchSession, logout } from '@/lib/auth/bff';
import { useSession } from '@/stores/session';

import { SESSION_WATCH_INTERVAL_MS, SESSION_WATCH_MIN_GAP_MS, useSessionWatch } from './useSessionWatch';

const runtime = vi.hoisted(() => ({ apiBaseUrl: '/api', accent: 'iris', bffEnabled: true }));

vi.mock('@/lib/auth/bff', () => ({ fetchSession: vi.fn(), logout: vi.fn(), signIn: vi.fn() }));
vi.mock('@/lib/config/runtime', () => ({ getRuntimeConfig: () => runtime }));

const originalRefresh = useSession.getState().refresh;
let visibility: DocumentVisibilityState = 'visible';

function signIn(capabilities: string[] = ['access']) {
  useSession.setState({
    status: 'signed-in',
    user: { id: 'subject', name: 'Ada', initials: 'A', capabilities: new Set(capabilities) },
    sessionScope: 'scope',
    accessDenied: false,
    sessionError: null,
  });
}

function focusWindow() {
  act(() => {
    window.dispatchEvent(new Event('focus'));
  });
}

function setVisibility(state: DocumentVisibilityState) {
  visibility = state;
  act(() => {
    document.dispatchEvent(new Event('visibilitychange'));
  });
}

const ADA: BffSession = { id: 'subject', name: 'Ada', capabilities: ['access'] };

/** Lässt `fetchSession` hängen, bis der Test die Antwort freigibt. */
function holdSessionResponse() {
  let respond: (session: BffSession | null) => void = () => {};
  vi.mocked(fetchSession).mockImplementationOnce(
    () => new Promise<BffSession | null>((resolve) => { respond = resolve; }),
  );
  return (session: BffSession | null) => act(async () => { respond(session); });
}

/** Lässt Zeit vergehen und arbeitet dabei auch abgeschlossene Abfragen ab. */
async function elapse(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

describe('Sitzungsüberwachung der Konsole', () => {
  let refresh: ReturnType<typeof vi.fn<() => Promise<void>>>;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.resetAllMocks();
    runtime.bffEnabled = true;
    visibility = 'visible';
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => visibility });
    refresh = vi.fn<() => Promise<void>>().mockResolvedValue(undefined);
    signIn();
    useSession.setState({ refresh });
  });

  afterEach(() => {
    useSession.setState({ refresh: originalRefresh, status: 'anonymous', user: null, sessionScope: null });
    // Das eigene Property entfernen, damit wieder der jsdom-Getter am Prototyp gilt.
    delete (document as { visibilityState?: DocumentVisibilityState }).visibilityState;
    vi.useRealTimers();
  });

  // Testzweck: Solange das Fenster sichtbar ist, fragt die Konsole die Sitzung alle fünf
  // Minuten erneut ab — ein Rollenentzug soll ohne Reload ankommen.
  it('fragt die Sitzung im festen Abstand erneut ab', async () => {
    renderHook(() => useSessionWatch());

    await elapse(SESSION_WATCH_INTERVAL_MS - 1);
    expect(refresh).not.toHaveBeenCalled();

    await elapse(1);
    expect(refresh).toHaveBeenCalledTimes(1);

    await elapse(SESSION_WATCH_INTERVAL_MS);
    expect(refresh).toHaveBeenCalledTimes(2);
  });

  // Testzweck: Ein Hintergrund-Tab fragt nicht regelmäßig nach; beim Zurückkehren
  // (Sichtbarkeit oder Fensterfokus) prüft die Konsole die Sitzung dafür sofort.
  it('fragt bei Rückkehr ins Fenster ab, nicht im Hintergrund', async () => {
    renderHook(() => useSessionWatch());

    setVisibility('hidden');
    await elapse(2 * SESSION_WATCH_INTERVAL_MS);
    expect(refresh).not.toHaveBeenCalled();

    setVisibility('visible');
    expect(refresh).toHaveBeenCalledTimes(1);

    await elapse(SESSION_WATCH_MIN_GAP_MS);
    focusWindow();
    expect(refresh).toHaveBeenCalledTimes(2);
  });

  // Testzweck: Fokus und Sichtbarkeitswechsel kommen beim Zurückkehren meist gemeinsam;
  // innerhalb von 30 s darf daraus nur eine Abfrage werden — auch direkt nach dem Start.
  it('hält den Mindestabstand zwischen zwei Abfragen ein', async () => {
    renderHook(() => useSessionWatch());

    focusWindow();
    expect(refresh).not.toHaveBeenCalled();

    await elapse(SESSION_WATCH_MIN_GAP_MS);
    setVisibility('visible');
    focusWindow();
    expect(refresh).toHaveBeenCalledTimes(1);

    await elapse(SESSION_WATCH_MIN_GAP_MS - 1);
    focusWindow();
    expect(refresh).toHaveBeenCalledTimes(1);

    await elapse(1);
    focusWindow();
    expect(refresh).toHaveBeenCalledTimes(2);
  });

  // Testzweck: Hängt eine Abfrage, startet die Überwachung keine zweite daneben.
  it('startet keine parallele Abfrage', async () => {
    let finish: () => void = () => {};
    refresh.mockImplementation(() => new Promise<void>((resolve) => { finish = resolve; }));
    renderHook(() => useSessionWatch());

    await elapse(SESSION_WATCH_MIN_GAP_MS);
    focusWindow();
    await elapse(SESSION_WATCH_MIN_GAP_MS);
    focusWindow();
    expect(refresh).toHaveBeenCalledTimes(1);

    await act(async () => { finish(); });
    focusWindow();
    expect(refresh).toHaveBeenCalledTimes(2);
  });

  // Testzweck: Anonym gibt es nichts zu überwachen, und ohne BFF (lokaler
  // Entwicklungsmodus) gibt es keine serverseitige Sitzung, die sich ändern könnte.
  it.each([
    ['anonym', () => useSession.setState({ status: 'anonymous', user: null })],
    ['ohne BFF', () => { runtime.bffEnabled = false; }],
  ])('fragt %s nicht ab', async (_label, arrange) => {
    arrange();
    renderHook(() => useSessionWatch());

    await elapse(2 * SESSION_WATCH_INTERVAL_MS);
    setVisibility('visible');
    focusWindow();
    expect(refresh).not.toHaveBeenCalled();
  });

  // Testzweck: Nach dem Abbau der Anwendungshülle oder dem Ende der Sitzung laufen weder
  // Timer noch Listener weiter.
  it('baut Timer und Listener nach Unmount und Abmeldung ab', async () => {
    const first = renderHook(() => useSessionWatch());
    first.unmount();

    await elapse(2 * SESSION_WATCH_INTERVAL_MS);
    focusWindow();
    setVisibility('visible');
    expect(refresh).not.toHaveBeenCalled();

    renderHook(() => useSessionWatch());
    act(() => {
      useSession.setState({ status: 'anonymous', user: null });
    });
    await elapse(2 * SESSION_WATCH_INTERVAL_MS);
    focusWindow();
    expect(refresh).not.toHaveBeenCalled();
  });

  // Testzweck: Eine gescheiterte Abfrage darf keinen unbehandelten Fehler auslösen und
  // die Überwachung nicht blockieren; die nächste Gelegenheit fragt erneut.
  it('übersteht eine fehlgeschlagene Abfrage', async () => {
    refresh.mockRejectedValue(new Error('network unavailable'));
    renderHook(() => useSessionWatch());

    await elapse(SESSION_WATCH_MIN_GAP_MS);
    expect(() => focusWindow()).not.toThrow();
    await elapse(SESSION_WATCH_MIN_GAP_MS);
    focusWindow();

    expect(refresh).toHaveBeenCalledTimes(2);
  });

  // Testzweck: Ende-zu-Ende mit dem echten Store — entzieht der Identity Provider eine
  // Rolle, verschwindet die Fähigkeit nach der nächsten Abfrage ohne Reload.
  it('macht einen Rollenentzug ohne Reload sichtbar', async () => {
    useSession.setState({ refresh: originalRefresh });
    signIn(['access', 'modeler']);
    vi.mocked(fetchSession).mockResolvedValue({ id: 'subject', name: 'Ada', capabilities: ['access'] });
    renderHook(() => useSessionWatch());

    await elapse(SESSION_WATCH_INTERVAL_MS);

    expect(fetchSession).toHaveBeenCalledTimes(1);
    expect(useSession.getState().user?.capabilities.has('modeler')).toBe(false);
    expect(useSession.getState().user?.capabilities.has('access')).toBe(true);
    expect(useSession.getState().sessionScope).toBe('scope');
  });

  // Testzweck: Liefert der BFF 401, meldet der echte Store ab; danach fragt die
  // Überwachung nicht mehr nach, die Anmeldeseite übernimmt.
  it('stoppt nach einer 401-Antwort der Sitzungsabfrage', async () => {
    useSession.setState({ refresh: originalRefresh });
    vi.mocked(fetchSession).mockResolvedValue(null);
    renderHook(() => useSessionWatch());

    await elapse(SESSION_WATCH_INTERVAL_MS);
    expect(useSession.getState().status).toBe('anonymous');

    await elapse(2 * SESSION_WATCH_INTERVAL_MS);
    focusWindow();
    setVisibility('visible');
    expect(fetchSession).toHaveBeenCalledTimes(1);
  });

  // Testzweck: Ein Netz- oder Serverfehler ist kein Sitzungsende. Der echte Store bleibt
  // angemeldet und zeigt den Verbindungshinweis; die nächste erfolgreiche Abfrage räumt ihn.
  it('behält die Sitzung bei einem Verbindungsfehler und versucht es erneut', async () => {
    useSession.setState({ refresh: originalRefresh });
    vi.mocked(fetchSession)
      .mockRejectedValueOnce(new Error('503 Service Unavailable'))
      .mockResolvedValue(ADA);
    renderHook(() => useSessionWatch());

    await elapse(SESSION_WATCH_INTERVAL_MS);
    expect(useSession.getState().status).toBe('signed-in');
    expect(useSession.getState().sessionError).toBeTruthy();

    await elapse(SESSION_WATCH_INTERVAL_MS);
    expect(fetchSession).toHaveBeenCalledTimes(2);
    expect(useSession.getState().status).toBe('signed-in');
    expect(useSession.getState().sessionError).toBeNull();
  });

  // Testzweck: Kommt das 200 einer vor dem Sitzungsende gestellten Abfrage erst nach dem
  // Abmelden oder nach einem 401 an, darf es die Person nicht wieder anmelden.
  it.each([
    ['nach dem Abmelden', async () => {
      // Seit dem Provider-Logout liefert logout() ein optionales Ziel; hier: kein Ziel.
      vi.mocked(logout).mockResolvedValue(undefined);
      await act(async () => { await useSession.getState().signOut(); });
    }],
    ['nach Unmount und 401', async (view: { unmount: () => void }) => {
      view.unmount();
      act(() => { useSession.getState().endSessionForUnauthorized(); });
    }],
  ])('verwirft eine späte Antwort %s', async (_label, endSession) => {
    useSession.setState({ refresh: originalRefresh });
    const respond = holdSessionResponse();
    const view = renderHook(() => useSessionWatch());

    await elapse(SESSION_WATCH_INTERVAL_MS);
    expect(fetchSession).toHaveBeenCalledTimes(1);

    await endSession(view);
    expect(useSession.getState().status).toBe('anonymous');

    await respond(ADA);
    expect(useSession.getState().status).toBe('anonymous');
    expect(useSession.getState().user).toBeNull();
    expect(useSession.getState().sessionScope).toBeNull();
  });

  // Testzweck: Ein bloßes Aushängen der Anwendungshülle während einer Abfrage beendet
  // keine Sitzung; deren Antwort darf niemanden abmelden.
  it('meldet nach bloßem Unmount niemanden ab', async () => {
    useSession.setState({ refresh: originalRefresh });
    const respond = holdSessionResponse();
    const view = renderHook(() => useSessionWatch());

    await elapse(SESSION_WATCH_INTERVAL_MS);
    view.unmount();
    await respond(ADA);

    expect(useSession.getState().status).toBe('signed-in');
    expect(useSession.getState().user?.id).toBe('subject');
  });
});
