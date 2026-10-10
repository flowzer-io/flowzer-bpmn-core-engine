// Ausschließlich geschlossene Zähler; keine Titel, URLs, Attachments, Exceptions oder Tokens.
const fs = require('fs');
class SafeReporter {
  /**
   * Beobachtet nur den einen bereits vorhandenen Discovery-Aufruf. Alle Argumente,
   * die ursprüngliche Antwort-/Fehleridentität und die HTTP-/TLS-Implementierung
   * bleiben erhalten. Keine zusätzliche Anfrage und kein Zugriff auf Rohfehler.
   * Die feste Issuerbindung kommt ausschließlich aus dem unveränderten CF-Harness.
   */
  static discoveryRequest(httpRequest, issuer, args) {
    if (args[0] !== issuer + '/.well-known/openid-configuration') return httpRequest(...args);
    const write = value => {
      // Diagnose darf nie den vorhandenen HTTP-Erfolg/-Fehler ersetzen. Fehlende
      // oder unbeschreibbare Bindung bleibt unbekannt; keine freie Fallbackdatei.
      try {
        const output = process.env.FLOWZER_RUNTIME_REPORT;
        if (typeof output !== 'string' || !output.startsWith('/') || !output.endsWith('/auth-result.json')
          || output.split('/').some(part => part === '.' || part === '..')) return;
        fs.writeFileSync(output.slice(0, -'auth-result.json'.length) + 'discovery-result.json',
          JSON.stringify(value) + '\n', { flag: 'wx', mode: 0o600 });
      } catch (_) { /* wx/IO-Fehler sind keine neue HTTP-/Authentscheidung. */ }
    };
    return (async () => {
      const value = { status: null, transport_code: null, issuer_matches: null, pkce_s256: null };
      let response;
      try { response = await httpRequest(...args); }
      catch (error) {
        // Nur exakte Node-Codes → feste Nummern. Kein message/stack/address/cause,
        // kein String-Fallback, unbekannter Code bleibt null (niemals Nullcode).
        try {
          const codes = ['ENOTFOUND', 'EAI_AGAIN', 'ECONNREFUSED', 'ECONNRESET', 'ETIMEDOUT', 'EPROTO',
            'ERR_TLS_CERT_ALTNAME_INVALID', 'UNABLE_TO_VERIFY_LEAF_SIGNATURE', 'SELF_SIGNED_CERT_IN_CHAIN',
            'DEPTH_ZERO_SELF_SIGNED_CERT', 'UNABLE_TO_GET_ISSUER_CERT_LOCALLY', 'CERT_HAS_EXPIRED', 'ERR_SSL_WRONG_VERSION_NUMBER'];
          const index = error && typeof error.code === 'string' ? codes.indexOf(error.code) : -1;
          if (index >= 0) value.transport_code = index + 1;
        } catch (_) { /* Auch ein unbekannter Codegetter ändert den Originalfehler nicht. */ }
        write(value); throw error;
      }
      try {
        // Der feste CF-Client hat bereits die Antwort gelesen. Nur RAM-Parsing
        // derselben Bytes, keine Header, kein json()-Methodenaufruf und kein Export.
        if (Number.isInteger(response.status) && response.status >= 100 && response.status <= 599) {
          value.status = response.status;
          const discovery = JSON.parse(response.text);
          if (discovery && typeof discovery === 'object' && !Array.isArray(discovery)) {
            value.issuer_matches = discovery.issuer === issuer;
            value.pkce_s256 = Array.isArray(discovery.code_challenge_methods_supported)
              && discovery.code_challenge_methods_supported.includes('S256');
          }
        }
      } catch (_) { /* Status bleibt tatsächlich beobachtet; unlesbare Felder bleiben null. */ }
      write(value); return response;
    })();
  }
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
