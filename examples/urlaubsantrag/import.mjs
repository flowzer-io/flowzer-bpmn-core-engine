/**
 * Legt den Beispielprozess „Urlaubsantrag" in einer Flowzer-Instanz an: die
 * wiederverwendbaren Formulare, das Antragsformular, den Katalogeintrag und die erste
 * deployte Version.
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
import { createClient } from '../lib/flowzer.mjs';
import { ensureGenericForms } from '../formulare-generisch/import.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const DEFINITION_ID = 'flowzer-urlaubsantrag';

async function main() {
  const baseUrl = process.argv[2] ?? process.env.FLOWZER_API_URL ?? 'http://localhost:5182';
  const client = createClient(baseUrl);
  console.log(`Flowzer-API: ${client.base}`);

  // Drei der vier Aufgaben benutzen die allgemeinen Formulare; nur der Antrag selbst
  // braucht ein eigenes.
  await ensureGenericForms(client);
  await client.ensureForm('Urlaubsantrag', resolve(here, 'formulare/urlaubsantrag.json'));

  const catalogue = (await client.call('/definition/meta'))?.result ?? [];
  if (!catalogue.some((meta) => meta.definitionId === DEFINITION_ID)) {
    await client.call('/definition/meta', {
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
  const deployed = await client.call('/definition/deploy', { method: 'POST', rawBody: xml });
  const version = deployed.result.version;
  console.log(`✓ Deployt als v${version.major}.${version.minor}`);
  console.log('\nDie drei Service-Tasks brauchen Worker. Zum Ausprobieren:');
  console.log('  node examples/urlaubsantrag/demo-worker.mjs');
}

main().catch((error) => {
  console.error(`✗ ${error.message}`);
  process.exitCode = 1;
});
