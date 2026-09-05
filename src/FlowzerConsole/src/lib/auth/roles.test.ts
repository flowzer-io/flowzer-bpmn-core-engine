import { describe, expect, it } from 'vitest';

import { decodeJwtPayload, readRoles } from './roles';

describe('readRoles', () => {
  // Testzweck: Keycloak liefert Clientrollen unter der Audience der API; genau die zaehlen.
  it('liest die Clientrollen der konfigurierten Audience', () => {
    const roles = readRoles(
      { resource_access: { 'flowzer-api': { roles: ['access', 'operator'] }, other: { roles: ['admin'] } } },
      'flowzer-api',
    );

    expect([...roles].sort()).toEqual(['access', 'operator']);
  });

  // Testzweck: Ohne konfigurierte Audience gilt jede Clientrolle; das ist der
  // Entwicklungsfall mit einem einzigen Client.
  it('nimmt ohne Audience alle Clientrollen', () => {
    const roles = readRoles({ resource_access: { a: { roles: ['x'] }, b: { roles: ['y'] } } }, '');

    expect([...roles].sort()).toEqual(['x', 'y']);
  });

  // Testzweck: Entra ID liefert App-Rollen flach im Claim `roles`.
  it('liest flache App-Rollen', () => {
    expect([...readRoles({ roles: ['access'] }, 'flowzer-api')]).toEqual(['access']);
  });

  // Testzweck: Ein Token ohne Rollen fuehrt nicht zu einem Absturz, sondern zu keiner Rolle.
  it('kommt ohne Rollen zurecht', () => {
    expect(readRoles(undefined, 'flowzer-api').size).toBe(0);
    expect(readRoles({}, 'flowzer-api').size).toBe(0);
  });
});

describe('decodeJwtPayload', () => {
  // Testzweck: Die Nutzlast wird gelesen, ohne die Signatur zu pruefen; die
  // Entscheidung trifft die API, die Konsole richtet nur ihre Anzeige danach.
  it('liest die Nutzlast eines Tokens', () => {
    const payload = { sub: 'abc', resource_access: { 'flowzer-api': { roles: ['access'] } } };
    const token = `x.${btoa(JSON.stringify(payload)).replace(/=+$/, '')}.y`;

    expect(decodeJwtPayload(token)).toEqual(payload);
  });

  // Testzweck: Unlesbare Werte duerfen die Anmeldung nicht zum Absturz bringen.
  it('liefert bei unlesbaren Werten nichts', () => {
    expect(decodeJwtPayload(undefined)).toBeUndefined();
    expect(decodeJwtPayload('kein-token')).toBeUndefined();
    expect(decodeJwtPayload('a.@@@.c')).toBeUndefined();
  });
});
