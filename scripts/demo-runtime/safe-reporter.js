// Ausschließlich geschlossene Zähler; keine Titel, URLs, Attachments, Exceptions oder Tokens.
const fs = require('fs');
class SafeReporter {
  constructor() {
    this.rows = { total: 0, passed: 0, failed: 0, skipped: 0, interrupted: 0, errors: 0 };
    this.testIndexes = new Map(); this.failedIndexes = new Set();
  }
  onBegin(_config, suite) {
    const tests = suite.allTests(); this.rows.total = tests.length;
    // Playwrights sitzungseindeutige ID dient nur als RAM-Zuordnung. Keine Titel,
    // Pfade oder Roh-IDs exportieren; unklare Identitäten bekommen niemals Index 0.
    const ids = tests.map(value => value && value.id);
    if (tests.length <= 16 && ids.every(id => typeof id === 'string' && id.length > 0 && id.length <= 256)
      && new Set(ids).size === ids.length) ids.forEach((id, index) => this.testIndexes.set(id, index + 1));
  }
  onTestEnd(_test, result) {
    const key = { passed: 'passed', failed: 'failed', timedOut: 'failed', skipped: 'skipped', interrupted: 'interrupted' }[result.status];
    if (key) this.rows[key] += 1; else this.rows.errors += 1;
    if (key === 'failed' && _test && this.testIndexes.has(_test.id)) this.failedIndexes.add(this.testIndexes.get(_test.id));
  }
  onError() { this.rows.errors += 1; }
  onEnd(result) {
    const output = process.env.FLOWZER_RUNTIME_REPORT;
    if (!output || !output.endsWith('/auth-result.json')) throw new Error('Geschlossene Reportbindung fehlt.');
    const value = { ...this.rows, success: result.status === 'passed'
      && this.rows.total > 0 && this.rows.total === this.rows.passed && this.rows.errors === 0 };
    // Erfolgsvertrag bleibt exakt gleich. Nur bekannte tatsächliche Fehltests
    // ergänzen eine begrenzte Liste positiver Suiteordinale, keine freie Diagnose.
    if (!value.success && this.failedIndexes.size) value.failed_test_indexes = [...this.failedIndexes].sort((a, b) => a - b);
    fs.writeFileSync(output, JSON.stringify(value) + '\n', { flag: 'wx', mode: 0o600 });
  }
  // Playwright darf interne Fehler-/Testausgaben nicht an den Actions-Log weiterreichen.
  onStdOut() {}
  onStdErr() {}
}
module.exports = SafeReporter;
