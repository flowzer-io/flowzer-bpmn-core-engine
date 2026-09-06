/**
 * Beispiel-Worker für die drei Service-Tasks des Urlaubsantrags.
 *
 *   node examples/urlaubsantrag/demo-worker.mjs [http://localhost:5182]
 *
 * Er ersetzt keine Anbindung: Die Vertretungsprüfung schaut nur in die laufenden
 * Instanzen dieses Prozesses, benachrichtigt wird auf der Konsole, und TickyTask
 * bekommt eine erfundene Vorgangsnummer. Er zeigt, wie der Ablauf durchläuft, und
 * dient als Vorlage für die echten Worker — der Vertrag steht in
 * docs/SERVICE-TASK-WORKER.md.
 *
 * Beenden mit Strg+C.
 */
const BASE_URL = (process.argv[2] ?? process.env.FLOWZER_API_URL ?? 'http://localhost:5182').replace(/\/+$/, '');
const TOKEN = process.env.FLOWZER_TOKEN ?? '';
// NUR FUER DIE ENTWICKLUNG: Ohne FLOWZER_TOKEN geht diese feste Kennung als
// X-Flowzer-UserId mit. Eine abgesicherte Instanz ignoriert den Header und antwortet 401.
// Ein echter Worker meldet sich mit einem Token der Worker-Rolle an.
const USER_ID = 'd266f2b6-e96e-4d4a-9c20-c8e541394df0';
const WORKER_ID = 'urlaub-demo-worker';

const TYPES = [
  'urlaub-vertretung-pruefen',
  'urlaub-genehmigung-mitteilen',
  'urlaub-ablehnung-mitteilen',
  'urlaub-tickytask-eintragen',
];

function headers() {
  return {
    'content-type': 'application/json',
    ...(TOKEN ? { authorization: `Bearer ${TOKEN}` } : { 'X-Flowzer-UserId': USER_ID }),
  };
}

async function call(path, body) {
  const response = await fetch(`${BASE_URL}${path}`, {
    method: 'POST',
    headers: headers(),
    body: JSON.stringify(body),
  });
  const text = await response.text();
  const payload = text ? JSON.parse(text) : null;
  if (!response.ok || payload?.successful === false) {
    throw new Error(`POST ${path} → ${response.status}: ${payload?.errorMessage ?? text}`);
  }
  return payload;
}

/**
 * Ergebnis je Auftragstyp. `variables` des Auftrags sind die Prozessvariablen —
 * hier also die Angaben aus dem Urlaubsantrag.
 */
function handle(type, variables) {
  const wer = variables?.mitarbeiter ?? 'unbekannt';
  const vertretung = variables?.vertretung ?? '—';

  switch (type) {
    case 'urlaub-vertretung-pruefen':
      // Eine echte Anbindung fragt hier den Urlaubskalender. Der Demo-Worker sagt immer ja
      // und legt den geprüften Namen daneben, damit im Verlauf sichtbar ist, worauf sich
      // die Antwort bezieht.
      console.log(`  Vertretung „${vertretung}" geprüft → frei`);
      return { vertretungFrei: 'ja', vertretungGeprueftFuer: vertretung };

    case 'urlaub-genehmigung-mitteilen':
      console.log(`  Nachricht an ${wer}: Urlaub genehmigt`);
      return { benachrichtigtAm: new Date().toISOString() };

    case 'urlaub-ablehnung-mitteilen':
      console.log(`  Nachricht an ${wer}: Antrag abgelehnt`);
      return { benachrichtigtAm: new Date().toISOString() };

    case 'urlaub-tickytask-eintragen':
      console.log(`  TickyTask-Eintrag für ${wer} angelegt`);
      return { tickytaskVorgang: `TT-${Math.floor(Math.random() * 90000 + 10000)}` };

    default:
      return {};
  }
}

async function runOnce() {
  for (const type of TYPES) {
    const fetched = await call('/job/fetch', {
      type,
      workerId: WORKER_ID,
      maxJobs: 10,
      lockSeconds: 60,
    });

    for (const job of fetched?.result ?? []) {
      console.log(`▸ ${type} (${job.id})`);
      try {
        await call(`/job/${job.id}/complete`, {
          workerId: WORKER_ID,
          variables: handle(type, job.variables),
        });
      } catch (error) {
        // Ein Fehlschlag gehört gemeldet, nicht verschwiegen: Sonst läuft die Sperre ab und
        // der Auftrag wird stillschweigend erneut vergeben.
        await call(`/job/${job.id}/fail`, {
          workerId: WORKER_ID,
          errorMessage: error.message,
          retryBackoffSeconds: 30,
        }).catch(() => {});
        console.error(`  ✗ ${error.message}`);
      }
    }
  }
}

console.log(`Worker für ${TYPES.join(', ')}`);
console.log(`Flowzer-API: ${BASE_URL} — Strg+C beendet.`);

let running = true;
process.on('SIGINT', () => {
  running = false;
  console.log('\nBeendet.');
  process.exit(0);
});

while (running) {
  try {
    await runOnce();
  } catch (error) {
    console.error(`✗ ${error.message}`);
  }
  await new Promise((resolve) => setTimeout(resolve, 2000));
}
