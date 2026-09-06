/**
 * Spielt die wiederverwendbaren Formulare ein.
 *
 *   node examples/formulare-generisch/import.mjs [http://localhost:5182]
 *
 * Gegen eine abgesicherte Instanz zusätzlich ein Zugangstoken mit der Modelliererrolle:
 *
 *   FLOWZER_TOKEN=<access-token> node examples/formulare-generisch/import.mjs https://flowzer.example
 *
 * Wiederholbar: Vorhandenes bleibt stehen, es kommt nur eine neue Version dazu.
 */
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createClient } from '../lib/flowzer.mjs';

const here = dirname(fileURLToPath(import.meta.url));

/** Formularname → Datei. Der Name ist zugleich der Form-Key im BPMN. */
export const GENERISCHE_FORMULARE = {
  'Freigabe': 'freigabe.json',
  'Prüfung': 'pruefung.json',
  'Erledigung bestätigen': 'erledigung.json',
  'Kenntnisnahme': 'kenntnisnahme.json',
};

export async function ensureGenericForms(client) {
  for (const [name, file] of Object.entries(GENERISCHE_FORMULARE)) {
    await client.ensureForm(name, resolve(here, file));
  }
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const baseUrl = process.argv[2] ?? process.env.FLOWZER_API_URL ?? 'http://localhost:5182';
  const client = createClient(baseUrl);
  console.log(`Flowzer-API: ${client.base}`);
  ensureGenericForms(client).catch((error) => {
    console.error(`✗ ${error.message}`);
    process.exitCode = 1;
  });
}
