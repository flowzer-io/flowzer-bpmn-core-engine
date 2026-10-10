// Testzweck: Verbindungsgrenze mit reinen Node-Transportmocks, keine echten Listener/DNS/TCP-Aufrufe.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const { permittedConnect } = require('./loopback-proxy');

class Socket extends EventEmitter {
  constructor() { super(); this.output = ''; this.destroyed = false; }
  write(value) { this.output += value; }
  end(value = '') { this.output += value; }
  pipe() { return this; }
  destroy() { this.destroyed = true; this.emit('close'); }
}
function mockedProxy() {
  let handler; const listener = new EventEmitter(); const connections = []; let binding; let closed = false;
  listener.listen = (port, host, callback) => { binding = { port, host };callback(); };
  listener.address = () => ({ port: 7777 });
  listener.close = callback => { closed = true;callback(); };
  const sandbox = { module: { exports: {} }, Promise, Set, Error, require: name => {
    if (name === 'node:http') return { createServer: fn => { handler = fn;return listener; } };
    assert.equal(name, 'node:net');
    return { connect: options => { const socket = new Socket();connections.push({ options, socket });return socket; } };
  } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'loopback-proxy.js'), 'utf8'), sandbox);
  return { open: sandbox.module.exports.openProxy, listener, connections,
    get binding() { return binding; }, get closed() { return closed; }, get handler() { return handler; } };
}

test('CONNECT erlaubt nur zwei exakte synthetische Authorities', () => {
  // Testzweck: Auch Redirect-Hops, IPs, Credentials, Fremdports und echte IdPs bekommen keinen Socket.
  for (const value of ['flowzer.test:8443', 'auth.flowzer.test:8443']) assert.equal(permittedConnect(value), true);
  for (const value of ['flowzer.test', 'flowzer.test:443', 'flowzer.test:8443@other', '127.0.0.1:8443',
    'auth.tickytask.de:443', 'demo.tickytask.de:443', 'other.invalid:8443', null]) assert.equal(permittedConnect(value), false);
});

test('Proxy bindet nur Loopback, keine angeforderte Authority fließt in DNS/TCP', async () => {
  // Testzweck: Der CONNECT-Pfad verbindet ausschließlich den bekannten lokalen TLS-Port.
  const fake = mockedProxy(); const proxy = await fake.open();
  assert.ok(proxy && proxy.server === 'http://127.0.0.1:7777', 'Lokale Proxybindung fehlt');
  assert.equal(proxy.bypass, '<-loopback>');
  assert.deepEqual(fake.binding, { port: 0, host: '127.0.0.1' });
  const socket = new Socket();fake.listener.emit('connection', socket);
  fake.listener.emit('connect', { url: 'flowzer.test:8443' }, socket, Buffer.alloc(0));
  assert.equal(fake.connections.length, 1);
  assert.equal(fake.connections[0].options.host, '127.0.0.1');assert.equal(fake.connections[0].options.port, 8443);
  fake.connections[0].socket.emit('connect');assert.match(socket.output, /^HTTP\/1.1 200/);
  await proxy.close();assert.equal(fake.closed, true);assert.equal(socket.destroyed, true);
  assert.equal(fake.connections[0].socket.destroyed, true);
});

test('Fremder Redirect-CONNECT wird vor jedem neuen Socket abgewiesen', async () => {
  // Testzweck: Eine erlaubte Erstverbindung verleiht dem nächsten Redirect-Hop kein Recht.
  const fake = mockedProxy();const proxy = await fake.open();
  assert.ok(proxy && typeof proxy.blocked === 'function', 'Proxyzähler fehlt');
  for (const url of ['flowzer.test:8443', '203.0.113.1:443', 'other.invalid:443']) {
    const socket = new Socket();fake.listener.emit('connection', socket);
    fake.listener.emit('connect', { url }, socket, Buffer.alloc(0));
    if (url !== 'flowzer.test:8443') assert.match(socket.output, /^HTTP\/1.1 403/);
  }
  assert.equal(fake.connections.length, 1);assert.equal(proxy.blocked(), 2);
  let status;fake.handler({}, { writeHead: code => { status = code; }, end() {} });
  assert.equal(status, 403);assert.equal(proxy.blocked(), 3);
  await proxy.close();
});
