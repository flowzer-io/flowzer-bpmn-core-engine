# Serverseitiges Formular-Prüfprofil

M0/M2-Teilpaket #182 / PR #183, aufbauend auf der Formularbindung #180 / PR #181.

`flowzer.forms/1` ist eine **begrenzte**, serverseitig prüfbare Form.io-Teilmenge,
keine vollständige Form.io-Kompatibilität. Neue Deployments und Wiederaktivierungen
prüfen alle gebundenen Schemas vor einer Änderung der aktiven Version. Der Snapshot
trägt `ValidationProfile`; das Formular-DTO liefert `validationProfile` mit.
`flowzer.contractVersion: 1` im Schema ist optional; andere Versionen werden abgelehnt.

## Datenfluss und Rechte

- HTTP-Starts mit Startformular sowie `POST /usertask` und `POST /form/result`
  verwenden denselben Compiler/Validator innerhalb des Engine-Mutationszyklus.
  Bei Aufgaben erfolgen Identitäts- und Berechtigungsprüfung **vor** der Feldprüfung.
  Fremde Aufgaben liefern weiterhin `404`, keine Informationen über ihre Felder.
- Nur deklarierte Eingaben werden als neues Ergebnisobjekt übernommen. Unbekannte
  Felder, Objekte an Skalarfeldern und manipulierte Kontextwerte werden abgelehnt.
  `UserId` ist ausschließlich ein ignorierter historischer Transportwert; der Akteur
  kommt aus dem authentifizierten Kontext. Buttons/Inhaltsfelder sind keine Ausgaben.
- `disabled: true` oder `flowzer.access: "context"` bezeichnet nur lesbaren Kontext.
  Ein mitgesendeter Wert muss dem serverseitigen Kontext entsprechen; selbst dann
  wird er nicht ins Ergebnis übernommen. Gleichheit ist typstreng, nicht formatiert.
- Anzeige und Read-only-Prüfung verwenden lokale Taskvariablen, ergänzt aus dem
  nächsten Prozess-/Subprozessscope. Explizite Input-Mappings sperren diesen Fallback.
  Fehlende/mehrdeutige/zyklische Parent-Tokens ergeben keinen Kontext.
  Auch Operator-Formulare erhalten nur deklarierte Werte; vollständige Diagnosedaten
  gehören in die berechtigte Instanzansicht, nicht in eine Formular-Submission.
- Startformulare verlangen weiterhin ein `variables`-Objekt (sonst kompatibel `400`).
  Ein leeres Objekt ist kein Validierungs-Bypass. Technische Nachrichtenstarts ohne
  Startformular sind nicht Teil dieses Formularvertrags.

## Unterstützte Felder und Regeln

| Bereich | Prüfprofil 1 |
| --- | --- |
| Text | `textfield`, `textarea`, `email`, `url`, `phoneNumber`, `password`; String, Länge, Muster; URL nur HTTP(S), E-Mail ohne Anzeigenamen |
| Zahlen | `number`, `currency`; JSON-Zahl, Decimal-Bereich, `validate.min` / `max`; **keine** String-zu-Zahl-Konvertierung |
| Boolesch | `checkbox`; JSON-Boolean, erforderlich bedeutet `true` |
| Auswahl | `select` mit `dataSrc: "values"`, `radio`; typstrenge skalare statische Optionswerte |
| Zeit | `datetime`: ISO-Datum oder ISO-Zeitstempel; `time`: `HH:mm` / `HH:mm:ss` |
| Versteckt | `hidden`: skalarer String/Zahl/Boolean, nicht automatisch vertrauenswürdig |
| Mehrfach | `multiple: true`: Array skalarer Werte, `minSelectedCount` / `maxSelectedCount` |
| Layout | `panel`, `fieldset`, `columns`, `table`, `tabs`, `well`; flacher Ergebnisscope |
| Pflicht | `validate.required`; null, fehlend, Leer-/Whitespace-String und leeres Array gelten als leer |
| Sichtbarkeit | `conditional.when` / `eq` / `show`, einschließlich Layout-Vererbung; keine Zyklen oder berechneten Quellen |
| Kontext | `disabled` / `flowzer.access`; keine Ausgabezuweisung über Browserwerte |

Nicht-leere Werte in inaktiven Feldern werden abgelehnt. Bedingungen lesen nur
deklarierte Felder; Checkboxwerte werden für Form.io-kompatible `eq`-Strings verglichen.
Optionale Leerstrings werden zu null normalisiert, Arrays bleiben Arrays. Fachliche
Prozessausgaben werden anschließend wie bisher durch modellierte I/O-Mappings zugeordnet.

Deklarativer Datumsvergleich im Wurzelschema:

```json
{
  "flowzer": {
    "contractVersion": 1,
    "rules": [{ "kind": "dateOrder", "start": "von", "end": "bis", "allowEqual": true }]
  },
  "components": [
    { "type": "datetime", "key": "von", "validate": { "required": true } },
    { "type": "datetime", "key": "bis", "validate": { "required": true } }
  ]
}
```

`allowEqual` ist ohne Angabe false. Der Vergleich ersetzt keine Pflichtregeln und
keine fachliche Zeitzonen-/Arbeitstageberechnung.

## Benannte Berechnungen statt Formular-JavaScript

Die feste, versionierte Registry enthält zunächst nur `join.v1`:

```json
{ "type": "hidden", "key": "vorgang", "flowzer": {
  "calculation": { "name": "join.v1", "fields": ["mitarbeiter", "art", "von", "bis"] }
} }
```

Der Server verbindet 1–20 deklarierte skalare, nicht berechnete Quellen mit ` · `.
Leere Werte entfallen; Datum und Auswahlcode bleiben unverändert, ohne Übersetzung
oder Formatierung. Keine Scripts, URLs, Shell, Netzaufrufe oder verketteten Berechnungen.
Ein abweichender nicht-leerer Browserwert wird abgelehnt. Das Urlaubsbeispiel verwendet
diese Berechnung und `dateOrder` statt seiner bisherigen zwei Custom-Skripte.
Es gibt noch keine Live-Vorschau dieser Berechnung im Renderer.

## Grenzen und Fehlervertrag

Nicht unterstützt und bei Veröffentlichung abgelehnt: Container/Datagrids/Editgrids,
verschachtelte Objekt-/dotted-path-Werte, Verzeichnis-/Dateifelder, dynamische Quellen,
Custom-JavaScript, JSON-Logic, Input-Masks, Widget-Datumsgrenzen, unbekannte aktive
Validierungsregeln und zum Feldtyp unpassende Regeln. Weitere Geschäftsregeln müssen
vor Veröffentlichung explizit implementiert werden. Kein stiller JavaScript-Fallback.

Grenzen: Schema maximal 1 Mi Zeichen, JSON-Tiefe 32, 500 deklarierte Schlüssel,
500 statische Optionen/Arraywerte, 100 übergreifende Regeln, Text 131072 Zeichen.
Schlüssel sind ASCII-Buchstaben/Ziffern/Unterstrich, beginnen mit einem Buchstaben
und sind höchstens 128 Zeichen lang; reservierte Identitäts-/Prototypschlüssel entfallen.
Regex: höchstens 1024 Zeichen, vollständig verankert, .NET NonBacktracking mit
50-ms-Limit; kein Anspruch auf beliebige JavaScript-Regex-Kompatibilität.

Ungültige Submissions liefern `422 application/problem+json` mit `errors` als
Feld→Fehlercode-Arrays. Die kompatiblen Felder `successful: false` / `errorMessage`
bleiben enthalten. Keine eingesandten Werte im Fehlertext. Die Konsole übersetzt
bekannte Codes, zeigt Feldlabels und fokussiert die Fehlerübersicht, ohne das
Formular neu zu mounten. Die API bleibt auch ohne Browserprüfung verbindlich.

## Upgrade und offene Arbeit

Vor einem Upgrade bestehende Formulare inventarisieren und eine Testinstallation
mit laufenden Instanzen prüfen. Nicht unterstützte gebundene Altschemas werden auch
beim Abschluss abgelehnt; sie brauchen einen fachlich geprüften Migrationsweg.
Externe Altverweise ohne Snapshot werden weiterhin nicht auf heutiges `latest` geraten.
Dieser PR migriert nur das Beispiel, **keine Kundendaten oder produktiven Workflows**.

Noch offen: gemeinsame Client-/Server-Konformitätsvektoren (insbesondere der neuen
Flowzer-Regeln), erweiterte Komponenten, Verzeichniswahl, Entwürfe/Konflikte,
Formular-Veröffentlichungsoberfläche, vollständiges Skriptinventar und kontrollierte
Bestandsmigration. Auch Idempotenz, BFF und Mehrprozess-Transaktionsschutz sind nicht
Bestandteil dieses Slices. Tests ersetzen keine allgemeine Produktionsfreigabe.
