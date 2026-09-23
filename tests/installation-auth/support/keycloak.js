// Zugriff auf den isolierten Keycloak: Bearer-Tokens per Direct Grant für API-Tests und die
// Admin-API (Bootstrap-Administrator admin/admin, nur lokal) für Deaktivierung und Rollenentzug.
const { ALICE_ROLES, API_AUDIENCE, AUTH_URL, REALM, USERS } = require('./constants');
const { httpRequest } = require('./http');

const TOKEN_ENDPOINT = `${AUTH_URL}/realms/${REALM}/protocol/openid-connect/token`;
const ADMIN_BASE = `${AUTH_URL}/admin/realms/${REALM}`;

/** Passwort-Grant ohne Auswertung; liefert Status und Antwort für Negativfälle. */
async function passwordGrant(user, clientId = 'flowzer-test-cli') {
  const response = await httpRequest(TOKEN_ENDPOINT, {
    method: 'POST',
    form: { grant_type: 'password', client_id: clientId, username: user.username, password: user.password }
  });
  return { status: response.status, body: response.json() };
}

/** Access-Token einer Testperson; wirft bei jeder Ablehnung. */
async function getUserToken(user, clientId = 'flowzer-test-cli') {
  const { status, body } = await passwordGrant(user, clientId);
  if (status !== 200 || !body?.access_token) {
    throw new Error(`Token fuer ${user.username} ueber ${clientId} abgelehnt: HTTP ${status} ${body?.error || ''}`);
  }

  return body.access_token;
}

async function adminToken() {
  const response = await httpRequest(`${AUTH_URL}/realms/master/protocol/openid-connect/token`, {
    method: 'POST',
    form: { grant_type: 'password', client_id: 'admin-cli', username: 'admin', password: 'admin' }
  });
  if (response.status !== 200) {
    throw new Error(`Admin-Token abgelehnt: HTTP ${response.status}`);
  }

  return response.json().access_token;
}

async function admin(method, path, json) {
  const response = await httpRequest(`${ADMIN_BASE}${path}`, {
    method,
    headers: { authorization: `Bearer ${await adminToken()}` },
    ...(json === undefined ? {} : { json })
  });
  if (response.status >= 300) {
    throw new Error(`Keycloak-Admin ${method} ${path} fehlgeschlagen: HTTP ${response.status} ${response.text}`);
  }

  return response.text ? response.json() : null;
}

async function findUser(username) {
  const users = await admin('GET', `/users?username=${encodeURIComponent(username)}&exact=true`);
  if (!Array.isArray(users) || users.length !== 1) {
    throw new Error(`Testperson ${username} nicht eindeutig gefunden.`);
  }

  return users[0];
}

async function setUserEnabled(username, enabled) {
  const user = await findUser(username);
  // Vollständige Darstellung zurückschreiben: Ein Teilobjekt könnte Profilfelder leeren.
  const representation = await admin('GET', `/users/${user.id}`);
  await admin('PUT', `/users/${user.id}`, { ...representation, enabled });
}

async function clientRole(clientId, roleName) {
  const clients = await admin('GET', `/clients?clientId=${encodeURIComponent(clientId)}`);
  if (!Array.isArray(clients) || clients.length !== 1) {
    throw new Error(`Client ${clientId} nicht eindeutig gefunden.`);
  }

  const role = await admin('GET', `/clients/${clients[0].id}/roles/${encodeURIComponent(roleName)}`);
  return { clientUuid: clients[0].id, role };
}

async function removeApiRole(username, roleName) {
  const user = await findUser(username);
  const { clientUuid, role } = await clientRole(API_AUDIENCE, roleName);
  await admin('DELETE', `/users/${user.id}/role-mappings/clients/${clientUuid}`, [role]);
}

async function grantApiRole(username, roleName) {
  const user = await findUser(username);
  const { clientUuid, role } = await clientRole(API_AUDIENCE, roleName);
  await admin('POST', `/users/${user.id}/role-mappings/clients/${clientUuid}`, [role]);
}

/**
 * Stellt den Ausgangszustand des Realms her: alle Testpersonen aktiv, alice mit allen vier
 * Rollen. Macht die Specs unabhängig von ihrer Reihenfolge und von abgebrochenen Läufen.
 */
async function ensureBaseline() {
  for (const user of Object.values(USERS)) {
    const current = await findUser(user.username);
    if (!current.enabled) {
      await setUserEnabled(user.username, true);
    }
  }

  for (const role of ALICE_ROLES) {
    await grantApiRole(USERS.alice.username, role);
  }
}

module.exports = {
  ensureBaseline,
  findUser,
  getUserToken,
  grantApiRole,
  passwordGrant,
  removeApiRole,
  setUserEnabled
};
