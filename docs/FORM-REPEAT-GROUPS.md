# Wiederholbare Formulargruppen und Hilfetexte

M2-Teilpaket #214. Das additive Profil `flowzer.forms/3` erweitert die bisherigen
flachen Verträge um genau eine begrenzte Struktur: eine Form.io-`datagrid`-Komponente
als Array deklarierter Zeilenobjekte.

## Vertrag

```json
{
  "flowzer": { "contractVersion": 3 },
  "components": [
    {
      "type": "datagrid",
      "key": "positions",
      "description": "Fachliche Positionen.",
      "validate": { "required": true, "minLength": 1, "maxLength": 20 },
      "flowzer": {
        "helpText": "Bitte nur abrechenbare Positionen erfassen.",
        "repeat": { "minItems": 1, "maxItems": 20 }
      },
      "components": [
        { "type": "textfield", "key": "name", "validate": { "required": true } },
        { "type": "number", "key": "amount", "validate": { "min": 1 } }
      ]
    }
  ]
}
```

- `minItems` liegt zwischen 0 und `maxItems`; `maxItems` liegt zwischen 1 und 50.
  Ohne Angabe gelten 0 und 20.
- Der Form.io-Builder bindet seine `validate.minLength`-/`maxLength`-Werte beim
  Speichern an dieselbe Flowzer-Policy. Abweichende Doppelangaben blockieren Publish.
- Innerhalb einer Zeile sind nur die bereits unterstützten skalaren Feldtypen erlaubt.
  Weitere Arrays, Layouts, verschachtelte Datagrids, `flowzerSubject`, Read-only-Felder
  und benannte Berechnungen sind in diesem ersten Slice gesperrt.
- Eine Zeilenbedingung darf nur ein Feld derselben Zeile lesen. Bedingungen am
  Datagrid selbst lesen wie andere Top-Level-Komponenten den flachen Formularscope.
- `description` und `flowzer.helpText` sind in Profil 3 Plaintext bis 2.000 Zeichen.
  HTML, Steuerzeichen und Scriptregeln werden beim Veröffentlichen abgelehnt.

## Submission, Entwurf und Kontext

Start und beide Task-Abschlussrouten verwenden denselben Servervalidator. Unbekannte
Zeilenfelder, falsche Array-/Objektformen, Anzahlgrenzen und Feldregeln werden mit
indexierten, wertefreien Pfaden wie `positions[0].name` gemeldet. Nur deklarierte
Zeilenwerte werden als neue Prozessausgabe aufgebaut.

Private Aufgabenentwürfe erlauben weiterhin unvollständige fachliche Werte, prüfen aber
Struktur, deklarierte Felder, Skalarformen, Payload- und Zeilengrenzen vor Persistenz.
Die Leseprojektion entfernt nicht deklarierte Nachbarwerte aus jeder Zeile.

Die React-Konsole stellt indexierte Fehler als Gruppenname, Zeilennummer und Feldlabel
dar. Form.io rendert und bearbeitet das Datagrid; der Browser bleibt eine Vorprüfung,
der gebundene veröffentlichte Serververtrag ist autoritativ.

## Kompatibilität und Grenzen

Profile 1 und 2 bleiben unverändert. Ein Datagrid ohne explizites Profil 3 wird
abgelehnt, statt vorhandene Objektwerte still freizugeben. Veröffentlichte Formversionen
und Workflow-Bindings bleiben unveränderliche Snapshots; es gibt keine Datenbankmigration.

Formularübergreifend wiederverwendbare, versionierte Abschnitte und explizite
Entscheidungsaktionen sind eigene Folgeslices. Insbesondere wird ein Datagrid nicht als
allgemeine Freigabe für beliebige Form.io-Container, Editgrids oder verschachtelte Daten
interpretiert.
