# Jobgebundener Initiatorzugang für TT-Ticketaktionen

## Abgegrenzter Baustein

`POST /job/{jobId}/initiator-access` ist ein **read-only Worker-Endpunkt**.
Er prüft den aktuellen Flowzer-Zugang des serverseitig am Master-Token
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

Der API-Client wird exakt über seinen konfigurierten `clientId` gesucht. Die
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

Die Prüfung hat ein Gesamtbudget von **zehn Sekunden**, einschließlich Token-
und Antwortbody-I/O; es passt in den bestehenden 15-Sekunden-TT-Transport.
Antwortgrenzen zählen tatsächliche Bytes auch ohne beziehungsweise mit zu
kleiner `Content-Length`. Strikte Antworten verlangen JSON, begrenzte Tiefe und
rekursiv eindeutige Schlüssel, einschließlich case-insensitiver Duplikate.
Redirects, Cookies und automatische Requestlogger des Directory-HTTP-Clients
sind ausgeschaltet. Quellfehler verlassen die Grenze ohne Raw-Body, URI,
Token, Secret oder ursprüngliche InnerException. Caller-Abbruch bleibt Abbruch.

## Ergebnis und Fehler

- **200 / `allowed: true`**: aktuelles aktives Konto, aktuelle Scope-Mitgliedschaft
  und effektive Pflichtrolle des aktiven API-Clients sind belegt.
- **200 / `allowed: false`**: bestätigte Deaktivierung/Löschung des Einzelkontos,
  fehlende Scope-Mitgliedschaft, fehlende Pflichtrolle oder deaktivierter API-Client.
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
frischer persönlicher TT-Verknüpfung, dauerhafter Vorgangspause bei Rechteentzug
und auditierter Einzelfreigabe (TT-Administration **plus** nachgewiesenes Flowzer-
Betriebsrecht) bleibt als nächster Lieferabschnitt offen. Provider-Unklarheit darf
nicht als bestätigter Entzug gespeichert werden; ein späteres Ja darf einen
pausierten Vorgang nicht automatisch reaktivieren. Bereits ausgeführte
Ticketänderungen werden bei Prozessabbruch nicht zurückgerollt.

Die Tests verwenden ausschließlich synthetische HTTP-, Auth- und Storagequellen.
Sie ersetzen keine echte Keycloak-/HTTPS-, PostgreSQL-Konkurrenz-, Einbettungs-
oder 45-Minuten-Abnahme. Demo-Gruppenzuordnung, Secret-/Client-Einrichtung,
Onlineinstallation und koordinierter Demo-Rollout sind nicht Teil dieses lokalen
Nachweises. Keine Timerimplementierung, Mail, Kalender oder KI-Outboundeffekte.

### Lokaler Nachweis dieses Lieferabschnitts

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
