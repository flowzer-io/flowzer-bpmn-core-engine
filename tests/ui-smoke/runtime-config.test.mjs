import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const roles = {
  Authentication__JwtBearer__RequiredRole: ['FLOWZER_AUTH_REQUIRED_ROLE', 'flowzer-access'],
  Authentication__JwtBearer__Roles__Modeler: ['FLOWZER_AUTH_ROLE_MODELER', 'flowzer-modeler'],
  Authentication__JwtBearer__Roles__Operator: ['FLOWZER_AUTH_ROLE_OPERATOR', 'flowzer-operator'],
  Authentication__JwtBearer__Roles__Worker: ['FLOWZER_AUTH_ROLE_WORKER', 'flowzer-worker'],
};
const aiFlags = {
  Ai__AllowCloudProviders: 'FLOWZER_AI_ALLOW_CLOUD_PROVIDERS',
  Ai__AllowLocalEndpoints: 'FLOWZER_AI_ALLOW_LOCAL_ENDPOINTS',
  AiExecution__Enabled: 'FLOWZER_AI_EXECUTION_ENABLED',
};

/** Echte Compose-Interpolation, aber ohne Daemon, .env oder geerbte Produktivkonfiguration. */
function configuration(file, overrides = {}, sharedEnvFile) {
  // Diese Coolify-Erweiterung ist keine Docker-Compose-Eigenschaft. Nur sie entfernen;
  // alle sicherheitsrelevanten Werte bleiben unverändert im tatsächlichen Parser.
  let source = readFileSync(resolve(root, file), 'utf8')
    .replace(/^\s+exclude_from_hc: true\s*$/gm, '');
  // Coolify ergänzt in verarbeiteten Stacks dieselbe env_file an allen Diensten.
  // Der Test bildet genau diese zusätzliche Vererbung nach, ohne echte Secrets zu lesen.
  if (sharedEnvFile) source = source.replace(/^  (migrate|api|console):$/gm,
    (line) => `${line}\n    env_file: [${JSON.stringify(sharedEnvFile)}]`);
  const env = {
    PATH: process.env.PATH,
    HOME: process.env.HOME,
    FLOWZER_AUTH_AUTHORITY: 'https://issuer.example.invalid',
    FLOWZER_AUTH_AUDIENCE: 'test-api',
    STORAGE_CONNECTION_STRING: 'synthetic-test-configuration',
    STORAGE_MIGRATION_CONNECTION_STRING: 'synthetic-migration-configuration',
    ...overrides,
  };
  function compose(input, flags = []) {
    return execFileSync('docker', [
      'compose', '--project-directory', root, '--project-name', 'flowzer-contract-test',
      '--env-file', '/dev/null', '-f', '-', 'config', ...flags, '--format', 'json',
    ], { input, encoding: 'utf8', env });
  }
  if (sharedEnvFile) {
    // Relevante Coolify-Parser-Semantik, kein vollständiger Nachbau: referenzierte
    // Aliase werden zusätzlich injiziert, leere Werte aus der Konfiguration ersetzt.
    // Quelle: coollabsio/coolify, bootstrap/helpers/parsers.php, applicationParser.
    const parsed = JSON.parse(compose(source, ['--no-interpolate', '--no-env-resolution']));
    for (const service of Object.values(parsed.services)) {
      for (const [key, value] of Object.entries(service.environment)) {
        const reference = typeof value === 'string' && value.match(/^\$\{([A-Z_]+)(?::[-?].*)?\}$/);
        if (reference && reference[1] !== key && Object.hasOwn(overrides, reference[1])) {
          service.environment[reference[1]] = overrides[reference[1]];
        }
      }
      for (const [key, value] of Object.entries(service.environment)) {
        if (value === '' && overrides[key]) service.environment[key] = overrides[key];
      }
    }
    source = JSON.stringify(parsed);
  }
  const json = compose(source);
  return JSON.parse(json).services;
}

for (const file of ['compose.runtime.yml', 'compose.coolify.yaml']) {
  // Testzweck: Fehlende oder leere Rollenkonfiguration darf in den Installationsvorlagen
  // niemals zur historischen rollenlosen Modeler-/Operator-/Worker-Freigabe werden.
  test(`${file}: sichere Rollen auch bei leeren Umgebungswerten`, () => {
    for (const overrides of [{}, Object.fromEntries(Object.values(roles).map(([name]) => [name, '']))]) {
      const api = configuration(file, overrides).api.environment;
      for (const [key, [, expected]] of Object.entries(roles)) assert.equal(api[key], expected, key);
    }
  });

  // Testzweck: Bestehende Installationen dürfen ihre ausdrücklich zugeordneten Rollennamen
  // weiter benutzen; die sicheren Defaults überschreiben keine nichtleeren Angaben.
  test(`${file}: explizite Rollen bleiben konfigurierbar`, () => {
    const custom = Object.fromEntries(Object.values(roles).map(([name]) => [name, `custom-${name}`]));
    const api = configuration(file, custom).api.environment;
    for (const [key, [name]] of Object.entries(roles)) assert.equal(api[key], custom[name], key);
  });

  // Testzweck: Provider-Ausführung und Datenfluss sind getrennte, standardmäßig gesperrte
  // Opt-ins. Die Compose-Verträge müssen eine bewusste Aktivierung tatsächlich weiterreichen.
  test(`${file}: KI bleibt aus und lässt sich ausdrücklich aktivieren`, () => {
    const disabled = configuration(file).api.environment;
    for (const key of Object.keys(aiFlags)) assert.equal(disabled[key], 'false', key);
    const enabled = configuration(file, {
      ...Object.fromEntries(Object.values(aiFlags).map((name) => [name, 'true'])),
      FLOWZER_AUTH_ROLE_AI_CONNECTION_USER: 'ai-use',
      FLOWZER_AUTH_ROLE_AI_CONNECTION_MANAGER: 'ai-manage',
    }).api.environment;
    for (const key of Object.keys(aiFlags)) assert.equal(enabled[key], 'true', key);
    assert.equal(enabled.Authentication__JwtBearer__Roles__AiConnectionUser, 'ai-use');
    assert.equal(enabled.Authentication__JwtBearer__Roles__AiConnectionManager, 'ai-manage');
  });
}

// Testzweck: Coolifys Parser plus gemeinsame env_file dürfen keine rohen Secrets in
// fachfremde Dienste tragen; explizite Konfigurationszuordnungen müssen weiterhin funktionieren.
test('compose.coolify.yaml: gemeinsame env_file wahrt die Secret-Grenzen aller Dienste', () => {
  const directory = mkdtempSync(resolve(tmpdir(), 'flowzer-compose-isolation-'));
  const secrets = {
    STORAGE_CONNECTION_STRING: 'synthetic-runtime',
    STORAGE_MIGRATION_CONNECTION_STRING: 'synthetic-migration',
    FLOWZER_BFF_CLIENT_SECRET: 'synthetic-bff',
    FLOWZER_DIRECTORY_CLIENT_SECRET: 'synthetic-directory',
  };
  try {
    const sharedEnvFile = resolve(directory, 'synthetic.env');
    writeFileSync(sharedEnvFile, Object.entries(secrets).map(([key, value]) => `${key}=${value}`).join('\n'), { mode: 0o600 });
    const services = configuration('compose.coolify.yaml', secrets, sharedEnvFile);
    for (const [name, service] of Object.entries(services)) {
      const allowed = name === 'api'
        ? ['STORAGE_CONNECTION_STRING', 'FLOWZER_BFF_CLIENT_SECRET', 'FLOWZER_DIRECTORY_CLIENT_SECRET']
        : name === 'migrate' ? ['STORAGE_MIGRATION_CONNECTION_STRING'] : [];
      for (const key of Object.keys(secrets)) {
        assert.equal(service.environment[key], allowed.includes(key) ? secrets[key] : '__FLOWZER_NOT_FOR_THIS_SERVICE__', `${name}: ${key}`);
      }
    }
    const api = services.api.environment;
    assert.equal(api.Storage__PostgreSql__ConnectionString, secrets.STORAGE_CONNECTION_STRING);
    assert.equal(api.Authentication__Bff__ClientSecret, secrets.FLOWZER_BFF_CLIENT_SECRET);
    assert.equal(api.IdentityDirectory__ClientSecret, secrets.FLOWZER_DIRECTORY_CLIENT_SECRET);
    assert.equal(api.Storage__PostgreSql__MigrationConnectionString, undefined);
    const migration = services.migrate.environment;
    assert.equal(migration.Storage__PostgreSql__ConnectionString, secrets.STORAGE_MIGRATION_CONNECTION_STRING);
    assert.equal(migration.Storage__PostgreSql__MigrationConnectionString, secrets.STORAGE_MIGRATION_CONNECTION_STRING);
    for (const key of ['Authentication__Bff__ClientSecret', 'IdentityDirectory__ClientSecret']) {
      assert.equal(migration[key], undefined);
      assert.equal(services.console.environment[key], undefined);
    }
    assert.equal(services.console.environment.Storage__PostgreSql__ConnectionString, undefined);
    assert.equal(services.console.environment.Storage__PostgreSql__MigrationConnectionString, undefined);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
