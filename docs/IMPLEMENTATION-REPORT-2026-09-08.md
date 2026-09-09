# Umsetzungsbericht – 8. September 2026

## Ergebnis

Dreiundzwanzig aufeinander aufbauende Teilpakete der freigegebenen Flowzer-Roadmap sind
implementiert und lokal getestet; ein vierundzwanzigster Security-Slice ist in Arbeit.
Der **gesamte M0–M6-Produktplan ist noch nicht umgesetzt**. Alle Änderungen liegen in
Topic-Branches/PRs nach `main`; kein Merge, kein Produktivdeployment, keine Änderung
produktiver Benutzer oder Datenbanken.

| Teilpaket | Ergebnis | PR |
| --- | --- | --- |
| Abschlussrechte | Beide HTTP-Abschlussrouten autorisieren dieselbe tatsächliche Aufgabe innerhalb des bestehenden Mutationszyklus. Fremde Aufgaben liefern 404; verifizierter Akteur getrennt von Formulardaten. | [#177](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/177) |
| Instanzrechte | Vertrauenswürdiger Initiator über Issuer/Subject, datensparsame Vorgangsübersicht für Antragsteller/Bearbeiter und getrennte Betriebsdiagnose. Die Konsole fordert ohne Recht keine Diagnosedaten an. | [#179](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/179) |
| Formularstände | Deployment bindet konkrete Formular-Snapshots. Spätere Fassungen oder Umbenennungen ändern laufende und später aktivierte Aufgaben nicht. | [#181](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/181) |
| Formularprüfung | Begrenztes serverseitiges Prüfprofil für Pflichtfelder, Typen, Bereiche, statische Auswahl, Sichtbarkeit und Datumsvergleich. Read-only-Schutz und Feldfehler ohne Eingabeverlust. Zwei Beispielskripte durch deklarative/benannte Serverregeln ersetzt. | [#183](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/183) |
| Aufgabenidentität | Bestehende Task-IDs/Zuweisungen bleiben über parallelen Fortschritt, Timer und Neuladen erhalten. Nur neue Tokens bekommen neue IDs; erledigte Aufgaben werden gezielt entfernt. | [#185](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/185) |
| HTTP-Idempotenz | Direkte Starts und beide Abschlussrouten erhalten akteurs-/ressourcengebundene, persistente Wiederholungen; Inhaltswechsel liefert 409. | [#187](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/187) |
| Browser-BFF | Vertraulicher OIDC-Code-Flow, kurzlebige HttpOnly-Cookie-Sitzung, Origin-/CSRF-Schutz, minimale Sitzungsprojektion und weiter kompatible Bearer-API. | [#189](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/189) |
| Keycloak-Verzeichnis | Lesender, paginierter und atomar veröffentlichter Verzeichnisabgleich mit stabilen lokalen IDs, Hierarchie, Deaktivierungshistorie und Mehrprozess-Lease. | [#191](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/191) |
| Typisierte Verzeichnissuche | Workflowgebundene Suche und Prüfung stabiler Benutzer-/Gruppenreferenzen ohne Namensfallback. | [#193](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/193) |
| Aufgaben-Zuweisungsvertrag | Explizite Wahl zwischen bewahrtem Freitextmodus und stabilen Directory-Referenzen samt serverseitiger Deployment-/Rechteprüfung. | [#195](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/195) |
| Modelerauswahl | Diagramm und Gliederung pflegen denselben Text-/Directory-Vertrag mit Such-, Lade-, Fehler- und historischen Warnzuständen. | [#197](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/197) |
| Formular-Identitätsfeld | `flowzer.forms/2` ergänzt gebundene Einzel-/Mehrfachauswahl von Benutzern und Gruppen samt serverseitiger Filter- und Submission-Prüfung. | [#199](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/199) |
| Ordnerrechte | Ordnerberechtigungen verwenden dieselben stabilen Directory-Referenzen und behalten den ausdrücklich gewählten Freitextmodus. | [#201](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/201) |
| Aufgabenentwürfe | Private serverseitige Entwürfe mit Größen-/Feldgrenzen, optimistischer Revision, Wiederaufnahme und Konfliktdarstellung. | [#203](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/203) |
| Human-Task-Lifecycle | Claim, Release, Operator-Zuweisung und berechtigte Delegation mit tatsächlichem Bearbeiter, Revision und Auditspur. | [#205](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/205) |
| Fristen und Meldungen | Einmalig gebundene UTC-Termine, nachholbarer Scheduler sowie persistenter, deduplizierter und objektberechtigter In-App-Feed. | [#207](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/207) |
| Formularvertragsvektoren | Ein gemeinsamer versionierter JSON-Katalog sichert Profile 1/2 in .NET und Vitest; Directory und Serverberechnungen bleiben ausdrücklich serverautoritativ. | [#209](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/209) |
| Formularpflege | Gemeinsamer CAS-Entwurf, lokale Vorschau und ausdrückliches servervalidiertes Publish; konkrete Versionen sind unveränderlich, PostgreSQL veröffentlicht atomar. | [#211](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/211) |
| Formular-Kompatibilität | Modellierergeschütztes, datensparsames Inventar jeder veröffentlichten Fassung und des Autorenentwurfs mit isolierter Prüfung und stabilen Migrationscodes. | [#213](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/213) |
| Wiederholbare Formulargruppen | `flowzer.forms/3` bindet Datagrids, sichere Hilfetexte, Zeilengrenzen sowie indexierte Serverfehler durchgängig an Submission, Entwurf und Kontextprojektion. | [#215](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/215) |
| Entscheidungsaktionen | `flowzer.forms/4` bindet fachliche Human-Task-Aktionen an den veröffentlichten Snapshot; Browserwerte können feste Belegungen nicht ändern, die Konsole rendert und pflegt den Vertrag. | [#217](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/217) |
| Headless-TypeScript-SDK | `@flowzer/sdk` kapselt Aufgaben, Formulare, Entwürfe, Aktionen, gebundene Verzeichnissuche und Vorgangsstatus ohne Host- oder UI-Abhängigkeit; ein objektberechtigter Task-Deep-Link ergänzt den OpenAPI-Vertrag. | [#219](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/219) |
| React-Integrationsbausteine | `@flowzer/react` ergänzt darstellungsfreie Hooks und Controller mit sicheren Installations-/Sitzungs-Caches, bewusst nicht wiederholten Task-Mutationen und neutralem Formularadapter; eine unabhängige Host-Fixture kompiliert ausschließlich gegen öffentliche Pakete. | [#221](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/221) |
| CodeQL-/Storage-Härtung | Offene Deserialisierungs-, Log-, Codegenerierungs-, Revisions- und SDK-RegEx-Befunde werden ohne Suppression geschlossen; polymorphe Arbeitsdaten werden durch konkrete Dokumente und stabile Referenzen ersetzt. | [#223](https://github.com/flowzer-io/flowzer-bpmn-core-engine/pull/223) |

Die PRs sind gestapelt: **177 → 179 → 181 → 183 → 185 → 187 → 189 → 191 → 193 → 195 → 197 → 199 → 201 → 203 → 205 → 207 → 209 → 211 → 213 → 215 → 217 → 219 → 221 → 223**. Deshalb zeigen spätere
PRs bis zum Merge ihrer Vorgänger auch deren Änderungen. CI-Ergebnisse und
slice-spezifische Testnachweise stehen jeweils im PR. Die freigegebene finale
Zusammenführung erfolgt erst nach Umsetzung der verbleibenden Pakete und dem
abschließenden Astra-/High-Gesamtreview.

## Nachweise

- Aktuelle lokale .NET-Suite einschließlich #222: **111 Engine + 674 API-/Storage-Tests bestanden**,
  keine übersprungenen Tests; einschließlich isolierter PostgreSQL-Integration,
  Rechte-Negativfällen, Formular- und OpenAPI-Regressionsfällen.
- React-Konsole einschließlich Security-Codegenerator: **289 Tests**, Typecheck und Build erfolgreich;
  Lint ohne Fehler, acht bestehende Warnungen.
- Headless-SDK: **13 Tests**, Typecheck, Build, OpenAPI-Neugenerierung,
  Paket-Trockenlauf und npm-Audit ohne Befund erfolgreich.
- React-Integrationspaket: **9 Tests**, Typecheck, Build, Paket-Trockenlauf und
  npm-Audit ohne Befund; eine unabhängige Host-Fixture kompiliert erfolgreich gegen
  `@flowzer/sdk` und `@flowzer/react`.
- Lokale Playwright-Suite auf dem Formular-Slice: **29 Tests bestanden**. Insbesondere
  Feldfehler/Fokus/Eingabeerhalt, Aufgaben-/Startformulare und Vorgangsübersichten.
- Vorgangsübersichten auf Desktop und Mobil visuell geprüft. Das ersetzt noch nicht
  den vollständigen M4-UX-Audit aller Modellierungs- und Betriebswege.
- Neue Regressionen zuerst rot, danach implementiert; Testzweckprüfung und
  `git diff --check` erfolgreich. Bestehende Nullable-/Obsoleszenz- und Vite-
  Chunkwarnungen wurden nicht als neue Fehlerfreiheit der gesamten Codebasis ausgegeben.
- Frühere gezielte Astra-/High-Teilreviews prüften Idempotenz und den BFF-Slice.
  Beim Idempotenzpfad wurden offene Dateireservierungen, mathematisch gleiche
  JSON-Zahlen sowie der Projektstatus korrigiert und durch Fault-Injection-,
  PostgreSQL-Rollback- und Retentionstests abgesichert. Der BFF-Teilreview fand und
  behob die an das Access-Token gebundene, nicht gleitende Sitzungsdauer, Logout für
  angemeldete Konten ohne Fachrolle, fehlgeschlagene Logout-Anzeige sowie abgeschaltete
  BFF-Routen. Negativtests decken Signatur und CSRF ab. Unmittelbarer Provider-Widerruf
  vor Tokenablauf, echter Keycloak-Code-Flow und Keyring-Restore bleiben Abnahmen.
- Der vereinbarte **finale Astra-/High-Gesamtreview aller M0–M6-Punkte ist noch nicht
  erfolgt**. Er findet erst nach Abschluss der Implementierung statt und darf nicht
  durch die früheren Teilreviews ersetzt werden.

## Bewahrte Produktentscheidungen

- Flowzer bleibt eigenständig, modular und unter MPL-2.0; kein Rewrite und keine
  Abhängigkeit von einer konsumierenden Fachanwendung. Erste Kundenstufe mit getrennter Installation.
- Bei Task-Zuweisungen bleibt **Text ausdrücklich erhalten**. Die spätere Auswahl
  „Bekannter Benutzer / bekannte Gruppe“ oder „Text-String“ ist in Roadmap und
  Auth-Epic verbindlich ergänzt. Kein stilles Umwandeln gleichnamiger Texte in IDs.
- Der Mobil-PR #153 wurde gegen `main` auf Überschneidungen geprüft, nicht dupliziert
  oder ungefragt gemergt. Prozessverbund #154 bleibt hinter lokalen Call Activities
  und Fehlerbehandlung eingeordnet.
- Die Bestandsissues #93–#96 und #98 wurden bereinigt bzw. mit Teilpaketen verknüpft.

## Vor Installation oder Upgrade beachten

1. **Formular-Kompatibilität:** `flowzer.forms/1` ist keine vollständige Form.io-
   Unterstützung. Container/Datagrids, Verzeichnis-/Dateifelder, dynamische Quellen,
   Custom-JavaScript und weitere nicht prüfbare Regeln blockieren Veröffentlichung.
   Nicht unterstützte Altschemas können auch laufende Abschlüsse blockieren.
2. **Historische Formularstände:** Externe Altverweise ohne belegten Snapshot werden
   nicht auf das heutige `latest` geraten. Formularinventar und laufende Instanzen
   brauchen eine geprüfte Migration in einer Testinstallation.
3. **Rollen:** `Roles:Operator` ausdrücklich konfigurieren. Der alte permissive
   Vertrag für leere Fähigkeitsrollen ist noch nicht ersetzt.
4. **Persistenz:** Dateiablage besitzt keinen Rollback und bleibt ein
   Einzelprozess-Entwicklungsweg. PostgreSQL-Konkurrenztests belegen die neuen
   Idempotenz-, Directory-, Draft-, Lifecycle- und Deadline-Verträge, aber noch nicht
   jede Runtime-Transition des vollständigen M6-Mehrprozessbetriebs.
5. **Benachrichtigungen:** Der aktuelle Feed ist taskgebunden und wird beim Taskende
   entfernt. Langfristige Vorgangshistorie und externe Zustellung benötigen eigene
   Aufbewahrungs-, Rechte- und Outbox-Verträge.

Keine allgemeine Produktionsfreigabe durch grüne Tests oder diese Teilpakete.

## Nächste Umsetzungsschritte

1. **M0/M1 integrieren:** Gestapelte BFF-/Directory-PRs später in Reihenfolge mergen
   und mit echtem Keycloak, HTTPS, Secret-Store und Keyring-Restore abnehmen.
   Historische Identitätsauflösung und Klärung mehrdeutiger Altwerte bleiben offen.
2. **M2:** Weitere deklarative Regeln, Wiederholgruppen, Anhänge, freigegebene
   dynamische Quellen, Skriptinventar und geprüfte Bestandsmigration.
3. **M3/M4:** Kommentare/Vorgangshistorie, Migration der Flowzer-Konsole auf die
   öffentlichen Integrationspakete, gemeinsame Modellfähigkeiten,
   Laufzeitdiagramme und vollständiger UX-Audit. Flowzer erhält dabei keine Abhängigkeit
   von einer konkreten konsumierenden Fachanwendung.
4. **M5/M6:** Sichere KI-Verbindungen/Werkzeuge/Freigaben/Wiederaufnahme; nötige
   PostgreSQL-, Lease-, Runtime-, Betriebs- und Upgrade-Bausteine vorziehen.
5. **Finale Abnahme:** Direkter Astra-/High-Subagent prüft alle M0–M6-Punkte und
   liefert Korrekturen im gemeinsamen Worktree; danach vollständige Tests,
   Zusammenführung nach `main` und Deployment-Verifikation.

Die erste vollständige Produktabnahme – frische Installation mit Keycloak-Auswahl,
Konsole/Host-Aufgabe und nach Neustart fortgesetztem KI-Task samt Werkzeugfreigabe –
steht weiterhin aus. Verbindliche Details und Abnahmen:
[Produkt-Roadmap](PRODUCT-ROADMAP-2026-09.md).
