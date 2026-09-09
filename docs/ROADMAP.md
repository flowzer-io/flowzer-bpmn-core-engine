# Roadmap

**Stand: 9. September 2026**

Die freigegebene, führende Produkt-Roadmap steht in
[PRODUCT-ROADMAP-2026-09.md](PRODUCT-ROADMAP-2026-09.md). Sie ersetzt den früheren
Rettungs-/Pilotplan und führt offene Abnahmen ausdrücklich als Checkliste.

## Reihenfolge

1. **M0 – Sicherheit und Verträge:** zentraler Aufgabenabschluss (#176, PR #177),
   objektbezogene Instanzrechte, serverseitige Validierung und Idempotenz sind als
   gestapelte PRs umgesetzt. #188 ist der laufende, noch nicht nach `main` gemergte
   BFF-Slice: vertraulicher OIDC-Code-Flow, `HttpOnly`/`Secure`-Cookies,
   `X-Flowzer-CSRF`, persistenter API-Keyring und kompatibler externer Bearer-Vertrag.
   #222 härtet zusätzlich die offenen CodeQL-Befunde, konkrete Storage-Dokumente,
   Logausgaben und Codegeneratoren ohne Suppression. Bis Merge, grünem CodeQL und
   HTTPS-/Secret-/Restore-Abnahme ist M0 nicht vollständig geschlossen.
2. **M1/M2 – Verzeichnis und Formulare:** Keycloak, stabile Benutzer-/Gruppenreferenzen,
   generische Auswahlfelder, Versionierung, validierte Eingaben und Entwürfe. Der erste
   M1-Slice #190 / PR #191 implementiert den atomaren, lesenden Keycloak-Abgleich samt
   stabiler Historie, Mehrprozess-Lease und Operatorstatus. #192 / PR #193 ergänzt darauf
   aufbauend typisierte `SubjectRef`-Werte und eine workflowgebundene, aktive Suche für
   berechtigte Modellierende. #194 / PR #195 ergänzt den serverseitigen Text-/Directory-Vertrag für
   User-Task-Zuweisungen; #196 ergänzt die Auswahl in Diagramm und Gliederung. #198 ergänzt
   das gebundene `flowzerSubject`-Formularfeld; #200 verwendet dieselbe Auswahl für typisierte
   Ordnerrechte und erhält daneben den expliziten Freitextmodus. #202 ergänzt private,
   revisionsgeschützte Aufgabenentwürfe samt Wiederaufnahme und Konfliktdarstellung
   in PR #203. #208 / PR #209 sichert die Formularprofile mit demselben versionierten
   Vertragsvektor-Katalog in .NET und Vitest ab. #210 / PR #211 trennt Formularautoren-Entwurf,
   Vorschau und ausdrückliche unveränderliche Veröffentlichung.
   #212 / PR #213 inventarisiert danach alle veröffentlichten Fassungen und Autorenentwürfe
   anhand stabiler, datensparsamer Kompatibilitätscodes.
   #214 / PR #215 erweitert den Vertrag additiv um begrenzte Wiederholgruppen und sichere
   Hilfetexte. #216 / PR #217 ergänzt servergebundene Human-Task-Entscheidungsaktionen;
   #230 / PR #231 ergänzt die hostneutrale Bibliothek unveränderlicher Formularabschnittsversionen
   und vollständige, serverseitig gebundene Formularsnapshots.
   #234 / PR #237 ergänzt die exakte historische Anzeigeauflösung für gespeicherte Referenzen in
   Workflow, Ordner, Formular und Task-Lifecycle. `isActive` und `isSelectable` bleiben
   getrennt; beliebige UUIDs und fremde Kontexte liefern keine Verzeichnisdaten.
3. **M3/M4 – Aufgaben und Oberflächen:** #204 / PR #205 ergänzt Übernahme, Freigabe,
   Operator-Zuweisung und berechtigte Delegation mit stabiler Revision und Auditspur.
   #206 / PR #207 ergänzt darauf aufbauend serverseitig gebundene Fristen, Wiedervorlagen,
   Erinnerungen, Eskalationsmeldungen und den deduplizierten In-App-Feed. Beide
   Slices liegen auf `codex/m3-user-task-deadlines`; Merge nach `main` und die
   fachliche Abnahme bleiben offen. Das hostneutrale SDK (#218/PR #219) und die
   React-Bausteine (#220/PR #221) liegen vor. #224/PR #225 migriert die Flowzer-Konsole auf
   genau diese öffentlichen Verträge und entfernt ihren parallelen Human-Task-
   Transport. #228 / PR #229 ergänzt die zentrale versionierte BPMN-Fähigkeitsmatrix, gemeinsame
   Vorab-/Save-/Deploy-Prüfung und anwählbare Diagramm-/Gliederungsdiagnosen. #232 / PR #233 ergänzt
   das objektberechtigte Laufzeitdiagramm mit exakt gebundener, bereinigter BPMN-Version
   und append-only Engine-Ereignisspur. #235 / PR #236 ergänzt gezählte statt überlagerter
   Laufzeitmarker sowie den getrennten Blick auf Prozessvariablen und persistierte
   Knotenein-/ausgaben. Der vollständige UX-Audit folgt. #226/PR #227 stellt als ersten Historienbaustein die vorhandene append-only
   Human-Task-Auditspur objektberechtigt und datensparsam bereit; weitere Engine-
   Ereignisse bleiben getrennte Slices.
4. **M5 – KI-Tasks:** Cloud/lokale Modelle, Secret-Referenzen, begrenzte Werkzeuge,
   parametergebundene Freigaben und sichere Wiederaufnahme. Der vorgezogene M6-Baustein
   #238 ergänzt bereits die atomare Lease-Verlängerung für lang laufende Worker; der PR
   folgt auf #237. #240 / PR #241 ergänzt darauf die sichere, revisionsgeschützte Verwaltung von
   Verbindungsmetadaten und nur schreibbaren Secret-Referenzen in PostgreSQL und Dateiablage,
   getrennte Use-/Manage-Rollen, Installations-Opt-ins sowie den austauschbaren
   Laufzeit-Secret-Store. #242 / PR #243 ergänzt den serverseitig geprüften, in Diagramm und
   Gliederung pflegbaren KI-Aufgabenvertrag. Er ist bis zur persistenten Runtime bewusst
   speicherbar, aber nicht deploybar. #244 / PR #245 ergänzt bereits die providerneutrale HTTP-
   Aufrufschicht und das portable serverseitige Ergebnisschema, ohne diesen Blocker zu
   verfrüht zu entfernen.
5. **M6 begleitend:** Runtime, Persistenz, Recovery, Installation und Open Source.
   Notwendige Grundlagen werden vor dem jeweils abhängigen Feature umgesetzt.

## M3-Slice-Status

- [x] **#204 / PR #205 – Human-Task-Lifecycle:** Claim, Release, Operator-Zuweisung
  und berechtigte Delegation mit monotoner Revision, Akteur, Begründung und Auditspur
  sind im Topic-Branch umgesetzt.
- [x] **#206 / PR #207 – Human-Task-Fristen:** Due-/Follow-up-Werte werden einmalig serverseitig
  aufgelöst und als UTC-Termine gebunden. Der Scheduler holt fällige Meilensteine nach;
  Benachrichtigungen sind taskbezogen, persistent und per Unique-Schlüssel dedupliziert.
  Die Dateiablage bleibt Einzelprozess-Entwicklung; PostgreSQL ist der vorgesehene
  Mehrprozesspfad. BPMN-Eskalationspropagation, externe Zustellung und automatische
  Vertretung sind ausdrücklich nicht enthalten.
- [x] **#218/#220 – öffentliche Integrationspakete:** Das zustandslose SDK sowie
  darstellungsfreie React-Hooks/-Controller bleiben frei von konkreten Hosts.
- [x] **#224 / PR #225 – Console-Paketmigration:** Die Flowzer-Konsole verwendet für Human
  Tasks die öffentlichen Pakete; BFF, Form.io und Development-Details bleiben
  ausschließlich Console-Adapter. Ein konkreter externer Host ist nicht Bestandteil
  von Flowzer.
- [x] **#226 / PR #227 – Human-Task-Vorgangshistorie:** Die bestehende append-only Auditspur
  ist nach Instanz indexiert, objektberechtigt und über SDK sowie Console als
  datensparsame Minimalprojektion verfügbar. Vollständige Engine-Historie folgt.
- [x] **#228 / PR #229 – BPMN-Fähigkeiten und Modellprüfung:** Ein versionierter, hostneutraler
  Vertrag trennt modellierbar, parsebar und ausführbar. API, Save und Deploy verwenden
  denselben Validator; Diagramm und Gliederung zeigen stabile, anwählbare Befunde.
  Error-/Escalation-Semantik und lokale Call Activities folgen separat.
- [x] **#232 / PR #233 – Laufzeitdiagramm und Engine-Ereignisse:** Persistenzgrenzen schreiben
  idempotente, datensparsame Knotenfakten. Der Betrieb erhält die exakt gebundene und
  von Ausführungsdaten bereinigte BPMN-Version, verdichtete Knotenstatus sowie eine
  echte Zeitleiste über API, SDK, React-Schicht und responsive Console. Eine lineare
  „Schritt x von y“-Anzeige wird nicht mehr behauptet.
- [x] **#235 / PR #236 – Markerzählung und technische Instanzdaten:** Parallele Token am selben
  aktiven Knoten erscheinen als ein Kreis mit Anzahl. Der Master-Token liefert den
  aktuellen Prozessscope; pro ausgewähltem Knoten bleiben gebundene Input- und
  Output-Snapshots aller Ausführungen getrennt sichtbar. Fehlende historische
  Snapshots werden benannt und nicht aus dem aktuellen Scope rekonstruiert.

## M5-Slice-Status

- [x] **#238 / PR #239 – verlängerbare Worker-Lease:** Ein noch gültiger Job kann seine
  besitzergebundene Lease atomar verlängern; abgelaufene oder fremde Leases werden nicht
  wiederbelebt.
- [x] **#240 / PR #241 – KI-Verbindungen und Secret-Referenzen:** Persistente, revisionsgeschützte
  und hostneutrale Verbindungsmetadaten, getrennte Rollen, Installationsgrenzen,
  Secret-Store-Abstraktion, sichere API-/SDK-Verträge und eine Verwaltungsseite liegen vor.
  Kein Provideradapter und keine KI-Task-Runtime werden damit vorgetäuscht.
- [x] **#242 / PR #243 – versionierter KI-Aufgabenvertrag:** Eigene KI-Kachel als Standard-Service-Task,
  stabile Verbindungs-ID, Modelloverride, versionierte Anweisung, JSON-Ergebnisschema,
  deklarierte I/O-Zuordnungen und harte Limits werden serverseitig und in beiden
  Modellieransichten gleich behandelt. Geheimnisattribute werden abgelehnt. Bis eine
  dauerhafte Provider-Runtime folgt, blockiert der Fähigkeitsvertrag das Deployment.
- [x] **#244 / PR #245 – Provideradapter und Ergebnisschema:** OpenAI Responses, Anthropic Messages
  und administrativ gebundene OpenAI-kompatible Chat-Completions laufen über einen
  gemeinsamen, timeout- und größenbegrenzten Gateway-Vertrag. Providerfähigkeit, Ziel,
  Installations-Opt-in und Secret werden ohne Fallback erneut geprüft. Fremde Antworten
  müssen das begrenzte Schema-Profil `flowzer.ai-result-schema/1` serverseitig erfüllen.
  Die persistente KI-Laufzeit bleibt der nächste notwendige Slice.
- [x] **#246 / PR #247 – Persistente KI-Laufzustände:** Ein unveränderlicher Auftragssnapshot,
  eindeutige Tokenbindung, Revisionen, atomare Provider-/Ergebnis-Claims, Lease-Verlängerung
  und konservative Recovery liegen in Dateiablage und PostgreSQL vor. Unklare Provider-
  oder Engine-Ausgänge werden angehalten statt blind wiederholt. Der Executor und damit die
  Aktivierung des Deploymentpfads bleiben der nächste Slice.
- [x] **#248 – Gebundene Netzwerkziele:** Benutzerdefinierte Cloudendpunkte werden nur bei
  ausschließlich öffentlichen DNS-Ergebnissen zugelassen. Der Socket verwendet danach
  exakt die geprüften Adressen sowie denselben Host und Port; lokale Ziele bleiben an das
  ausdrückliche Installations-Opt-in gebunden. Der Deploymentblocker bleibt bestehen.

## Vorhandenes nicht neu bauen

React-Konsole, API-seitiger BFF-/Bearer-Auth-Vertrag (laufender ungemergter Slice), PostgreSQL, Service-Task-Worker, Startformulare und
Workflow-Ordner existieren. Der offene Mobil-PR #153 enthält noch nicht auf `main`
enthaltene Korrekturen und bleibt ein eigener Strang; sie werden hier nicht dupliziert.

#98 verfolgt die gesamte Roadmap; #93–#96 bleiben fachliche Folge-Epics.
Prozessverbund #154 folgt auf lokale Call Activities und Fehlersemantik.
Echtes Mehrmandanten-Hosting und vollständige Kompensation sind spätere Vorhaben.

## Arbeitsweise

Kleine, testgetriebene PRs nach `main`; laufende Instanzen und öffentliche Verträge
kompatibel migrieren. Kein Produktivdeployment allein durch einen Feature-PR.
Tests, Reviews und nicht erfüllte Abnahmen werden pro Slice dokumentiert.
