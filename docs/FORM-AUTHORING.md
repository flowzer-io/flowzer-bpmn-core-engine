# Formularpflege: Entwurf, Vorschau und Veröffentlichung

M2-Teilpaket #210. Der Vertrag trennt den veränderlichen Arbeitsstand von den
unveränderlichen Versionen im Formularbestand.

## Zustände

- `POST /form/meta/{formId}` legt weiterhin den Katalogeintrag an. Ein neues Formular
  ist danach noch nicht deploybar.
- `GET /form/{formId}/draft` liefert Modellierenden entweder den vorhandenen Entwurf
  oder Revision `0` mit der neuesten veröffentlichten Version als Bearbeitungsbasis.
- `PUT /form/{formId}/draft` speichert das vollständige JSON-Schema nur, wenn
  `expectedRevision` dem Serverstand entspricht. Ein Entwurf darf noch nicht vom
  gesamten Veröffentlichungsprofil unterstützt werden; er muss aber ein JSON-Objekt
  innerhalb der Größen-/Tiefengrenze sein.
- `DELETE /form/{formId}/draft?expectedRevision=…` verwirft nur genau den erwarteten
  Stand.
- `POST /form/{formId}/publish` prüft genau die erwartete Draft-Revision mit dem
  serverseitigen Formularvertrag. Nur bei Erfolg entsteht die nächste Version und
  der Entwurf verschwindet.

Ein Revisionskonflikt ist `409 application/problem+json` mit dem stabilen Code
`form_draft.revision_conflict`, `expectedRevision` und `currentRevision`. Der
konkurrierende Formularinhalt wird nicht in der Fehlerantwort offengelegt. Ein
nicht veröffentlichbarer Vertrag liefert `422 application/problem+json`; der
Entwurf bleibt erhalten.

## Unveränderlichkeit und Bindung

`IFormStorage.SaveForm` ist jetzt insert-only. Eine vorhandene konkrete Form-ID oder
dieselbe Versionsnummer unter derselben `formId` wird nicht überschrieben. Der
kompatible Altendpunkt `POST /form` bleibt für bestehende API-Clients verfügbar,
führt aber vor dem direkten Veröffentlichen dieselbe Vertragsprüfung aus.

Workflow-Deployments speichern weiterhin den konkreten Formular-Snapshot. Eine neue
Formularveröffentlichung ändert daher weder laufende Instanzen noch bereits deployte
Workflow-Versionen; erst ein neues Workflow-Deployment bindet sie.

## Persistenz und Konkurrenz

Migration `009_form_authoring_drafts.sql` legt einen Entwurf je Formular mit Revision,
Zeitpunkt und FK-Kaskade an. PostgreSQL sperrt beim Publish den Metadatensatz und den
Entwurf, bestimmt die Folgeversion und schreibt Version sowie Draft-Löschung in einer
Transaktion. Konkurrenztests verwenden getrennte Sessions.

Die Dateiablage nutzt einen prozesslokalen Form-Lock und atomare Einzeldateien. Sie
kann das Schreiben der Versionsdatei und das Löschen der Draftdatei nicht als
gemeinsamen Rollback garantieren und bleibt deshalb ausdrücklich Entwicklung und
Einzelprozessbetrieb vorbehalten.

## Oberfläche

Die React-Konsole zeigt „Noch nicht veröffentlicht“, die veröffentlichte Basis oder
die Draft-Revision. „Entwurf speichern“ und „Veröffentlichen“ sind getrennte Aktionen;
die Vorschau verwendet auch ungespeicherte lokale Änderungen. Bei `409` bleibt die
lokale Fassung stehen, bis die modellierende Person bewusst den Serverstand lädt.
Verwerfen und Veröffentlichen sind bestätigt und an die sichtbare Revision gebunden.
Nicht-Modellierende sehen ausschließlich veröffentlichte Fassungen.

## Grenzen

- Es gibt zunächst einen gemeinsamen Autorenentwurf je Formular, keine Feld-Merges
  oder gleichzeitige Echtzeit-Kollaboration.
- Der Browser-Builder darf breitere Form.io-Elemente erzeugen; verbindlich ist erst
  die serverseitige Prüfung beim Publish.
- Der vorhandene Direkt-Publish-Endpunkt ist ein Kompatibilitätsadapter und soll in
  neu generierten Clients nicht mehr als Autoren-Workflow angeboten werden.
