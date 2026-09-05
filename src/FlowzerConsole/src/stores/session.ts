import { create } from 'zustand';

import { setAccessDeniedHandler, setAuthTokenProvider, setUserIdProvider } from '@/lib/api/client';
import { decodeJwtPayload, FLOWZER_ROLES, readRoles } from '@/lib/auth/roles';
import { getUser, getUserManager, signIn, signOut } from '@/lib/auth/oidc';
import { getRuntimeConfig, isAuthenticationConfigured } from '@/lib/config/runtime';

export interface SessionUser {
  id: string;
  name: string;
  email?: string;
  initials: string;
  roles: Set<string>;
}

type SessionStatus = 'unknown' | 'anonymous' | 'signed-in';

interface SessionState {
  status: SessionStatus;
  user: SessionUser | null;
  /** Die API hat einen Aufruf mit 403 abgelehnt, weil die Zugangsrolle fehlt. */
  accessDenied: boolean;
  refresh: () => Promise<void>;
  signIn: () => Promise<void>;
  signOut: () => Promise<void>;
  setAccessDenied: (denied: boolean) => void;
}

function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');
}

/**
 * Ohne Identity Provider läuft die Konsole als technischer Benutzer. Die API
 * akzeptiert das ausschließlich im Entwicklungsmodus, und nur dort greift dieser
 * Zweig: In einem Produktionsbündel bliebe die Anwendung sonst mit voller Ansicht
 * stehen, während die API jeden Aufruf ablehnt.
 */
const DEVELOPMENT_USER: SessionUser = {
  id: 'd266f2b6-e96e-4d4a-9c20-c8e541394df0',
  name: 'Entwicklungsbenutzer',
  initials: 'EB',
  roles: new Set(Object.values(FLOWZER_ROLES)),
};

export const useSession = create<SessionState>()((set, get) => ({
  status: 'unknown',
  user: null,
  accessDenied: false,

  refresh: async () => {
    if (!isAuthenticationConfigured()) {
      if (!import.meta.env.DEV) {
        // Fail-closed: lieber die Anmeldeseite als eine Oberfläche, die nichts kann.
        set({ status: 'anonymous', user: null });
        return;
      }

      set({ status: 'signed-in', user: DEVELOPMENT_USER });
      return;
    }

    const user = await getUser();
    if (!user || user.expired) {
      set({ status: 'anonymous', user: null });
      return;
    }

    const claims = decodeJwtPayload(user.access_token);
    const profile = user.profile;
    const name = (profile.name ?? profile.preferred_username ?? profile.email ?? 'Unbekannt') as string;

    set({
      status: 'signed-in',
      user: {
        id: (profile.sub ?? '') as string,
        name,
        email: profile.email,
        initials: initials(name),
        roles: readRoles(claims, getRuntimeConfig().oidcAudience),
      },
    });
  },

  signIn: () => signIn(),
  signOut: () => signOut(),
  setAccessDenied: (denied) => {
    if (get().accessDenied !== denied) set({ accessDenied: denied });
  },
}));

/**
 * Kurzer Zusatz unter dem Namen: die stärkste Rolle, die die Person trägt.
 * Mehr gehört nicht in die Kopfzeile; die vollständige Liste steht im Menü.
 */
export function describeRole(user: SessionUser | null): string {
  if (!user) return '';
  if (user.roles.has(FLOWZER_ROLES.operator)) return 'Betrieb';
  if (user.roles.has(FLOWZER_ROLES.modeler)) return 'Modellieren';
  if (user.roles.has(FLOWZER_ROLES.access)) return 'Aufgaben';
  return 'Kein Zugang';
}

/** Prüft eine Rolle für die Anzeige. Die Entscheidung trifft weiterhin die API. */
export function useHasRole(role: string): boolean {
  return useSession((state) => state.user?.roles.has(role) ?? false);
}

export function hasRole(role: string): boolean {
  return useSession.getState().user?.roles.has(role) ?? false;
}

// Der API-Client holt Token und Benutzer-Id bei jedem Aufruf frisch.
setAuthTokenProvider(() => {
  const manager = getUserManager();
  if (!manager) return null;
  return currentAccessToken;
});
setUserIdProvider(() => (!isAuthenticationConfigured() && import.meta.env.DEV ? DEVELOPMENT_USER.id : null));
setAccessDeniedHandler((denied) => useSession.getState().setAccessDenied(denied));

/**
 * Das Token wird beim Anmelden und bei jeder Erneuerung gespiegelt: Der API-Client
 * ist synchron, `getUser()` nicht.
 */
let currentAccessToken: string | null = null;

export function initialiseSession(): void {
  const manager = getUserManager();
  if (!manager) {
    void useSession.getState().refresh();
    return;
  }

  manager.events.addUserLoaded((user) => {
    currentAccessToken = user.access_token;
    void useSession.getState().refresh();
  });
  manager.events.addUserUnloaded(() => {
    currentAccessToken = null;
    void useSession.getState().refresh();
  });
  manager.events.addAccessTokenExpired(() => {
    currentAccessToken = null;
    void useSession.getState().refresh();
  });

  void manager.getUser().then((user) => {
    currentAccessToken = user && !user.expired ? user.access_token : null;
    return useSession.getState().refresh();
  });
}
