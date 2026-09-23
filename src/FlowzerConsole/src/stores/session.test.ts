import { beforeEach, describe, expect, it, vi } from 'vitest';

const auth = vi.hoisted(() => ({ user: null as unknown }));

vi.mock('@/lib/auth/oidc', () => ({
  getUser: vi.fn(() => Promise.resolve(auth.user)),
  getUserManager: vi.fn(() => null),
  signIn: vi.fn(() => Promise.resolve()),
  signOut: vi.fn(() => Promise.resolve()),
}));
vi.mock('@/lib/config/runtime', () => ({
  getRuntimeConfig: () => ({ oidcAudience: 'flowzer', roleNames: { access: 'access', modeler: 'modeler', operator: 'operator', worker: 'worker' } }),
  isAuthenticationConfigured: () => true,
}));
vi.mock('@/lib/auth/roles', () => ({
  decodeJwtPayload: () => ({}),
  readRoles: () => new Set<string>(),
}));

describe('Sitzungs-Scope', () => {
  beforeEach(() => {
    auth.user = null;
    vi.resetModules();
  });

  it('behält den opaken Scope bei einer Token-Erneuerung derselben Subject-ID', async () => {
    // Testzweck: Ein Refresh für dasselbe Konto darf keine öffentlichen Query-Caches verlieren.
    auth.user = { expired: false, access_token: 'token-a', profile: { sub: 'subject-a', name: 'Person A' } };
    const { useSession } = await import('./session');

    await useSession.getState().refresh();
    const firstScope = useSession.getState().publicScope;
    auth.user = { expired: false, access_token: 'token-b', profile: { sub: 'subject-a', name: 'Person A' } };
    await useSession.getState().refresh();

    expect(useSession.getState().publicScope).toBe(firstScope);
  });

  it('rotiert den Scope bei einem Wechsel der stabilen Subject-ID', async () => {
    // Testzweck: Ein Kontowechsel darf keine Cache-Daten der vorherigen Person wiederverwenden.
    auth.user = { expired: false, access_token: 'token-a', profile: { sub: 'subject-a', name: 'Person A' } };
    const { useSession } = await import('./session');
    await useSession.getState().refresh();
    const firstScope = useSession.getState().publicScope;

    auth.user = { expired: false, access_token: 'token-b', profile: { sub: 'subject-b', name: 'Person B' } };
    await useSession.getState().refresh();
    const secondScope = useSession.getState().publicScope;

    expect(secondScope).not.toBe(firstScope);
    expect(secondScope).not.toContain('subject-b');
    expect(secondScope).not.toContain('Person B');
  });
});
