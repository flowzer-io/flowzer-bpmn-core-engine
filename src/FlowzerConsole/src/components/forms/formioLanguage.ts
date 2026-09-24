import { browserFormLanguage, type FormLanguage } from '@/lib/locale';

/**
 * Deutsche Renderer-Texte, die Form.io nicht selbst mitbringt.
 *
 * Form.io 5 liefert eine deutsche Übersetzung seiner Prüfmeldungen mit
 * (`@formio/js/lib/cjs/translations/de.js`): `required`, `minLength`, `maxLength`, `pattern`,
 * `invalid_email`, `invalid_date`, `min`, `max`, `minDate`, `maxDate` und einige mehr. Sie greift
 * allein über `language: 'de'`; eine eigene Fassung hier wäre nur eine zweite Quelle für
 * dieselben Sätze.
 *
 * Ergänzt werden nur die häufigsten sichtbaren Texte, die dort fehlen: die Schaltflächen von
 * Tabellenfeldern und die Hinweise der Auswahlliste. Alles Weitere bleibt englisch — etwa
 * Meldungen rund um Datei-Upload, Unterschrift oder Bearbeitungstabellen.
 */
export const FORM_RENDERER_TEXTS_DE: Readonly<Record<string, string>> = {
  addAnother: 'Weiteren Eintrag hinzufügen',
  remove: 'Entfernen',
  loading: 'Wird geladen',
  noResultsFound: 'Keine Treffer',
  noChoices: 'Keine Auswahl vorhanden',
  typeToSearch: 'Zum Suchen tippen',
};

/**
 * Sprachoptionen für `Formio.createForm`.
 *
 * `language` steuert zweierlei: die Texte des Renderers und die Sprache des Kalenders. Form.io
 * reicht sie als `locale` an flatpickr weiter und lädt die passende Übersetzung von demselben
 * CDN, von dem es flatpickr selbst holt — deutsche Wochentage und Monate, Wochenbeginn Montag.
 * Das Datumsformat bestimmt weiterhin das Feld (`format`), nicht die Sprache.
 */
export function formioLanguageOptions(language: FormLanguage = browserFormLanguage()) {
  return {
    language,
    i18n: { de: { ...FORM_RENDERER_TEXTS_DE } },
  };
}
