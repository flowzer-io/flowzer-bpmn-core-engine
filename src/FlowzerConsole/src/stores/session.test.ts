import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { fetchSession, logout } from '@/lib/auth/bff';
import { registerPublicPackageScopeCleanup } from '@/lib/flowzer/sessionScope';
import { useSession } from './session';

vi.mock('@/lib/auth/bff', () => ({ fetchSession: vi.fn(), logout: vi.fn(), signIn: vi.fn() }));
vi.mock('@/lib/config/runtime', () => ({ getRuntimeConfig: () => ({ bffEnabled: true }) }));

describe('BFF-Sitzungsverwaltung', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    useSession.setState({ status: 'anonymous', user: null, sessionScope: null, accessDenied: false });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** jsdom navigiert nicht; der Test ersetzt deshalb nur die benötigten Teile von location. */
  function stubLocation(protocol: 'https:' | 'http:') {
    const assign = vi.fn();
    vi.stubGlobal('location', { protocol, origin: `${protocol}//flowzer.example`, assign });
    return assign;
  }

  async function signedIn() {
    vi.mocked(fetchSession).mockResolvedValue({ id: 'subject', name: 'Ada', capabilities: ['access'] });
    await useSession.getState().refresh();
  }

  // Testzweck: Ein kurzfristiger BFF-/Netzwerkausfall ist kein bestätigter Logout.
  // Bereits erfasste Formulare und der Sitzungsscope bleiben beim Wiederholen erhalten.
  it('behält die Sitzung bei vorübergehend fehlgeschlagener Prüfung', async () => {
    vi.mocked(fetchSession).mockResolvedValue({ id: 'subject', name: 'Ada', capabilities: ['access'] });
    await useSession.getState().refresh();
    const scope = useSession.getState().sessionScope;
    vi.mocked(fetchSession).mockRejectedValue(new Error('unavailable'));
    await useSession.getState().refresh();
    expect(useSession.getState().status).toBe('signed-in');
    expect(useSession.getState().sessionScope).toBe(scope);
    expect(useSession.getState().sessionError).toBeTruthy();
  });

  // Testzweck: Fehlende Freischaltung darf keine Login-Schleife ausloesen; das Konto bleibt
  // fuer die Anzeige und Abmeldung angemeldet, aber ohne Fachfaehigkeiten.
  it('unterscheidet Anmeldung von Freischaltung', async () => {
    vi.mocked(fetchSession).mockResolvedValue({ id: 'subject', name: 'Ada', capabilities: [] });
    await useSession.getState().refresh();
    expect(useSession.getState().status).toBe('signed-in');
    expect(useSession.getState().accessDenied).toBe(true);
    expect(useSession.getState().user?.capabilities.size).toBe(0);
  });

  // Testzweck: Ein fehlgeschlagener Logout darf die weiterhin gueltige Server-Sitzung nicht
  // lokal als beendet darstellen; die Person muss es erneut versuchen koennen.
  it('behaelt die Sitzung bei fehlgeschlagener Abmeldung', async () => {
    vi.mocked(fetchSession).mockResolvedValue({ id: 'subject', name: 'Ada', capabilities: ['access'] });
    await useSession.getState().refresh();
    vi.mocked(logout).mockRejectedValue(new Error('network unavailable'));
    await expect(useSession.getState().signOut()).rejects.toThrow('network unavailable');
    expect(useSession.getState().status).toBe('signed-in');
    expect(useSession.getState().user?.id).toBe('subject');
  });

  // Testzweck: Der öffentliche Paketcache erhält für jede erfolgreiche BFF-Sitzung
  // einen zufälligen Scope und wird beim bestätigten Logout gezielt wieder entfernt.
  it('trennt und bereinigt den öffentlichen Paketcache beim Logout', async () => {
    const cleanup = vi.fn();
    const unregister = registerPublicPackageScopeCleanup(cleanup);
    vi.spyOn(crypto, 'randomUUID').mockReturnValue('00000000-0000-4000-8000-000000000001');
    vi.mocked(fetchSession).mockResolvedValue({ id: 'subject', name: 'Ada', capabilities: ['access'] });
    vi.mocked(logout).mockResolvedValue(undefined);

    await useSession.getState().refresh();
    expect(useSession.getState().sessionScope).toBe('00000000-0000-4000-8000-000000000001');

    await useSession.getState().signOut();
    expect(cleanup).toHaveBeenCalledWith('00000000-0000-4000-8000-000000000001');
    expect(useSession.getState().sessionScope).toBeNull();
    unregister();
  });

  // Testzweck: Eine 401-Antwort beendet die Browser-Sitzung ohne Benutzerkennung im
  // Query-Key und löscht deshalb sofort ausschließlich deren früheren Paket-Scope.
  it('bereinigt den öffentlichen Paketcache bei einer 401-Antwort', async () => {
    const cleanup = vi.fn();
    const unregister = registerPublicPackageScopeCleanup(cleanup);
    useSession.setState({
      status: 'signed-in',
      user: { id: 'subject', name: 'Ada', initials: 'A', capabilities: new Set(['access']) },
      sessionScope: 'session-old',
    });

    // Der tatsächliche Client-Handler ruft dieselbe, Console-interne Transition auf.
    useSession.getState().endSessionForUnauthorized();

    expect(cleanup).toHaveBeenCalledWith('session-old');
    expect(useSession.getState().sessionScope).toBeNull();
    unregister();
  });

  // Testzweck: Eine stille Token-Erneuerung für dieselbe Subject-ID darf den
  // öffentlichen Query-Scope nicht wechseln und damit unnötig Taskdaten neu laden.
  it('behält den öffentlichen Paketcache bei derselben Subject-ID', async () => {
    vi.mocked(fetchSession).mockResolvedValue({ id: 'subject', name: 'Ada', capabilities: ['access'] });
    useSession.setState({
      status: 'signed-in',
      user: { id: 'subject', name: 'Ada', initials: 'A', capabilities: new Set(['access']) },
      sessionScope: 'session-stable',
      accessDenied: false,
    });

    await useSession.getState().refresh();

    expect(useSession.getState().sessionScope).toBe('session-stable');
  });

  // Testzweck: Wechselt die authentifizierte Subject-ID ohne vorherigen Logout,
  // müssen der alte Paketcache entfernt und ein neuer opaker Scope erzeugt werden.
  it('rotiert und bereinigt den Scope bei einem Kontowechsel', async () => {
    const cleanup = vi.fn();
    const unregister = registerPublicPackageScopeCleanup(cleanup);
    vi.spyOn(crypto, 'randomUUID').mockReturnValue('00000000-0000-4000-8000-000000000002');
    vi.mocked(fetchSession).mockResolvedValue({ id: 'subject-new', name: 'Grace', capabilities: ['access'] });
    useSession.setState({
      status: 'signed-in',
      user: { id: 'subject-old', name: 'Ada', initials: 'A', capabilities: new Set(['access']) },
      sessionScope: 'session-old',
      accessDenied: false,
    });

    await useSession.getState().refresh();

    expect(cleanup).toHaveBeenCalledWith('session-old');
    expect(useSession.getState().sessionScope).toBe('00000000-0000-4000-8000-000000000002');
    expect(useSession.getState().sessionScope).not.toContain('subject-new');
    unregister();
  });
  // Testzweck: Mit Provider-Logout endet zuerst die lokale Sitzung, danach navigiert die
  // Konsole zum Identity Provider, der auf die Konsole zurückleitet.
  it('navigiert nach der Abmeldung zum Provider-Logout', async () => {
    const assign = stubLocation('https:');
    const redirectTo =
      'https://idp.example/realms/r/protocol/openid-connect/logout?post_logout_redirect_uri=https%3A%2F%2Fflowzer.example%2F&client_id=c';
    await signedIn();
    vi.mocked(logout).mockImplementation(async () => {
      expect(assign).not.toHaveBeenCalled();
      return redirectTo;
    });

    await useSession.getState().signOut();

    expect(useSession.getState().status).toBe('anonymous');
    expect(assign).toHaveBeenCalledExactlyOnceWith(redirectTo);
  });

  // Testzweck: Ohne Abmeldeadresse (204) bleibt es bei der lokalen Abmeldung ohne Navigation.
  it('meldet ohne Abmeldeadresse nur lokal ab', async () => {
    const assign = stubLocation('https:');
    await signedIn();
    vi.mocked(logout).mockResolvedValue(undefined);

    await useSession.getState().signOut();

    expect(useSession.getState().status).toBe('anonymous');
    expect(assign).not.toHaveBeenCalled();
  });

  // Testzweck: Unerwartete Ziele (relativ, fremdes Schema, HTTP unter HTTPS) werden ignoriert;
  // die lokale Abmeldung gilt trotzdem.
  it.each([
    '/bff/login',
    'javascript:alert(1)',
    'data:text/html,logout',
    'http://idp.example/logout',
    'kein-url',
  ])('ignoriert das unsichere Abmeldeziel %s', async (redirectTo) => {
    const assign = stubLocation('https:');
    await signedIn();
    vi.mocked(logout).mockResolvedValue(redirectTo);

    await useSession.getState().signOut();

    expect(useSession.getState().status).toBe('anonymous');
    expect(assign).not.toHaveBeenCalled();
  });

  // Testzweck: Läuft die Konsole selbst über HTTP (lokale Entwicklung), ist ein HTTP-Ziel des
  // Providers zulässig.
  it('erlaubt ein HTTP-Abmeldeziel nur bei einer HTTP-Konsole', async () => {
    const assign = stubLocation('http:');
    await signedIn();
    vi.mocked(logout).mockResolvedValue('http://localhost:8080/realms/r/protocol/openid-connect/logout');

    await useSession.getState().signOut();

    expect(assign).toHaveBeenCalledExactlyOnceWith('http://localhost:8080/realms/r/protocol/openid-connect/logout');
  });
});
