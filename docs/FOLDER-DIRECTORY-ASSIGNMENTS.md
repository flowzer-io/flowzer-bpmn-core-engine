# Ordnerzuweisungen: Freitext und Directory

Issue #200 ergänzt Ordnerrechte additiv um stabile Benutzer- und Gruppenreferenzen. Bestehende
Zuweisungen ohne `referenceMode` bleiben `text`; es gibt keine automatische Migration anhand
von Namen oder E-Mail-Adressen.

## Öffentlicher Vertrag

Freitext bleibt unverändert:

```json
{
  "referenceMode": "text",
  "subjectKind": "group",
  "subject": "/abteilungen/einkauf",
  "role": "steward"
}
```

Eine bekannte Identität wird ausdrücklich als Directory-Referenz gesendet:

```json
{
  "referenceMode": "directory",
  "subjectKind": "user",
  "subject": "20000000-0000-0000-0000-000000000001",
  "subjectRef": {
    "kind": "user",
    "id": "20000000-0000-0000-0000-000000000001"
  },
  "role": "editor"
}
```

`subjectKind`, `subject` und `subjectRef` müssen im Directory-Modus dieselbe Identität
beschreiben. Diese Redundanz erhält den bisherigen DTO-Vertrag, ohne widersprüchliche Werte
zuzulassen. Der Server übernimmt `displayName` nicht als Berechtigungsnachweis, sondern setzt
ihn aus dem aktiven Snapshot.

## Rechte und Suche

Der Textmodus nutzt weiterhin die dokumentierte Legacy-Auswertung für Benutzerkennungen und
Gruppenclaims. Der Directory-Modus löst die angemeldete Person dagegen ausschließlich über
das exakte Paar `(Issuer, Subject)` im aktiven Snapshot auf. Benutzerrechte vergleichen die
stabile lokale Benutzer-ID; Gruppenrechte verlangen eine aktive direkte Mitgliedschaft und
eine aktive Gruppe. Anzeigenamen, E-Mail-Adressen und gleichlautende Claimgruppen sind kein
Fallback.

Die Oberfläche sucht über
`GET /identity-directory/folders/{folderId}/subjects`. Der Endpunkt prüft zuerst, ob die
aufrufende Person die Zuweisungen dieses Ordners verwalten darf. Fremde und unbekannte Ordner
antworten einheitlich mit `404`; ohne erfolgreich publizierten Snapshot folgt `503`.

Deaktivierte oder gelöschte Referenzen bleiben mit der beim Speichern gesicherten
Anzeigeprojektion sichtbar und entfernbar, gewähren aber keine Rechte und können nicht neu
ausgewählt werden. Datei- und PostgreSQL-Ablage speichern den additiven Modus im bestehenden
Ordnerdokument; eine Schemaänderung ist nicht erforderlich.
