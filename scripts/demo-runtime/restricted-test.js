// Testzweck: Der Ressourcenpilot darf aus Chromium nur den isolierten Teststack ansprechen.
// Original-Specs verwenden dieselben Assertions. Gesperrte Ziele werden nicht protokolliert.
const { test: base, expect } = require('@playwright/test');
const { openProxy } = require('./loopback-proxy');
const allowedOrigins = new Set(['https://flowzer.test:8443', 'https://auth.flowzer.test:8443']);

/** Zusätzliche frühe Origin-/WS-Sperre; Redirect-Verbindungen schützt darunter der CONNECT-Proxy. */
async function installNetworkGuards(context) {
  let blocked = 0;
  await context.route('**/*', route => {
    let allowed = false;
    try { const url = new URL(route.request().url()); allowed = !url.username && !url.password && allowedOrigins.has(url.origin); } catch { /* geschlossen */ }
    if (allowed) return route.continue();
    blocked += 1;
    return route.abort('blockedbyclient');
  });
  await context.routeWebSocket('**/*', socket => { blocked += 1; socket.close(); });
  return () => blocked;
}
const test = base.extend({
  // Eigener Workerproxy besitzt auch den Browserprozess: alle Contexts/Browser enden vor den Sockets.
  _egress: [async ({}, use) => {
    for (const key of ['PLAYWRIGHT_DISABLE_FORCED_CHROMIUM_PROXIED_LOOPBACK',
      'PW_TEST_CONNECT_WS_ENDPOINT', 'PW_TEST_CONNECT_HEADERS', 'PW_TEST_REUSE_CONTEXT']) {
      if (process.env[key]) throw new Error('Fremde Browserverbindung oder Proxyumgehung nicht erlaubt.');
    }
    const proxy = await openProxy();
    try { await use(proxy); } finally { await proxy.close(); }
  }, { scope: 'worker' }],
  // Nicht nur Browsercontexts: auch Hintergrundverkehr des Browserprozesses vermittelt der eigene Proxy.
  launchOptions: [async ({ _egress, browserName, launchOptions }, use) => {
    if (browserName !== 'chromium' || launchOptions.proxy) throw new Error('Fremder Browser oder Proxy nicht erlaubt.');
    await use({ ...launchOptions, args: [...(launchOptions.args || []), '--disable-quic',
      '--force-webrtc-ip-handling-policy=disable_non_proxied_udp'],
      proxy: { server: _egress.server, bypass: _egress.bypass } });
  }, { scope: 'worker' }],
  proxy: async ({ _egress }, use) => use({ server: _egress.server, bypass: _egress.bypass }),
  _networkGuard: [async ({ context, _egress }, use) => {
    const blocked = await installNetworkGuards(context);
    await use();
    expect(_egress.blocked(), 'Keine Proxyverbindungen außerhalb des isolierten Teststacks').toBe(0);
    expect(blocked(), 'Keine Browseranfragen außerhalb des isolierten Teststacks').toBe(0);
  }, { auto: true }]
});
module.exports = { test, expect, installNetworkGuards };
