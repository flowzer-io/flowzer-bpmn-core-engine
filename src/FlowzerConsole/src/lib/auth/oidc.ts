import { UserManager, WebStorageStateStore, type User } from 'oidc-client-ts';

import { getRuntimeConfig, isAuthenticationConfigured } from '@/lib/config/runtime';

export const LOGIN_CALLBACK_PATH = '/authentication/login-callback';
export const LOGOUT_CALLBACK_PATH = '/authentication/logout-callback';

let manager: UserManager | null = null;

/**
 * Der Zugang läuft als Authorization Code Flow mit PKCE. Die Konsole ist ein
 * öffentlicher Client ohne Geheimnis; ein Geheimnis im Browser wäre keines.
 */
export function getUserManager(): UserManager | null {
  if (!isAuthenticationConfigured()) return null;
  if (manager) return manager;

  const config = getRuntimeConfig();
  manager = new UserManager({
    authority: config.oidcAuthority,
    client_id: config.oidcClientId,
    redirect_uri: new URL(LOGIN_CALLBACK_PATH, window.location.origin).toString(),
    post_logout_redirect_uri: new URL(LOGOUT_CALLBACK_PATH, window.location.origin).toString(),
    response_type: 'code',
    scope: ['openid', 'profile', 'email', ...config.oidcScopes].join(' '),

    // Erneuern im Hintergrund, damit eine lange Sitzung nicht mitten in der Arbeit endet.
    automaticSilentRenew: true,
    // Der Zustand liegt in sessionStorage: Er endet mit dem Tab und wandert nicht
    // in andere Fenster, in denen jemand anderes angemeldet sein könnte.
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
  });

  return manager;
}

export async function getUser(): Promise<User | null> {
  return (await getUserManager()?.getUser()) ?? null;
}

export async function signIn(returnTo?: string): Promise<void> {
  await getUserManager()?.signinRedirect({
    // Nach der Anmeldung dort weitermachen, wo die Person hinwollte.
    state: returnTo ?? `${window.location.pathname}${window.location.search}`,
  });
}

export async function signOut(): Promise<void> {
  await getUserManager()?.signoutRedirect();
}

export async function completeSignIn(): Promise<string> {
  const user = await getUserManager()?.signinCallback();
  const target = typeof user?.state === 'string' ? user.state : '/';
  return target.startsWith('/') ? target : '/';
}
