// Dünner Aufruf von `docker compose` mit festem Projektnamen und fester Datei, damit Specs
// und run.sh garantiert denselben Stack ansprechen.
const { spawnSync } = require('child_process');
const path = require('path');

const PROJECT_NAME = 'flowzer-installation-auth';
const PROJECT_DIR = path.resolve(__dirname, '..');
const COMPOSE_FILE = path.join(PROJECT_DIR, 'compose.yml');

function compose(args, { timeoutMs = 180_000 } = {}) {
  const result = spawnSync('docker', ['compose', '-p', PROJECT_NAME, '-f', COMPOSE_FILE, ...args], {
    cwd: PROJECT_DIR,
    encoding: 'utf8',
    timeout: timeoutMs,
    maxBuffer: 16 * 1024 * 1024
  });

  if (result.error) {
    throw result.error;
  }

  return { status: result.status, stdout: result.stdout || '', stderr: result.stderr || '' };
}

function composeOrThrow(args, options) {
  const result = compose(args, options);
  if (result.status !== 0) {
    throw new Error(`docker compose ${args.join(' ')} failed with ${result.status}: ${result.stderr.trim()}`);
  }

  return result.stdout;
}

module.exports = { COMPOSE_FILE, PROJECT_NAME, compose, composeOrThrow };
