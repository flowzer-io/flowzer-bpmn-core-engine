# Formularpflege: Entwurf, Vorschau und Veröffentlichung

M2-Teilpaket #210 / PR #211. Der Vertrag trennt den veränderlichen Arbeitsstand von den
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

Altbestände und der aktuelle Entwurf werden zusätzlich durch das
[Formular-Kompatibilitätsinventar](FORM-COMPATIBILITY-INVENTORY.md) geprüft. Die Anzeige
ist datensparsam und ersetzt weder die fachliche Migration noch die ausdrückliche
Veröffentlichung.

## Grenzen

- Es gibt zunächst einen gemeinsamen Autorenentwurf je Formular, keine Feld-Merges
  oder gleichzeitige Echtzeit-Kollaboration.
- Der Browser-Builder darf breitere Form.io-Elemente erzeugen; verbindlich ist erst
  die serverseitige Prüfung beim Publish.
- Der vorhandene Direkt-Publish-Endpunkt ist ein Kompatibilitätsadapter und soll in
  neu generierten Clients nicht mehr als Autoren-Workflow angeboten werden.

## Regressionen des Produktionseditors (#290)

- Eigene Form.io-Komponenten liefern einen `tabs`-Knoten im Edit-Dialog. Dies ist
  auch bei nur einer Einstellungsseite erforderlich: `WebformBuilder.hasEditTabs`
  greift darauf beim Einfügen und erneuten Öffnen zu.
- Builder und Vorschau besitzen je asynchroner Aufbau-Generation einen eigenen
  DOM-Host. Ein verspätet fertiggestellter alter Aufbau darf nur seinen alten Host
  zerstören, nicht das inzwischen geöffnete Formular.
- Die allgemeine Autorenvorschau ist kein freigegebener Verzeichnis-Suchkontext.
  Benutzer-/Gruppenfelder zeigen dort einen Hinweis. Im gebundenen Startformular
  wird der bestehende QueryClient in den separaten React-Root weitergereicht;
  Aufgaben verwenden weiterhin den gebundenen Directory-Adapter. Es entsteht kein
  eigener Cache und keine zusätzliche Verzeichnisberechtigung.
- Form.io-Zahlen-/Währungsfelder enthalten standardmäßig `validate.step="any"`.
  Der Server akzeptiert exakt diesen neutralen Default; konkrete Schrittweiten,
  Integerregeln und unbekannte aktive Validierungen bleiben ohne implementierten
  Vertrag abgelehnt. Gespeicherte Formularversionen werden nicht verändert.

Der Produktions-Smoke in `tests/ui-smoke/production-tests/form-builder.spec.js`
verwendet den tatsächlichen ausgelieferten Form.io-Builder mit synthetischen
API-Antworten. Er ersetzt keine Abnahme einer realen Verzeichnissynchronisierung.
Die geplante Zusammenführung von Formularen, Ordnern und Abschnittsbibliothek ist
als getrenntes Migrationspaket [#291](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/291)
erfasst; die bestehende Abschnittsablage wird durch diesen Hotfix nicht migriert.
