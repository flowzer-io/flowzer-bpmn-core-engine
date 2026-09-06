/**
 * Legt den Beispielprozess „Urlaubsantrag" in einer Flowzer-Instanz an: vier Formulare,
 * den Katalogeintrag und die erste deployte Version.
 *
 *   node examples/urlaubsantrag/import.mjs [http://localhost:5182]
 *
 * Gegen eine abgesicherte Instanz zusätzlich ein Zugangstoken mit der Modelliererrolle:
 *
 *   FLOWZER_TOKEN=<access-token> node examples/urlaubsantrag/import.mjs https://flowzer.example
 *
 * Das Skript ist wiederholbar: Vorhandene Formulare und der Katalogeintrag bleiben
 * stehen, es kommt nur eine neue Version dazu.
 */
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const BASE_URL = (process.argv[2] ?? process.env.FLOWZER_API_URL ?? 'http://localhost:5182').replace(/\/+$/, '');
const TOKEN = process.env.FLOWZER_TOKEN ?? '';

// Entspricht dem Entwicklungsbenutzer der API. Ohne Token wertet die API im
// Development-Modus diesen Header aus; in Produktion wird er ignoriert.
const USER_ID = 'd266f2b6-e96e-4d4a-9c20-c8e541394df0';

const DEFINITION_ID = 'flowzer-urlaubsantrag';

/** Formularname → Datei. Der Name ist zugleich der Form-Key im BPMN. */
const FORMS = {
  'Urlaubsantrag': 'formulare/urlaubsantrag.json',
  'Urlaub – Restanspruch': 'formulare/restanspruch.json',
  'Urlaub – Fachliche Entscheidung': 'formulare/fachliche-entscheidung.json',
  'Urlaub – Eintrag LexOffice': 'formulare/lexoffice.json',
};

function headers(extra = {}) {
  return {
    ...(TOKEN ? { authorization: `Bearer ${TOKEN}` } : { 'X-Flowzer-UserId': USER_ID }),
    ...extra,
  };
}

async function call(path, { method = 'GET', body, rawBody, contentType } = {}) {
  const response = await fetch(`${BASE_URL}${path}`, {
    method,
    headers: headers(
      rawBody ? { 'content-type': contentType ?? 'application/xml' } : body ? { 'content-type': 'application/json' } : {},
    ),
    body: rawBody ?? (body ? JSON.stringify(body) : undefined),
  });

  const text = await response.text();
  const payload = text ? JSON.parse(text) : null;

  if (!response.ok || payload?.successful === false) {
    throw new Error(`${method} ${path} → ${response.status}: ${payload?.errorMessage ?? text}`);
  }

  return payload;
}

async function ensureForm(name, file) {
  const existing = ((await call('/form/meta'))?.result ?? []).find((meta) => meta.name === name);
  const formId = existing?.formId ?? crypto.randomUUID();

  if (!existing) {
    await call(`/form/meta/${formId}`, { method: 'POST', body: { formId, name } });
  }

  const formData = readFileSync(resolve(here, file), 'utf8');
  const versions = existing ? ((await call(`/form/${formId}`))?.result ?? []) : [];
  const next = versions.length === 0
    ? { major: 1, minor: 0 }
    : { major: 1, minor: Math.max(...versions.map((v) => v.version?.minor ?? 0)) + 1 };

  await call('/form', { method: 'POST', body: { formId, version: next, formData } });
  console.log(`✓ Formular „${name}" v${next.major}.${next.minor}${existing ? '' : ' (neu angelegt)'}`);
}

async function main() {
  console.log(`Flowzer-API: ${BASE_URL}`);

  for (const [name, file] of Object.entries(FORMS)) {
    await ensureForm(name, file);
  }

  const catalogue = (await call('/definition/meta'))?.result ?? [];
  if (!catalogue.some((meta) => meta.definitionId === DEFINITION_ID)) {
    await call('/definition/meta', {
      method: 'POST',
      body: {
        definitionId: DEFINITION_ID,
        name: 'Urlaubsantrag',
        description:
          'Antrag stellen, parallel Urlaubstage, fachliche Entscheidung und Vertretung prüfen, ' +
          'danach benachrichtigen, in LexOffice und in TickyTask eintragen.',
      },
    });
    console.log('✓ Katalogeintrag „Urlaubsantrag" angelegt');
  } else {
    console.log('· Katalogeintrag „Urlaubsantrag" existiert bereits');
  }

  const xml = readFileSync(resolve(here, 'urlaubsantrag.bpmn'), 'utf8');
  const deployed = await call('/definition/deploy', { method: 'POST', rawBody: xml });
  const version = deployed.result.version;
  console.log(`✓ Deployt als v${version.major}.${version.minor}`);
  console.log('\nDie drei Service-Tasks brauchen Worker. Zum Ausprobieren:');
  console.log('  node examples/urlaubsantrag/demo-worker.mjs');
}

main().catch((error) => {
  console.error(`✗ ${error.message}`);
  process.exitCode = 1;
});
