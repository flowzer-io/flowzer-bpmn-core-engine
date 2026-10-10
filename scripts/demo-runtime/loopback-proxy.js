// HTTPS-Verbindungsgrenze für jeden Browserhop, auch Redirects ohne erneute Playwrightroute.
// Kein DNS für angeforderte Ziele: erlaubt sind nur zwei Authorities, TCP geht stets zu Loopback.
const http = require('node:http');
const net = require('node:net');
const allowed = new Set(['flowzer.test:8443', 'auth.flowzer.test:8443']);
function permittedConnect(authority) { return typeof authority === 'string' && allowed.has(authority); }

/** Eigener pro-Test-Listener mit geschlossenen Zählern; niemals Request-/Header-/Exceptiontexte loggen. */
async function openProxy() {
  let blocked = 0;
  const sockets = new Set();
  const own = socket => { sockets.add(socket);socket.once('close', () => sockets.delete(socket));return socket; };
  const server = http.createServer((_request, response) => {
    blocked += 1;response.writeHead(403, { Connection: 'close' });response.end();
  });
  server.on('connection', own);
  server.on('clientError', (_error, socket) => socket.destroy());
  server.on('connect', (request, socket, head) => {
    if (!permittedConnect(request.url)) {
      blocked += 1;socket.end('HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n');return;
    }
    const upstream = own(net.connect({ host: '127.0.0.1', port: 8443 }));
    const stop = () => { socket.destroy();upstream.destroy(); };
    socket.once('error', stop);upstream.once('error', stop);
    socket.once('end', () => upstream.destroy());upstream.once('end', () => socket.destroy());
    upstream.once('connect', () => {
      socket.write('HTTP/1.1 200 Connection Established\r\n\r\n');
      if (head.length) upstream.write(head);
      socket.pipe(upstream);upstream.pipe(socket);
    });
  });
  await new Promise((resolve, reject) => {
    server.once('error', () => reject(new Error('Lokale Proxybindung nicht verfügbar.')));
    server.listen(0, '127.0.0.1', resolve);
  });
  return {
    server: `http://127.0.0.1:${server.address().port}`,
    // Chromium soll auch Literal-Loopbackziele NICHT direkt verbinden dürfen.
    bypass: '<-loopback>', blocked: () => blocked,
    close: async () => {
      for (const socket of sockets) socket.destroy();
      await new Promise(resolve => server.close(resolve));
    }
  };
}
module.exports = { permittedConnect, openProxy };
