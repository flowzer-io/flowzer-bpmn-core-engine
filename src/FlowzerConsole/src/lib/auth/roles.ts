/**
 * Rollen, die die Flowzer-API kennt. Sie stehen im Access-Token unter
 * `resource_access.<audience>.roles` (Keycloak) oder als `roles` (Entra ID) und
 * entscheiden, was die Konsole überhaupt anbieten darf.
 */
export const FLOWZER_ROLES = {
  access: 'access',
  modeler: 'modeler',
  operator: 'operator',
  worker: 'worker',
} as const;

export type FlowzerRole = (typeof FLOWZER_ROLES)[keyof typeof FLOWZER_ROLES];

interface AccessTokenClaims {
  resource_access?: Record<string, { roles?: string[] } | undefined>;
  roles?: string[];
  [claim: string]: unknown;
}

/**
 * Liest die Rollen aus einem Access-Token. Ohne konfigurierte Audience wird jede
 * Clientrolle akzeptiert; das ist der Entwicklungsfall mit einem einzigen Client.
 */
export function readRoles(claims: AccessTokenClaims | undefined, audience: string): Set<string> {
  const roles = new Set<string>();
  if (!claims) return roles;

  for (const role of claims.roles ?? []) {
    if (typeof role === 'string') roles.add(role);
  }

  const resourceAccess = claims.resource_access;
  if (resourceAccess && typeof resourceAccess === 'object') {
    const entries = audience ? [resourceAccess[audience]] : Object.values(resourceAccess);
    for (const entry of entries) {
      for (const role of entry?.roles ?? []) {
        if (typeof role === 'string') roles.add(role);
      }
    }
  }

  return roles;
}

/**
 * Zerlegt ein JWT ohne Signaturprüfung. Das genügt hier: Die Anzeige richtet sich
 * nach den Rollen, die Entscheidung trifft die API bei jedem Aufruf erneut.
 */
export function decodeJwtPayload(token: string | undefined): AccessTokenClaims | undefined {
  if (!token) return undefined;

  const segments = token.split('.');
  if (segments.length < 2) return undefined;

  try {
    const base64 = segments[1]!.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const json = decodeURIComponent(
      atob(padded)
        .split('')
        .map((character) => `%${character.charCodeAt(0).toString(16).padStart(2, '0')}`)
        .join(''),
    );
    return JSON.parse(json) as AccessTokenClaims;
  } catch {
    return undefined;
  }
}
