# Produkt-Roadmap: installierbare Open-Source-Workflow-Plattform

**Freigegeben am:** 8. September 2026. **Basis:** `212705a`.

Dieses Dokument hält die freigegebene Weiterentwicklung und ihren tatsächlichen
Umsetzungsstand fest. Eine Checkbox wird erst nach belegter Implementierung und
Verifikation geschlossen. Vorhandene Grundlagen sind kein Nachweis für ein ganzes
Paket. `docs/ROADMAP.md` verweist auf diesen führenden Plan.

## Aktuelles Arbeitsmandat

Am 8. September 2026 hat Christian die autonome Umsetzung aller Pakete beauftragt.
Terra/Luna dürfen für begrenzte Teilaufgaben unterstützen; vor der finalen
Zusammenführung prüft Astra mit hoher Reasoning-Stufe den Gesamtstand und erkannte
Probleme werden behoben. Bis zur belegten Gesamt-Abnahme bleibt die Arbeit in
Topic-Branches und PRs. Für den danach verifizierten Gesamtstand hat Christian den
Merge nach `main` und das dadurch ausgelöste Deployment ausdrücklich freigegeben;
direkte Zwischenstände werden weiterhin weder nach `main` noch `release` geschrieben
oder produktiv ausgerollt.

## Ziel und Grenzen

- Flowzer bleibt ein eigenständig installierbares Produkt unter MPL-2.0.
- Zunächst eine getrennte Installation mit eigener Datenbank und Identitätsanbindung
  je Kunde; echtes Mehrmandanten-Hosting ist ein späteres eigenes Vorhaben.
- Bestehenden modularen .NET-/React-Aufbau schrittweise verbessern, kein Rewrite.
- TickyTask integriert dieselben Human Tasks und Formulare, ist aber keine Abhängigkeit
  der Flowzer-Engine. Kein gemeinsamer Datenbankzugriff zwischen den Produkten.
- Der Urlaubsantrag ist ein Beispiel für generische Fähigkeiten, keine vollständige
  Personalverwaltung und keine pauschale Übertragung von Rechten auf Vertretungen.
- Produktivkonfiguration, echte Anbieteraufrufe und Deployment sind separate Freigaben.

## Ausgangslage

React-Konsole, OIDC/Rollen, PostgreSQL, Service-Task-Worker, Startformulare,
Workflow-Ordner und eine BPMN-Gliederungsansicht sind bereits vorhanden.
Die älteren Reviews bleiben historische Dokumente; ihre offenen Listen sind nicht
automatisch der aktuelle Bestand. Ein visueller Audit des heutigen Stands ist noch offen.

**Aktiver Slice:** #176 / PR #177 implementiert den gemeinsamen Aufgabenabschluss
und die geschützte Akteurzuordnung. Offene Checkboxen bezeichnen noch nicht
abgenommene Ergebnisse; weder dieser Slice noch vorhandene Grundlagen schließen
die gesamte M0- oder Produktabnahme.

**Folgeslice:** #178 / PR #179 ergänzt issuergebundene Antragstellerrechte,
aufgabenbezogene Vorgangsübersichten und reduzierte API-/UI-Projektionen. Die
Aufgaben-Leseprojektion ersetzt noch keine immutable Formularbindung oder
serverseitige Submission-Validierung.

**Formularbindung:** #180 / PR #181 friert alle referenzierten Start-/Aufgabenformulare
beim Deployment ein, einschließlich Subprozessen. Externe Altbestände ohne Snapshot
benötigen ausdrückliche Klärung; es erfolgt keine automatische Migration auf `latest`.
**Formularprüfung:** #182 / PR #183 ergänzt das begrenzte Profil `flowzer.forms/1`,
verbindliche Submission-Prüfung, Read-only-Schutz, Feldfehler und die Migration der
Beispielskripte. Vollständige Form.io-Parität und Bestandsmigration bleiben offen.
Details: [Prüfprofil](FORM-VALIDATION-PROFILE.md).

**Aufgabenidentität:** #184 / PR #185 erhält IDs und gespeicherte Zuweisungen über
parallelen Fortschritt/Timer/Neuladen. Neue Tokens erhalten neue IDs, veraltete Aufgaben
werden entfernt. Claims/Revisionen/Entwürfe und Mehrprozessschutz folgen separat.
Details: [Aufgabenidentität](STABLE-TASK-IDENTITY.md).

**HTTP-Idempotenz:** #186 / PR #187 bindet optionale Schlüssel an Akteur,
Operation, Ziel und kanonischen Inhalt. PostgreSQL-Konkurrenztests belegen genau einen
Start/Abschluss; geänderter Inhalt endet mit 409. Externe Effekte folgen separat.
Details: [HTTP-Idempotenz](IDEMPOTENCY.md).

**BFF:** #188 ist der nächste M0-Slice. Die Web-API übernimmt den serverseitigen
OIDC-Code-Flow, hält Browser-Tokens aus Storage und Antworten fern und schützt
Cookie-Mutationen per CSRF. Die externe Bearer-API bleibt kompatibel.

Die sechs PRs sind gestapelte, noch separat zu mergende Teilpakete, kein Produktabschluss.

## M0 – Sicherheit und Verträge (zuerst)

- [x] Einheitlicher, transaktionsgebundener autorisierter Aufgabenabschluss für alle
  HTTP-Routen; fremde Ressourcen liefern `404`, keine freigebenden Fallbacks.
- [x] Ausführende Identität getrennt von untrusted Formulardaten speichern; eingehende
  `UserId` darf den authentifizierten Akteur nicht ersetzen.
- [x] Objektbezogene Instanzrechte und datensparsame Projektionen: Antragsteller sehen
  eigene Vorgänge, Bearbeiter nur benötigten Kontext, Modellierer nicht automatisch
  Personalvorgänge; Betrieb erhält ausdrücklich berechtigte Diagnoseansichten.
- [x] Serverseitige Validierung für Starts und Aufgabenabschlüsse (gemeinsam mit M2).
- [ ] BFF-Anmeldung mit HttpOnly-/Secure-Cookies und CSRF-Schutz; keine Browser-Tokens
  in `sessionStorage`, Bearer-Vertrag für externe Konsumenten bleibt bestehen.
- [x] Idempotente Starts und Abschlüsse; derselbe Schlüssel mit abweichendem Inhalt
  erzeugt einen Konflikt statt einen weiteren Vorgang.
- [x] Bestandsissues #93–#96 und #98 bereinigt und #176 / PR #177 verknüpft;
  Mobil-PR #153 gegen `main` auf Überschneidungen geprüft, nicht dupliziert.
  Sein Review/Sync/Merge bleibt ein gesonderter Vorgang; hier wurde nichts daraus übernommen.

**Abnahme:** Kein alternativer Abschlussweg umgeht die Rechte; Wiederholungen
erzeugen keine weiteren Starts oder Abschlüsse.

## M1 – Verzeichnis und Auswahl von Benutzern/Gruppen

- [ ] Keycloak bleibt führend; nur lesender, minimal berechtigter Servicezugang über
  die Admin REST API. Keine Passwörter oder unnötigen Profilattribute übernehmen.
- [ ] Lokales Verzeichnis mit stabiler interner ID, `(Issuer, Subject)`, Anzeigename,
  Benutzerstatus, externer Gruppenkennung, Hierarchie und Mitgliedschaften.
- [ ] Erst- und periodischer Abgleich mit Pagination, Retry und sichtbarem Status;
  Generation erst nach vollständigem Erfolg veröffentlichen. Teilfehler dürfen
  keine Massen-Deaktivierung auslösen.
- [ ] Gelöschte/deaktivierte Identitäten historisch auflösbar halten, aber aus neuen
  Auswahlen entfernen. Mehrdeutige Bestandszuweisungen explizit klären.
- [ ] Generisches Form.io-Feld: Einzel-/Mehrfachauswahl, nur aktive Benutzer (Default
  ja), erlaubte Benutzer/Gruppen, Untergruppen (Default nein), Gruppen auswählbar
  (Default nein), Suche, Auswahl-Chips, Mindest-/Höchstanzahl.
- [ ] Typisierte `SubjectRef` statt Freitext; ausgewählte Gruppen nicht still in
  Benutzer expandieren. Server leitet erlaubte Werte aus der Formularversion ab.
- [ ] Dieselbe Auswahl in Aufgaben-Zuweisungen und Ordnerberechtigungen verwenden.
- [ ] **Ergänzung vom 8. September 2026:** Task-Zuweisungen behalten zusätzlich den
  freien Textmodus. Vor der Eingabe explizit „Bekannter Benutzer / bekannte Gruppe“
  oder „Text-String“ wählen. Verzeichniswahl speichert eine typisierte stabile
  Referenz; Text bleibt ein explizit als solcher markierter Wert und wird nicht
  automatisch anhand von Anzeigename/E-Mail einer Verzeichnisidentität zugeordnet.
  Bestehende Textzuweisungen ohne automatische Konvertierung erhalten. Auflösen und
  Berechtigungsprüfung bleiben serverseitig; Verzeichnisbeschränkungen nicht durch
  einen vom Aufrufer gewählten Modus umgehen. Der Modus gehört ins veröffentlichte
  Modell, nicht in den Abschluss-Request. Gruppen bleiben Kandidatengruppen bzw.
  Gruppenreferenzen und werden nicht zum behaupteten individuellen Bearbeiter.
  Modellierer zeigen Modus und eventuelle Mehrdeutigkeit verständlich an.

**Abnahme:** Gleichnamige Identitäten bleiben unterscheidbar; manipulierte,
ausgeschlossene oder deaktivierte Werte werden serverseitig abgelehnt.

## M2 – Verlässliche und wiederverwendbare Formulare

- [ ] Form.io behalten; versionierter, serverseitig prüfbarer Komponentenvertrag mit
  gemeinsamen Testvektoren für Typen, Pflichtwerte, Bereiche, Datumsvergleiche,
  Auswahlregeln und deklarative Bedingungen.
- [ ] Vorhandene Custom-JavaScript-Regeln inventarisieren und vor erneuter
  Veröffentlichung in unterstützte Regeln oder benannte Serverberechnungen überführen.
- [ ] Eingaben, Ausgaben und readonly Kontext trennen; unbekannte Ergebnisse dürfen
  keine geschützten Prozessvariablen überschreiben.
- [ ] Unveränderliche veröffentlichte Formularversionen beim Deployment binden;
  laufende Aufgaben behalten ihre gebundene Version. Entwurf/Vorschau/Veröffentlichung
  klar unterscheiden.
- [ ] Serverseitige Bearbeitungsentwürfe mit Wiederaufnahme und Konflikterkennung;
  Refetch darf keine ungespeicherten Eingaben zurücksetzen.
- [ ] Wiederverwendbare Abschnitte, bedingte Felder, wiederholbare Gruppen, Hilfetexte
  und explizite Entscheidungsaktionen ergänzen.
- [ ] Anhänge als eigener Slice: Größen-/Typgrenzen, Quarantäne, Prüfung,
  objektbezogene Downloadrechte und Aufbewahrung.
- [ ] Dynamische Kunden-/Projekt-/andere Auswahldaten nur über administrativ
  freigegebene Datenquellen anbinden.
- [ ] Urlaubsbeispiel auf strukturierte Benutzerwahl, Zeitraumprüfung, Freigabe und
  optionalen externen Abgleich umstellen.

## M3 – Human Tasks und TickyTask-Einbettung

- [x] Stabile Aufgabenidentität je Token; bestehende Subscriptions aktualisieren
  statt bei jedem Instanzfortschritt neue IDs zu vergeben.
- [ ] Claim, Release, Zuweisung und berechtigte Delegation mit Revision, Akteur und
  Begründung; tatsächlicher Bearbeiter ist nicht die Kandidatengruppe.
- [ ] Fälligkeiten, Wiedervorlagen, Erinnerungen und Eskalationen serverseitig;
  dauerhafte, deduplizierte Benachrichtigungen.
- [ ] Entwürfe, Kommentare und Vorgangshistorie mit eigenen Sichtbarkeitsregeln.
- [ ] Headless TypeScript-SDK und optionale React-Komponenten für Aufgabenliste,
  Formular, Aktionen und Status; Host-Adapter für Styling und Auswahlkomponenten.
- [ ] Identischer API-/Formularvertrag in Konsole und TickyTask; Flowzer besitzt
  Prozesse/Aufgaben, TickyTask seine Fachobjekte.
- [ ] Benutzergebundene Einbettung mit Flowzer-Audience, bei gemeinsamem Keycloak
  über korrekt berechtigten Token Exchange; keine frei übergebenen Benutzerheader
  und kein pauschales Administratorkonto.
- [ ] Technische Konnektoren mit eigenen Rechten, Korrelation und Idempotenz.

**Abnahme:** Dieselbe Aufgabe in Flowzer oder im Host bearbeiten; Rechte, Entwurf
und Abschluss bleiben identisch.

## M4 – Diagramme und Bedienbarkeit

- [ ] Aktueller Browseraudit auf Desktop/Mobil für Finden, Starten, Aufgaben,
  Formularpflege, Modellierung und Störungsbehandlung, mit visueller Evidenz.
- [ ] BPMN bleibt führend; Gliederung und Diagramm verwenden gemeinsame Eigenschaften
  für Formulare, Identitäten, Fristen, Datenzuordnung, Konnektoren und KI.
- [ ] Unterstützte Teilmenge zentral deklarieren; nicht ausführbare BPMN-Elemente und
  verlustbehaftete Gliederungsänderungen vor Speicherung/Deployment anzeigen.
- [ ] Anwählbare Validierungsfehler für unerreichbare Schritte, fehlende Zuordnungen,
  ungültige Bedingungen und unvollständige Integrationskonfiguration.
- [ ] Laufzeitdiagramm und echte Ereigniszeitleiste für aktive, abgeschlossene,
  abgebrochene und gestörte Schritte. Kein scheinexaktes „Schritt x von y“ bei
  offenen Verzweigungen.
- [ ] Versionsvergleich, Änderungsübersicht und atomare Veröffentlichung von
  zusammengehörigem BPMN-/Formularstand.
- [ ] Such-/Filterzustände, Tastatur, Fokus, Formularfehler, Ladezustände und mobile
  Dialoge vereinheitlichen.
- [ ] Danach Warte-/Durchlaufzeit- und Störungsdiagramme auf echter Historie mit
  Stichprobengröße und Rechtefiltern.

## M5 – KI-Tasks und Werkzeuge

- [ ] KI-Kachel als BPMN-Service-Task mit dokumentierter Flowzer-Erweiterung:
  Verbindung, Modell, versionierte Anweisung, deklarierte Ein-/Ausgaben,
  Ergebnisschema, Werkzeuge, Freigaben und Limits.
- [ ] Adapter für OpenAI, OpenAI-kompatible Cloud-/lokale Endpunkte und Anthropic;
  Fähigkeiten prüfen, keine universelle Kompatibilität unterstellen.
- [ ] Cloud-Verarbeitung explizit je Installation freigeben, kein stiller Wechsel
  von lokalen Modellen in die Cloud.
- [ ] Verbindungen und Secret-Referenzen administrieren; Verwenden und Verwalten
  getrennt berechtigen. Secrets nur über austauschbaren Secret-Store zur Laufzeit.
- [ ] Keine Secrets in BPMN, Formularen, Exporten, Prompts oder Browserantworten;
  lokale Endpunkte nur mit expliziter administrativer Freigabe.
- [ ] Worker-Vertrag um Lease-Verlängerung und dauerhafte, begrenzt fortsetzbare
  KI-Läufe mit Störungsbehandlung erweitern.
- [ ] Typisierte Werkzeugregistry mit Schemas und expliziten Rechten. Keine freie
  Shell/SQL-Ausführung oder beliebigen HTTP-Ziele.
- [ ] Effektive Rechte als Schnittmenge von Verbindung, Workflow-Freigabe,
  Task-Werkzeugliste und fachlichem Kontext, niemals aus dem Prompt.
- [ ] Außenwirkung standardmäßig mit menschlicher Freigabe; administrative
  Vorabfreigaben pro Workflow nur für einzelne begrenzte Aktionen. Freigabe an
  konkrete Parameter binden; Änderungen verlangen eine neue Freigabe.
- [ ] Modell-/Tool-Ausgaben als untrusted behandeln; Prompt-Injection darf keine
  Rechte, Secret-Referenzen oder Freigaben verändern.
- [ ] Limits für Aufrufe, Laufzeit, Tokens und Aktionen; Kosten nur mit belegbarer
  Preisgrundlage anzeigen.
- [ ] Persistentes Ausführungsjournal und Idempotenz für Seiteneffekte; unklare
  Schreibausgänge anhalten, nicht blind wiederholen.
- [ ] Testmodus ohne Außenwirkung, nachvollziehbare Modell-/Prompt-/Tool-Versionen.
- [ ] Vorlagen: Klassifizieren, Extrahieren, Antwortentwurf, freigegebene Konnektoraktion.

## M6 – Runtime, Betrieb und Open Source

- [ ] Lokale Call Activities, Boundary Errors und erforderliche Eskalationen.
- [ ] Inclusive Gateway, Parallel-/Multi-Instance- und Timer-Recovery-Tests.
- [ ] Legacy-Abweichung zwischen `Token.ProcessInstanceId` und persistierter
  `InstanceId` bereinigen; laufende Instanzen vorwärtskompatibel migrieren. Bis dahin
  Aufgaben über die tatsächliche Mitgliedschaft in geladenen Instanz-Tokens prüfen.
- [ ] Explizites, am Deployment gespeichertes Expression-Profil, kein stiller
  Semantikwechsel durch V8-Fallback.
- [ ] Störungszentrum mit Diagnose, sicherem Retry, Eingabekorrektur, Abbruch und Audit.
- [ ] PostgreSQL-Revisionen, atomare Lease-Prüfung und gemeinsamer Commit von
  Instanz/Aufgaben/Jobs; Mehrprozessbetrieb erst nach Konkurrenztests freigeben.
- [ ] Dateiablage auf Entwicklung begrenzen; bestehende No-op-Transaktionen sind
  kein Rollback- oder Crash-Konsistenzversprechen.
- [ ] Große Einheiten nach Verantwortung aufteilen, nicht allein nach Zeilenzahl.
- [ ] Installation, Konfigurationsprüfung, Gesundheitsübersicht, Backup/Restore
  und Upgrade mit laufenden Instanzen reproduzierbar machen.
- [ ] MPL-2.0, Abhängigkeits-/Lizenzhinweise, SBOM, Sicherheitsmeldestelle und
  Beitragsdokumentation vervollständigen.
- [ ] Prozesspakete aus BPMN, Formularen, Verträgen und Fähigkeiten exportieren;
  Verbindungen/Identitäten beim Import explizit zuordnen, niemals Secrets exportieren.

Prozessverbund #154 folgt auf lokale Call Activities und Fehlerbehandlung;
vollständige Kompensation und echtes Mehrmandanten-Hosting bleiben separate Stränge.

## Verträge, Migration und Fertigkriterien

- Task-Zuweisungen erhalten einen diskriminierten Vertrag für stabile Referenzen
  und explizite Textwerte. Legacy-Text wird beim Laden als Text behandelt; Änderungen
  am Modus erfordern eine berechtigte Modelländerung/Veröffentlichung. Tests für
  beide Modi, unveränderte Legacy-Roundtrips, gleichnamige Einträge und manipulierte
  Moduswechsel gehören zum M1-/M3-Abnahmepaket.
- Versionierte OpenAPI-Verträge und generierte Clients; Problem Details für neue
  Fehlerverträge, kompatible Adapter statt abruptem Bruch vorhandener Endpunkte.
- Append-only-Historie mit Akteur, Zeitpunkt, Korrelation und datensparsamen Änderungen.
- Vorwärtsmigrationen; laufende Instanzen behalten Definition und gebundene Formulare.
- Reihenfolge M0 → M1/M2 → M3/M4 → M5; notwendige M6-Bausteine jeweils vorziehen.
- TDD, Testzweck-Kommentare und fokussierte PRs nach `main`. Vor nichttrivialen Pushes
  zwei unabhängige Reviews über die zentralen Wrapper; keine direkten Main-Writes.
- Negative Rechte-/Verzeichnis-/Formulartests, Konkurrenz und Neustart, Host-Parität,
  KI-Injection/Freigabe/Limits/unklarer Ausgang sowie echte DB-/Upgrade-/Restore-Tests.
- CI um Architektur, OpenAPI-/Client-Drift, Migration, Secret- und Lizenzprüfungen
  erweitern. KI-Tests verwenden Fake-Provider; echte Aufrufe bleiben Opt-in.

**Erste Produktabnahme:** Eine frische Installation synchronisiert Keycloak-Identitäten,
veröffentlicht einen versionierten Workflow mit Auswahlfeldern, bearbeitet Aufgaben
gleichberechtigt in Flowzer und einem Host und setzt einen begrenzten KI-Task samt
Werkzeugfreigabe nach einem Neustart korrekt fort. Diese Abnahme ist noch offen.
