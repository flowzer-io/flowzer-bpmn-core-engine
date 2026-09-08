# Explizite Entscheidungsaktionen in Aufgabenformularen

M2/M3-Teilpaket #216. Das additive Profil `flowzer.forms/4` ergänzt fachlich
benannte Human-Task-Aktionen, ohne den bisherigen generischen Abschluss für
Formulare ohne Aktionen zu brechen.

## Vertrag

```json
{
  "flowzer": {
    "contractVersion": 4,
    "actions": [
      {
        "id": "approve",
        "label": "Freigeben",
        "variant": "primary",
        "set": [{ "field": "decision", "value": "approved" }]
      },
      {
        "id": "reject",
        "label": "Ablehnen",
        "variant": "danger",
        "set": [{ "field": "decision", "value": "rejected" }]
      }
    ]
  },
  "components": [
    { "type": "textarea", "key": "comment", "label": "Begründung" },
    { "type": "hidden", "key": "decision" }
  ]
}
```

- `id` ist eine stabile, maximal 64 Zeichen lange ASCII-Kennung. Sie beginnt
  mit einem Buchstaben und darf danach Buchstaben, Zahlen, `_` und `-` enthalten.
- `label` ist Plaintext mit maximal 100 Zeichen.
- `variant` ist genau `primary`, `secondary` oder `danger`.
- `set` enthält 1–20 feste Feldbelegungen. Werte sind ausschließlich JSON-String,
  Zahl, Boolean oder `null`; Objekt- und Arraywerte sind nicht freigegeben.
- Ein Formular besitzt 1–20 Aktionen, jede ID ist eindeutig.
- Zielfelder müssen als beschreibbare, skalare Root-Felder deklariert sein. Read-only-
  Felder, benannte Berechnungen, Directory-Felder, Mehrfachwerte, Wiederholgruppen und
  bedingte Zielfelder sind gesperrt. Der feste Wert muss bereits beim Veröffentlichen
  Typ, Bereich, Auswahl und sonstige Feldregeln erfüllen.

Profil 4 ist additiv: Directory-Felder aus Profil 2 sowie Wiederholgruppen und
Plaintext-Hilfen aus Profil 3 bleiben verfügbar.

## Abschluss und Sicherheit

`POST /usertask` und der kompatible Adapter `POST /form/result` akzeptieren das
optionale Body-Feld `actionId`. Die gebundene Formularversion entscheidet:

1. Enthält sie Aktionen, ist `actionId` verpflichtend.
2. Der Server löst die ID ausschließlich im Deployment-Snapshot auf.
3. Die festen Feldwerte werden serverseitig in einen neuen Eingabescope eingesetzt.
4. Sendet der Browser für ein Aktionsfeld einen abweichenden Wert, wird der Abschluss
   mit `422` abgelehnt. Übereinstimmende Werte erweitern das Recht nicht.
5. Erst danach laufen dieselben Pflicht-, Typ-, Directory- und Geschäftsregeln wie
   bei jedem anderen Aufgabenabschluss.

Die ausführende Identität stammt weiterhin allein aus dem authentifizierten Kontext.
`actionId` ist Bestandteil des kanonischen Idempotenz-Hashes: Derselbe
`Idempotency-Key` mit einer anderen Entscheidung liefert `409` und führt keinen
zweiten Effekt aus.

Private Aufgabenentwürfe schließen aktionsbelegte Felder aus. Dadurch kann ein
veralteter oder manipulierter Browserwert nicht nach Wiederaufnahme eine spätere
Entscheidung blockieren.

## Konsole und Formularpflege

Der Form.io-Builder besitzt unterhalb der Feldpflege einen begrenzten Editor für
Aktions-ID, Beschriftung, Darstellung und typisierte feste Feldbelegungen. Beim
Speichern bindet er automatisch `contractVersion: 4`; die Veröffentlichung bleibt
serverautoritativ und weist unvollständige oder unpassende Konfigurationen zurück.

Die Aufgabenansicht zeigt bei Profil 4 die konfigurierten Aktionsknöpfe und entfernt
den generischen Knopf „Aufgabe abschließen“. Form.io prüft dabei auch Pflichtfelder,
die erst durch die gewählte Aktion fest belegt werden. Die Konsole sendet dennoch nur
die Action-ID und die echten Formulareingaben; der Server leitet feste Werte erneut
aus seinem Snapshot ab.

Formulare ohne Aktionen behalten den bisherigen generischen Abschluss und benötigen
keine `actionId`. Ein alter Client, der ein Profil-4-Aufgabenformular ohne Action-ID
abschließt, scheitert sicher mit `action.required`.

## Fehlercodes und Grenzen

Submission-Codes:

- `action.required` – keine Aktion gewählt
- `action.invalid` – ID gehört nicht zur gebundenen Formularversion
- `action.conflict` – Browserwert widerspricht der festen Belegung
- `action.not_allowed` – Aktionsvertrag auf einem nicht freigegebenen Formularweg

Compile-Codes beginnen mit `action.` und benennen nur die verletzte Vertragsregel,
niemals Labels, Feldwerte oder sonstige Formulardaten.

Im ersten Slice sind Aktionen ausschließlich für Human Tasks freigegeben. Ein
Startformular mit Aktionen wird bereits beim Deployment mit `action.start_form`
abgelehnt. Aktionen führen selbst keine externen Effekte aus; sie schreiben nur
deklarierte Prozesswerte. Benachrichtigungen, Konnektoren und KI-Werkzeuge bleiben
separate, ausdrücklich berechtigte Ausführungspfade.
