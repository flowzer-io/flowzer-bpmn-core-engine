// HTTPS-Client für die Node-Seite der Tests. Chromium löst flowzer.test und
// auth.flowzer.test über --host-resolver-rules auf; Node kennt diese Namen nicht. Dieser
// Client bildet dieselbe Zuordnung nach und prüft das Zertifikat gegen die Test-CA aus dem
// Stack – strenger als der Browser, der Zertifikatsfehler ignoriert.
const https = require('https');
const { composeOrThrow } = require('./compose');
const { BASE_URL } = require('./constants');

const HOST_MAP = new Map([
  ['flowzer.test', '127.0.0.1'],
  ['auth.flowzer.test', '127.0.0.1']
]);

let cachedCa;

function testCaCertificate() {
  if (!cachedCa) {
    cachedCa = composeOrThrow(['exec', '-T', 'tls', 'cat', '/certs/ca.crt'], { timeoutMs: 30_000 });
  }

  return cachedCa;
}

function mappedLookup(hostname, options, callback) {
  const address = HOST_MAP.get(hostname);
  if (!address) {
    callback(new Error(`Host ${hostname} gehoert nicht zum Abnahme-Stack.`));
    return;
  }

  if (options && options.all) {
    callback(null, [{ address, family: 4 }]);
    return;
  }

  callback(null, address, 4);
}

/**
 * Führt eine Anfrage aus und liefert Status, Kopfzeilen und Rohtext. Weiterleitungen werden
 * bewusst nicht verfolgt, damit die Specs 302/401/403 exakt sehen.
 */
function httpRequest(url, { method = 'GET', headers = {}, body, form, json, timeoutMs = 30_000 } = {}) {
  const target = new URL(url);
  let payload;
  const requestHeaders = { accept: 'application/json', ...headers };

  if (form) {
    payload = new URLSearchParams(form).toString();
    requestHeaders['content-type'] = 'application/x-www-form-urlencoded';
  } else if (json !== undefined) {
    payload = JSON.stringify(json);
    requestHeaders['content-type'] = 'application/json';
  } else if (body !== undefined) {
    payload = body;
  }

  if (payload !== undefined) {
    requestHeaders['content-length'] = Buffer.byteLength(payload);
  }

  return new Promise((resolve, reject) => {
    const request = https.request({
      protocol: target.protocol,
      hostname: target.hostname,
      port: target.port || 443,
      path: `${target.pathname}${target.search}`,
      method,
      headers: requestHeaders,
      lookup: mappedLookup,
      servername: target.hostname,
      ca: testCaCertificate(),
      agent: false,
      timeout: timeoutMs
    }, response => {
      const chunks = [];
      response.on('data', chunk => chunks.push(chunk));
      response.on('end', () => {
        const text = Buffer.concat(chunks).toString('utf8');
        resolve({
          status: response.statusCode,
          headers: response.headers,
          text,
          json() {
            return text ? JSON.parse(text) : null;
          }
        });
      });
    });

    request.on('timeout', () => request.destroy(new Error(`Timeout nach ${timeoutMs} ms: ${method} ${url}`)));
    request.on('error', reject);
    if (payload !== undefined) {
      request.write(payload);
    }
    request.end();
  });
}

/** Aufruf der Flowzer-API über TLS-Proxy und Konsolen-nginx, optional mit Bearer. */
function apiRequest(pathname, { bearer, ...options } = {}) {
  const headers = { ...(options.headers || {}) };
  if (bearer) {
    headers.authorization = `Bearer ${bearer}`;
  }

  return httpRequest(new URL(pathname, BASE_URL).toString(), { ...options, headers });
}

/** Liest das Nutzdatenstück eines JWT ohne Prüfung – nur für Diagnose und Vorbedingungen. */
function decodeJwtPayload(token) {
  const [, payload] = token.split('.');
  return JSON.parse(Buffer.from(payload, 'base64url').toString('utf8'));
}

async function waitFor(description, probe, { timeoutMs = 120_000, intervalMs = 1_000 } = {}) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const value = await probe();
      if (value) {
        return value;
      }
    } catch (error) {
      lastError = error;
    }

    await new Promise(resolve => setTimeout(resolve, intervalMs));
  }

  throw new Error(`${description}: Zeitlimit von ${timeoutMs} ms ueberschritten${lastError ? ` (${lastError.message})` : ''}`);
}

module.exports = { apiRequest, decodeJwtPayload, httpRequest, testCaCertificate, waitFor };
