// Ein frischer normaler Build einschließlich des isolierten IIFE-Einstiegs ist
// Voraussetzung. Niemals einen ignorierten alten Bundle-Stand als Abnahme nutzen.
const { spawnSync } = require('node:child_process');
const { resolve } = require('node:path');
const root = resolve(__dirname, '../..');
const build = spawnSync('npm', ['run', 'build'], { cwd: resolve(root, 'src/FlowzerConsole'), stdio: 'inherit' });
if (build.error) throw build.error;
if (build.status !== 0) process.exit(build.status ?? 1);
const result = spawnSync(process.execPath, [resolve(root, 'tests/ui-smoke/node_modules/@playwright/test/cli.js'),
  'test', 'tests/form-embedding/production-embed.test.cjs', '--workers=1', '--reporter=line',
  '--output=tests/form-embedding/test-results', ...process.argv.slice(2)], { cwd: root, stdio: 'inherit' });
if (result.error) throw result.error;
process.exit(result.status ?? 1);
