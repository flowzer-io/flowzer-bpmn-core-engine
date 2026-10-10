// Ausschließlich geschlossene Zähler; keine Titel, URLs, Attachments, Exceptions oder Tokens.
const fs = require('fs');
class SafeReporter {
  constructor() { this.rows = { total: 0, passed: 0, failed: 0, skipped: 0, interrupted: 0, errors: 0 }; }
  onBegin(_config, suite) { this.rows.total = suite.allTests().length; }
  onTestEnd(_test, result) {
    const key = { passed: 'passed', failed: 'failed', timedOut: 'failed', skipped: 'skipped', interrupted: 'interrupted' }[result.status];
    if (key) this.rows[key] += 1; else this.rows.errors += 1;
  }
  onError() { this.rows.errors += 1; }
  onEnd(result) {
    const output = process.env.FLOWZER_RUNTIME_REPORT;
    if (!output || !output.endsWith('/auth-result.json')) throw new Error('Geschlossene Reportbindung fehlt.');
    fs.writeFileSync(output, JSON.stringify({ ...this.rows, success: result.status === 'passed'
      && this.rows.total > 0 && this.rows.total === this.rows.passed && this.rows.errors === 0 }) + '\n', { flag: 'wx', mode: 0o600 });
  }
  // Playwright darf interne Fehler-/Testausgaben nicht an den Actions-Log weiterreichen.
  onStdOut() {}
  onStdErr() {}
}
module.exports = SafeReporter;
