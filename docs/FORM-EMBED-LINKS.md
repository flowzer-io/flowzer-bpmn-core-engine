# Persönlicher Read-only-Formulareinstieg und isolierte Formularansicht

Status: **API und separater Produktionsrenderer vorhanden; noch nicht in TT
angebunden oder ausgerollt.** Der persönliche Hostkanal wird mit synthetischen
Backend-Antworten geprüft. Echte Container-/HTTPS-/Keycloak-Abnahme einschließlich
realer Token-Erneuerung und mindestens 45 Minuten Bearbeitung bleiben zwingende
Folge-Gates. Das Installations-Opt-in bleibt standardmäßig geschlossen und wird
noch nicht in Compose aktiviert.

## Vertrag

### Versionsbindung beim Workflowstart

Der Host verwendet die tatsächlich angezeigte `deployedId` des Katalogs als
`expectedDefinitionId`: beim Startformularabruf als Queryparameter und beim
`POST /definition/meta/{id}/instance` im Body. Eine Abweichung liefert vor
Fachmutationen `409` mit `workflow.definition_changed`; das Formular muss
ausdrücklich neu geöffnet werden. Identische persönliche Idempotenz-Replays
geben dagegen die bereits gestartete Instanz in ihrer ursprünglichen Version
zurück, auch nach einem Deployment. Eine andere Versionskennung mit demselben
Schlüssel ist anderer Requestinhalt und wird abgewiesen.
Der neue Hashinhalt ist außerhalb des frei belegbaren JSON domänensepariert;
Legacy-Variablen können ihn nicht imitieren. Der persönliche Schlüssel-Scope
bleibt gemeinsam, damit ein Vertragswechsel keine zweite Instanz erzeugt.

Die Bindung bleibt für bestehende Aufrufer optional; der neue TT-Host muss sie
immer senden. Ohne Versionsbindung bleiben auch bestehende Requesthashes gleich.
Die TT-seitige 24-Stunden-Grenze für unklare Starts ist damit noch nicht gebaut.
Der vorhandene generische Flowzer-Idempotenzspeicher behält seine Aufbewahrung.
Das Startformular selbst hat weiterhin keinen persistenten Benutzerentwurf.

### Persönliches Zurückziehen

`POST /instance/{id}/withdraw` beendet nur den eigenen direkten Start. Der
verifizierte Initiator wird unter Engine-/Instanzsperre ordinal an Issuer und
Subject gebunden; Namen, gleichlautende technische GUIDs aus anderem Issuer und
Betriebsrechte sind kein Ersatz. Intern aufgerufene Kindinstanzen sind keine
persönlichen direkten Starts und werden identisch zu fremden/fehlenden Vorgängen
mit 404 abgewiesen. Der Rückzug des gesamten Elternvorgangs beendet seine Kinder.

Der erste tatsächliche Akteur, seine Benutzer-GUID und UTC-Zeit bleiben intern
am Master-Token auditiert. Öffentlich liefert `wasWithdrawn` nur den Status;
der Mutationsweg enthält auch für Operatoren keine Tokens, Variablen oder
Auditidentität. Wiederholung bestätigt denselben Auditfakt; fachlicher Abschluss
oder Betriebsabbruch ist ein expliziter 409. Technische Engine-/Ablagefehler
bleiben technische Fehler und werden nicht als definitiver Fachkonflikt maskiert.
Bereits ausgeführte Außenwirkungen werden nicht zurückgerollt.

PostgreSQL bindet Zustand und Kindabbruch an dieselbe Transaktion. Die nur für
Einzelprozessentwicklung geeignete Dateiablage kann mitten im Abbruch scheitern:
Ein persönlicher Retry repariert deshalb vorhandene terminale Persistenz,
Anmeldungen, deduplizierte Laufzeitereignisse und den Kindbaum idempotent, statt
nur einen gespeicherten Eltern-Audit als vollständigen Erfolg zu bestätigen.
Auch beendete Zwischenknoten werden für ihre Nachkommen besucht. Ein nach dem
Datenbanklock frisch gelesener, schon fachlich abgeschlossener Kindprozess wird
nicht nachträglich terminiert. Weder Kompensation noch automatische HTTP-Retries.

HTTP-Tests prüfen tatsächliche Identität, Umbenennung, Fremd-/Issuer-/Operatorfälle,
Wiederholung, Fachabschluss, technischen Fehler und echte Call Activities samt
partieller Dateipersistenz. Zwei zusätzliche PostgreSQL-Fälle müssen in der CI
gegen den echten Container laufen; lokale HTTP-/Dateitests ersetzen sie nicht.
TT-Endpunkt, TT-Vorgang und Vermittler-Audit sind damit noch nicht gebaut.

### Persönlicher Human-Task-Einstieg

- `POST /usertask/{id}/form-link`: authentifizierter, persönlich berechtigter
  Bearbeiter, Body `{ "hostOrigin": "https://host.example" }`.
- Antwort `url` hat einen zufälligen 256-Bit-Einstieg ausschließlich im Fragment
  von `/embed.html`, `redeemBeforeUtc` ist die **Einlösefrist**, keine Arbeitsfrist.
- `POST /form-embed/redeem`: anonymer, strikt lesender Snapshot, Body mit `secret`.
  Der isolierte Endpunkt akzeptiert `Origin: null`, aber keine CORS-Credentials.
  Kein Secret im Pfad, Queryparameter, Browserstorage, Zugriffstoken oder Host-DB.
- Einlösung wird atomar verbraucht. Auch bei aktuellem Operatorrecht darf kein
  persönlicher Einstieg für fremde Arbeit erstellt werden.
- Aufgabenbindung, Definition, Lifecycle-Revision und aktuelle aktive
  Directory-Identität werden bei Ausgabe **und Einlösung** geprüft. Der neue Pfad
  unterstützt bewusst nur stabile Directory-Zuweisungen, keinen Namensfallback.
- Der Snapshot enthält das gebundene, erneut kompilierte Formular, ausschließlich
  deklarierte Kontextfelder und den privaten Entwurf desselben Akteurs. Keine
  Instanzdiagnose, kein Token oder Mutationsrecht.
- Claim, Release, Entwurf, Directory und Abschluss bleiben unverändert
  authentifizierte API-Operationen. Ein Formularlink ist kein Bearer-Token.
  Eine zwischenzeitlich geänderte Aufgabenrevision erfordert einen neuen Einstieg.

## Lebenszeit

Fünf Minuten gelten nur für die noch nicht erfolgte Einlösung. Nach dem Bootstrap
vermittelt der Host alle Aktionen über seine aktuelle Benutzersitzung. Ein neuer
Zugriffstoken darf das Formular nicht remounten. Aktueller Rechteentzug, Release,
Abschluss oder Abbruch wird dennoch erzwungen; der Host muss den Frame entfernen.

Manuelles Speichern nutzt den bestehenden privaten Human-Task-Entwurf mit Revision.
Unvollständige Pflichtfelder sind dort zulässig; erst der Abschluss validiert sie.
Keine neue Entwurfsablage, kein Autosave und keine Startformular-Entwürfe.

## Konfiguration und Ablage

`FormEmbedding:Enabled=false` ist der Default. Für die spätere geprüfte Aktivierung
werden `PublicOrigin` und `AllowedHostOrigins` als exakte HTTPS-Origins benötigt
(keine Pfade, Wildcards, Queries oder Credentials).

Migration `021_form_embed_grants.sql` ergänzt eine leere PostgreSQL-Tabelle.
Persistiert werden nur SHA-256-Hash und stabile persönliche Bindung, kein Secret
oder Formularinhalt. `DELETE RETURNING` entscheidet die Einlösung zwischen
API-Prozessen, Aufgabenlöschung entfernt Freigaben per indiziertem FK. Erst die
Aufgabe, dann die Freigabe werden gesperrt; ein PostgreSQL-Service-Test erzwingt
einen parallelen Abschluss. Eine sperrfreie Hash-Vorprüfung hält zufällige anonyme
Anfragen aus der globalen Engine-Sperre heraus. Der Einlösepfad hat zusätzlich
eigene Endpoint-gebundene, geordnete Kontingente (standardmäßig zuerst 60 POSTs je
IPv4-Adresse bzw. IPv6-/64 und Minute, danach 600 je API-Prozess, keine Warteschlange),
auch bei deaktiviertem allgemeinem Limiter. Beide Limits sind über
`RedeemPerCallerPermitLimit`/`RedeemGlobalPermitLimit` konfigurierbar. Eine bereits
gedrosselte Quelle verbraucht das Globalbudget nicht weiter; Routingvarianten wie
`/redeem/` haben dieselben Metadaten. Dies ist kein verteiltes Gateway-Ratenlimit.
Hinter Proxys müssen `ForwardedHeaders:KnownNetworks`/`KnownProxies` sowie
`ForwardLimit` für die konkrete vertrauenswürdige Kette gesetzt sein; beliebige
Forwarded-Header sind niemals vertrauenswürdig. Ohne diesen Nachweis teilen alle
Quellen die Proxyadresse. Auch Firmen-NAT teilt eine IPv4-Partition; die Grenzen
sind für die erwartete Nutzung bewusst zu dimensionieren und im echten Gateway
zu prüfen. Ein verteiltes Botnetz oder eine große IPv6-Zuteilung mit mehreren
/64-Netzen kann das Globalbudget weiterhin erschöpfen. Dieses Restrisiko bleibt
ohne zusätzliche Gateway-Abwehr ausdrücklich bestehen; kein zweiter verteilter
Limiter wird für den Demo-Durchstich gebaut.

Höchstens vier noch nicht eingelöste Links bleiben je Aufgabe bestehen; weitere
Ausgaben verdrängen die ältesten Links, niemals ein bereits geöffnetes Formular.
Diese Begrenzung ist bewusst aufgabenweit, nicht persönlich; zwischen Übergabe und
Einlösung kann deshalb ein späterer berechtigter Besitzer einen alten Link verdrängen.
Der Host muss zuerst atomar übernehmen und erst danach den persönlichen Link anfordern.
Abgelaufene Personenbindungen werden minütlich in begrenzten Batches sowie vor
neuen Ausgaben außerhalb aller Engine-/Aufgabensperren bereinigt. Die Dateiablage
bleibt Einzelprozess-Entwicklung mit atomarer Entnahme und kollisionsfreiem
Neuanlegen. Anders als PostgreSQL kann sie bei einem anschließenden internen
Fehler keinen Verbrauch zurückrollen; der Host muss dann einen neuen Link holen.

## Nachweisgrenzen

HTTP-TDD deckt Fremdakteure, Replay, Ablauf, unvollständige Entwürfe, Wiederaufnahme,
Definition-/Lifecycle-Revisionen, Directory-Entzug/Abbruch, Operator-Fremdzugriff,
Lastkontingent und isoliertes CORS einschließlich JSON-Preflight ab. Ein entzogener
Keycloak-Basisrollenclaim wird an dieser anonymen Grenze nicht live introspektiert:
Die maximal fünf Minuten gültige Anzeige-Freigabe prüft das aktuelle Directory und
die Aufgabenbindung erneut, Mutationen benötigen immer die aktuelle Anmeldung.
Der virtuelle 46-Minuten-Test
belegt nur, dass die Einlösefrist die vorhandenen Benutzeroperationen nicht sperrt;
er ersetzt weder reale Token-Erneuerung noch einen echten 45-Minuten-Browserlauf.
PostgreSQL-Tests prüfen konkurrierende Sessions, Transaktionsrollback und FK-Cleanup.

Der Sandbox-Probe verwendet die bestehende Renderer-Komponente mit eigenem
Offline-Runtime-Setup und gesperrten Console-API-Adaptern, `allow-scripts` ohne
`allow-same-origin` und CSP ohne `unsafe-eval`. Das separate Setup entfernt die
unnötigen Form.io-Cookie-/Storage-Identitätsleser, deaktiviert Skriptauswertung und
bündelt die bereits verwendeten Kalender-/Zeitzonenversionen lokal. Die normale
Console bleibt unverändert. Das öffentliche statische Probe-Stylesheet erlaubt
`Origin: null` ohne Credentials für CSSOM; keine API- oder Session-CORS-Lockerung.
Textarea-Editoren/WYSIWYG und Kalender-Shortcut-Plugins sind im Embed-Profil derzeit
geschlossen: Die API erstellt für solche Formulare keinen Link. Der isolierte
SDK-Nachlader weist alle nicht gebündelten Libraries sofort zurück statt endlos zu
pollen. Die normale Console und der allgemeine Formularvertrag werden nicht beschränkt.

`node tests/form-embedding/run-sandbox-probe.cjs` baut die isolierte Renderer-Probe
immer frisch. Sie prüft auch weiterhin den Offline-Library-Nachlader. Der neue
`node tests/form-embedding/run-production-embedding.cjs` baut dagegen den echten
normalen Console-Auslieferungspfad einschließlich `/embed.html` und eines eigenen
klassischen Einzeldatei-IIFE in `/embed-assets/`. Die vorhandenen Formularfelder,
Directory-Picker und globalen Flowzer-Stile werden wiederverwendet; der normale
Console-Einstieg bekommt keine Cookie-/Evaluator-Änderungen. Console-/BFF-Transporte
sind im separaten Bundle durch explizit geschlossene Adapter ersetzt.

Der Produktions-Browsertest verwendet die tatsächlich erzeugte Gateway-CSP mit
synthetischen HTTPS-Routen und Backend-Antworten. Er prüft persönliche Einlösung
nur einmal ohne Cookies/Authheader/Referrer, entfernten URL-Secret ohne Remount,
Text/Datum und echten Kalender auf Deutsch/Englisch, gebundene Directory-Auswahl,
unvollständigen Save, CAS-Konflikt mit erhaltenen Eingaben, Abschlussvalidierung,
langsame/unklare Abschlussbestätigung und nicht freigegebene Einbettungsseiten.
Virtuelle 46 Minuten sind ausdrücklich kein echter Keycloak-/45-Minuten-Nachweis.
Formularabschnitte werden nur als bereits serverseitig expandierter Snapshot
verwendet; deren spezieller Browser-Durchstich bleibt ein Folgeprüfpunkt.
Ein fehlgeschlagener Test darf nie durch eine lockere Sandbox repariert werden.
Kein Demo- oder Produktivdeployment ohne abgeschlossene Folge-Gates.

## Produktionsauslieferung und Opt-in

Die nginx-Console liefert `/embed.html` und `/embed-assets/` standardmäßig als
**404**, niemals als SPA-/Login-Fallback. Die separate Gateway-Aktivierung benötigt:

- `FLOWZER_EMBED_API_ORIGIN`: exakte öffentliche HTTPS-Origin der Installation.
- `FLOWZER_EMBED_HOST_ORIGINS`: höchstens acht exakte freigegebene HTTPS-Origins,
  getrennt durch ASCII-Leerzeichen; keine Wildcards, Pfade oder Steuerzeichen.

Beide Werte müssen mit den oben genannten API-Optionen `PublicOrigin` und
`AllowedHostOrigins` übereinstimmen. Teilkonfiguration oder unsichere Werte stoppen
den Container vor nginx. `embedding-policy.sh` erzeugt die vollständigen
Location-Header. Nur `/embed.html` ersetzt das geerbte `X-Frame-Options: DENY` durch
exakte CSP-`frame-ancestors` plus **`sandbox allow-scripts`**. Alle sonstigen
Console-Routen behalten ihren bisherigen Schutz. Das Dokument darf weder Formulare
selbst abschicken noch fremde Skripte, Links, Objektinhalte oder Cookies/Storage
verwenden. `connect-src` nennt ausschließlich den Read-only-Einlöseendpunkt.
Statische Embed-Assets erlauben `Origin: null` ohne Credentials für Kalender-CSSOM.
Form.io benötigt Inline-Stile, aber kein `unsafe-eval` oder `allow-same-origin`.

## Gebundener Nachrichtenkanal

1. Nach erfolgreicher Einlösung meldet der Frame `flowzer.embed.ready`, Version 1,
   mit frischer `sessionId` ausschließlich an die servergeprüfte Host-Origin.
2. Der Host muss Quelle `iframe.contentWindow`, opaque `event.origin === 'null'`
   und Sitzung binden. Seine Antwort `flowzer.embed.connect` richtet er an genau
   dieses Window (opaque Ziele erfordern `targetOrigin: '*'`); keine freien Ports.
3. Der Frame akzeptiert nur seinen tatsächlichen Parent, exakte Host-Origin und
   dieselbe Nonce. Er erzeugt ein frisches `MessageChannel` und überträgt genau
   einen Port mit `flowzer.embed.connected`. Danach gibt es keine globale
   Window-Mutationsschnittstelle mehr.
4. Private Requests enthalten `kind: request`, `sessionId`, UUID-`id`, `operation`
   und `payload`. Nur `draft.save`, `task.complete`, `directory.search` und
   `directory.resolve` sind vorgesehen. Aufgabe und tatsächlicher Benutzer werden
   immer durch den authentifizierten TT-Host gebunden, nicht durch den Payload.
5. Antworten korrelieren `kind: response`, Sitzung und ID und enthalten entweder
   `result` oder einen objektförmigen sicheren Fehler mit `code` und optional
   explizit sicheren `fieldMessages`. Malforme Antworten sind kein Erfolg.
   `task.complete` benötigt zusätzlich **`result: { completed: true }`**.

Jeder einzelne RPC hat 30 Sekunden Transportbudget; der Kanal selbst hat keine
Bearbeitungsfrist. Es gibt keine Tokens, Refresh-Tokens oder Secrets im Kanal.
TT erneuert seine Anmeldung unabhängig und vermittelt jede Aktion erneut mit dem
aktuellen Akteur. Das Backend bleibt die einzige Mutationsautorisierung.

Manuelles Speichern übermittelt `expectedRevision`, `expectedTaskRevision` und
`data`, ohne Pflichtfeld-/Entscheidungsvalidierung. Der Renderer wird nicht ersetzt;
Edits während eines Saves bleiben anschließend als ungespeichert markiert.
Ein Revisionskonflikt überschreibt nichts und lässt den lokalen Inhalt erhalten.
Der Abschluss dagegen friert Aktion, Daten, Aufgabenrevision und Idempotenzschlüssel
als einen Auftrag ein. Sein vorhandener Feldbaum ist während Abschluss und
unklarem Ausgang `disabled`/`inert`; keine spätere Eingabe kann beim Erfolg verloren
gehen. Unklare Ausgänge werden nur identisch wiederholt, keine neue Entscheidung
mit demselben Schlüssel oder einem vorschnell neuen Schlüssel. Ausschließlich
bekannte Fachfehler geben eine korrigierte neue Übermittlung frei. Diese UI-Regel
ersetzt weder serverseitige Idempotenz noch die laufende Aufgaben-/Rechteprüfung.
