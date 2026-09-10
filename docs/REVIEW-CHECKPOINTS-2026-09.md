# Gesamtprüfung der Checkpoints und weitere Umsetzung

**Stand:** 10. September 2026. **Reviewbasis:** `212705a..8d493f7` plus die hier
zugehörigen Reviewkorrekturen. **Aktueller Integrations-PR:** #255.

## Einordnung

Die sechs Checkpoints #189, #201, #217, #227, #237 und #255 enthielten vor diesen Korrekturen zusammen
577 geänderte Dateien; der Umfang umfasst auch generierte OpenAPI-/SDK-Verträge,
Lockfiles, Tests und Dokumentation. Das ist ein Produktprogramm, kein einzelnes
fertig abgenommenes Feature. Grüne Tests belegen konkrete Fälle, nicht vollständige
BPMN-Abdeckung oder die erste Kundeninstallations-Abnahme.

Geprüft wurden die aktuelle kumulative Implementierung, ihre Paket-Issues und
PR-Kommentare. Historische Einzel-PRs bleiben Nachweise ihrer damaligen Slices;
Korrekturen in späteren Checkpoints machen ältere Branchköpfe nicht rückwirkend sicher.

## Belegte und testgetrieben korrigierte Befunde

| Priorität | Bereich | Befund | Korrektur / Beweis |
| --- | --- | --- | --- |
| P0 | M0/M6 Installation | Die Compose-Vorlagen ließen Zugangs-/Modeler-/Worker-Rollen fehlen oder leer; die Legacy-Policies erteilten dadurch rein angemeldeten Benutzern zu weitgehende Rechte. | Nichtleere sichere Rollen-Defaults in beiden Vorlagen, explizite Overrides weiterhin möglich. Echte Compose-Interpolation mit fehlenden, leeren und individuellen Werten getestet; Upgrade-Rollenzuordnung dokumentiert. |
| P1 | M3/M6 Containerkonsole | Die ausgelieferte API-Adresse `/` wurde intern leer und vom SDK beim Provideraufbau abgelehnt. | Adapter übergibt den expliziten Wurzelpfad an das SDK; zunächst roter Test mit echtem Runtime-Loader und Request auf `/usertask`. |
| P1 | M5 XML-Vertrag | Validator und Runtime erkannten I/O über LocalNames bzw. rekursive Suche; fremde Namespaces und zusätzliche Verträge konnten Datenzuordnungen erweitern. | Exakte direkte Zeebe-Mappings, genau ein KI-Mappingvertrag, alle Einträge vollständig. Acht zunächst rote Parser-/Deploymentfälle, einschließlich Wrapper, Duplikaten und unvollständigen Zusatzeinträgen. |
| P1 | M5 Installation | Executor-Opt-in fehlte in beiden Compose-Verträgen; Coolify fehlten außerdem KI-Rollen und Datenfluss-Opt-ins. | Konfiguration vollständig durchgereicht; alle Datenfluss-/Ausführungsschalter bleiben standardmäßig aus. Tests prüfen explizite Aktivierung ohne Provideraufrufe. |
| P2 | M1/M2 Directory-Anzeige | Ein aktives, per Gruppenfilter erlaubtes Formularmitglied verlor beim Neuladen seine Beschriftung, weil die Auflösung nur direkte Policy-IDs akzeptierte. | Heute weiterhin auswählbare IDs werden nach demselben Serverfilter aufgelöst; fremde und inaktive ungebundene IDs bleiben verborgen. Zwei zunächst rote HTTP-Fälle für Startformular und gespeicherten Task-Entwurf. |
| P1 | M1/M3/M0 | Nach Claim oder administrativer Zuweisung genügte der gespeicherte Besitzerschlüssel. Eine inzwischen deaktivierte Directory-Identität konnte mit gültigem JWT weiter Formular, Entwurf und Vorgang lesen sowie abschließen. | Gemeinsame Prüfung des tatsächlichen Bearbeiters gegen aktive stabile Directory-ID und exaktes Issuer/Subject. Auch Textmodelle mit späterer Directory-Zuweisung sind erfasst. Vier API-Regressionsfälle für beide Abschlussrouten; Operator-Freigabe bleibt möglich. |
| P1 | M3 React | Ein Task-Refetch mit 401/403/404 behielt bei React Query die alten erfolgreichen Daten. Der Workspace behandelte deren `canWork=true` weiter als gültig und lud Inhalte erneut. | Definitiver HTTP-Rechteverlust aus Task, Formular oder Entwurf sperrt Taskprojektion und Arbeitsrecht bereits während Retries, entfernt Inhaltscaches und hält die Sperre im Sitzungscache auch über Navigation/Remount bis zu vollständig erfolgreichem explizitem Reload. Drei zunächst rote Task-Refetch-Tests; die unabhängige Gegenprüfung ergänzte neun zunächst rote Fälle für Unterabfragen, Retry-Pausen und geprüfte Wiederaufnahme. Die sechs Unterabfragefälle beweisen außerdem die Sperre nach Remount. |
| P1 | M3 React | Query-Keys waren sitzungsgebunden, lokaler React-State aber nicht. Beim Wechsel von Sitzung oder Installation konnten Eingaben derselben Task-ID aus dem alten Kontext erhalten bleiben. | Der Provider setzt seinen Teilbaum bei Wechsel des opaken Scopes neu auf. Zwei zunächst rote Regressionen für Sitzung und Installation; unveränderter Scope bewahrt Eingaben. |
| P2 | M3 Claim-Nebenweg | Ein deaktivierter tatsächlicher Directory-Bearbeiter eines offenen Textmodells erhielt beim erneuten Claim noch `409` statt verborgenem `404`. | Sonderpfad für konkurrierende Kandidaten schließt diesen entzogenen Besitzer aus; Operatorrechte bleiben erhalten. Zwei zunächst rote API-Fälle sind korrigiert. |
| P2 | Planung | Die führende Roadmap und KI-Dokumentation enthielten veraltete aktive Slices, einen widersprüchlichen Hinweis zur historischen Verbindungsbindung und eine pauschale spätere Merge-/Deployment-Freigabe, die für diesen Reviewauftrag nicht gilt. | Aktuellen Auftrag, sechs Checkpoints, Integrationsgrenze und getrennten Releasepfad ausdrücklich festgehalten. |

Die Directory-Korrektur ändert nicht die bewusste Freitextsemantik. Reine Text-Claims
funktionieren ohne Verzeichnis; sie werden nicht heimlich einer gleichnamigen Person
zugeordnet. Das bisherige Initiatorrecht auf eigene Vorgänge bleibt ein separates Recht.
Ein deaktivierter Bearbeiter verliert keine gespeicherte Historie, sondern das Arbeitsrecht.

## Prüfflächen und bewusste Grenzen

| Checkpoint | Prüfgegenstand | Noch keine Abnahme dafür |
| --- | --- | --- |
| M0 / #189 | Gemeinsamer Abschluss, Objektberechtigung, Identitätsherkunft, BFF/CSRF, idempotente Mutationsgrenzen und Formularbindung | Echter HTTPS-OIDC-Roundtrip, sichere Zielkonfiguration und Keyring-Restore |
| M1 / #201 | Atomarer Verzeichnissnapshot, Pagination-/Teilfehlerverhalten, aktive Auswahl, exakte Identitäten, Text-/Directory-Modi | Last-/Abnahmetest mit realer IdP-Struktur und explizite Migration mehrdeutiger Legacy-Texte |
| M2/M3 / #217 | Vertragscompiler/-validator, Read-only-/Outputgrenzen, CAS-Entwürfe, Claim/Delegation und persistente Task-ID | Sichere Anhänge, generische externe Datenquellen, fachliche Bestandsmigration |
| M3 / #227 | SDK-Transport, Query-Scope, lokale Formzustände, Rechteverlust, Vorgangsprojektion | Vollständiger externer Host-/Audience-/Session-Abnahmelauf |
| M4 / #237 | Runtime-Ereignisse, objektberechtigte Diagramme, Markerzählung und I/O-Snapshots, Modellierungsgrenzen | Lückenlose Kurzzeit-/Loop-Historie, vollständiger UX-/Tastaturaudit und belastbare Kennzahlen |
| M5 / #255 | Pinned Verbindungen, Secretgrenze, Provider-/Schemaschicht, Lease/Recovery, atomarer Engine-Commit und geschlossene Werkzeugverträge | Werkzeugausführung, persistentes Aktionsjournal, konkrete Freigaben und unklarer externer Ausgang |

Insbesondere bleibt der Werkzeug-Deploymentblocker richtig und wird nicht allein
wegen vorhandener Auswahloberflächen entfernt. Dateiablage ist nur Entwicklung,
kein ersatzweiser Mehrprozess-/Rollbacknachweis. Leere historische Modeler-/Operator-/
Worker-Rollen außerhalb der nachgeschärften Compose-Vorlagen bleiben permissiv; eine
Kundeninstallation benötigt überprüfte Rollen und IdP-Zuordnungen. Das gehört in die nächste Installations-Sicherheitsabnahme.

## Verifikation

Aktueller lokaler Prüfstand nach den Rechte-/React-Korrekturen:

- 180 Engine-Tests und 892 API-/Storage-Tests, **keine übersprungenen Tests**;
  darunter echte isolierte PostgreSQL-Integrations- und Konkurrenzfälle.
- 347 Konsolen-, 25 SDK- und 34 React-Pakettests.
- TypeScript-Typprüfung, ESLint ohne Fehler, Produktionsbuild,
  Gateway-Routen- und Testzweckprüfung sowie sechs neue Compose-Vertragstests erfolgreich.
- Release-Build erfolgreich; fünf vorhandene .NET-Warnungen und sieben bekannte
  Fast-Refresh-Warnungen bleiben sichtbar. Keine pauschale Formatierung fremder Dateien.
- 33 Browser-Smokes im abschließenden vollständigen Lauf erfolgreich. Ein erster Lauf
  hatte einen Read-only-Modeler-Lade-Timeout; beide Read-only-Fälle bestanden anschließend
  dreifach isoliert. Ein zusätzlicher Lifecycle-Test sichert den XML-Neuimport nach
  Rollenwechsel ab. Der Timeout ist nicht reproduziert und deshalb nicht als behoben
  verbucht; es wurden weder Testtimeout noch Produktcode auf Verdacht geändert.

Die erste unabhängige Codex-Gegenprüfung bestätigte zusätzliche Lücken bei
Unterabfragen/Retry-Pausen und dem Claim-Nebenweg; die bereits gestartete Nachprüfung
ergänzte den Verlust der Sperre bei Workspace-Remount. Diese Hinweise wurden gegen den
Code reproduziert und testgetrieben korrigiert, nicht ungeprüft übernommen.
Der Zweier-Orchestratorlauf war wegen des ausgefallenen zweiten Providers insgesamt
unvollständig; er gilt nicht als bestandene Zweierfreigabe. Die breitere zweite
Codex-Gegenprüfung lieferte die oben dokumentierten fünf Integrations-/Vertragsbefunde
und endete erfolgreich. Sie wurden ebenfalls selbst reproduziert und korrigiert.
Die beiden unabhängigen Prüfungsperspektiven sind damit dokumentiert; die zuletzt
korrigierten Stellen wurden auf Christians Wunsch ohne weiteren externen Reviewlauf
selbst geprüft und erneut getestet.

Fremdprovider waren wegen Nutzungs-/Reservelimits nicht verfügbar. Astra war im
Wrapper-Modellkatalog nicht auswählbar; es wurden keine Reserven aufgehoben und
keine direkten Provideraufrufe verwendet. Auf Christians Ergänzung hin werden
**keine weiteren Reviewläufe gestartet**. Verbleibende bestätigte Korrekturen werden
im laufenden Auftrag selbst geprüft und getestet.

## Integrationsstrategie für die vorhandenen Pakete

1. Keine weiteren Feature-Branches auf die vorhandene Kette stapeln.
2. Reviewfixes und ihre Regressionen am Gesamtstand in #255 sammeln; alle Gates
   dort erneut prüfen. Offene Reviewbefunde sind Mergeblocker, nicht nur Hinweise.
3. Bei späterer ausdrücklicher Freigabe die sechs Checkpoints in der dokumentierten
   Reihenfolge integrieren. Dafür Merge-Commits statt einzelner Squash-Merges
   verwenden, damit Folgediffs bereits übernommene Änderungen nicht nochmals als neu zeigen.
4. Zwischenstände nicht produktiv ausrollen: manche Sicherheits-/Buildkorrekturen
   existieren erst in späteren Checkpoints. Soll ein Checkpoint einzeln freigegeben
   werden, müssen die relevanten Folgefixes vorher zurückübernommen und separat geprüft werden.
5. Nach vollständiger Integration erneut CI, Migrationen und Browser-Smokes prüfen.
   #153 bleibt ein eigener Mobil-PR und wird anschließend gegen diesen Stand geprüft.
6. `release` ausschließlich separat nach Installations-/Restore-Abnahme und expliziter
   Freigabe aktualisieren. Dieser Auftrag führt weder Merge noch Deployment aus.

Der lokale Abstammungscheck bestätigt diese Reihenfolge. Die einzige Abweichung
zwischen #217 und #227 ist derselbe UI-Smoke-Fix als Cherry-pick (`b64b160`/`544b30e`).
`git merge-tree` ergibt konfliktfrei exakt den Baum von #227; es wurde dabei kein
Branch verändert. Die drei historischen CodeQL-Kommentare an #217 betreffen bereits
in dessen Folgepaket #223/#227 gehärtete Stellen. Sie sind nicht durch eine Änderung
am älteren #217-Kopf erledigt und müssen bei der Integration entsprechend nachvollziehbar
zugeordnet werden.

## Nächste Pakete: zuerst ein nutzbarer, sicherer Installationspfad

Die folgende Reihenfolge ersetzt keine Fachabnahme. Sie macht jeweils ein Ergebnis
klar prüfbar. Neue Issues sollen vor der Umsetzung aus diesen Zuschnitten entstehen,
mit Nicht-Zielen, Tests, Abhängigkeit, Zuständigkeit und einem eigenen kleinen PR.
Bestehende Epics #93–#96 und #98 bleiben führend; keine doppelten Sammelissues anlegen.

### R1 – Installations- und Auth-Abnahme (zu #94/#95)

- Isolierter Keycloak-/HTTPS-Testaufbau mit synthetischen Personen und Gruppen.
- BFF-Login, CSRF, Logout, falsche Audience, Rollenentzug und Directory-Deaktivierung
  über Konsole **und** Bearer-API prüfen.
- Fehlende privilegierte Rollen im Installationscheck erkennen; einen etwaigen
  Legacy-Kompatibilitätsmodus ausdrücklich statt still erlaubend konfigurieren.
- Persistenten Keyring und Secret-Store prüfen; keine produktiven Secrets in Tests.
- **Fertig:** reproduzierbarer Installations-Golden-Path und dokumentierter Negativlauf.
- **Nicht:** neue Fachfunktionen, Mehrmandantenhosting, produktive IdP-Änderungen.

### R2 – PostgreSQL-Upgrade und Wiederherstellung (zu #95)

- Vorherige Schemafassung mit laufenden Tasks/Entwürfen/Timern/Jobs aufbauen,
  vorwärts migrieren, Backup in eine frische Datenbank wiederherstellen.
- Leases, Formularbindungen, aktive Verzeichnisse, Idempotenz und KI-Resultate nach
  Wiederanlauf vergleichen. Kein Restore in die Quellinstallation.
- **Fertig:** automatisierter Upgrade-/Restore-Nachweis; Konkurrenztests schlagen bei
  fehlendem PostgreSQL in diesem verpflichtenden CI-Job fehl statt still zu überspringen.
- **Nicht:** genereller Nachweis für beliebigen Mehrprozessbetrieb.

### R3 – Vollständiges generisches Beispiel und Bedienungsabnahme (zu #98)

- Urlaubsantrag mit typisierter Vertreterauswahl, Datumsregeln, Entscheidungen,
  Claim, Entwurf und Wiederaufnahme; Freitextmodus weiterhin separat demonstrieren.
- Desktop/Mobil, Tastatur, Fokus, Fehlerzusammenfassung, Sessionwechsel und Rechteverlust.
- Dasselbe Beispiel in der vorhandenen unabhängigen React-Host-Fixture bearbeiten.
- **Fertig:** nachvollziehbare Demo plus reproduzierbare UI-/API-Abnahme.
- **Nicht:** Personalverwaltung oder automatische Rechtevertretung.

## Danach: KI-Runtime in vier getrennten, sicher gesperrten Schritten

### K1 – Persistentes Werkzeug-Aktionsjournal

Eindeutige Aktion je Lauf/Schritt, kanonischer Parameterhash, Werkzeug-/Vertragsversion,
Revision und Zustände für geplant, freigabepflichtig, genehmigt, laufend, bestätigt,
unklar und abgelehnt. Test zuerst: Konkurrenz, Neustart und unbekannter Schreibausgang.
Noch **keine** externen Werkzeuge freischalten.

### K2 – Objektberechtigte parametergebundene Freigaben

API, Audit und minimale UI für konkrete Aktionen. Ändert sich ein Parameter, gilt die
alte Freigabe nicht mehr. Verbindung, Workflow, Werkzeugrechte und Fachkontext bleiben
Serverentscheidungen. Tests: fremde Freigabe, doppelte Entscheidung, Entzug und Parameterwechsel.
Noch **keine** freie Tool-Call-Schleife.

### K3 – Begrenzte Tool-Call-Ausführung mit Fake-Provider

Providerfähigkeiten explizit prüfen; feste Schritt-/Zeit-/Tokenbudgets, Registryauflösung
und sichere Wiederaufnahme. Zunächst ein deterministisches Read-only-Werkzeug und ein
Fake-Schreibwerkzeug mit idempotentem Empfänger. Unklare Effekte anhalten. Deployment-
Blocker erst nach Ende-zu-Ende-Nachweis für diese ausdrücklich unterstützte Teilmenge lösen.

### K4 – Vorschau und Störungsbedienung

Seiteneffektfreier Testmodus, sichtbare Run-/Werkzeugzustände, sichere Wiederholung,
Abbruch und Entscheidung bei unklarem Ausgang. Keine geschätzten Kosten ohne Preisquelle.
Erst danach ein reales, eng freigegebenes Konnektorwerkzeug als eigener Slice.

## Verbleibender Backlog: fachlich getrennt halten

| Strang | Nächster begrenzter Zuschnitt | Abhängigkeit |
| --- | --- | --- |
| Runtime / #93 | Lokale Call Activity mit gebundener Definition und I/O, danach separat Boundary Error/Eskalation | Vor Prozessverbund #154; vollständige Kompensation später |
| Expressions / #93 | Explizites, mit Deployment gebundenes Profil ohne stillen V8-Fallback | Vor breiter Kundenmigration |
| Konkurrenz / #95 | Timer gegen Abbruch, Human Task gegen Worker/KI, Lock-Reihenfolge und Zustandsrevisionen über mehrere Prozesse | Vor Freigabe mehrerer API-Replikate |
| Benachrichtigung / #98 | Externe Zustellung über dauerhafte Outbox und deduplizierenden Empfänger | Vor verbindlichen E-Mail-Erinnerungen |
| Kommentare / #98 | Task-/Vorgangskommentar mit eigener Sichtbarkeit, Audit und Aufbewahrung | Nicht mit technischem Eventlog vermischen |
| Anhänge / #98 | Uploadquarantäne, Größen-/Typgrenzen, Prüfung und objektberechtigter Download | Speicher-/Retention-Konzept zuerst |
| Dynamische Auswahl / #98 | Administrative Read-only-Datenquelle und feldgebundene Suche | Kein freies HTTP aus dem Formular |
| Modellierung / #98 | Versionen vergleichen und zusammengehörige BPMN-/Formularpakete atomar veröffentlichen | Unveränderliche Bindungen erhalten |
| Diagrammmetriken / #98 | Persistierte Dauer-/Wartezeitdaten mit Stichprobe und Rechtefilter | Erst ausreichende echte Historie, keine Scheingenauigkeit |
| Produktpflege / #96 | Modulsteckbriefe, gezielte Aufteilung, reproduzierbarer Prüfentrypoint, SBOM/Lizenzen/Sicherheitsmeldestelle | Keine kosmetische Komplettformatierung |
| Paketexport / #98 | BPMN, Formulare und Fähigkeiten versionieren; Import mit expliziten Identitäts-/Verbindungszuordnungen | Keine Secrets exportieren |

**Arbeitsregel:** höchstens ein neues fachliches Paket gleichzeitig bis zur Abnahme.
Ein Status nennt implementiert, getestet, reviewt, integriert und ausgerollt getrennt.
Neue Anforderungen erweitern nicht still den laufenden Slice; sie erhalten einen
benannten Folgepunkt. So bleibt der nächste Zwischenstand abnehmbar.
