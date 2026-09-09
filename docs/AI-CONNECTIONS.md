# KI-Verbindungen und Secret-Referenzen

**Stand:** 9. September 2026 · Issue #240 / PR #241, ergänzt durch #244 / PR #245, #248 / PR #249 und #252

Dieses Teilpaket stellt die sichere Verwaltungsbasis fuer KI-Tasks bereit. #244 / PR #245 ergänzt
eine ausschließlich interne Provideraufrufschicht; #252 bindet eine konkrete, unveränderliche
Verbindungsrevision beim Deployment und verwendet sie im persistenten BPMN-Lauf. Es gibt
weiterhin keinen öffentlichen Test- oder frei auslösbaren Providerendpunkt.

## Persistierter Vertrag

Flowzer speichert ausschließlich:

- stabile Verbindungs-ID und eindeutigen Namen,
- Providerfamilie (`OpenAi`, `OpenAiCompatible`, `Anthropic`),
- expliziten Verarbeitungsort (`Cloud` oder `Local`),
- bei kompatiblen Providern die administrierte Basisadresse,
- Standardmodell, Aktivstatus, Revision und Aenderungsakteur,
- eine opake Secret-Referenz wie `env:FLOWZER_AI_PRIMARY`.

Ein API-Key oder anderer geheimer Wert steht **nicht** im Datensatz. Deaktivieren ist eine
revisionierte Zustandsaenderung und kein Loeschen. Jede erfolgreiche Fassung wird zusätzlich
unveränderlich historisiert. Dadurch bleiben bereits gebundene Workflowfassungen ausführbar
und erklaerbar, ohne neue Konfiguration still zu übernehmen. Der Aktivstatus der aktuellen
Fassung bleibt ein Kill-Switch: Eine deaktivierte Verbindung stoppt auch historisch gebundene
Läufe vor Secret- oder Netzwerkzugriff.

## Installationsgrenzen

| Einstellung | Bedeutung |
|---|---|
| `Ai__AllowCloudProviders` | ausdrueckliches Opt-in fuer Cloud-Verarbeitung; Default `false` |
| `Ai__AllowLocalEndpoints` | getrenntes Opt-in fuer lokale/private Ziele; Default `false` |
| `Ai__SecretEnvironmentVariablePrefix` | erlaubter Namensraum; Default `FLOWZER_AI_` |

Standard-OpenAI und Anthropic sind feste Cloudfamilien. Eine abweichende Basisadresse ist
nur fuer `OpenAiCompatible` moeglich. Cloudziele muessen HTTPS verwenden und duerfen keine
eingebetteten Credentials, Queryparameter, Fragmente, Loopback- oder private IP-Adressen
enthalten. Lokale Ziele benoetigen das gesonderte Installations-Opt-in.

#248 / PR #249 ergänzt die Laufzeitgrenze für benutzerdefinierte Endpunkte: Der Host wird unmittelbar
vor jedem Aufruf genau einmal aufgelöst. Ein Cloudziel wird nur akzeptiert, wenn **alle**
Ergebnisse öffentliche Unicast-Adressen sind; gemischte öffentliche/private Antworten werden
insgesamt abgelehnt. Der anschließende Socketaufbau verwendet ausschließlich diesen geprüften
Adressvorrat und stimmt Host sowie Port erneut ab. Systemproxys und Weiterleitungen sind für
diesen Transport abgeschaltet. Ausdrücklich lokale Verbindungen dürfen private und Loopback-
Adressen nutzen, jedoch keine unspezifizierten oder Multicast-Ziele. Der aufgabengebundene
Timeout umfasst bereits diese DNS- und Clientvorbereitung.

Es gibt keinen stillen Wechsel von lokal zu Cloud und keinen Modell-Fallback. Eine
Workflowdefinition darf diese Grenzen spaeter nur weiter einschraenken, nie erweitern.

## Secret-Store

`IAiSecretStore` trennt Verbindungsmetadaten von der Laufzeitauflösung. Die erste
Implementierung akzeptiert nur Referenzen im Format `env:NAME`, wobei `NAME` aus
Grossbuchstaben, Ziffern und Unterstrichen besteht und mit dem konfigurierten Prefix beginnt.
`env:PATH`, Dateipfade und fremde Namensraeume werden abgewiesen.

Der Wert wird erst unmittelbar vor einem internen Provideraufruf in einen kurzlebigen
`AiSecretValue`-Puffer geladen. Der Puffer maskiert seine Textdarstellung und wird beim
Entsorgen ueberschrieben. Ein Adapter darf ihn weder persistieren noch protokollieren.

Beispiel fuer einen nur zur Laufzeit injizierten Namen:

```text
Verbindungsfeld: env:FLOWZER_AI_PRIMARY
Prozessumgebung: FLOWZER_AI_PRIMARY=<aus dem Installations-Secret-Store>
```

Der geheime Wert gehoert nicht in `.env`, BPMN, Formulare, Exporte, Logs oder den
Konsolen-Container.

## Rollen

Im authentifizierten Betrieb sind beide neuen Policies fail-closed:

- `Authentication__JwtBearer__Roles__AiConnectionUser`: sichere Metadaten lesen und spaeter
  eine aktive Verbindung im Modell verwenden; deaktivierte Eintraege liefern fuer diese
  Rolle weder Treffer in der Liste noch im Direktzugriff,
- `Authentication__JwtBearer__Roles__AiConnectionManager`: Metadaten, Endpunkt,
  Secret-Referenz und Aktivstatus verwalten; diese Rolle darf auch lesen.

Ein leerer Rollenname verweigert die jeweilige neue Faehigkeit. Im ausdrücklich lokalen
`Authentication__Scheme=None`-Entwicklungsmodus bleiben die Policies offen.

## API und Konsole

- `GET /ai/connection`
- `GET /ai/connection/{connectionId}`
- `POST /ai/connection`
- `PUT /ai/connection/{connectionId}`
- `PUT /ai/connection/{connectionId}/enabled`

Antworten enthalten `ready`, aber weder `secretReference` noch ein Secret. `ready` ist nur
dann wahr, wenn der Eintrag aktiv und die referenzierte Laufzeitvariable aktuell gesetzt
ist. Updates und Aktivstatuswechsel senden `expectedRevision`; ein veralteter Stand liefert
`409 application/problem+json` samt erwarteter und aktueller Revision.

Provider-Secrets werden im mitgelieferten Compose-Stack absichtlich nicht als
`${...}`-Variable aus `.env` uebernommen. Die Installation injiziert die referenzierte
`FLOWZER_AI_*`-Variable ausschließlich ueber ihren Orchestrator oder einen nicht
eingecheckten Deployment-Override in den API-Prozess.

Die React-Konsole bietet den Bereich nur der Verwaltungsrolle an. Bei einem vorhandenen
Eintrag bleibt das Feld fuer eine neue Secret-Referenz bewusst leer. Leer speichern behaelt
die bisherige Referenz serverseitig, anstatt sie zum Browser zurueckzuliefern.

## Persistenzgrenzen

PostgreSQL erzwingt Revision und case-insensitiv eindeutige Namen auch ueber mehrere
API-Prozesse. Migration `015_ai_connection_revisions.sql` speichert jede erfolgreiche
Verbindungsfassung unveränderlich unter `(id, revision)` und übernimmt vorhandene aktuelle
Einträge. Die Runtime löst exakt die in der Workflowdefinition gebundene Revision auf. Die
aktuelle Liste bleibt davon getrennt und kann deaktiviert oder weiterentwickelt werden.

Die Dateiablage führt dieselbe Historie unter
`FileStorage/AiConnections/History/<id>/<revision>.json`, serialisiert Schreibzugriffe aber
nur innerhalb eines Prozesses und bleibt der Entwicklungsweg ohne Mehrprozess- oder
Rollbackversprechen.

## Folgeschritte

Provideradapter, portables Ergebnisschema, KI-Task-Erweiterung, dauerhafter Laufzustand
(#246 / PR #247), DNS-/Socketbindung (#248 / PR #249), Provider-Executor (#250 / PR #251)
und atomare Engine-Anbindung (#252) liegen als getrennte Slices vor. Werkzeugregistry,
Freigaben, Kosten, Testmodus und Störungsbedienung folgen in eigenen Paketen.
