# Gemeinsame Formularbibliothek

Issue #291 führt die frühere Abschnittsbibliothek in den normalen Formularkatalog zurück:
**Ein Formular ist selbst eine Komponente.** Flowzer braucht daher weder einen zweiten
Objekttyp noch einen eigenen Navigationspunkt „Abschnitte“. Die Verträge bleiben
hostneutral; Flowzer enthält kein Wissen über eine konkrete konsumierende Anwendung.

## Katalog und Ordner

`FormMetadata` ist der stabile Katalogeintrag. `FormAuthoringDraft` hält den per
Compare-and-swap geschützten Arbeitsstand, `Form` eine unveränderliche veröffentlichte
Version. Ein optionaler `FolderId` ordnet das Formular einem hierarchischen `FormFolder`
zu. Ordner sind reine Organisation: Sie werden nicht in Form.io-Schemata, Deployments oder
Prozessinstanzen übernommen.

Die API ergänzt:

- `GET/POST /form/folders` – Ordner lesen beziehungsweise anlegen,
- `PUT/DELETE /form/folders/{folderId}` – umbenennen/verschieben beziehungsweise einen
  leeren Ordner löschen,
- `PUT /form/meta/{formId}/folder` – ein Formular verschieben,
- `GET /form/{formId}/versions` – datensparsame Liste konkreter Fassungen.

Der Server verhindert fehlende Eltern, Zyklen, doppelte Geschwisternamen sowie das Löschen
nicht leerer Ordner. Dieselben Wege stehen im hostneutralen TypeScript-SDK zur Verfügung.

## Formular als Komponente

Der Editor speichert ausschließlich eine konkret ausgewählte Referenz:

```json
{
  "type": "flowzerForm",
  "key": "applicant",
  "formId": "7d62bc50-2769-4b6f-aad5-f38366074784",
  "version": "0.1"
}
```

Beim Vorschau- oder Publish-Aufruf:

1. lädt der Server exakt `formId` plus `version`,
2. ersetzt die Referenz durch geklonte Komponenten,
3. übernimmt bewusst keine Root-Entscheidungsaktionen des Komponentenformulars,
4. expandiert importierte verschachtelte Referenzen mit Zyklusschutz,
5. prüft den vollständigen Formularvertrag einschließlich globaler Key-Kollisionen,
6. schreibt `flowzer.boundForms` mit Referenzschlüssel, Formular-/Versions-ID, Version und
   SHA-256 des tatsächlich gebundenen Inhalts,
7. speichert den vollständig expandierten Snapshot als unveränderliche Formularversion.

`latest`, Selbst-/Zyklusreferenzen, fehlende Fassungen und Marker mit eigenem Verhalten,
Datenquellen oder Kindern werden mit stabilen, wertefreien Fehlercodes abgelehnt. Vom
Browser behauptete Bindungsmetadaten werden verworfen. Laufzeit, Aufgaben und externe
Clients benötigen die Bibliothek nach der Veröffentlichung nicht mehr.

## Migration und Abwärtskompatibilität

PostgreSQL-Migration `016_form_library.sql` kopiert bestehende Metadaten, Entwürfe und
Versionen der früheren Abschnittsbibliothek IDs- und versionserhaltend in den Formularkatalog.
Die Dateiablage führt denselben idempotenten Schritt beim Öffnen aus. Die alten Datensätze
bleiben lesbar, damit bereits gespeicherte `flowzerSection`-Referenzen weiterhin exakt ihre
ursprüngliche Fassung expandieren können.

`/form-section` bleibt für bestehende Integrationen erhalten, arbeitet nach dem Upgrade aber
als Adapter auf denselben Formularbestand. Bestehende Lesezeichen auf `/form-sections`
öffnen den gemeinsamen Formularbereich. Neue Schemas und neue SDK-Integrationen verwenden
ausschließlich `flowzerForm` und die `/form`-API.

Dateianhänge, administrativ freigegebene dynamische Datenquellen und atomare Pakete aus BPMN
und Formularen bleiben eigenständige Ausbaupakete.
