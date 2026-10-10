// Ein einziger Einstieg für lokale Abnahme und CI: niemals ein altes ignoriertes
// Bundle testen. Vite und Playwright stammen ausschließlich aus den Lockfiles.
const { spawnSync } = require('node:child_process');
const { resolve } = require('node:path');
const root = resolve(__dirname, '../..');
const consoleRoot = resolve(root, 'src/FlowzerConsole');
function run(script, args, cwd) {
  const result = spawnSync(process.execPath, [script, ...args], { cwd, stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}
run(resolve(consoleRoot, 'node_modules/vite/bin/vite.js'), ['build', '--config', 'vite.sandbox-probe.config.ts'], consoleRoot);
run(resolve(root, 'tests/ui-smoke/node_modules/@playwright/test/cli.js'),
  ['test', 'tests/form-embedding/opaque-renderer.test.cjs', '--workers=1', '--reporter=line',
    '--output=tests/form-embedding/test-results', ...process.argv.slice(2)], root);
