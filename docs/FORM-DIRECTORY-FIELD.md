# Benutzer- und Gruppenauswahl in Formularen

Issue #198 erweitert den begrenzten Formularvertrag um `flowzer.forms/2` und den
Form.io-Feldtyp `flowzerSubject`. Profil 1 bleibt unverändert lesbar; eine automatische
Umwandlung bestehender Text-, E-Mail- oder Gruppenwerte findet nicht statt.

## Einfache Konfiguration im Formular-Editor (#303)

Nach dem Einfügen wählt man **Nur Benutzer**, **Nur Gruppen** oder **Benutzer und
Gruppen**. Mehrfachauswahl und die sichtbaren Profilinformationen sind direkt
konfigurierbar. Optionale Suchfelder begrenzen die Auswahl auf bestimmte Benutzer,
Mitglieder ausgewählter Gruppen oder bestimmte Gruppen – ohne UUID- oder JSON-Eingabe.
Untergruppen sind standardmäßig ausgeschlossen. Beim Abwählen einer Art werden ihre
nicht mehr passenden Filter entfernt; die Oberfläche weist darauf hin.

Unter **Erweitert** stehen technischer Schlüssel, Pflichtfeld und Mindest-/Höchstanzahl.
Die Oberfläche schreibt weiterhin den bestehenden Vertrag; alte Formulare benötigen
keine manuelle Migration.

Standardmäßig erscheinen Name und E-Mail-Adresse. Zusätzlich sind Benutzername,
Vorname und Nachname auswählbar. Diese optionalen Standardfelder stammen aus der
[Keycloak Admin REST API](https://www.keycloak.org/docs-api/latest/rest-api/index.html#_users).
Sie werden beim nächsten erfolgreichen vollständigen Verzeichnisabgleich ergänzt;
alte Snapshots bleiben lesbar. Fehlende Angaben werden ausgelassen. Freie
Keycloak-Custom-Attribute, Zugangsdaten und Passwörter werden nicht übernommen.
Gruppen behalten immer ihren Namen und vollständigen Pfad.

Die rein darstellende Einstellung am Feld ist beispielsweise
`flowzer.subjectDisplay: { "name": true, "email": true }`. Sie verändert weder die
Identitätsreferenz noch die Berechtigung und ist keine Zugriffskontrolle für Attribute.

## Vertrag

```json
{
  "flowzer": { "contractVersion": 2 },
  "components": [
    {
      "type": "flowzerSubject",
      "key": "vertretung",
      "label": "Vertretung",
      "multiple": false,
      "validate": { "required": true },
      "flowzer": {
        "subjectSelection": {
          "allowUsers": true,
          "allowGroups": false,
          "allowedUserIds": [],
          "userMemberOfGroupIds": [],
          "allowedGroupIds": [],
          "includeSubgroups": false
        }
      }
    }
  ]
}
```

- Ohne Policy sind aktive Benutzer erlaubt, Gruppen nicht.
- `allowedUserIds` und Benutzer aus `userMemberOfGroupIds` werden vereinigt.
- `allowedGroupIds` beschränkt auswählbare Gruppen, sofern `allowGroups` aktiv ist.
- `includeSubgroups` erweitert nur konfigurierte Gruppenbeschränkungen. Standard ist
  direkte Mitgliedschaft.
- Profil 2 erlaubt absichtlich keine neue Auswahl deaktivierter Identitäten.
- Alle Filter enthalten stabile lokale UUIDs. Doppelte, leere UUID-Einträge, falsch
  typisierte oder beim Deployment nicht aktive Referenzen machen den Vertrag ungültig;
  eine vollständig leere Filterliste bedeutet dagegen „keine Einschränkung“.

Ein Einzelwert wird ausschließlich so gespeichert:

```json
{ "kind": "user", "id": "20000000-0000-0000-0000-000000000001" }
```

Bei `multiple: true` ist der Wert ein Array solcher Referenzen. `kind` ist `user`
oder `group`; weitere Eigenschaften sind nicht zulässig. Eine Gruppe bleibt eine
Gruppenreferenz und wird nicht in ihre derzeitigen Mitglieder expandiert.

## Suche und Submission

Im Laufzeitkontext übergibt der Browser keine Policy. Der Server löst immer den gebundenen Formularstand
und das konkrete Feld auf:

- `GET /identity-directory/start-forms/{definitionId}/fields/{fieldKey}/subjects`
- `GET /identity-directory/user-tasks/{taskId}/fields/{fieldKey}/subjects`

Beide Endpunkte nehmen nur `query`, `kind` und `limit` an. Bei Aufgaben wird zuerst
die objektbezogene Aufgabenberechtigung geprüft; fremde Aufgabe, fehlender Workflow
und unbekanntes Feld antworten einheitlich mit `404`. Fehlt ein erfolgreich publizierter
Directory-Snapshot, antwortet die Suche mit `503`.

Start und beide Aufgaben-Abschlussrouten prüfen die eingesandte Referenz erneut gegen
den dann aktiven Snapshot. Eine Deaktivierung zwischen Suche und Absenden führt daher
zu `422`, ohne die Instanz zu verändern. Für Suche, Auflösung und Submission gilt
derselbe Policykern.

Bereits persistierte Werte werden getrennt und exakt im Batch aufgelöst:

- `POST /identity-directory/start-forms/{definitionId}/fields/{fieldKey}/subjects/resolve`
- `POST /identity-directory/user-tasks/{taskId}/fields/{fieldKey}/subjects/resolve`

Die Auflösung ist kein freier UUID-Lookup. Das Startformular darf nur veröffentlichte
explizite Filterreferenzen beschriften; beim Aufgabenformular kommen tatsächlich
persistierte Werte des aktiven Task-Kontexts hinzu. Details und Lifecycle-Grenzen stehen
unter [Historische Identitätsreferenzen](HISTORICAL-IDENTITY-RESOLUTION.md).

## Veröffentlichung und Historie

Beim Workflow-Deployment werden Formularinhalt, Profil und Filterreferenzen fest an
die Definitionsversion gebunden. Ohne erfolgreichen Snapshot oder mit inaktiven
Filterreferenzen wird die neue Version nicht aktiv. Laufende Instanzen behalten ihren
Formularstand; die Zulässigkeit einer neuen Auswahl wird trotzdem gegen den aktuellen
Identitätsstatus geprüft. Historische/inaktive Referenzen dürfen zur Erklärung bereits
gespeicherter Vorgänge angezeigt, aber nicht neu eingereicht werden.

Der Form.io-Builder und Renderer sind reine Bedienoberflächen. Die verbindliche
Entscheidung trifft stets die API; ein eigener Client oder manipulierter Browser hat
keinen schwächeren Vertrag.

## Interaktive Autorenvorschau ohne Instanz

Modellierende können bereits ungespeicherte Felder und Filter im Editor sowie auf
„Vorschau“ ausprobieren. Testeingaben werden nicht gespeichert und starten keinen
Workflow. Dafür existieren getrennte, ausschließlich mit der Modeler-Policy
berechtigte Endpunkte für ein vorhandenes Bibliotheksformular:

- `POST /identity-directory/authoring-forms/{formId}/subjects/search`
- `POST /identity-directory/authoring-forms/{formId}/subjects/resolve`

Ohne `formData` und `fieldKey` dient die Suche nur der Filterkonfiguration. Mit beiden
Werten wird der lokale Formularstand innerhalb der üblichen Größen-/Tiefengrenzen
geprüft und derselbe Feld-Policykern wie zur Laufzeit angewendet. Unbekannte Formulare
oder Felder ergeben `404`, fehlende Verzeichnissnapshots `503`. Resolve akzeptiert
höchstens 50 eindeutige Referenzen und liefert keine ausgeschlossenen oder inaktiven
Identitäten. Es entsteht weder ein Autorenentwurf noch eine Formularversion oder
Prozessinstanz. Laufzeit-/Aufgaben-Clients erhalten dadurch keine zusätzlichen Rechte.
