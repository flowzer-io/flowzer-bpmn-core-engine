import { beforeEach, describe, expect, it, vi } from 'vitest';

import { fetchSession, logout } from '@/lib/auth/bff';
import { useSession } from './session';

vi.mock('@/lib/auth/bff', () => ({ fetchSession: vi.fn(), logout: vi.fn(), signIn: vi.fn() }));
vi.mock('@/lib/config/runtime', () => ({ getRuntimeConfig: () => ({ bffEnabled: true }) }));

describe('BFF-Sitzungsverwaltung', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    useSession.setState({ status: 'anonymous', user: null, accessDenied: false });
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
});
