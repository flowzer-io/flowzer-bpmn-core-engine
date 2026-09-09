# Versionierte wiederverwendbare Formularabschnitte

M2-Teilpaket #230. Flowzer verwaltet eine eigene, hostneutrale Bibliothek
deklarativer Form.io-Abschnitte. Eine konsumierende Anwendung kann diese API und die
öffentlichen Pakete verwenden; Flowzer enthält weder Abhängigkeiten noch Fachwissen über
eine konkrete Host-Anwendung.

## Daten- und Versionsmodell

Ein Abschnitt besteht aus drei getrennten Zuständen:

- `FormSectionMetadata` ist der stabile Katalogeintrag mit serverseitig erzeugter ID
  und einem änderbaren Namen.
- `FormSectionAuthoringDraft` ist der gemeinsame, per Compare-and-swap-Revision
  geschützte Autorenentwurf.
- `FormSectionVersion` ist eine unveränderliche, append-only veröffentlichte Fassung.

Die Dateiablage serialisiert Änderungen innerhalb eines API-Prozesses und bleibt ein
Entwicklungsadapter. PostgreSQL-Migration `011_form_sections.sql` legt getrennte Tabellen
für Metadaten, Entwürfe und Versionen an. Dort werden Folgeversion, Insert und
Entwurfsentfernung in derselben Transaktion ausgeführt. Veröffentlichte Fassungen besitzen
bewusst keinen Update- oder Delete-Weg.

## Öffentliche Modellierungs-API

Alle Routen unter `/form-section` verlangen die Modellierer-Policy:

- `GET/POST /form-section` – Katalog lesen beziehungsweise Eintrag anlegen
- `GET/PUT /form-section/{sectionId}` – Metadaten lesen beziehungsweise umbenennen
- `GET /form-section/{sectionId}/versions` – verfügbare konkrete Versionen
- `GET /form-section/{sectionId}/versions/{major.minor}` – unveränderliche Fassung
- `GET/PUT/DELETE /form-section/{sectionId}/draft` – Entwurf mit Revision
- `POST /form-section/{sectionId}/publish` – erwarteten Entwurf veröffentlichen

Revisionskonflikte antworten mit `409` und dem stabilen Code
`form_section_draft.revision_conflict`. Der Vertrag enthält nur erwartete und aktuelle
Revision, niemals den konkurrierenden Inhalt. Unsichere oder nicht unterstützte
Abschnitte enden beim Publish mit `422`.

## Bewusst begrenzter Abschnittsvertrag

`FormSectionCompiler` verwendet dieselbe serverseitige Feld-, Pflicht-, Bereichs-,
Bedingungs-, Directory- und Plaintext-Prüfung wie ein vollständiges Formular. Im ersten
Profil sind zusätzlich gesperrt:

- beliebiges JavaScript und dynamische Datenquellen,
- verschachtelte `flowzerSection`-Referenzen,
- Wiederholgruppen innerhalb eines Abschnitts,
- Root-Regeln und Entscheidungsaktionen,
- aktive Buttons, HTML- oder Content-Komponenten.

Damit bleiben Auflösung, Vorschau und resultierender Variablenscope endlich und
nachvollziehbar. Diese Grenzen werden nicht durch clientseitige Filter ersetzt.

## Bindung an ein Formular

Der Formularentwurf speichert eine ausschließlich konkrete Referenz:

```json
{
  "type": "flowzerSection",
  "key": "applicant",
  "sectionId": "7d62bc50-2769-4b6f-aad5-f38366074784",
  "version": "0.1"
}
```

`latest`, fehlende Versionen und frei eingegebene IDs sind in der Konsole nicht
auswählbar und werden serverseitig abgelehnt. Beim Formular-Publish:

1. lädt der Server exakt `sectionId` plus `version`,
2. prüft die gespeicherte Fassung erneut,
3. ersetzt die Referenz durch geklonte Abschnittskomponenten,
4. prüft das vollständige Formular einschließlich Key-Kollisionen,
5. schreibt `flowzer.boundSections` mit Referenzschlüssel, Abschnitts-/Versions-ID,
   Version und SHA-256 des tatsächlich gebundenen Inhalts,
6. speichert den vollständig expandierten Snapshot atomar als Formularversion.

Vom Browser behauptete Bindungsmetadaten werden verworfen. Laufzeit, Aufgaben und externe
Clients benötigen die Abschnittsbibliothek nicht: Sie arbeiten ausschließlich mit dem
vollständigen veröffentlichten Formularsnapshot. Eine neue Abschnittsversion ändert daher
weder alte Formularversionen noch laufende Instanzen.

## Noch nicht enthalten

Verschachtelte Abschnitte, Abschnitts-Datagrids, automatische Aktualisierung vorhandener
Formulare, Dateianhänge und administrativ freigegebene dynamische Datenquellen bleiben
eigene Ausbaupakete. Es gibt keinen Script- oder `latest`-Fallback.
