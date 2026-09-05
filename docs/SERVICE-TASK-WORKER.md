# Service-Tasks: Vertrag für externe Worker

**Stand:** 5. September 2026

Die Engine führt Service-Tasks nicht selbst aus. Sie hätte dafür Netzwerkzugriff, Zugangsdaten und eine eigene Wiederholungslogik nötig, und jede fachliche Anbindung würde in der Engine landen. Stattdessen wird jeder wartende Service-Task ein **Auftrag**, den ein eigener Dienst holt, abarbeitet und zurückmeldet.

## Wie ein Auftrag entsteht

Erreicht ein Token einen Service-Task, entsteht ein Auftrag mit dem Typ aus `zeebe:taskDefinition/@type`. Der Auftrag trägt Prozess, Instanz, Token und die Eingabewerte des Tokens. Er verschwindet, sobald er zurückgemeldet wurde oder sein Token nicht mehr wartet.

```xml
<bpmn:serviceTask id="ServiceTask_1" name="Zahlung auslösen">
  <bpmn:extensionElements>
    <zeebe:taskDefinition type="zahlung" retries="3" />
  </bpmn:extensionElements>
</bpmn:serviceTask>
```

Ohne `retries` bekommt der Auftrag einen Versuch.

## Abholen und zurückmelden

Alle Endpunkte verlangen ein gültiges Token wie jeder andere Fachendpunkt.

**Aufträge übernehmen**

```http
POST /job/fetch
{ "type": "zahlung", "workerId": "zahlungsdienst-1", "maxJobs": 10, "lockSeconds": 300 }
```

Die Antwort enthält die übernommenen Aufträge. Jeder gehört für `lockSeconds` diesem Worker; ein zweiter Worker bekommt ihn in dieser Zeit nicht. Läuft die Frist ab, ohne dass zurückgemeldet wurde, wird der Auftrag wieder vergeben. Ein abgestürzter Worker blockiert die Instanz damit nicht dauerhaft.

**Ergebnis melden**

```http
POST /job/{jobId}/complete
{ "workerId": "zahlungsdienst-1", "variables": { "belegNummer": "R-2026-118" } }
```

Der Prozess läuft weiter, die Werte stehen den folgenden Schritten zur Verfügung.

**Fehlschlag melden**

```http
POST /job/{jobId}/fail
{ "workerId": "zahlungsdienst-1", "errorMessage": "Endpunkt nicht erreichbar", "retryBackoffSeconds": 30 }
```

Bleiben Versuche übrig, wird der Auftrag nach der Wartezeit wieder vergeben. Ist der letzte verbraucht, bleibt er liegen und wartet auf einen Eingriff, statt still zu verschwinden. `GET /job` zeigt alle Aufträge samt Zustand; der Endpunkt verlangt die Betriebsrolle.

Meldet ein Worker zurück, dem der Auftrag nicht mehr gehört, antwortet die API mit 409. Das passiert, wenn seine Frist abgelaufen war und inzwischen ein anderer Worker übernommen hat. Zwei Ergebnisse für denselben Token würden den Prozess doppelt weiterführen.

## Benachrichtigung statt Nachfragen

Ein Worker, der nicht regelmäßig fragen möchte, meldet eine Adresse an:

```http
POST /job/webhook
{ "type": "zahlung", "url": "https://zahlungsdienst.example/flowzer", "secret": "…" }
```

Liegt ein Auftrag dieses Typs frei, ruft Flowzer die Adresse einmal je Auftrag auf:

```json
{ "event": "service-task.available", "jobId": "…", "type": "zahlung",
  "processInstanceId": "…", "createdAt": "2026-09-05T12:00:00Z" }
```

Die Benachrichtigung enthält bewusst keine Prozessdaten. Sie sagt nur, dass Arbeit vorliegt; geholt und zurückgemeldet wird über dieselben Endpunkte wie oben, mit Anmeldung. Ist ein `secret` hinterlegt, trägt der Aufruf den Header `X-Flowzer-Signature` mit `sha256=<HMAC-SHA256 über den Nachrichtentext>`. Der Worker bildet dieselbe Signatur und erkennt daran, dass die Benachrichtigung von dieser Installation stammt.

Nach `MaxConsecutiveFailures` Fehlversuchen in Folge wird eine Adresse nicht mehr benachrichtigt. `GET /job/webhook` zeigt Zustand und letzten Fehler; das Geheimnis wird nie zurückgeliefert.

## Konfiguration

| Einstellung | Bedeutung |
| --- | --- |
| `ServiceTaskWebhooks__Enabled` | Default `true`; schaltet die Benachrichtigung ab, das Abholen bleibt |
| `ServiceTaskWebhooks__AllowedHosts__0` | Freigegebene Zieladresse, optional mit führendem `*.` für eine Domain. **Ohne Eintrag wird keine Anmeldung angenommen** |
| `ServiceTaskWebhooks__AllowHttp` | Default `false`; nur für Worker ohne TLS im selben Netz |
| `ServiceTaskWebhooks__TimeoutSeconds` | Default 10 |
| `ServiceTaskWebhooks__PollIntervalSeconds` | Default 5; wie oft nach freien Aufträgen gesehen wird |
| `ServiceTaskWebhooks__MaxConsecutiveFailures` | Default 10 |

Die leere Freigabeliste ist Absicht: Eine Webhook-Anmeldung ist eine Aufforderung an die Engine, eine fremde Adresse aufzurufen. Ohne ausdrückliche Freigabe nimmt sie keine an.

## Was noch fehlt

- Ein Auftrag ohne verbleibende Versuche bleibt liegen; einen Endpunkt, ihn erneut freizugeben, gibt es noch nicht. Bis dahin hilft nur ein Abbruch der Instanz.
- Fehler eines Workers führen nicht zu einem BPMN-Fehlerereignis, weil die Engine Error- und Escalation-Semantik noch nicht umsetzt.
