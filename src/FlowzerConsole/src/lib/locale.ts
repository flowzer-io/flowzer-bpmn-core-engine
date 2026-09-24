/** Sprachen, in denen die Formulare (Form.io und sein Kalender flatpickr) ihre Texte zeigen. */
export type FormLanguage = 'de' | 'en';

/** Die Konsole ist deutschsprachig — ohne passende Browsersprache bleibt es bei Deutsch. */
export const DEFAULT_FORM_LANGUAGE: FormLanguage = 'de';

const SUPPORTED_FORM_LANGUAGES: readonly FormLanguage[] = ['de', 'en'];

function isFormLanguage(value: string): value is FormLanguage {
  return (SUPPORTED_FORM_LANGUAGES as readonly string[]).includes(value);
}

/**
 * Wählt aus den bevorzugten Sprachen die erste, die die Formulare beherrschen.
 *
 * Maßgeblich ist nur die Hauptsprache: `de-AT` zählt als Deutsch, `en-GB` als Englisch. Die
 * Liste wird der Reihe nach durchsucht wie bei der Sprachwahl des Browsers — `fr, en` ergibt
 * also Englisch. Findet sich keine unterstützte Sprache, gilt Deutsch.
 */
export function resolveFormLanguage(preferred: readonly (string | null | undefined)[]): FormLanguage {
  for (const tag of preferred) {
    const primary = tag?.trim().toLowerCase().split(/[-_]/)[0];
    if (primary && isFormLanguage(primary)) return primary;
  }
  return DEFAULT_FORM_LANGUAGE;
}

/**
 * Die Formularsprache des laufenden Browsers, aus `navigator.languages` bzw. `navigator.language`.
 * `null` steht für eine Umgebung ohne `navigator`.
 */
export function browserFormLanguage(
  nav: Pick<Navigator, 'languages' | 'language'> | null = globalThis.navigator ?? null,
): FormLanguage {
  if (!nav) return DEFAULT_FORM_LANGUAGE;
  const preferred = nav.languages && nav.languages.length > 0 ? nav.languages : [nav.language];
  return resolveFormLanguage(preferred);
}
