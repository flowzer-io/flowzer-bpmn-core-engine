# Begrenzter stdin-Bootstrap (Source-Prototyp)

## Aktueller Folgevertrag

Der lokale komponierte Source folgt dem [manuellen Start-/Restartvertrag](SUPERVISED-DEMO-BOOTSTRAP.md).
Der isolierte x64-Pilot `39482867524/a1` bestand vollständig; das ist keine
private ARM64-/TT-/Demoabnahme. Die folgende ursprüngliche Prototypbeschreibung
ist historisch zu lesen. Die Produktquelle validiert nun zusätzlich fehlerhafte
Npgsql-Syntax ohne Treiberexception; Details und verbleibender Betriebslauncher
stehen im Folgevertrag.

## Stand und Sicherheitsgrenze

Dieser opt-in API-/Migrator-Bootstrap wurde für den isolierten TT-Demo-Pfad
entworfen. Er ist **keine ausgelieferte Installation**, kein Coolify-Fix und keine
Demo-Abnahme. Ein öffentlicher synthetischer Test hat im nativen Coolify-Fehlerpfad
einen rekonstruierbaren Marker gezeigt. Dieser unveränderte RED-Referenztest bleibt
maßgeblich; dessen Fehlerpfad wird hier nicht nachgebaut oder still als sicher erklärt.

Der Prototyp verarbeitet Secretwerte ausschließlich im Prozessspeicher. Er erzeugt
keine Secretdateien, ENV-Werte, argv-Secrets oder persistierten Konfigurationen.
Ein sicherer externer Feeder, PostgreSQL-Bootstrap, Containerstart und beaufsichtigter
Neustart sind **nicht implementiert**. Es gibt noch keinen freigegebenen Runtimepfad.
Ein Prozessneustart muss in diesem späteren isolierten Demomodell erneut beaufsichtigt
beliefert werden; kein unbeaufsichtigter Start ohne Lieferung oder ENV-Fallback.

## Expliziter Vertrag

- Ohne `--secrets-stdin` bleibt der bestehende Startpfad unverändert; stdin wird nicht gelesen.
- Gewählter Modus: ausschließlich `--secrets-stdin` (API) oder zusätzlich genau einmal
  `--migrate` (Groß-/Kleinschreibung wie bisher). Keine sonstigen CLI-Overrides.
  Falsche/mehrfache `--secrets-stdin`-Schalter werden abgewiesen.
- stdin enthält genau ein UTF-8-JSON-Dokument, danach **EOF**. Version exakt `1`, Rolle
  exakt `api` oder `migrate`, passend zu den Startargumenten. Keine unbekannten oder
  doppelten Schlüssel, Kommentare, nachfolgenden Werte oder ungültigen Typen.
- Maximal 16 KiB einschließlich Whitespace, Tiefe 4, jeder Wert höchstens 4096
  UTF-16-Codeeinheiten, nicht leer/Whitespace und ohne Steuerzeichen. Fünf Sekunden
  Gesamtlesefrist, nicht pro Leseoperation. Offene Pipe ohne EOF beendet den Start.
- API-Felder: `runtimeConnectionString`, `bffClientSecret`, `directoryClientSecret`.
  Sie binden ausschließlich `Storage:PostgreSql:ConnectionString`,
  `Authentication:Bff:ClientSecret`, `IdentityDirectory:ClientSecret`.
- Migratorfeld: ausschließlich `migrationConnectionString`. Dieselbe explizite
  Migrationsidentität bindet beide PostgreSQL-Verbindungsoptionen, keine API-Secrets.
- Jede der vier geschützten Konfigurationsoptionen wird vor dem Apply in **allen**
  vorhandenen Providern auf nichtleere Werte geprüft. Ein auch durch eine höhere
  leere Quelle überdeckter Fremdwert blockiert, auch in verschachtelten
  `ChainedConfigurationProvider`-Wurzeln. Nicht prüfbare Abschnittsketten, Zyklen,
  mehr als acht Kanten Tiefe oder 64 Provider pro Schlüssel werden abgewiesen.
  Leere Vorlagen sind erlaubt;
  Whitespace gilt als vorhandener Wert. Auch rollenfremde DB-/API-Secrets blockieren.
- Anwendung genau einmal als In-Memory-Provider; keine beliebigen Schlüssel oder
  konfigurierbare Allowlist. Fehlende Daten werden niemals aus ENV/Dateien/argv ergänzt.
- Ablehnung: Prozesscode `2` und feste Meldung `FLOWZER_SECRET_INPUT_REJECTED`, ohne
  Parser-, I/O-, Callback- oder Konfigurationsausnahme weiterzugeben. Kein Host-,
  Migrations-, Authentifizierungs- oder Datenbankstart nach Ablehnung.

Alle übrigen **nichtgeheimen** Einstellungen bleiben Sache der bestehenden
Konfiguration. Dieser Bootstrap ändert weder Authentifizierungsregeln noch
Storage-/Migrationsvalidierung. Die spätere Demobindung muss PostgreSQL, getrennte
Rollen, deaktivierte API-Startmigrationen und sämtliche bestehenden Guards belegen.

## Nachweis und Grenzen

`tests/startup-secret-input/` kompiliert die echte Bootstrap-Quelle in einen
paketfreien eigenen CLI-Runner. Der Adapter nutzt das bereits installierte
`Microsoft.AspNetCore.App`-Framework ausschließlich für eigene synthetische
`ConfigurationManager`-/In-Memory-Provider, **ohne** Webhost, Listener, TestServer, Datenbank,
Docker, Keycloak oder Netzwerkdienste. Die Python-Vertragstests benötigen den
absoluten Pfad des selbst gebauten Runner-DLL in `FLOWZER_STDIN_TEST_DLL` und
den expliziten absoluten SDK-Hostpfad in `FLOWZER_STDIN_TEST_DOTNET`.
Das vorhandene SDK aus `global.json` muss verwendet werden; kein SDK-Download.
Ein eigener temporärer CLI-/HOME-/NuGet-/Buildbereich verhindert Änderungen an
fremden Entwicklungsstacks. Offline-Restore mit leerer Paketquellenliste gilt nur
für diesen paketfreien Runner, nicht als Paketgraph-/Auditnachweis der API.

Die Prüfungen verwenden ausschließlich öffentliche synthetische Marker und testen
auch Klartext, Base64 und doppelte Base64 in Exceptionfallen. Rollen, Grenzen,
EOF/Timeout, Duplikate, Providerkonflikte, Einmaligkeit und Callbackfehler werden
prüfbar abgewiesen. Beide Ausgabekanäle werden geprüft, nicht nur die letzte Zeile.
Der Runner gehört derzeit **nicht** zur bestehenden CI; kein CI-Grün behaupten.
Der reine Contract-Runner ist vom folgenden separaten API-Nachweis zu unterscheiden:
Am 20. Oktober 2026 wurde auch `WebApiEngine/Program.cs` tatsächlich als Teil der API
und ihrer zehn Projekt-DLLs kompiliert (Exit 0, 17,024 Sekunden). Dabei erschienen
exakt drei ASPDEPR005-Warnungen zu `KnownNetworks`/`IPNetwork` in der unveränderten
Bestandsdatei `FlowzerForwardedHeadersExtensions.cs`, keine Fehler. Es wird weder
ein warnungsfreier API-Build noch ein früherer Baseline-Buildvergleich behauptet.
Die 17 synthetischen Vertragstests und der einzelne unabhängige gpt-6-sol-Reviewer
mit zwei notwendigen tatsächlichen Deltafollowups waren grün beziehungsweise ohne
offene P1/P2/P3. Die Kompilation ist kein tatsächlicher API-/Migratorstart und
kein Runtime-, Image-, Secretliefer- oder Demoabnahmenachweis.

Die Eingabebytes werden bestmöglich überschrieben. .NET-Strings, JSON-interne Kopien
und spätere Konfigurationswerte sind verwalteter Speicher: vollständige Zeroisierung
oder Schutz vor einem privilegierten Speicherzugriff wird nicht versprochen. Eine
unkündbare Betriebssystem-Leseoperation kann bis zum terminalen Prozessende bestehen;
der Bootstrap wartet darauf nach Ablauf nicht unbegrenzt und startet keinen Host.

Die Prüfung der Bootstrap-Ausgabe beweist **nicht**, dass nachfolgende Treiber,
Authentifizierungsbibliotheken, Anwendungsausnahmen, Betriebssysteme oder ein
späterer Feeder niemals Secrets ausgeben. Vor einer Runtimefreigabe sind deshalb
Source-/Imageparität, tatsächliche Prozess-/Logging-/Fehlerpfade, Ressourcenbesitz,
SDK-/CI-Effekte und ein isolierter synthetischer Durchstich separat zu binden.
Keine privaten Werte oder Produktionsaktionen sind Teil dieser Sourceprüfungen.

## Separates Dependency-Gate

Der erste vollständige API-Dependency-Restore am unveränderten Prototyp scheiterte
korrekt an NU1903/NU1902: MailKit/MimeKit4.18.0 lösten PKCS10.0.0 auf. Die explizite
API-Referenz auf PKCS10.0.13 hebt ausschließlich diesen erlaubten Mindeststand an;
Audit und Warning-as-Error bleiben unverändert. Das ist kein Runtime-/Imagepatch.
Der Regressionstest prüft die Source-Mindestversion. Der separat freigegebene
zweite öffentliche Restore belegte PKCS 10.0.13 als einziges aufgelöstes
Versionsdelta, 42 Pakete ausschließlich von NuGet.org, unverändertes Audit
(true/all/low, NU1903/NU1904 weiterhin Fehler) und keine Auditwarnung. Die effektiven
Sources enthielten ausschließlich NuGet.org; der im ersten Restore unerwartet
angezeigte lokale SDK-library-packs-Feed war in dieser zweiten Invokation über
die vorhandene SDK-Property explizit ausgeschlossen. Original-REDs bleiben erhalten.

Ein Restore ist kein Compile. Vor der separaten echten API-Kompilation wurden die
Rootproperties ausgewertet. `OpenApiGenerateDocuments=false` und
`OpenApiGenerateDocumentsOnBuild=false` waren explizite globale Guards ausschließlich
jener Nachweisinvokationen, keine Projekt-/Produktänderung. Die Imports enthalten
weiterhin einen OpenAPI-App-Exec; dessen Auslassen ist kein bestandener
OpenAPI-Vertrag und keine vollständige transitive Target-/Netzabnahme. Weitere
Binarytests, tatsächlicher APIstart, sichere Secretzufuhr, Downstream-Fehlerpfade,
Feeder/PG/Neustart und TT-Overlay-/Imageparität bleiben gesondert freizugeben.
