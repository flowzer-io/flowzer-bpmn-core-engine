/**
 * Fähigkeiten, die die Konsole unterscheidet. Wie die zugehörigen Rollen im Identity
 * Provider heißen, entscheidet der Betrieb; die Namen stehen deshalb in der
 * Laufzeitkonfiguration und nicht hier. Die Rollen selbst stehen im Access-Token unter
 * `resource_access.<audience>.roles` (Keycloak) oder als `roles` (Entra ID).
 */
export type FlowzerCapability = 'access' | 'modeler' | 'operator' | 'worker';

interface AccessTokenClaims {
  resource_access?: Record<string, { roles?: string[] } | undefined>;
  roles?: string[];
  aud?: string | string[];
  [claim: string]: unknown;
}

/**
 * Liest die Rollen aus einem Access-Token.
 *
 * Maßgeblich ist die Audience der API: Genau dort stehen die Rollen, die die API
 * auswertet. Ist keine konfiguriert, gilt die Audience aus dem Token selbst. Alle
 * Clients zusammenzuwerfen wäre falsch — jemand mit `operator` auf einer anderen
 * Anwendung sähe hier sonst die vollständige Konsole.
 */
export function readRoles(claims: AccessTokenClaims | undefined, audience: string): Set<string> {
  const roles = new Set<string>();
  if (!claims) return roles;

  // Entra ID liefert App-Rollen flach; sie gelten per Definition für die Audience des Tokens.
  for (const role of claims.roles ?? []) {
    if (typeof role === 'string') roles.add(role);
  }

  const resourceAccess = claims.resource_access;
  if (!resourceAccess || typeof resourceAccess !== 'object') return roles;

  for (const name of resolveAudiences(claims, audience)) {
    for (const role of resourceAccess[name]?.roles ?? []) {
      if (typeof role === 'string') roles.add(role);
    }
  }

  return roles;
}

/** Konfigurierte Audience, sonst die des Tokens. Niemals alle Clients. */
function resolveAudiences(claims: AccessTokenClaims, audience: string): string[] {
  if (audience) return [audience];

  const fromToken = claims.aud;
  if (typeof fromToken === 'string') return [fromToken];
  if (Array.isArray(fromToken)) return fromToken.filter((value): value is string => typeof value === 'string');

  return [];
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
