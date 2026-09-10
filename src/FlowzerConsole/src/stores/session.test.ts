import { beforeEach, describe, expect, it, vi } from 'vitest';

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
    vi.mocked(logout).mockResolvedValue();

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
});
