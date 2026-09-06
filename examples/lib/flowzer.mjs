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

  async function call(path, { method = 'GET', body, rawBody, contentType, allow404 = false } = {}) {
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

    // Manche Endpunkte antworten mit 404 statt einer leeren Liste — etwa die Versionen
    // eines Formulars, das zwar angelegt, aber noch nie gespeichert wurde.
    if (allow404 && response.status === 404) return null;

    const text = await response.text();
    const payload = text ? JSON.parse(text) : null;
    if (!response.ok || payload?.successful === false) {
      throw new Error(`${method} ${path} → ${response.status}: ${payload?.errorMessage ?? text}`);
    }
    return payload;
  }

  /**
   * Legt ein Formular an oder hängt eine neue Version an ein vorhandenes. Wiederholbar:
   * Vorhandenes bleibt stehen.
   */
  async function ensureForm(name, file) {
    const existing = ((await call('/form/meta'))?.result ?? []).find((meta) => meta.name === name);
    const formId = existing?.formId ?? crypto.randomUUID();

    if (!existing) {
      await call(`/form/meta/${formId}`, { method: 'POST', body: { formId, name } });
    }

    const versions = existing
      ? ((await call(`/form/${formId}`, { allow404: true }))?.result ?? [])
      : [];
    // Raten waere hier gefaehrlich: Faellt eine fehlende Angabe still auf 0 zurueck,
    // schreibt der naechste Aufruf eine Version, die es schon gibt.
    const minors = versions.map((v) => v.version?.minor);
    if (minors.some((minor) => typeof minor !== 'number')) {
      throw new Error(
        `Formular „${name}": Eine vorhandene Version hat keine Nummer. Bitte in der Konsole nachsehen.`,
      );
    }
    const next =
      versions.length === 0 ? { major: 1, minor: 0 } : { major: 1, minor: Math.max(...minors) + 1 };

    await call('/form', {
      method: 'POST',
      body: { formId, version: next, formData: readFileSync(file, 'utf8') },
    });

    console.log(`✓ Formular „${name}" v${next.major}.${next.minor}${existing ? '' : ' (neu angelegt)'}`);
    return formId;
  }

  return { base, call, ensureForm };
}
