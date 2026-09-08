# Roadmap

**Stand: 8. September 2026**

Die freigegebene, führende Produkt-Roadmap steht in
[PRODUCT-ROADMAP-2026-09.md](PRODUCT-ROADMAP-2026-09.md). Sie ersetzt den früheren
Rettungs-/Pilotplan und führt offene Abnahmen ausdrücklich als Checkliste.

## Reihenfolge

1. **M0 – Sicherheit und Verträge:** zentraler Aufgabenabschluss (#176, PR #177),
   objektbezogene Instanzrechte, serverseitige Validierung und Idempotenz sind als
   gestapelte PRs umgesetzt. #188 ist der laufende, noch nicht nach `main` gemergte
   BFF-Slice: vertraulicher OIDC-Code-Flow, `HttpOnly`/`Secure`-Cookies,
   `X-Flowzer-CSRF`, persistenter API-Keyring und kompatibler externer Bearer-Vertrag.
   Bis Merge und HTTPS-/Secret-/Restore-Abnahme ist M0 nicht vollständig geschlossen.
2. **M1/M2 – Verzeichnis und Formulare:** Keycloak, stabile Benutzer-/Gruppenreferenzen,
   generische Auswahlfelder, Versionierung, validierte Eingaben und Entwürfe. Der erste
   M1-Slice #190 / PR #191 implementiert den atomaren, lesenden Keycloak-Abgleich samt
   stabiler Historie, Mehrprozess-Lease und Operatorstatus. #192 / PR #193 ergänzt darauf
   aufbauend typisierte `SubjectRef`-Werte und eine workflowgebundene, aktive Suche für
   berechtigte Modellierende; Formularfeld und Task-Zuweisungsmodus folgen separat.
3. **M3/M4 – Aufgaben und Oberflächen:** Human-Task-Lifecycle, SDK/Einbettung für
   TickyTask, Modellierungsprüfung, Laufzeitdiagramm und belastbare Historie.
4. **M5 – KI-Tasks:** Cloud/lokale Modelle, Secret-Referenzen, begrenzte Werkzeuge,
   parametergebundene Freigaben und sichere Wiederaufnahme.
5. **M6 begleitend:** Runtime, Persistenz, Recovery, Installation und Open Source.
   Notwendige Grundlagen werden vor dem jeweils abhängigen Feature umgesetzt.

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
