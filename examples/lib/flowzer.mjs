/**
 * Gemeinsames Handwerkszeug für die Beispielskripte: ein schmaler Zugang zur Flowzer-API
 * und das Anlegen von Formularen.
 */
import { readFileSync } from 'node:fs';

// NUR FUER DIE ENTWICKLUNG. Ohne FLOWZER_TOKEN schickt der Client diese feste Kennung im
// Header X-Flowzer-UserId. Die API wertet ihn ausschliesslich im Development-Modus aus; eine
// abgesicherte Instanz ignoriert ihn und antwortet mit 401. Nicht in Produktivskripte
// uebernehmen — dort gehoert ein Zugangstoken hin.
const DEV_USER_ID = 'd266f2b6-e96e-4d4a-9c20-c8e541394df0';

export function createClient(baseUrl, token = process.env.FLOWZER_TOKEN ?? '') {
  const base = baseUrl.replace(/\/+$/, '');

  async function call(path, { method = 'GET', body, rawBody, contentType } = {}) {
    const response = await fetch(`${base}${path}`, {
      method,
      headers: {
        ...(token ? { authorization: `Bearer ${token}` } : { 'X-Flowzer-UserId': DEV_USER_ID }),
        ...(rawBody
          ? { 'content-type': contentType ?? 'application/xml' }
          : body
            ? { 'content-type': 'application/json' }
            : {}),
      },
      body: rawBody ?? (body ? JSON.stringify(body) : undefined),
    });

    const text = await response.text();
    const payload = text ? JSON.parse(text) : null;
    if (!response.ok || payload?.successful === false) {
      throw new Error(`${method} ${path} → ${response.status}: ${payload?.errorMessage ?? text}`);
    }
    return payload;
  }

  /**
   * Legt ein Formular an oder haengt eine neue Version an ein vorhandenes. Wiederholbar:
   * Vorhandenes bleibt stehen.
   *
   * Die Versionsnummer kommt von der Engine — sie zaehlt beim Speichern selbst hoch und
   * ignoriert eine mitgeschickte Angabe. Deshalb wird hier keine berechnet, sondern die
   * vergebene aus der Antwort gemeldet.
   */
  async function ensureForm(name, file) {
    const existing = ((await call('/form/meta'))?.result ?? []).find((meta) => meta.name === name);
    const formId = existing?.formId ?? crypto.randomUUID();

    if (!existing) {
      await call(`/form/meta/${formId}`, { method: 'POST', body: { formId, name } });
    }

    const saved = await call('/form', {
      method: 'POST',
      body: { formId, formData: readFileSync(file, 'utf8') },
    });

    const v = saved?.result?.version;
    const version = v ? `v${v.major}.${v.minor}` : '(Version unbekannt)';
    console.log(`✓ Formular „${name}" ${version}${existing ? '' : ' (neu angelegt)'}`);
    return formId;
  }

  return { base, call, ensureForm };
}
