// Feste Adressen und synthetische Testkonten des isolierten Abnahme-Stacks.
// Die Passwörter sind offensichtliche Testwerte aus keycloak/flowzer-test-realm.json.

const BASE_URL = 'https://flowzer.test:8443';
const AUTH_URL = 'https://auth.flowzer.test:8443';
const REALM = 'flowzer-test';
const ISSUER = `${AUTH_URL}/realms/${REALM}`;
const API_AUDIENCE = 'flowzer-api';

// Der Stack vergibt 30 Sekunden Access-Token-Laufzeit (Realm-Einstellung).
const ACCESS_TOKEN_LIFETIME_SECONDS = 30;

const USERS = {
  alice: { username: 'alice', password: 'alice-test-password' },
  bob: { username: 'bob', password: 'bob-test-password' },
  carol: { username: 'carol', password: 'carol-test-password' },
  dave: { username: 'dave', password: 'dave-test-password' }
};

// Rollen des Clients flowzer-api, die alice im Ausgangszustand trägt.
const ALICE_ROLES = ['access', 'modeler', 'operator', 'worker'];

module.exports = {
  ACCESS_TOKEN_LIFETIME_SECONDS,
  ALICE_ROLES,
  API_AUDIENCE,
  AUTH_URL,
  BASE_URL,
  ISSUER,
  REALM,
  USERS
};
