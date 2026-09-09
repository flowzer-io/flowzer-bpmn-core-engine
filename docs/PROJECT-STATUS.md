# Projektstatus: Flowzer BPMN Core Engine

**Stand:** 9. September 2026; Basis `212705a`. Die beschriebenen Slices bis PR #247
liegen in noch nicht nach `main` gemergten, gestapelten Arbeitsständen.

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

## CodeQL- und Storage-Härtung – #222 / PR #223 (noch nicht gemergt)

Der Slice beseitigt die offenen CodeQL-Befunde ohne Suppression: konkrete
Dateidokumente lesen keine CLR-Typnamen mehr, Worker-Jobs duplizieren keinen
polymorphen Token, SDK-URL-Normalisierung und Icon-Codegenerierung sind gegen
pathologische beziehungsweise ausbrechende Eingaben abgesichert und Betriebslogs
übernehmen keine freien Worker-/Pfadinhalte. Negative Revisionswerte behalten ihre
bisherigen HTTP-Fehlerverträge. Prozessinstanzen und BPMN-Definitionen verbleiben
vorerst in einer gesonderten polymorphen Legacy-Grenze; die Dateiablage bleibt
Einzelprozess-Entwicklung. Details: [CodeQL- und Storage-Härtung](CODEQL-STORAGE-HARDENING.md).

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

## Wiederverwendbare Formularabschnitte – #230 / PR #231 (noch nicht gemergt)

Eine hostneutrale Bibliothek trennt Katalogmetadaten, revisionsgeschützte Entwürfe und
append-only Abschnittsversionen. Formulare referenzieren nur konkrete Fassungen; beim
Publish expandiert und validiert der Server sie, verwirft behauptete Browserbindungen
und speichert einen eigenständigen Formularsnapshot mit nachvollziehbarem Inhalts-Hash.
Neue Abschnittsversionen ändern keine veröffentlichten Formulare oder laufenden Instanzen.
PostgreSQL publiziert Fassung und Draft-Löschung atomar, die Dateiablage bleibt
Einzelprozess-Entwicklung. Details: [Formularabschnitte](FORM-SECTIONS.md).

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
erzeugt; die CI prüft Drift, Paketbau, Tests und Abhängigkeiten.

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
Die Flowzer-Konsole konsumiert die Pakete mit #224/PR #225 inzwischen selbst: Ihre BFF-,
Form.io- und Development-Details bleiben in schmalen Console-Adaptern, während der
parallele Human-Task-Transport entfernt wurde. Eine reale externe Identity-/HTTPS-
Einbettungsabnahme bleibt separat offen. Details:
[Hostneutrale Einbettung](HOST-INTEGRATION.md).

## Console auf öffentlichen Task-Paketen – #224 / PR #225 (noch nicht gemergt)

Aufgabenlisten in Dashboard, Navigation und Arbeitsplatz verwenden `@flowzer/react`;
Detail, Formular, privater Entwurf, Lifecycle, Directory und idempotenter Abschluss
laufen über denselben öffentlichen Workspace-/Action-Vertrag. Ein opaker
Sitzungsscope wird bei Logout, `401` und Kontowechsel gezielt bereinigt. Form.io erhält
nur feldgebundene Directory-Callbacks; konkrete Hosts bleiben vollständig außerhalb
des Produkts. Der frühere Console-eigene Tasktransport und sein alternativer
`/form/result`-Aufruf wurden entfernt. Details:
[Console-Paketintegration](CONSOLE-TASK-PACKAGE-INTEGRATION.md).

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

## Verzeichnis-Slices #190, #192, #194, #196, #198, #200 und #234 (noch nicht gemergt)

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

#234 / PR #237 ergänzt einen getrennten, begrenzten Batch-Vertrag für historische Anzeigeauflösung.
Workflow, Ordner, gebundenes Formular und Task-Lifecycle erlauben nur Referenzen, die im
jeweiligen berechtigten Kontext bereits gespeichert sind; eine manipulierte bekannte UUID
bleibt ohne Treffer. Antworten unterscheiden aktuellen Directory-Status (`isActive`) von
heutiger Auswählbarkeit (`isSelectable`). Console, SDK und React-Schicht markieren inaktive
oder nicht mehr erlaubte Werte, ohne sie erneut einreichbar zu machen. Details:
[Historische Identitätsreferenzen](HISTORICAL-IDENTITY-RESOLUTION.md).

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

## Append-only Human-Task-Vorgangshistorie – #226 / PR #227 (noch nicht gemergt)

Die vorhandene Lifecycle-Auditspur lässt sich indexiert nach Prozessinstanz lesen und
bleibt auch nach dem Taskende erhalten. Der neue History-Vertrag veröffentlicht nur
Ereignis-/Task-ID, Flow-Node, Aktion, Revision und Zeitpunkt; interne Personen-,
Begründungs-, Korrelations-, Variablen- und Formulardaten verlassen den Server nicht.
Sichtbarkeit verwendet die zentrale Instanz-Objektberechtigung. SDK, React-Schicht und
Console nutzen denselben hostneutralen Vertrag. Details:
[Append-only Vorgangshistorie](PROCESS-HISTORY.md).

## Zentrale BPMN-Fähigkeiten – #228 / PR #229 (noch nicht gemergt)

`flowzer.bpmn-capabilities/1` trennt modellierbare, parsebare und tatsächlich
ausführbare BPMN-Elementarten. Vorabprüfung, Save und Deploy erzwingen dieselbe
serverseitige Matrix samt Graph-, Referenz- und Pflichtkonfiguration; laufende
Instanzen werden nicht rückwirkend neu bewertet. `422`-Befunde tragen stabile Codes,
Element-ID und Eigenschaftspfad. Diagramm und Gliederung zeigen sie dauerhaft,
markieren beziehungsweise öffnen den betroffenen Knoten. Details:
[BPMN-Fähigkeitsvertrag](BPMN-CAPABILITIES.md).

## Laufzeitdiagramm und Engine-Ereignisse – #232 / PR #233 (noch nicht gemergt)

Persistenzgrenzen schreiben append-only, idempotente und datensparsame
Flow-Node-Zustände. PostgreSQL koppelt sie transaktional an den Instanzstand; die
Dateiablage bleibt Einzelprozess-Entwicklung. Ein eigener objektberechtigter
Operatorendpunkt liefert ausschließlich die exakt an die Instanz gebundene,
bereinigte BPMN-Struktur, verdichtete Knotenstatus und die kanonisch sortierte
Ereignisspur. Token-/Korrelations-IDs, Personen, Variablen, Formulardaten und
Erweiterungskonfiguration verlassen den Server nicht.

SDK und React-Schicht stellen denselben hostneutralen Vertrag bereit. Die Console
zeigt Diagramm, Statuslegende, tastaturbedienbare Knotenliste und echte
Ereigniszeitleiste responsiv; die fachlich falsche lineare Fortschrittsanzeige ist
entfernt. Details: [Laufzeitdiagramm](RUNTIME-DIAGRAM.md).

## Gezählt markierte Ausführungen und Instanzdaten – #235 / PR #236 (noch nicht gemergt)

Mehrere Token am selben aktiven BPMN-Knoten werden in Diagramm und Klartextliste
als eine verdichtete Anzahl angezeigt, statt deckungsgleiche Punkte zu zeichnen.
Die technische Instanzansicht liest Prozessvariablen zuverlässig aus dem
Master-Token. Für einen ausgewählten Knoten zeigt sie alle persistierten
Ausführungen mit getrenntem Input und Output, Zustand und Startzeit; fehlende und
leere Snapshots bleiben unterscheidbar. Diese Informationen verlassen die bereits
objektberechtigte Operatoransicht nicht, und der Runtime-Diagramm-Vertrag bleibt
datensparsam.

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

## Verlängerbare Worker-Leases – #238 ([PR #239](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/239))

Lang laufende Service-Task-Worker können ihre noch gültige Lease über einen eigenen
Heartbeat verlängern. Der Besitz bleibt an authentifizierte Person und Worker-Kennung
gebunden; eine abgelaufene oder bereits fremde Lease wird nicht wiederbelebt. PostgreSQL
prüft Besitzer, Ablauf und Aktualisierung atomar in einem Statement. Der öffentliche
Vertrag liefert den tatsächlich gespeicherten UTC-Ablauf zurück und begrenzt jede
angeforderte Dauer auf höchstens eine Stunde. Dieser M6-Baustein bereitet dauerhafte
KI-Läufe vor, implementiert aber noch keinen Modellanbieter oder KI-Ausführungszustand.

## KI-Verbindungen und Secret-Referenzen – #240 ([PR #241](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/241))

Der erste M5-Verbindungsslice persistiert stabile, revisionsgeschützte Metadaten für
OpenAI, Anthropic und ausdrücklich OpenAI-kompatible Endpunkte. Cloud- und lokale
Verarbeitung bleiben getrennte Installations-Opt-ins; Standardprovider erlauben keine
umgedeutete Basisadresse. PostgreSQL erzwingt Revisionen und case-insensitiv eindeutige
Namen atomar, die Dateiablage bleibt ein Einzelprozess-Entwicklungsweg.

Secret-Referenzen sind ausschließlich schreibbar und auf `env:FLOWZER_AI_*` begrenzt.
Weder Referenz noch Wert stehen in API-, OpenAPI-, SDK- oder Browserantworten. Ein
austauschbarer `IAiSecretStore` löst Werte erst serverseitig und kurzlebig auf. Use und
Manage sind getrennte, im authentifizierten Betrieb fail-closed Rollen; reine Verwender
sehen keine deaktivierten Verbindungen. Konsole und headless SDK verwenden denselben
hostneutralen Vertrag. Provideraufrufe, KI-Task-Modellierung, Werkzeuge, Freigaben und
dauerhafte Ausführung sind ausdrücklich noch nicht Bestandteil dieses Slices.

## KI-Aufgabenmodellierung – #242 ([PR #243](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/243))

Der KI-Schritt bleibt technisch ein BPMN-Service-Task und trägt den neuen
`flowzer:aiTask`-Vertrag in Version 1. Verbindung, optionales Modell, versionierte
Anweisung, objektförmiges JSON-Ergebnisschema, deklarierte I/O-Zuordnungen sowie Token-
und Zeitgrenzen werden serverseitig geprüft. Unbekannte Attribute – insbesondere eine
Secret-Referenz – blockieren das Modell. Die referenzierte Verbindung muss beim Speichern
existieren, aktiv und serverseitig einsatzbereit sein.

Diagramm und Gliederung pflegen denselben Vertrag; die Diagrammpalette besitzt eine eigene
KI-Kachel. Der freie Worker-Textmodus normaler Service-Tasks bleibt erhalten. Mangels
Provideradapter und dauerhaftem KI-Lauf ist `serviceTask.aiTask` bewusst noch nicht
deploybar. Die Save- und Deployment-Vorabprüfungen unterscheiden diesen Zustand
explizit, statt eine später hängenbleibende Instanz zu erzeugen. Details:
[Versionierter KI-Aufgabenvertrag](AI-TASKS.md).

Die additive Elementart erscheint in `flowzer.bpmn-capabilities/2`; der historische
Version-1-Vertrag bleibt unverändert im Repository.

## Provideradapter und Ergebnisschema – #244 / PR #245 (noch nicht gemergt)

Ein interner, nicht öffentlich auslösbarer Gateway bindet den gespeicherten Provider ohne
Fallback an OpenAI Responses, Anthropic Messages oder den administrierten
OpenAI-kompatiblen Chat-Completions-Endpunkt. Feste Standardziele, erneut geprüfte
Installationsgrenzen, deaktivierte Weiterleitungen, aufgabengebundene Timeouts,
Envelope-Größe, kurzlebige Secret-Auflösung und redigierte Auth-Header bilden die
gemeinsame Transportgrenze. Automatische Retries finden nicht statt.

`flowzer.ai-result-schema/1` begrenzt unterstützte Typen und Validierungsregeln, verbietet
externe Referenzen und prüft jede Providerantwort erneut lokal. Stabile Fehlerklassen
unterscheiden Authentifizierung, Rate Limit, Timeout, Transport, Providerablehnung,
ungültige Antwort, Schemaverletzung und Budgetüberschreitung, ohne fremde Rohantworten zu
übernehmen. Die explizite Adapterfähigkeit ersetzt keine unzuverlässige Modellannahme:
lehnt ein Ziel strukturierte Ausgabe ab, erfolgt insbesondere kein Wechsel auf ein anderes
Modell oder in die Cloud.

Dieser Baustein führt noch keinen Provideraufruf aus einem BPMN-Prozess aus. KI-Aufgaben
bleiben nicht deploybar, bis persistente Läufe, Recovery und Engine-Fortschritt gemeinsam
implementiert sind.

## Persistente KI-Laufzustände – #246 / PR #247 (noch nicht gemergt)

Ein stabiler Lauf wird eindeutig an Prozessinstanz und Engine-Token gebunden. Der
unveränderliche Snapshot enthält Verbindung und Revision, Modell, Anweisungsversion,
deklarierte Eingaben, Ergebnisschema und Grenzen, jedoch keine Secret-Werte oder
Secret-Referenzen. Pending, Running, RetryScheduled, ResultReady, Completing, Completed,
Incident und Cancelled sind getrennte, revisionsgeschützte Zustände.

Provider- und Engine-Claims besitzen getrennte Leases. PostgreSQL vergibt sie atomar mit
Zeilensperren und `SKIP LOCKED`; Revision, Besitzer und Ablauf werden bei jedem Übergang
erneut geprüft. Ergebnis, tatsächliches Modell und konsistente Tokenmessung überleben einen
Neustart. Recovery gibt nur einen vor Aufrufbeginn verlorenen Claim erneut frei. Ein
abgelaufener Lease nach markiertem Provideraufruf oder während des Engine-Commits wird als
unklarer Ausgang angehalten und nicht blind wiederholt. Der Dateiadapter bleibt ausdrücklich
auf einen Entwicklungsprozess begrenzt.

Die Ablage allein aktiviert noch keine KI-Aufgabe. Hintergrund-Executor, DNS-Adressbindung
für benutzerdefinierte Cloudziele und atomarer Engine-Fortschritt folgen vor dem Entfernen
des Deployment-Blockers.

## Netzwerkbindung benutzerdefinierter KI-Endpunkte – #248 (noch nicht gemergt)

OpenAI-kompatible Cloudziele werden unmittelbar vor dem Aufruf aufgelöst und nur bei
ausschließlich öffentlichen Unicast-Adressen zugelassen. Der Socketaufbau ist an genau
diesen geprüften Adressvorrat sowie an Host und Port gebunden; eine zweite unkontrollierte
DNS-Auflösung, Systemproxys und Weiterleitungen entfallen. Gemischte öffentliche/private
Antworten werden insgesamt abgelehnt. Ausdrücklich lokale Verbindungen behalten bei
Installations-Opt-in ihren privaten beziehungsweise Loopback-Zugriff. Der Slice aktiviert
noch keine BPMN-KI-Aufgabe.

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
   liegen in #208–#216; die Abschnittsbibliothek folgt mit #230 / PR #231. Anhänge und freigegebene
   dynamische Quellen bleiben offen. Legacy-Namen und kurze Gruppenbezeichnungen bleiben bis zur
   Migration mehrdeutig; historische externe Formularstände benötigen Klärung.
3. **M3/M4:** Aufgabenrevisionen, Übernahme/Delegation, private Entwürfe, der
   serverseitige Fristen-/Benachrichtigungskern sowie SDK, React-Bausteine und die
   Console-Paketmigration liegen als gestapelte Topic-Branch-Slices
   vor (#202–#232). Merge/Abnahme, externe Zustellung sowie weiterführende Laufzeitkennzahlen
   und Störungsdiagnose folgen. Flowzer erhält keine Abhängigkeit von einer konkreten Host-
   Anwendung; diese konsumiert die generischen Verträge ausschließlich von außen.
   Mobil-PR #153 nicht duplizieren.
4. **M5:** Begrenzte KI-Tasks mit geprüften Werkzeugen, Freigaben und Wiederaufnahme.
   Worker-Lease-Verlängerung (#238), sichere Verbindungsverwaltung (#240 / PR #241),
   Task-Vertrag (#242 / PR #243), Provider-/Schemaschicht (#244 / PR #245) und der
   persistente Laufzustand (#246 / PR #247) sowie die DNS-/Socketbindung (#248) liegen vor;
   Executor und Werkzeugfreigaben bleiben offen.
5. **M6 begleitend:** Call Activities/Fehlersemantik, explizite Expressions,
   PostgreSQL-Konfliktschutz, Recovery/Upgrade und Open-Source-Produktreife.

Vorgangsübersichten und Laufzeitdiagramm wurden auf Desktop/Mobil visuell geprüft; 33 Browser-Smokes
sichern Kernwege und Feldfehler. Der aktuelle Stand besteht lokal aus 167 Engine-,
839 API-/Storage-, 342 Konsolen-, 24 SDK- und 20 React-Pakettests; zusätzlich bestehen
33 Chromium-Smoke-Tests. Der vollständige UX-Audit und die erste Produktabnahme aus der
Roadmap stehen weiterhin aus. Details zum bestehenden Betrieb: [OPERATIONS.md](OPERATIONS.md).

## Arbeits- und Release-Modell

Topic-Branches und kleine, testgetriebene PRs gehen nach `main`. `release` wird
nur über einen eigenen PR aus `main` befüllt; dessen Push ist ein Produktivrelease.
Ein Feature-PR allein autorisiert kein Deployment. Keine direkten Writes auf
`main` oder `release` ohne ausdrückliche Freigabe.
