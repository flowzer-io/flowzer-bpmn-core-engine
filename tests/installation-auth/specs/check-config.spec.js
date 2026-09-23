// Konfigurationsprüfung der installierten API gegen den echten Identity Provider.
const { test, expect } = require('@playwright/test');
const { compose } = require('../support/compose');
const { httpRequest } = require('../support/http');
const { AUTH_URL, ISSUER } = require('../support/constants');

test.describe('Konfigurationspruefung', () => {
  // Testzweck: `--check-config` im installierten API-Container endet gegen Datenbank und die
  // echte Keycloak-Discovery mit Exit 0, meldet Rollen und Schluesselring als OK und gibt
  // keine Secrets aus.
  test('--check-config meldet keine Beanstandungen', () => {
    // --no-deps: Der Stack laeuft bereits; der einmalige Migrationsdienst soll nicht erneut starten.
    const result = compose(['run', '--rm', '--no-deps', '-T', 'api', '--check-config'], { timeoutMs: 180_000 });
    const output = result.stdout;
    test.info().attachments.push({ name: 'check-config.txt', contentType: 'text/plain', body: Buffer.from(output) });

    expect(result.status, `${output}\n${result.stderr}`).toBe(0);
    expect(output).toMatch(/Konfiguration\s+OK/);
    expect(output).toMatch(/Ablage\s+OK\s+PostgreSQL erreichbar \(db:5432\/flowzer\), Schema flowzer vorhanden/);
    expect(output).toMatch(/Migrationen\s+OK\s+aktuell/);
    expect(output).toMatch(/Authentifizierung\s+OK\s+Schema Bff, Authority auth\.flowzer\.test antwortet auf die OIDC-Discovery; token_endpoint vorhanden, Issuer passt/);
    expect(output).toMatch(/Rollen\s+OK\s+Zugang access, Modeler modeler, Operator operator, Worker worker/);
    expect(output).toMatch(/Schluesselring\s+OK\s+Schluesselring beschreibbar: \/var\/lib\/flowzer\/data-protection/);
    expect(output).toMatch(/Ausdruecke\s+OK/);
    expect(output).toContain('Ergebnis: keine Beanstandungen.');

    for (const secretFragment of ['test-secret-not-for-production', 'test-runtime-password', 'Password=']) {
      expect(output + result.stderr).not.toContain(secretFragment);
    }
  });

  // Testzweck: Unabhaengig von `--check-config` belegt der Test gegen den echten Provider, dass
  // der Issuer exakt der konfigurierten Authority entspricht und PKCE S256 angeboten wird.
  test('Discovery liefert den konfigurierten Issuer und PKCE S256', async () => {
    const response = await httpRequest(`${ISSUER}/.well-known/openid-configuration`);
    expect(response.status).toBe(200);
    const discovery = response.json();
    expect(discovery.issuer).toBe(ISSUER);
    expect(discovery.token_endpoint.startsWith(`${AUTH_URL}/`)).toBe(true);
    expect(discovery.authorization_endpoint.startsWith(`${AUTH_URL}/`)).toBe(true);
    expect(discovery.code_challenge_methods_supported).toContain('S256');
  });
});
