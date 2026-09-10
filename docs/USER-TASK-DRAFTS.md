# Private Aufgabenentwürfe

**Stand:** 8. September 2026 · **Slice:** #202 / PR #203

Flowzer speichert den unfertigen Eingabestand einer offenen User-Task serverseitig.
Der Entwurf ist kein Prozesszustand und ersetzt nicht die vollständige Prüfung beim
Aufgabenabschluss.

## Öffentlicher Vertrag

Die stabile `UserTaskSubscription.Id` adressiert Aufgabe und Entwurf:

| Methode | Pfad | Bedeutung |
| --- | --- | --- |
| `GET` | `/usertask/{taskId}/draft` | Eigener Entwurf; Revision `0` und `{}` bedeuten „noch nicht gespeichert“ |
| `PUT` | `/usertask/{taskId}/draft` | Vollständigen Stand mit `expectedRevision` und optionaler `expectedTaskRevision` speichern |
| `DELETE` | `/usertask/{taskId}/draft?expectedRevision=…&expectedTaskRevision=…` | Eigenen Stand bei passender Revision verwerfen |

Ein erfolgreicher Schreibzugriff erhöht die Revision monoton. Hat ein anderer Tab
zwischenzeitlich gespeichert oder gelöscht, antwortet die API mit `409
application/problem+json`, Code `task_draft.revision_conflict` sowie erwarteter und
aktueller Revision. Der konkurrierende Inhalt wird nicht offengelegt.

Unbekannte, fremde, abgeschlossene und inkonsistent gebundene Aufgaben liefern für alle
drei Wege denselben `404`-Vertrag. Die Konsole behält bei einem Konflikt die lokalen
Eingaben und lädt den Serverstand nur nach einer ausdrücklichen Benutzeraktion.

## Rechte und Datenbegrenzung

- Eigentümer ist ausschließlich die authentifizierte `(Issuer, Subject)`-Identität.
  Der daraus abgeleitete Hash und interne Benutzerwerte werden nicht vom Request
  übernommen und nicht an den Browser ausgegeben.
- Die Aufgabenberechtigung wird bei jedem Zugriff erneut gegen Subscription, tatsächlich
  aktives Instanztoken, Definition und aktuelle Text-/Directory-Zuweisung geprüft.
- Operatoren dürfen eine Aufgabe bearbeiten, sehen aber nicht den privaten Entwurf einer
  anderen Person; sie besitzen gegebenenfalls einen eigenen Entwurf derselben Aufgabe.
- Gespeichert werden nur im gebundenen Formular deklarierte, beschreibbare Felder.
  Unbekannte und manipulierte Read-only-Felder werden mit feldbezogenen `422`-Fehlern
  abgewiesen. Benannte Serverberechnungen werden nicht als Eingabe übernommen.
- Pflichtwerte und Geschäftsregeln dürfen im Entwurf noch unvollständig sein und werden
  unverändert erst beim Abschluss verbindlich geprüft.
- JSON-Tiefe, Sammlungsform und Nutzlast sind begrenzt. Mehr als 256 KiB führen zu `413`
  mit `task_draft.payload_too_large`; der abgelehnte Inhalt wird nicht gespiegelt.

## Lebenszyklus und Persistenz

Der Entwurf ist zusätzlich an Token, Prozessinstanz und unveränderliche
Definitions-/Formularfassung gebunden. Erfolgreicher Abschluss, Instanzabbruch und sonstige
Entfernung der User-Task beseitigen alle zugehörigen privaten Entwürfe. Ein fachlich
abgewiesener Abschluss lässt sie bestehen.

PostgreSQL ist der Betriebspfad: Ein atomarer Compare-and-swap verhindert verlorene
Updates zwischen API-Prozessen; ein Fremdschlüssel mit `ON DELETE CASCADE` koppelt den
Lebenszyklus an die Aufgaben-Subscription. Konkurrenz und Kaskade werden gegen eine echte
PostgreSQL-Instanz getestet.

Die Dateiablage serialisiert Revisionen nur pro Schlüssel innerhalb eines API-Prozesses.
Sie dient Entwicklung und Einzelprozess-Demos, besitzt weder globale Transaktionen noch
eine Mehrprozessfreigabe.

## Bewusste Grenzen

Dieser Slice enthält keine gemeinsamen Entwürfe, automatische Speicherung, Anhänge,
Kommentare oder Entwurfshistorie. Claim, Release, Zuweisung und Delegation sind in #204 / PR #205
umgesetzt und binden Schreibzugriffe optional an `expectedTaskRevision`. Das spätere Headless-SDK
verwendet denselben HTTP-Vertrag; TickyTask erhält keinen direkten Datenbankzugriff.
