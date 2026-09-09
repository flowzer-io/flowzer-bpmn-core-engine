import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
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
function configuration(file, overrides = {}) {
  // Diese Coolify-Erweiterung ist keine Docker-Compose-Eigenschaft. Nur sie entfernen;
  // alle sicherheitsrelevanten Werte bleiben unverändert im tatsächlichen Parser.
  const source = readFileSync(resolve(root, file), 'utf8')
    .replace(/^\s+exclude_from_hc: true\s*$/gm, '');
  const json = execFileSync('docker', [
    'compose', '--project-directory', root, '--project-name', 'flowzer-contract-test',
    '--env-file', '/dev/null', '-f', '-', 'config', '--format', 'json',
  ], {
    input: source,
    encoding: 'utf8',
    env: {
      PATH: process.env.PATH,
      HOME: process.env.HOME,
      FLOWZER_AUTH_AUTHORITY: 'https://issuer.example.invalid',
      FLOWZER_AUTH_AUDIENCE: 'test-api',
      STORAGE_CONNECTION_STRING: 'synthetic-test-configuration',
      STORAGE_MIGRATION_CONNECTION_STRING: 'synthetic-migration-configuration',
      ...overrides,
    },
  });
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
