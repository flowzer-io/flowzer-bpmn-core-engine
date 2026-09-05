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

  // Testzweck: Rollen fremder Clients zaehlen nie mit. Wuerden sie das, saehe jemand
  // mit operator auf einer anderen Anwendung hier die vollstaendige Konsole.
  it('nimmt keine Rollen fremder Clients', () => {
    const roles = readRoles(
      { resource_access: { 'flowzer-api': { roles: ['access'] }, 'andere-app': { roles: ['operator'] } } },
      'flowzer-api',
    );

    expect([...roles]).toEqual(['access']);
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

describe('readRoles mit Audience aus dem Token', () => {
  // Testzweck: Ohne konfigurierte Audience zaehlt die des Tokens, nicht jeder Client.
  // Sonst saehe jemand mit operator auf einer anderen Anwendung hier die volle Konsole.
  it('nimmt ohne Konfiguration nur die Audience des Tokens', () => {
    const roles = readRoles(
      {
        aud: 'flowzer-api',
        resource_access: { 'flowzer-api': { roles: ['access'] }, 'andere-app': { roles: ['operator'] } },
      },
      '',
    );

    expect([...roles]).toEqual(['access']);
  });

  // Testzweck: Ohne jede Audience bleibt es bei den flachen App-Rollen.
  it('mischt ohne Audience keine fremden Clients', () => {
    const roles = readRoles({ resource_access: { 'andere-app': { roles: ['operator'] } } }, '');

    expect(roles.size).toBe(0);
  });
});
