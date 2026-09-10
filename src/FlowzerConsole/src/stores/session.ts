import { create } from 'zustand';

import { setAccessDeniedHandler, setUnauthorizedHandler } from '@/lib/api/client';
import { fetchSession, logout, signIn } from '@/lib/auth/bff';
import type { FlowzerCapability } from '@/lib/auth/roles';
import { getRuntimeConfig } from '@/lib/config/runtime';

export interface SessionUser {
  id: string;
  name: string;
  email?: string;
  initials: string;
  capabilities: Set<string>;
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
 * Ausschliesslich der Vite-Entwicklungsserver darf ohne BFF eine offene Konsole zeigen.
 * Dabei wird kein Token versendet. Der feste technische Identitaetsheader ist auf Vites
 * Development-Build begrenzt und wird von der API ebenfalls nur in Development akzeptiert.
 */
const DEVELOPMENT_USER: SessionUser = {
  id: 'd266f2b6-e96e-4d4a-9c20-c8e541394df0',
  name: 'Entwicklungsbenutzer',
  initials: 'EB',
  capabilities: new Set(['access', 'modeler', 'operator', 'worker']),
};

export const useSession = create<SessionState>()((set, get) => ({
  status: 'unknown',
  user: null,
  accessDenied: false,

  refresh: async () => {
    if (!getRuntimeConfig().bffEnabled) {
      set(import.meta.env.DEV
        ? { status: 'signed-in', user: DEVELOPMENT_USER }
        : { status: 'anonymous', user: null });
      return;
    }

    try {
      const session = await fetchSession();
      if (!session) {
        set({ status: 'anonymous', user: null });
        return;
      }

      set({
        status: 'signed-in',
        accessDenied: !session.capabilities.includes('access'),
        user: {
          id: session.id,
          name: session.name,
          email: session.email,
          initials: initials(session.name),
          capabilities: new Set(session.capabilities),
        },
      });
    } catch {
      // Ein BFF-Ausfall darf nicht mit einer halbgültigen Sitzung weiterlaufen.
      set({ status: 'anonymous', user: null });
    }
  },

  signIn: async () => {
    signIn();
  },

  signOut: async () => {
    // Erst ein bestaetigter Server-Logout (oder 401) beendet die lokale Sitzung.
    // Bei Netzwerk-/CSRF-Fehlern bleibt sie sichtbar und die Abmeldung wiederholbar.
    await logout();
    set({ status: 'anonymous', user: null, accessDenied: false });
  },

  setAccessDenied: (denied) => {
    if (get().accessDenied !== denied) set({ accessDenied: denied });
  },
}));

/**
 * Kurzer Zusatz unter dem Namen: die stärkste Fähigkeit, die die Person trägt.
 * Mehr gehört nicht in die Kopfzeile; die vollständige Liste steht im Menü.
 */
export function describeRole(user: SessionUser | null): string {
  if (!user) return '';
  if (hasCapability(user, 'operator')) return 'Betrieb';
  if (hasCapability(user, 'modeler')) return 'Modellieren';
  if (hasCapability(user, 'access')) return 'Aufgaben';
  return 'Kein Zugang';
}

/** Prüft eine BFF-Fähigkeit. Die API bleibt die maßgebliche Autorisierung. */
export function hasCapability(user: SessionUser | null, capability: FlowzerCapability): boolean {
  return user?.capabilities.has(capability) ?? false;
}

/** Die Vollansicht steht jedem BFF-Subjekt mit der `access`-Fähigkeit offen. */
export function seesFullConsole(user: SessionUser | null): boolean {
  return hasCapability(user, 'access');
}

/** Prüft eine Fähigkeit der angemeldeten Person für die reine Anzeige. */
export function useCan(): (capability: FlowzerCapability) => boolean {
  const user = useSession((state) => state.user);
  return (capability) => hasCapability(user, capability);
}

setUnauthorizedHandler(() => {
  useSession.setState({ status: 'anonymous', user: null, accessDenied: false });
});
setAccessDeniedHandler((denied) => useSession.getState().setAccessDenied(denied));

/** Lädt die aktuelle BFF-Sitzung einmal beim Start des SPA. */
export function initialiseSession(): void {
  void useSession.getState().refresh();
}
