# Persönlicher Read-only-Formulareinstieg und isolierte Formularansicht

Status: **API und separater Produktionsrenderer vorhanden; noch nicht in TT
angebunden oder ausgerollt.** Der persönliche Hostkanal wird mit synthetischen
Backend-Antworten geprüft. Echte Container-/HTTPS-/Keycloak-Abnahme einschließlich
realer Token-Erneuerung und mindestens 45 Minuten Bearbeitung bleiben zwingende
Folge-Gates. Das Installations-Opt-in bleibt standardmäßig geschlossen und wird
noch nicht in Compose aktiviert.

Der Host kann die Linkausgabe und alle persönlichen Datenaktionen zusätzlich
atomar an seine registrierte Instanz-/Versionsbindung und tatsächliche Übernahme
binden; siehe [Hostvertrag](HOST-INTEGRATION.md#atomare-bindung-weiterer-aufgabenaktionen).
Ein vorangehender Taskabruf allein ist keine Absicherung gegen Freigabe/Migration.
Bestehende Ablauf-, Sandbox-, CSP- und Read-only-Grenzen bleiben unverändert.

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

### Persönlicher Vermittler-Audit

Übernahme, Freigabe, Zuweisung, Delegation und Abschluss tragen jetzt optional
`AuthenticatedActor`: verifizierten Issuer/Subject, tatsächliche Benutzer-GUID
und den authentisierten OIDC-`azp` des vermittelnden Clients. Der Abschlusstoken
bewahrt denselben Akteur zusammen mit dem bereits fachlich validierten Ergebnis;
der persönliche Rückzug bewahrt den ersten Client im Audit auch bei Retry aus
einem anderen Client. Diese Werte kommen niemals aus Headern, Formular-JSON,
Directory-Anzeigenamen oder einem Link-Secret. Client-ID ist weder Secret noch
Besitz-/Autorisierungsrecht; der persönliche Besitz bleibt Issuer/Subject.

Bearer und BFF verwenden dieselbe reine Claimprüfung. Im Cookie wird ausschließlich
Access-Token-`azp` nach Signatur-/Issuer-/API-Audience-Prüfung und vor Claim-Dedup
übernommen. ID-Token-`azp` ist kein Ersatz; Refresh erneuert den Wert aus dem neuen
geprüften Access Token. Mehrdeutige, leere, überlange oder Steuer-/Leerzeichenwerte
werden abgewiesen, fehlender `azp` bleibt für historische Provider/Dev kompatibel.
TT muss zusätzlich sein tatsächliches Exchangeclient-Binding belegen; generische
Flowzer-Auditmetadaten allein beweisen noch keinen konkreten TT-Livepfad.

Die neuen Actorfelder bleiben intern und nullable. Normale History-/Token-/
Instanzprojektionen übernehmen weder Subject/Issuer noch Clientmetadaten.
Die vorhandenen JSON-Audit-/Tokendokumente speichern sie additiv, ohne zusätzliche
PostgreSQL-Spalten oder Schemaänderung. Tatsächlich feldloses Legacy-Event-JSON
und ein vollständiger polymorpher alter Tasktoken bleiben lesbar. Datei-/HTTP-
Tests prüfen beide Gruppenmodelle; der zusätzliche PostgreSQL-Roundtrip ist
weiterhin durch den verpflichtenden echten CI-Pfad nachzuweisen.

### Persönlicher Startformular-Einstieg

- `POST /definition/meta/{definitionId}/start-form-link?expectedDefinitionId=<deployedId>`
  ist authentifiziert und verlangt die tatsächlich angezeigte, nicht leere Version.
  Body ist dieselbe exakte `hostOrigin` wie beim Human Task. Stale Versionen liefern
  `409 workflow.definition_changed`, fehlende/leere Versionen `400`.
- Antwort ist `{ definitionId, formLink }`: Ohne Startformular ist `formLink`
  **ausdrücklich `null`**; andernfalls enthält sie `url` und `redeemBeforeUtc`.
  OpenAPI und generiertes SDK bilden sowohl Pflichtversion als auch Nullzweig ab.
- Der Einstieg lautet `/embed.html#start.<secret>`; allein der geschlossene Präfix
  wählt `POST /form-embed/start/redeem`. Freie Pfade/Zieladressen sind nicht möglich.
  Der historische Aufgabenfragmentvertrag bleibt unverändert.
- Einlösung liefert ausschließlich `definitionId`, `relatedDefinitionId`,
  `hostOrigin` und das erneut kompilierte, versionsgebundene `form`. Es gibt keine
  künstliche Aufgabe/Instanz, keinen Prozesskontext und keinen Startentwurf.
- Identität, aktuelle Directory-Mitgliedschaft, Installations-Wurzelgruppe,
  Hostfreigabe und Definitionsversion werden erneut geprüft. Anonyme Einlösung
  hat dieselbe isolierte Origin-null-CORS- und geordnete Ratenlimitgrenze wie Tasks.
- Die Directory-Routen `start-forms/{definitionId}/fields/{fieldKey}/subjects`
  und deren `/resolve` akzeptieren optional `expectedDefinitionId`. Bestehende
  Konsolenaufrufe bleiben kompatibel; ein neuer Host muss die angezeigte Version
  bei **jeder** Directory-Aktion fest binden, nicht nur beim Formularöffnen.

`EmbeddedStartForm` verwendet denselben `FormRenderer` und privaten Port wie die
Aufgabenansicht. Es bietet vollständige Startvalidierung und keinen Draft-Button.
`workflow.start` transportiert nur geklonte `data` und den persönlichen
`idempotencyKey`. Definition, Version, Benutzer und optionaler Ticketbezug werden
im authentifizierten Host aus dessen serverseitiger Bindung abgeleitet, nicht aus
Frame-Daten. Ausschließlich `result: { started: true }` bestätigt den Start.
Ein unklarer Ausgang friert Originaldaten und Schlüssel fest. Auch ein späterer
Zugangs-/Vorprüfungsfehler beweist **nicht**, dass ein früherer Start fehlgeschlagen
ist: Die Ungewissheit bleibt bis zur gebundenen Erfolgsbestätigung bestehen.
Erneuerung des Hosttokens benötigt weder neuen Link noch Renderer-Reload.

**Noch offen im nachfolgenden TT-Slice:** echter Katalog/Startdialog, persönlicher
Backend-Link-/Directory-Proxy und Schutz des unklaren Originalstarts bei
Schließen/Navigation/Reload. Der reine Flowzer-Port und dessen lokaler Retry
ersetzen diese Host-Lebenszyklusgrenze nicht. Keine persistenten Start-Drafts,
keine versteckten Tickets und keine browserseitige Secretpersistenz ergänzen.

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

Ein unklarer Human-Task-Abschluss bindet Originaldaten, Entscheidungsaktion,
Aufgabenrevision und Idempotenzschlüssel bis zur ausdrücklichen Erfolgsbestätigung.
Ein späterer Validierungs-, Zugangs- oder Revisionsfehler beweist nicht, dass der
erste Versand erfolglos war. Der Renderer bleibt erhalten, Eingaben und Draft-Save
bleiben gesperrt und ausschließlich derselbe Auftrag ist manuell wiederholbar.
Sichere Feldmeldungen sind weiterhin sichtbar, ohne eine neue Entscheidung oder
Neuladeanweisung freizugeben. Nur ein erster definitiver Fachfehler ohne früheren
unklaren Versand erlaubt korrigierte Daten und einen neuen Schlüssel. Das ist keine
Rechtefreigabe: Der authentifizierte Host setzt Aufgabenabbruch und Rechteentzug
weiterhin durch und entfernt den Frame bei bestätigtem Aufgabenverlust.

## Konfiguration und Ablage

`FormEmbedding:Enabled=false` ist der Default. Für die spätere geprüfte Aktivierung
werden `PublicOrigin` und `AllowedHostOrigins` als exakte HTTPS-Origins benötigt
(keine Pfade, Wildcards, Queries oder Credentials).

Migration `022_start_form_embed_grants.sql` ergänzt getrennt eine leere
Startgrant-Tabelle ohne Task-Fremdschlüssel. Die Obergrenze von vier Links je
stabiler Person/Definitionsversion wird über API-Prozesse hinweg unter einer
kurzen transaktionsgebundenen Advisory-Sperre gehalten; Verbrauch bleibt
`DELETE RETURNING`. Die Dateiablage serialisiert nur ihren Einzelprozess.
Beide Speichertypen enthalten ausschließlich Hash und stabile Metadaten,
keine Eingaben, Drafts, Zugriffstokens oder Rohsecrets. Bereits offene Formulare
werden durch Verdrängung alter Einstiege nicht verändert.

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
verwenden. `connect-src` nennt ausschließlich die zwei exakten Read-only-Einlösepfade
`/form-embed/redeem` und `/form-embed/start/redeem`, niemals die gesamte API.
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
   `directory.resolve` sowie separat `workflow.start` sind vorgesehen. Aufgabe und tatsächlicher Benutzer werden
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


## Zusätzlicher Startformular-Nachweis

Die neue HTTP-Suite wurde vor Implementierung mit **10/12 rot** reproduziert;
beide bereits geschlossenen Opt-in-Fälle waren unverändert grün. Feste
Start-Directory-Versionen wurden gesondert rot reproduziert. Zusätzliche Fälle
prüfen echten Redeploy, nachträglichen Installationsscope/Hostentzug, exakte
Ablaufgrenze, anonymes Mutationsverbot, persönlichen Einmalverbrauch und
Obergrenze. Der neue explizite Nullzweig wurde im tatsächlich gemounteten Swagger
zunächst rot reproduziert und lokal ohne Änderung historischer Schemas korrigiert.

Renderer-TDD reproduziert unbekannte Starts, spätere Vorprüfungsfehler und
wiederholte Feldfehlerpfade. Die echte frisch gebaute Produktions-IIFE-Suite
prüft auch den separaten Startpfad unter unverändertem opaque Sandbox-/CSP-Schutz.
Die Test-HTTPS-Routen und Portantworten sind synthetisch; virtuelle 46 Minuten
sind **keine** echte Token-Erneuerung oder 45-Minuten-Betriebsabnahme. Die neuen
PostgreSQL-Zwei-Session-/Rollback-/gleichzeitigen Vierergrenztests müssen im
verpflichtenden echten CI-Pfad bestehen; lokale Hermetik ersetzt sie nicht.
Installations-Opt-in, Staging und Production wurden dadurch nicht geöffnet.


Finaler lokaler Stand dieses Start-Slices: **68/68** fokussierte echte HTTP-/
Swagger-Vertragsfälle, **1514/1514** API-Hermetikfälle (PostgreSQL- und
Mehrprozessklassen ausdrücklich ausgeschlossen), **809/809** Consolefälle,
**24/24** fokussierte Embed-Fälle und **33/33** SDK-Fälle samt Typprüfung/Build.
Frisches Produktions-IIFE/CSP: **7/7** Browserfälle; Offline-Sandbox **2/2**;
Gateway-Policy **5/5**. Gesamt-Lint besitzt keine Fehler und die elf vorhandenen
Warnungen. Zwei unabhängige eigene Quellenreviews begleiteten den Slice;
Pflichtversion, explizite Nullability, sticky Start-Ungewissheit und sichere
Repeat-Feldfehler wurden mit roten Tests nachgewiesen und korrigiert.
Diese Nachweise öffnen weder Installations-Opt-in noch Merge-/Live-Gates.


Der erste Start-Slice-CI-Lauf (`81e0957`) stoppt im Gateway-Routenlistentest,
nach bereits grüner Typprüfung, Lint, Tests und Build. Der bestehende Test
vergleicht den Controller-Routenpräfix wörtlich mit der Proxy-Alternativenliste
und behandelt `form-embed/start` als eigenen Präfix. Lokal wurde derselbe
Fehler rot reproduziert. Der Controller verwendet deshalb den bestehenden
Präfix `form-embed` plus Aktionspfad `start/redeem`: Der tatsächliche
HTTP-/OpenAPI-Pfad bleibt exakt gleich. Gateway, CSP, Autorisierung und
Prüfscript wurden nicht erweitert oder gelockert. Erst eine neue vollständig
grüne CI attestiert diese enge Nacharbeit.


### Nachweislich veraltete Startfassung gegenüber unklarem Start

Der private Hostkanal akzeptiert für eine Startaktion zusätzlich den exakt
bekannten Code `flowzer.definition_changed`. Ein erstmaliger definitiver
Vor-Anlage-Versionskonflikt stoppt den angezeigten Startsnapshot: Eingaben bleiben
sichtbar, aber diese Fassung wird nicht mit neuem Schlüssel gestartet. Erst eine
bewusste neue Auswahl darf die aktuelle Fassung laden. Ein unbekannter Fehler
oder ein früherer unklarer Versand bleibt dagegen an Originalwerte und
Originalkey gebunden. Auch eine spätere Versions-/Validierungs-/Zugangsantwort
zeigt in diesem Zustand keine Neuauswahl- oder Korrekturanweisung.

Test-first: Versionsfall zunächst 1/8 rot, nach Umsetzung mit Kanaltests 16/16
grün. Der eigene unabhängige Quellenreview fand eine widersprüchliche
Neuauswahl-Anweisung nach Unknown; die ergänzte tatsächliche Regression war
1/8 rot. Nach enger Korrektur sind alle 26/26 Embed-Regressionsfälle, Typecheck
und Lint grün (11 bestehende Lintwarnungen, keine Fehler). Quellen-Nachreview
bestätigt Originalbindung und die korrigierte Handlungsanweisung. Dies ist
keine echte Browser-/HTTPS-/Keycloak-/45-Minuten-Abnahme und keine Freigabe
für Staging- oder Productionänderungen.

Der anschließende vollständige Console-Lauf enthält **811/811** bestandene
Tests in 115 Dateien; der frische Produktionsbuild einschließlich separatem
Embed-Bundle ist ebenfalls grün. Beide eigenen unabhängigen Quellenreviews
sehen im finalen eng begrenzten Renderer-/Kanal-Diff keinen Restbefund.


Sichere zentrale Feldmeldungen bleiben auch bei einem weiterhin unklaren Start
sichtbar. Sie sind ausdrücklich kein Nein-Beleg: Originalwerte/-key bleiben
unverändert gesperrt und nur derselbe Auftrag ist wiederholbar. Ungeprüfte
Remote-Texte, URLs und Traces werden weiterhin nicht angezeigt. Der zusätzliche
Feldmeldungsfall wurde zuerst tatsächlich 1/9 rot getestet; die anschließende
fokussierte Embed-Suite ist **27/27** grün, einschließlich sticky Unknown mit
sichtbarem Feldfehler. Typprüfung ist ebenfalls grün. Dies ist weiterhin kein
Live-, HTTPS-, Sandbox- oder Keycloak-Abnahmebeleg.

Der frische vollständige Console-Lauf nach dieser Feldmeldungs-Ergänzung besteht
mit **812/812** Tests in 115 Dateien; Lint hat weiterhin elf vorhandene Warnungen
und keine Fehler. Frischer Produktionsbuild einschließlich separatem Embed-Bundle
ist grün. Beide eigenen unabhängigen Quellen-Nachreviews sehen keinen konkreten
Restbefund in dieser engen Ergänzung. Die tatsächliche Demoabnahme bleibt offen.

### Eng begrenzte Sicherheitsaktualisierung der Console-Abhängigkeiten

Vor der Demoauslieferung werden die zwei tatsächlich im Console-Lockbaum
vorhandenen betroffenen Auflösungen aktualisiert: DOMPurify von 3.4.14 auf
3.4.16 ([GHSA-p98j-92pf-mc4p](https://github.com/advisories/GHSA-p98j-92pf-mc4p))
und Moment von 2.30.1 auf 2.31.0
([GHSA-4p3w-j4w9-5jqw](https://github.com/advisories/GHSA-4p3w-j4w9-5jqw)).
Der Lockdiff enthält ausschließlich Version, Bezugs-URL und Integrität dieser
beiden Pakete. Vorhandene Versionsbereiche bleiben kompatibel; Manifest,
Overrides, Lizenzen, Produktcode, Berechtigungen, CSP und Sandbox bleiben
unverändert. Das ist keine Aussage, dass alle Abhängigkeiten sicher sind.

Vier dokumentierte Regressionen sichern Mindestversionen aller passenden
Lockeinträge sowie die konkreten Bibliotheksgrenzen ab. Der DOMPurify-Fall prüft
nur ein Eventattribut an einem durch einen After-Hook abgetrennten lebenden
Teilbaum, ohne Events oder Skripte auszuführen. Der Moment-Fall prüft, dass
eine vom Aufrufer gelieferte `match`-Funktion nicht zur Modulnamensprüfung
verwendet wird. Die vorherige Normalisierung zu einem String ist gemäß dem
[Upstream-Patch](https://github.com/moment/moment/commit/5f7d983) ausdrücklich
erlaubt; eine anfänglich zu strenge Testannahme wurde entsprechend korrigiert.
Die Mindestversions- und DOM-Regressionsfälle waren vor der Aktualisierung rot.
Die korrigierte Moment-Fixture wurde zusätzlich unverändert gegen das isolierte
veröffentlichte Paket 2.30.1 wirklich rot reproduziert. Sie verwendet das bereits
geladene `en` und lädt kein fremdes Modul. Diese Pakettests beweisen keinen
konkreten ausnutzbaren Angriffspfad im produktiven Flowzer.

Nach frischer Lockinstallation bestehen **816/816** Console-Tests in 116 Dateien,
darunter **4/4** Sicherheitsfälle und **27/27** Embed-Fälle. Typprüfung, frischer
Produktionsbuild samt separatem Embed-Bundle und Testzweckprüfung sind grün;
Lint hat weiterhin elf vorhandene Warnungen und keine Fehler. Der erneut aus
dem frischen Produktionsbundle gestartete isolierte Browserpfad besteht mit
**7/7**, Offline-Sandbox mit **2/2** und Gateway-Policy mit **5/5** Fällen.
HTTPS-Routen und API-Antworten dieses Harness bleiben synthetisch; virtuelle
46 Minuten ersetzen keine reale 45-Minuten-, Keycloak- oder Demoabnahme.
Die Prüfung öffnet weder Installations-Opt-in noch Staging oder Production.
