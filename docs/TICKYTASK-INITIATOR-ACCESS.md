# Jobgebundener Initiatorzugang für TT-Ticketaktionen

## Abgegrenzter Baustein

`POST /job/{jobId}/initiator-access` ist ein **read-only Worker-Endpunkt**.
Er prüft den aktuellen **Flowzer- und TT-API-Zugang** des serverseitig am Master-Token
persistierten Initiators. Er schließt keinen Auftrag ab, ändert keine Retries
und führt keine Ticketaktion aus. Ein positiver Stand ersetzt **weder** den
registrierten TT-Workflow-Vorgang **noch** aktuelle TT-/Ticketrechte.

Der Request enthält ausschließlich die technische `workerId` (1–128 ASCII-
Buchstaben/Ziffern, Bindestrich oder Unterstrich). Frei gewählte Personen,
Issuer, Rollen oder Audiences sind keine Requestfelder und werden abgelehnt.
Die bestehende Worker-Policy inklusive Installationsscope schützt die Route.

Vor **und nach** der externen Abfrage muss derselbe Auftrag weiterhin dem
aktuellen authentifizierten Worker und seiner Nonce gehören. Die Engine prüft
Instanz, Definitionsversion, logische Definition, Prozess, aktiven Service-Task-
Token, BPMN-Knoten und genau einen Master mit verifiziertem Initiator. Abbruch,
Leaseverlust, Umbinden oder Initiatorwechsel entwerten den Stand. Immutable
Vorher-/Nachher-Kopien schützen auch Adapter mit geteilten mutablen Objekten.
Während Keycloak-I/O bleibt kein Storagekontext oder SQL-Lock offen.

Die Route ist zunächst auf die vier Typen `tt.ticket.read`, `tt.ticket.create`,
`tt.ticket.close` und `tt.ticket.delegate` begrenzt. Sie ist **kein** allgemeiner
Benutzersuch- oder Stellvertretungsendpunkt.

## Aktuelle Providerautorität

Der Leser verwendet je Prüfung ein frisches Client-Credentials-Token und liest
nur das angefragte Einzelprofil, den konfigurierten Root-Teilbaum, die aktuellen
Gruppenmitgliedschaften dieser Person und ihre effektiven API-Clientrollen.
Kein periodischer Directory-Snapshot, alter JWT-Rollenclaim oder Workeraccount
ersetzt das aktuelle Konto. Gruppenpfade dienen nur der Driftprüfung;
ausschließlich geladene Root-/Child-IDs verleihen Mitgliedschaft.

Beide API-Clients werden exakt über ihren konfigurierten `clientId` gesucht. Die
Rollenroute erhält seine **interne** ID. Keycloaks `/composite`-Route berücksichtigt
zusammengesetzte und gruppengeerbte Rollen; Realmrollen oder Rollen eines anderen
Clients gelten hier nicht. Grundlage: [Keycloak-REST-Vertrag](https://www.keycloak.org/docs-api/latest/rest-api/index.html)
und [offizielle Implementierung](https://github.com/keycloak/keycloak/blob/main/services/src/main/java/org/keycloak/services/resources/admin/ClientRoleMappingsResource.java).

Voraussetzungen: aktivierte, sichere Keycloak-Directory-Konfiguration,
**nicht leere** `RootGroupId`, exakter Issuer und aktivierte Flowzer-
Authentifizierung mit nicht leerer API-Audience und Pflichtrolle. Die Audience
muss für diesen Keycloak-Vertrag dem tatsächlichen API-`clientId` entsprechen;
kein URI-/Realm-/Legacy-Permissive-Fallback. Der vorhandene ausschließlich lesende
Directory-Serviceaccount muss die benötigten Profil-, Hierarchie-, Client- und
Rollenlesepfade vollständig sehen können. Seine konkreten minimalen Rechte sind
bei der echten Installation zu prüfen, nicht durch allgemeine Administrator-
oder Impersonationsrechte zu ersetzen. Secrets kommen ausschließlich zur
Laufzeit aus dem freigegebenen Secretstore.

Zusätzlich verlangt die TT-Route `TickyTaskTicketActions:ApiClientId`: den
**exakten TT-API-Clientwert aus TT `Oidc:Audience`** desselben Realms. Die dort
verbindliche Clientrolle ist fest `access`, nicht vom Workflow oder Worker
wählbar. Ein Flowzer-Zugang allein oder eine aktive lokale TT-Membership genügen
nicht: auch ein ausschließlich an der TT-API entzogenes `access` muss sperren.
Die beiden Clientnamen und internen Provider-IDs müssen verschieden sein.
Leerer, unsicherer oder als Flowzer-Client getarnter Wert schließt mit 503 vor
Storage-/Provider-I/O. Normale Flowzer-Installationen können den sicheren leeren
Default behalten; sie müssen für andere Funktionen keine TT-Integration einrichten.

Beispiel ausschließlich mit **synthetischen** Installationswerten:

```json
"TickyTaskTicketActions": { "ApiClientId": "synthetic-tt-api" }
```

Vor Workeraktivierung müssen die echte TT-Audience und diese Hostbindung
unabhängig übereinstimmend zurückgelesen sowie die eingesetzten TT-/Flowzer-
Images an die reviewten SHAs/Digests gebunden sein. Der unveränderte minimale
Elf-Felder-HTTP-Vertrag ersetzt keine Installationsattestation. Keine Zugänge,
Gruppen, Secrets oder Runtime-Konfigurationen werden durch diesen Quellbaustein
angelegt oder geöffnet.

Die Prüfung hat ein Gesamtbudget von **zehn Sekunden**, einschließlich Token-
und Antwortbody-I/O **beider** Clients; es passt in den bestehenden 15-Sekunden-TT-Transport.
Konto, Gruppenbaum und Mitgliedschaft werden nur einmal frisch gelesen. Es
gibt keine zwei sequenziellen Zehn-Sekunden-Prüfungen oder einen Rollencache.
Antwortgrenzen zählen tatsächliche Bytes auch ohne beziehungsweise mit zu
kleiner `Content-Length`. Strikte Antworten verlangen JSON, begrenzte Tiefe und
rekursiv eindeutige Schlüssel, einschließlich case-insensitiver Duplikate.
Redirects, Cookies und automatische Requestlogger des Directory-HTTP-Clients
sind ausgeschaltet. Quellfehler verlassen die Grenze ohne Raw-Body, URI,
Token, Secret oder ursprüngliche InnerException. Caller-Abbruch bleibt Abbruch.

## Ergebnis und Fehler

- **200 / `allowed: true`**: aktuelles aktives Konto, aktuelle Scope-Mitgliedschaft
  und effektive Pflichtrollen beider aktiven API-Clients sind belegt.
- **200 / `allowed: false`**: bestätigte Deaktivierung/Löschung des Einzelkontos,
  fehlende Scope-Mitgliedschaft, fehlende Pflichtrolle oder ein deaktivierter API-Client.
  Nur 404 am exakten Benutzerprofil bedeutet „gelöscht“; insbesondere ein fehlender
  Token-, Root- oder Rollenendpunkt ist kein persönlicher Löschbeweis.
- **400**: ungültiger beziehungsweise nicht geschlossener Request.
- **404**: Auftrag nicht vorhanden.
- **409**: Lease oder Ausführungskontext nicht mehr aktuell.
- **503**: Providerstand/Installation technisch unklar; **kein bestätigter Entzug**.

Der Erfolgsumschlag enthält die verbindlichen Job-/Instanz-/Definitions-/Token-/
Knoten-/Typkoordinaten, Initiator-Issuer und -Subject, `allowed` und den externen
UTC-Prüfzeitpunkt. Alle elf Ergebnisfelder und der Requestbody sind im tatsächlich
generierten OpenAPI-Vertrag erforderlich. Keine Profilnamen, E-Mails, Gruppen,
Formularwerte oder Prozessvariablen werden ausgegeben. Das Ergebnis ist ein
unmittelbarer Stand, **kein** persistierbarer oder übertragbarer Grant.

## Noch offene Integration und Abnahme

Dieser Baustein aktiviert keinen ausführenden TT-Worker. Der TT-Verbraucher mit
frischer persönlicher TT-Verknüpfung ist als kalter Adapter bereits vorhanden;
die dauerhafte Vorgangssperre bei Rechteentzug oder technisch unklarer Prüfung
ist im getrennten TT-Arbeitsbranch vorbereitet, aber nicht ausgerollt. Seine
konkrete Installationsbindung und Aktivierung sowie die auditierte Einzelfreigabe
(TT-Administration **plus** nachgewiesenes Flowzer-Betriebsrecht) bleiben offen.
Provider-Unklarheit darf
nicht als bestätigter Entzug gespeichert werden; ein späteres Ja darf einen
pausierten Vorgang nicht automatisch reaktivieren. Bereits ausgeführte
Ticketänderungen werden bei Prozessabbruch nicht zurückgerollt.

Die Tests verwenden ausschließlich synthetische HTTP-, Auth- und Storagequellen.
Sie ersetzen keine echte Keycloak-/HTTPS-, PostgreSQL-Konkurrenz-, Einbettungs-
oder 45-Minuten-Abnahme. Demo-Gruppenzuordnung, Secret-/Client-Einrichtung,
Onlineinstallation und koordinierter Demo-Rollout sind nicht Teil dieses lokalen
Nachweises. Keine Timerimplementierung, Mail, Kalender oder KI-Outboundeffekte.

## Persönlicher Betriebsnachweis für die ausdrückliche Wiederfreigabe

`GET /instance/{instanceId}/ticket-action-operator-access` ist ein separater,
persönlich authentifizierter **Read-only-Endpunkt** mit bestehender Operator-
Policy und `Cache-Control: no-store`. Er akzeptiert weder Link-Secrets noch
Workerrechte oder einen Body mit frei gewählter Person/Rolle. Die Identität und
`azp` stammen ausschließlich aus dem verifizierten aktuellen Benutzerkontext.

Für genau diese Person werden Flowzer-Zugang, TT-API-Zugang und konfigurierte
Flowzer-Betriebsrolle frisch unter **einem gemeinsamen Zehnsekundenbudget**
geprüft. Eine zweite Anmeldung ist hier kein Ersatz für den persönlichen
serverseitigen Token-Exchange aus TT. Leere Client-/Rollenbindungen, anonyme
Fallbackidentitäten oder unklare Providerantworten bleiben geschlossen.

Vor und nach Provider-I/O werden aktive Instanz, logische Definition,
Definitionsversion, Prozess und eindeutiger Master mit Initiator als Werte
gelesen; es bleibt dabei kein Storagekontext offen. Ein Abbruch oder Wechsel
dieser Bindungen entwertet den Nachweis. Der minimale, vollständig erforderliche
Neun-Felder-Vertrag liefert nur Instanz-/Definitionskoordinaten,
Initiator-Issuer/-Subject, Akteur-Issuer/-Subject, tatsächliches `azp` und den
externen UTC-Prüfzeitpunkt. Keine Formulare, Profilnamen, Rollenliste oder Tokens.

200 bedeutet nur diesen aktuellen Betriebsstand; 403 verweigert den
Betriebsnachweis (JWT-Policy oder bestätigte Live-Ablehnung), 409 ist ein nicht
mehr gültiger Kontext und 503 technisch unklarer Stand. Insbesondere belegt
ein früher Policy-403 allein keinen frisch bestätigten persönlichen Entzug. Der
Nachweis ist **kein persistierbarer Grant**. Er schließt keine Sperrperiode,
ändert keinen Initiator, startet keinen Worker und verändert weder Joblease
noch Retries. TT muss dieselbe Person zusätzlich frisch als TT-Administrator
autorisieren und genau die konkrete Sperrperiode mit Grund atomar auditieren.
Ein späteres positives Prüfungsergebnis darf niemals automatisch reaktivieren.

### Historischer lokaler Nachweis des ursprünglichen Einzelclient-Lieferabschnitts

- Initial **41/41 tatsächlich rot**, weiterer Job-/HTTP-/Fehlerlauf **39 rot,
  24 bestanden**. Der Review-Pflichtvertrag war ebenfalls tatsächlich rot;
  zwei gültige übergroße Streamprofile scheiterten nach gezielter Byteguard-
  Mutation wie erwartet (**3/3 Review-Red**). Die Guardquelle wurde danach exakt
  restauriert. Keine Compiler-/Sandboxabbrüche als Red gezählt.
- Final **132 fokussierte Verträge** erfolgreich und **1.662 hermetische API-
  Tests**, alle bestanden ohne Skips. Sämtliche 1.573 Baseline-Test-IDs und
  Outcomes bleiben erhalten; **89 additive Fälle**. Beide echten PostgreSQL-
  und Mehrprozess-Containerklassen sind aus diesem lokalen Nachweis ausgeschlossen.
- **395 Core-Verhaltenstests** bestanden; der eine vorhandene manuelle Explicit-
  Erwartungsgenerator bleibt `NotExecuted`. Alle Baseline-IDs/-Outcomes unverändert.
- SDK: Schema tatsächlich generiert, zweite Generierung byteidentisch;
  Typecheck, **33 Tests** und Build erfolgreich. Keine neue SDK-Workermethode.
- Zwei unabhängige native Quellenreviews: lokaler OpenAPI-P2 und Stream-P3
  bearbeitet, kein Restbefund. Exportierter Swagger-Snapshot bewahrt alle
  117 bisherigen Pfade und 230 bisherigen Schemas semantisch unverändert.
  Additiv: eine Route und drei Schemas. Testzweckprüfung und Whitespaceprüfung grün.

Diese lokale Liste ist ausdrücklich kein CI-, Merge-, PostgreSQL-Konkurrenz-
oder Runtime-/Demo-Nachweis; diese Zustände werden SHA-gebunden separat geführt.

### Erweiterung um den getrennten TT-API-Zugang

Der Nachweis bleibt read-only und jobgebunden. Die Tests prüfen insbesondere
TT-only-Rollenentzug bei weiterhin aktivem Flowzer-Konto/-Scope/-Zugang,
mehrdeutige oder fremde Hostclient-/Rollenantworten, Alias-IDs, fehlende
Installationsbindung, geschlossene HTTP-Requests sowie das gemeinsame Budget
einschließlich eines hängenden zweiten Antwortbodys. Initiale TDD-Stufe:
**38/38 tatsächlich rot** (ausgeführte Assertions beziehungsweise fehlende
Implementierung, kein Compiler-/Testhostabbruch). Der Review ergänzte sechs
Retry-/Backofffälle (503, Transport- und Body-I/O-Fehler, jeweils Deadline und
Caller-Abbruch). Eine temporäre Tokenentkoppelung ergab **3/3 echte Review-Red**;
die unveränderte Produktquelle wurde danach bytegenau restauriert.

Final **1.712/1.712 hermetische API-Tests** erfolgreich, keine Skips: alle 1.662
Baseline-IDs/-Outcomes erhalten, 50 additive Fälle. **395 Core-Verhaltenstests**
erfolgreich, der vorhandene manuelle Explicit-Generator zusätzlich
`NotExecuted`; Resultmultiset unverändert. Echter OpenAPI-Export und zweimalige
SDK-Generierung byteidentisch zum vorherigen Vertrag; SDK-Typecheck, **33 Tests**
und Build erfolgreich. Zwei unabhängige native Gesamtdiff-Quellenreviews ohne
Restbefund; Reviewer haben keine eigenen Tests oder Livezugriffe ausgeführt.
PostgreSQL-/Mehrprozess-Containerklassen bleiben aus dem lokalen API-Nachweis
ausgeschlossen. Frische CI und tatsächliche Runtime-Abnahme werden weiterhin
getrennt am konkreten SHA geführt.

### Lieferabschnitt persönlicher Operatornachweis

Vor Implementierung erreichten **25 echte Assertions Rot, sechs Fälle bestanden**;
vor der HTTP-Route waren zwei Rollen-/HTTP-Assertions rot, die anonyme 401-
Grenze bestand bereits. Nach dem additiven Endpunkt war der echte OpenAPI-
Snapshotvertrag zunächst rot und wurde über seinen ausdrücklichen Exportpfad
erneuert. SDK danach zweimal byteidentisch generiert, Typecheck, **33 Tests**
und Build erfolgreich. Die bisherigen **118 Pfade und 233 Schemas** bleiben
semantisch unverändert; additiv eine Route und zwei Schemas.

Final **1.749/1.749 hermetische API-Tests** bestanden, alle 1.712 vorherigen
Test-IDs/-Namen/-Outcomes erhalten, **37 neue Fälle**. Beide echten Container-
klassen bleiben ausdrücklich ausgeschlossen. Core: **395 erfolgreiche
Ausführungen und ein vorhandener Explicit-Generator nicht ausgeführt**;
das vollständige Resultmultiset bleibt gleich. Eine vorhandene gleich benannte
Parametrisierung tritt zweimal auf: 394 verschiedene bestandene Core-
Ergebnisse, keine verschluckte Wiederholung. Zwei unabhängige native
Gesamtdiff-Quellenreviews, ein P2-Dokuhinweis zur frühen Policy-403 fachlich
geschlossen, kein Restbefund. Keine eigenen Reviewer-Tests oder Livezugriffe.
Zwei VSTest-Sandbox-Socketabbrüche sind Umgebungsabbrüche, keine Produkt-TDD-Rots.

Dieser lokale Nachweis aktiviert keine Wiederfreigabe, keinen Worker und
keinen Onlinebetrieb. Aktuelle CI, Merge und reale Abnahme werden getrennt
am gelieferten SHA zurückgelesen.
