# Projektstatus: Flowzer BPMN Core Engine

**Stand:** 8. September 2026; Basis `212705a`, M0/M2-Teilpakete in PR #177, #179, #181 und #183.

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
- OIDC-Anmeldung in der Konsole, JWT-Bearer-Prüfung und Rollen in der API.
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

Das ist **kein vollständiger M0-Abschluss**: Noch fehlen insbesondere
Idempotenzschlüssel und BFF.
Wiederholter Abschluss liefert derzeit `404`, keine idempotente Erfolgswiederholung.
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

## Verbleibende Risiken und Reihenfolge

1. **M0:** BFF
   mit CSRF-Schutz und persistente Idempotenz. Rollen ausdrücklich konfigurieren;
   leere Fähigkeitsrollen bleiben im vorhandenen Vertrag permissiv.
2. **M1/M2:** Keycloak-Verzeichnis, stabile Identitätsreferenzen, generische Auswahl,
   unveränderliche Formularstände und Entwürfe. Namen/kurze Gruppenbezeichnungen
   bleiben bis zur Migration mehrdeutig; historische externe Formularstände benötigen Klärung.
3. **M3/M4:** Stabile Aufgaben-IDs, Übernahme/Delegation, SDK und TickyTask-Einbettung,
   Modellvalidierung und tatsächliche Laufzeithistorie. Mobil-PR #153 nicht duplizieren.
4. **M5:** Begrenzte KI-Tasks mit geprüften Werkzeugen, Freigaben und Wiederaufnahme.
5. **M6 begleitend:** Call Activities/Fehlersemantik, explizite Expressions,
   PostgreSQL-Konfliktschutz, Recovery/Upgrade und Open-Source-Produktreife.

Vorgangsübersichten wurden auf Desktop/Mobil visuell geprüft; 29 Browser-Smokes
sichern Kernwege und Feldfehler. Der vollständige UX-Audit und die erste
Produktabnahme aus der Roadmap stehen weiterhin aus. Details zum bestehenden Betrieb: [OPERATIONS.md](OPERATIONS.md).

## Arbeits- und Release-Modell

Topic-Branches und kleine, testgetriebene PRs gehen nach `main`. `release` wird
nur über einen eigenen PR aus `main` befüllt; dessen Push ist ein Produktivrelease.
Ein Feature-PR allein autorisiert kein Deployment. Keine direkten Writes auf
`main` oder `release` ohne ausdrückliche Freigabe.
