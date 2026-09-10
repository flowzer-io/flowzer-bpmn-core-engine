# Benutzer- und Gruppenauswahl in Formularen

Issue #198 erweitert den begrenzten Formularvertrag um `flowzer.forms/2` und den
Form.io-Feldtyp `flowzerSubject`. Profil 1 bleibt unverändert lesbar; eine automatische
Umwandlung bestehender Text-, E-Mail- oder Gruppenwerte findet nicht statt.

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

Der Browser übergibt keine Policy. Der Server löst immer den gebundenen Formularstand
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
