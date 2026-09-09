# Projektstatus: Flowzer BPMN Core Engine

**Stand:** 9. September 2026; Basis `212705a`. Die beschriebenen M0–M3-Slices bis
#218 liegen in noch nicht nach `main` gemergten, gestapelten Arbeitsständen.

## Einordnung

Flowzer ist eine eigenständige Open-Source-Workflow-Plattform in aktiver
Stabilisierung. Der modulare .NET-/React-Aufbau bleibt erhalten. Eine allgemeine
Produktionsfreigabe oder vollständige BPMN-2.0-Unterstützung ist damit nicht verbunden.
Die erste Produktstufe verwendet getrennte Installationen je Kunde.

Führend sind die [Produkt-Roadmap](PRODUCT-ROADMAP-2026-09.md) und #98.
Das [September-Review](REVIEW-2026-09.md) ist eine historische Bestandsaufnahme,
keine aktuelle Liste noch fehlender Funktionen.

## Bereits vorhandene Grundlagen

- Eine React-Konsole; die frühere Blazor-Oberfläche wurde entfernt.
- BFF-Implementierung für serverseitigen OIDC-Code-Flow, `HttpOnly`/`Secure`-Host-Cookies und CSRF; externe JWT-Bearer-Prüfung bleibt kompatibel. Der BFF-Slice ist noch ungemergt und nicht abgenommen.
- Aufgabenfilter anhand modellierter Personen und Gruppen; Workflow-Ordner mit
  Bearbeitungs-/Delegationsrechten. Diese ersetzen keine Instanz-Datenschutzrechte.
- PostgreSQL-Backend und dateibasierte Entwicklungsablage.
- Service-Task-Worker-Vertrag, Timer-Scheduler und Wiederanlaufpfade.
- Eingebettete/externe Aufgabenformulare, Startformulare und BPMN-Gliederungsansicht.
- Reproduzierbare .NET-/Frontend-CI, OpenAPI-Snapshot, Testzweckprüfung,
  Container-/Compose-Setup, Health-/Diagnose- und Telemetriegrundlagen.

## Aktuelles M0-Teilpaket – PR #177

Beide Abschlussrouten (`POST /usertask`, `POST /form/result`) verwenden denselben
Anwendungsfall. Er prüft Subscription, geladenes Token, Definition und Zuweisung
innerhalb des bestehenden serialisierten Storage-Zyklus. Fremde, fehlende oder
inkonsistente Aufgaben liefern einheitlich `404`. Der authentifizierte Akteur wird
separat am Token gespeichert; Formulardaten können ihn nicht ersetzen.

## Instanz-Datenschutz – PR #179 (aufbauend auf #177)

HTTP-Starts speichern den vertrauenswürdigen Initiator als `(Issuer, Subject)`.
Antragsteller und aktuell berechtigte Aufgabenbearbeiter sehen eine Vorgangsübersicht,
Operatoren die Diagnose. Listen, Details, Startantwort und alle technischen
Subscription-Routen verwenden diese Rechte. Bloße Modellierungsrechte gewähren
keinen Instanzzugriff. Aufgabenlisten geben nur deklarierte Formularwerte statt
vollständiger Tokenscopes aus. Die Konsole unterscheidet beide Ansichten und fordert
ohne `canInspect` keine Diagnosedaten an. Details: [Instanzrechte](INSTANCE-ACCESS.md).

Das ist **kein vollständiger M0-Abschluss**: Der BFF-Slice ist zwar in Arbeit,
aber noch nicht nach `main` gemergt oder integriert abgenommen. Ohne `Idempotency-Key`
liefert ein wiederholter Abschluss aus Kompatibilitätsgründen weiterhin `404`; mit dem
in PR #187 ergänzten Schlüssel greift die persistente Erfolgswiederholung.
Dateiablage bietet weiterhin keinen Rollback; die Sperre gilt nur innerhalb eines
API-Prozesses. Mehrprozessbetrieb ist dadurch nicht freigegeben.

## Formularbindung – PR #181 (aufbauend auf #179)

Externe und eingebettete Formulare erhalten beim Deployment einen festen Snapshot
an der Definitionsversion. Neue Fassungen und Umbenennungen verändern weder
Startformulare noch laufende oder später aktivierte Aufgaben. Fehlende/mehrdeutige
Referenzen werden vor der Aktivierung abgelehnt. Historische externe Referenzen
ohne belegten Stand werden bei der Auflösung nicht auf heutige Formulare geraten:
Sie benötigen eine ausdrücklich geprüfte Zuordnung. Keine produktive Migration.
Details: [Formularbindungen](FORM-DEPLOYMENT-BINDINGS.md).

## Formularprüfung – PR #183 (aufbauend auf #181)

Das begrenzte Profil `flowzer.forms/1` prüft Starts und beide Abschlussrouten
serverseitig. Deklarierte Typen/Pflichtwerte/Auswahl-/Datumsregeln sind verbindlich;
Read-only-Kontext und unbekannte Felder gelangen nicht ins Ergebnis. Nicht unterstützte
Regeln blockieren Veröffentlichung und Wiederaktivierung. Feldfehler erscheinen in
Konsole und API ohne Eingabeverlust. Das Urlaubsbeispiel nutzt deklarative Datumsregeln
und eine benannte serverseitige Zusammenfassung statt Custom-JavaScript.
Details und Kompatibilitätsgrenzen: [Prüfprofil](FORM-VALIDATION-PROFILE.md).

## Gemeinsame Formularvertragsvektoren – #208 / PR #209 (noch nicht gemergt)

Ein versionierter JSON-Katalog beschreibt Compile- und Submission-Fälle für
`flowzer.forms/1` und `/2`. Serverseitiger Compiler/Validator und eine begrenzte
Browser-Vorprüfung lesen dieselben Schemas, Eingaben und kanonischen Fehlercodes.
Directory-Auswahl und benannte Berechnungen sind ausdrücklich `server-authoritative`;
der Client erweitert weder Snapshotrechte noch Berechnungslogik. Der Slice verändert
keine gespeicherten Formulare oder laufenden Instanzen. Details:
[Prüfprofil](FORM-VALIDATION-PROFILE.md).

## Formularpflege – #210 / PR #211 (noch nicht gemergt)

Gemeinsame Autorenentwürfe besitzen eine Compare-and-swap-Revision und bleiben von
veröffentlichten Versionen getrennt. Vorschau, Speichern, Verwerfen und Publish sind
in der Konsole eigenständige Zustände. Publish prüft den Serververtrag, erzeugt unter
PostgreSQL atomar genau die Folgeversion und löscht den Entwurf; konkrete Versionen
sind insert-only. Revisionskonflikte erhalten lokale Eingaben und veröffentlichen
keinen inzwischen geänderten Stand. Details: [Formularpflege](FORM-AUTHORING.md).

## Formular-Kompatibilitätsinventar – #212 / PR #213 (noch nicht gemergt)

Ein modellierergeschützter Bericht prüft jede veröffentlichte Formularversion und den
aktuellen Autorenentwurf isoliert gegen den serverseitigen Vertrag. Schema- und
Scriptinhalte bleiben serverseitig; die API liefert nur stabile Codes und Referenzen.
Die Konsole markiert betroffene Formulare und bietet einen Migrationsfilter. Der
Bericht verändert keine Bestände und ersetzt keine fachlich geprüfte Migration. Details:
[Formular-Kompatibilitätsinventar](FORM-COMPATIBILITY-INVENTORY.md).

## Wiederholbare Formulargruppen – #214 / PR #215 (noch nicht gemergt)

`flowzer.forms/3` bindet Form.io-Datagrids als begrenzte Arrays deklarierter
Zeilenobjekte. Servervalidierung, private Entwürfe und Kontextprojektion verwenden
dieselbe Struktur- und Feldgrenze; Fehler tragen indexierte, wertefreie Pfade. Der
Builder bindet seine sichtbaren Anzahlgrenzen an die Flowzer-Policy, die Konsole zeigt
Zeile und Feldlabel. Profil-3-Hilfetexte sind begrenzter Plaintext. Details:
[Wiederholbare Formulargruppen](FORM-REPEAT-GROUPS.md).

## Entscheidungsaktionen – #216 / PR #217 (noch nicht gemergt)

`flowzer.forms/4` bindet fachlich benannte Human-Task-Aktionen an feste skalare
Belegungen deklarierter Formularfelder. Beide Abschlussrouten lösen ausschließlich
die stabile `actionId` im Deployment-Snapshot auf; fehlende, unbekannte oder
widersprüchliche Entscheidungen enden wertefrei mit `422`. Die Wahl gehört zum
Idempotenz-Hash. Die Konsole pflegt und rendert die Aktionen, Formulare ohne Aktionen
behalten den generischen Abschluss. Startformulare bleiben im ersten Slice gesperrt.
Details: [Entscheidungsaktionen](FORM-DECISION-ACTIONS.md).

## Hostneutrales TypeScript-SDK – #218 / PR #219 (noch nicht gemergt)

Das eigenständig baubare Paket `@flowzer/sdk` kapselt die generische Flowzer-HTTP-API
für Aufgabenliste, gebundene Formulare, private Entwürfe, Claim/Release/Assign/Delegate,
idempotenten Abschluss, feld- und aktionsgebundene Verzeichnissuche sowie
Vorgangsübersichten. Öffentliche DTOs werden aus dem versionierten OpenAPI-Snapshot
erzeugt; die CI prüft Drift, Paketbau, Tests, Abhängigkeiten und konkrete
Host-Anwendungsnamen im Produktcode.

Das SDK besitzt keine React- oder Laufzeitabhängigkeit und keinen globalen
Authentisierungszustand. Bearer-Token beziehungsweise BFF-CSRF-Werte kommen pro Aufruf
über diskriminierte Host-Callbacks; Benutzer-Header und still erzeugte
Idempotenzschlüssel gibt es nicht. Flowzer enthält dabei weder Abhängigkeit noch
Laufzeitwissen über eine konkrete konsumierende Fachanwendung. Optionale React-
Komponenten, Host-Adapter und eine reale Einbettungsabnahme bleiben Folgearbeiten.

## Hostneutrale React-Bausteine – #220 / PR #221 (noch nicht gemergt)

Das optionale Paket `@flowzer/react` setzt ausschließlich auf die öffentliche SDK-API
und stellt Hooks sowie Render-Prop-Controller für Aufgabenliste, Task-Arbeitsbereich
und Vorgangsstatus bereit. Installation und Sitzung bilden explizite, nicht geheime
Cache-Scopes. Mutationen werden nie automatisch wiederholt; ein Rechteentzug entfernt
bereits geladene Formular-/Entwurfsdaten aus der sichtbaren Projektion und dem Scope.

Form.io, CSS, Navigation und Fachobjekte bleiben beim Host. Ein neutraler
Formularadaptervertrag und eine unabhängig kompilierte Fixture belegen diese Grenze.
Die Flowzer-Konsole nutzt die Pakete noch nicht selbst; diese Migration und eine reale
Identity-/HTTPS-Einbettungsabnahme bleiben separat offen. Details:
[Hostneutrale Einbettung](HOST-INTEGRATION.md).

## Aufgabenidentität – PR #185 (aufbauend auf #183)

Fortschritt und Timer ersetzen wartende Aufgaben nicht länger durch neue IDs.
Subscriptions werden nach Tokenidentität aktualisiert; gespeicherte Zuweisungen
bleiben erhalten, nur erledigte/abgebrochene Aufgaben werden entfernt. Identische
Regressionen prüfen Dateiablage und PostgreSQL einschließlich neuer Engine nach
Persistierung. Mehrdeutige Bestände werden nicht automatisch zusammengeführt.
Details und Grenzen: [Aufgabenidentität](STABLE-TASK-IDENTITY.md).

## Persistente HTTP-Idempotenz – PR #187 (aufbauend auf #185)

Direkte Starts sowie beide Abschlussrouten akzeptieren einen an Operation, Ressource
und `(Issuer, Subject)` gebundenen `Idempotency-Key`. Identische Wiederholungen liefern
dieselbe Instanz beziehungsweise Erfolg; anderer Inhalt endet mit 409. PostgreSQL
reserviert den Hash atomar in derselben Transaktion. Details, Sieben-Tage-Aufbewahrung
und Datei-/Integrationsgrenzen: [HTTP-Idempotenz](IDEMPOTENCY.md).

## BFF-Slice #188 (noch nicht gemergt)

Der laufende Slice verlagert die Browser-Anmeldung in den API-seitigen,
vertraulichen OIDC-Code-Flow. Access-Tokens und das BFF-Client-Secret bleiben im
API-Prozess; die Konsole erhält nur die minimal projizierte Sitzung über
`HttpOnly`/`Secure` `__Host-`-Cookies. Schreibende Cookie-Anfragen benötigen
`X-Flowzer-CSRF` und gleichen Origin. Externe Bearer-Clients bleiben ohne
CSRF-Header kompatibel und ein fehlerhafter Bearer fällt nicht auf eine Cookie-
Sitzung zurück. Compose persistiert den getrennten Data-Protection-Keyring.

Das ist **kein vollständiger M0-Abschluss**: Der BFF-PR ist noch nicht nach
`main` gemergt, nicht integriert abgenommen und ersetzt keine offenen Betriebs-
und Recovery-Pakete.

## Verzeichnis-Slices #190, #192, #194, #196, #198 und #200 (noch nicht gemergt)

#190 / PR #191 synchronisiert Benutzer, Gruppenhierarchie und Mitgliedschaften lesend aus
Keycloak. Nur ein vollständig erfolgreicher Lauf ersetzt den atomaren lokalen Snapshot;
stabile lokale IDs und inaktive Historie bleiben erhalten. Überlappende paginierte IDs,
unvollständige Hierarchien und Teilfehler werden abgewiesen, ohne die aktive Generation
zu ersetzen. Operatorstatus und manueller Start geben keine Identitätsdaten aus.

#192 / PR #193 ergänzt `SubjectRef` für bekannte Benutzer und Gruppen sowie eine begrenzte
Suche. Sie ist an einen tatsächlich bearbeitbaren Workflow gebunden, bietet nur aktive
Identitäten an und liefert bei fremdem oder unbekanntem Kontext einheitlich `404`.
Formularfelder und historische Anzeige bleiben Folgepakete. #194 / PR #195 ergänzt
bereits die durchgängige Task-Zuweisung mit explizitem Text-/Directory-Vertrag, stabilen
Referenzen, Deployment-Prüfung und identischer Laufzeitberechtigung. #196 ergänzt die
workflowgebundene Auswahl im Diagramm und in der Gliederung: Freitext bleibt ausdrücklich
erhalten, bekannte Benutzer/Gruppen werden gesucht, als stabile IDs geschrieben und beim
erneuten Öffnen ohne allgemeine Verzeichnisliste aufgelöst.

#198 ergänzt `flowzer.forms/2` mit dem typisierten `flowzerSubject`-Feld. Auswahlpolicy,
aktive Filterreferenzen und Profil werden mit der Workflow-Version gebunden; Start und
beide Abschlussrouten prüfen stabile Referenzen erneut gegen den aktuellen Snapshot.
Startformular- und Task-Suche leiten ihre Grenzen ausschließlich aus dem gebundenen Feld ab.

#200 verwendet dieselbe stabile Auswahl für Ordnerberechtigungen. Jede Zuweisung entscheidet
explizit zwischen unverändertem Freitext und einer Directory-Referenz. Neue Referenzen werden
serverseitig auf Aktivität und Art geprüft; die Rechteauswertung verwendet ausschließlich das
exakte OIDC-Subject beziehungsweise aktive Mitgliedschaften. Deaktivierte Referenzen bleiben
mit ihrem gespeicherten Anzeigenamen sichtbar, gewähren aber keine Rechte mehr.

## Private Aufgabenentwürfe – #202 / PR #203 (noch nicht gemergt)

Der Bearbeitungsstand einer offenen User-Task kann serverseitig gespeichert, wieder
aufgenommen und verworfen werden. Er gehört der authentifizierten Person, nicht der
Kandidatengruppe; auch Operatoren sehen nur ihren eigenen Entwurf. Eine monotone Revision
meldet konkurrierende Tabs als 409, ohne den anderen Inhalt offenzulegen. Nur deklarierte,
beschreibbare Felder der gebundenen Formularversion werden übernommen; Pflicht- und
Geschäftsregeln bleiben dem Abschluss vorbehalten. Erfolgreicher Abschluss oder Abbruch
entfernt die Entwürfe der Aufgabe. PostgreSQL sichert Compare-and-swap und Lebenszyklus
atomar, die Entwicklungs-Dateiablage nur pro API-Prozess. Details:
[Private Aufgabenentwürfe](USER-TASK-DRAFTS.md).

## Human-Task-Lifecycle – #204 / PR #205 (noch nicht gemergt)

Aufgaben können revisionssicher übernommen, freigegeben, als Operator zugewiesen und an
einen aktiven Directory-Kandidaten delegiert werden. Nach einer Übernahme gelten Liste,
Formular, Entwurf, Abschluss und Instanzübersicht nur noch für den tatsächlichen Bearbeiter
oder den Betrieb. Gruppen bleiben Kandidatenmengen; tatsächliche Ziele sind Benutzer. Ein
Task-gebundener Suchendpunkt liefert je Aktion ausschließlich zulässige aktive Ziele.
PostgreSQL koppelt CAS-Zustand und Append-only-Audit atomar und bewahrt die Auditspur nach
dem Taskende; die Dateiablage bleibt auf einen Entwicklungsprozess begrenzt. Details:
[Human-Task-Lifecycle](HUMAN-TASK-LIFECYCLE.md).

## Human-Task-Fristen – #206 / PR #207 (noch nicht gemergt)

Der Fristenslice bindet `dueDate` und `followUpDate` beim ersten Auftreten einer
stabilen Task-ID an absolute UTC-Zeitpunkte. ISO-8601-Zeitpunkte mit Offset und
ISO-8601-Dauern werden unterstützt; lokale Zeitwerte ohne Offset und FEEL-Ausdrücke
bleiben ausdrücklich `unsupported` und erzeugen keine automatische Fälligkeit.

Der Deadline-Scheduler backfillt offene Altaufgaben, verarbeitet Follow-up-, Reminder-,
Due- und Eskalationsmeilensteine nachholbar und begrenzt pro Tick. PostgreSQL dedupliziert
Meldungen über einen eindeutigen Schlüssel und schützt den Fortschritt per Revision;
die Dateiablage bleibt ein Einzelprozess-Entwicklungsadapter. Der Feed unter
`/notifications` nutzt dieselbe objektbezogene Task-Autorisierung wie Liste, Formular,
Entwurf und Abschluss. Abschluss oder Abbruch entfernt offene Meldungen per
Fremdschlüssel. Details: [Human-Task-Fristen und Benachrichtigungen](HUMAN-TASK-DEADLINES.md).

Der Slice liefert keine E-Mail-/Push-/Chat-Zustellung, keine automatische Delegation
und keine BPMN-Eskalationspropagation. Der eigene Operations-Diagnoseblock für den
Deadline-Scheduler und eine produktionsnahe Aufbewahrungs-/Alerting-Abnahme bleiben offen.

## Verbleibende Risiken und Reihenfolge

1. **M0:** BFF-PR mergen und mit HTTPS-/Secret-Store-/Keyring-Restore-Übung
   abnehmen. Idempotenz externer Worker-/Connector-Effekte bleibt in den jeweiligen
   späteren Paketen. Rollen ausdrücklich konfigurieren; leere Fähigkeitsrollen
   bleiben im vorhandenen Vertrag permissiv.
2. **M1/M2:** Verzeichnissync und workflowgebundene stabile Identitätsreferenzen liegen
   gestapelt vor; Backend-Vertrag und Modelerauswahl für den expliziten
   Task-Zuweisungsmodus liegen in #194/#196.
   Das generische Formular-Auswahlfeld liegt in #198 vor, Ordnerreferenzen in #200 und
   private Aufgabenentwürfe in #202. Gemeinsame Client-/Server-Testvektoren, getrennte
   Entwurfs-/Vorschau-/Veröffentlichungszustände, Wiederholgruppen und Entscheidungsaktionen
   liegen in #208–#216. Wiederverwendbare Abschnitte, Anhänge und freigegebene dynamische
   Quellen bleiben offen. Legacy-Namen und kurze Gruppenbezeichnungen bleiben bis zur
   Migration mehrdeutig; historische externe Formularstände benötigen Klärung.
3. **M3/M4:** Aufgabenrevisionen, Übernahme/Delegation, private Entwürfe und der
   serverseitige Fristen-/Benachrichtigungskern liegen als gestapelte Topic-Branch-Slices
   vor (#202/#203, #204/#205, #206/#207). Merge/Abnahme, generisches Headless-SDK samt
   optionalen React-Komponenten, Modellvalidierung, externe Zustellung und vollständige
   Vorgangshistorie folgen. Flowzer erhält keine Abhängigkeit von einer konkreten Host-
   Anwendung; diese konsumiert die generischen Verträge ausschließlich von außen.
   Mobil-PR #153 nicht duplizieren.
4. **M5:** Begrenzte KI-Tasks mit geprüften Werkzeugen, Freigaben und Wiederaufnahme.
5. **M6 begleitend:** Call Activities/Fehlersemantik, explizite Expressions,
   PostgreSQL-Konfliktschutz, Recovery/Upgrade und Open-Source-Produktreife.

Vorgangsübersichten wurden auf Desktop/Mobil visuell geprüft; 29 Browser-Smokes
sichern Kernwege und Feldfehler. Der aktuelle Stand besteht lokal 111 Engine-,
657 API-/Storage- und 288 Konsolentests. Der vollständige UX-Audit und die erste
Produktabnahme aus der Roadmap stehen weiterhin aus. Details zum bestehenden Betrieb: [OPERATIONS.md](OPERATIONS.md).

## Arbeits- und Release-Modell

Topic-Branches und kleine, testgetriebene PRs gehen nach `main`. `release` wird
nur über einen eigenen PR aus `main` befüllt; dessen Push ist ein Produktivrelease.
Ein Feature-PR allein autorisiert kein Deployment. Keine direkten Writes auf
`main` oder `release` ohne ausdrückliche Freigabe.
