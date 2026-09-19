# Mitgelieferte Konnektoren: HTTP und E-Mail

**Stand:** 19. September 2026

Ein Service-Task wird in Flowzer zu einem [Auftrag für einen Worker](SERVICE-TASK-WORKER.md).
Für einen HTTP-Aufruf oder eine E-Mail einen eigenen Dienst zu bauen und zu betreiben, ist
dafür oft zu viel. Flowzer bringt deshalb zwei **eingebaute Worker** mit, die im API-Prozess
laufen.

Sie sind kein Sonderweg: Sie holen ihre Aufträge über dieselbe Vergabe, halten dieselbe
Sperre, verlängern dieselbe Lease und melden über dieselben drei Wege zurück wie ein externer
Worker — Ergebnis, technischer Fehlschlag oder fachlicher BPMN-Fehler. Die Engine merkt nicht,
ob am anderen Ende ein Konnektor oder ein fremder Dienst steht.

Beide sind **standardmäßig aus**. Ein Konnektor hat Außenwirkung; das Einschalten ist eine
Betriebsentscheidung und keine Modellierungsentscheidung.

## Aktivieren

| Einstellung | Bedeutung |
| --- | --- |
| `Connectors__PollIntervalSeconds` | Default 5; Abstand zwischen zwei Durchgängen je Konnektor |
| `Connectors__MaxConcurrentJobs` | Default 4; wie viele Aufträge ein Konnektor gleichzeitig bearbeitet |
| `Connectors__LeaseSeconds` | Default 120; Sperrdauer je Auftrag. Der Host verlängert sie bei langen Läufen auf der Hälfte der Zeit |
| `Connectors__SecretEnvironmentVariablePrefix` | Default `FLOWZER_CONNECTOR_SECRET_`; Namensraum für `secret:`-Referenzen |

Im Compose-Stack stehen dafür `FLOWZER_CONNECTOR_HTTP_ENABLED`,
`FLOWZER_CONNECTOR_HTTP_ALLOWED_HOSTS`, `FLOWZER_CONNECTOR_EMAIL_ENABLED` und die SMTP-Werte
bereit; siehe `.env.example`, `compose.runtime.yml` und `compose.coolify.yaml`.

### Technische Identität

Die Aufträge eines Konnektors gehören einer festen, dokumentierten Kennung:

- Benutzerkennung `f10c2e17-0000-4000-8000-000000000001`
- Worker-Kennung `flowzer-connector-http` bzw. `flowzer-connector-email`

Beides zusammen bildet den Sperrinhaber, den `GET /job` in der Betriebssicht zeigt. Die
Kennung ist **kein Verzeichniskonto**: Sie meldet sich nicht an, bekommt keine Rolle und
erscheint nicht in der Benutzerauswahl. Die Rollenprüfung der Worker-Endpunkte liegt am
Controller, den der eingebaute Host nicht benutzt — er ruft den Auftragsdienst direkt.

## Secrets

Ein Modell nennt nie einen geheimen Wert, sondern nur seinen Namen:

```
secret:ZAHLUNGS_API
```

Flowzer löst das erst unmittelbar vor dem Aufruf aus der Umgebungsvariablen
`FLOWZER_CONNECTOR_SECRET_ZAHLUNGS_API` auf. Der Klartext steht weder im Modell noch im
Auftrag, im Ergebnis, im Log oder in der Diagnose. Lässt sich die Referenz nicht auflösen,
antwortet der Konnektor mit einem BPMN-Fehler statt mit einem Wiederholungsversuch: Ein
zweiter Anlauf änderte nichts.

Die Werte selbst gehören nicht in `.env` und nicht in die Compose-Vorlage. Sie werden — wie
die KI-Provider-Secrets — ausschließlich zur Container-Laufzeit aus dem Secret-Store in die
Prozessumgebung injiziert.

## HTTP-Konnektor (`flowzer:http`)

### Modellierung

```xml
<bpmn:serviceTask id="ServiceTask_Beleg" name="Beleg abrufen">
  <bpmn:extensionElements>
    <zeebe:taskDefinition type="flowzer:http" retries="3" />
    <zeebe:ioMapping>
      <zeebe:input source="=&quot;https://api.example.com/belege/&quot; + belegNummer" target="url" />
      <zeebe:input source="GET" target="method" />
      <zeebe:input source="secret:ZAHLUNGS_API" target="authorization" />
    </zeebe:ioMapping>
  </bpmn:extensionElements>
  <bpmn:incoming>Flow_1</bpmn:incoming>
  <bpmn:outgoing>Flow_2</bpmn:outgoing>
</bpmn:serviceTask>
```

Die Eingaben sind ganz normale Auftragsvariablen. Ein `zeebe:ioMapping` ist trotzdem der
bessere Weg: Ohne Deklaration bekommt der Konnektor **alle Prozessvariablen**, und am Modell
wäre nicht ablesbar, was das Haus verlässt. `source` ist ein Ausdruck und braucht das
führende `=`; ohne das steht der Name selbst als Festwert im Auftrag.

### Eingaben

| Name | Pflicht | Bedeutung |
| --- | --- | --- |
| `url` | ja | Absolute Adresse. Ohne `Connectors__Http__AllowHttp` nur `https` |
| `method` | nein | Default `GET`; erlaubt sind GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS |
| `headers` | nein | Objekt aus Name und Textwert |
| `query` | nein | Objekt; wird an eine vorhandene Query angehängt |
| `body` | nein | Objekt wird zu JSON (`application/json`), ein Text geht als `text/plain` hinaus. Ein `Content-Type` in `headers` gewinnt |
| `timeoutSeconds` | nein | Default `Connectors__Http__TimeoutSeconds` (30), begrenzt durch `MaxTimeoutSeconds` (120) |
| `authorization` | nein | Wert des `Authorization`-Headers, üblicherweise als `secret:NAME`. Schlägt einen gleichnamigen Eintrag in `headers` |
| `errorOn4xx` | nein | Default `true` |

### Ergebnis

| Name | Bedeutung |
| --- | --- |
| `status` | Statuscode als Zahl |
| `headers` | Antwortheader als Objekt; nur lesbare Textwerte, mehrfach gesetzte zusammengefasst |
| `body` | Bei `application/json` (und `…+json`) das geparste Objekt, sonst Text |

Der Körper wird nach `Connectors__Http__MaxResponseBytes` (Default 1 MiB) abgeschnitten. Ein
gekürzter JSON-Körper kommt bewusst als **Text** zurück, nicht als halbes Objekt. Ohne diese
Grenze bestimmte das aufgerufene System, wie viel Speicher der Prozess belegt.

### Grenzen des Ziels (SSRF-Schutz)

Dieselben Regeln wie bei den [Worker-Webhooks](SERVICE-TASK-WORKER.md#benachrichtigung-statt-nachfragen):

- `Connectors__Http__AllowedHosts__0`, optional mit führendem `*.` für eine Domain.
  **Ohne Eintrag ist kein Aufruf möglich.**
- Weiterleitungen werden nicht gefolgt. Ein freigegebenes Ziel könnte sonst auf eine
  interne Adresse umleiten.
- Ziele, die auf Loopback, private Netze, Link-Local (einschließlich der Metadatendienste
  der Cloud) oder Carrier-Grade NAT auflösen, werden abgelehnt — auch wenn der Name
  freigegeben ist.
- `Connectors__Http__AllowHttp` (Default `false`) nur für Ziele ohne TLS im selben Netz.

### Fehlerabbildung

| Ausgang | Rückmeldung |
| --- | --- |
| Zeitablauf, Netzfehler, Status ≥ 500 | `Fail` mit `Connectors__Http__RetryBackoffSeconds` (Default 30). Die Versuche des Auftrags entscheiden, wann er liegen bleibt |
| Status 4xx mit `errorOn4xx` | BPMN-Fehler `HTTP_<status>`, etwa `HTTP_404`, mit `errorMessage` = Statuszeile und `variables` `{ status, body }` |
| Status 4xx ohne `errorOn4xx` | Regulärer Abschluss; der Prozess bewertet den Status selbst |
| Host nicht freigegeben, intern, falsches Schema | BPMN-Fehler `HTTP_NOT_ALLOWED`, kein Wiederholungsversuch |
| `url` fehlt, Methode unbekannt, Secret nicht auflösbar | BPMN-Fehler `HTTP_INVALID_REQUEST`, kein Wiederholungsversuch |

Der Unterschied ist Absicht: `Fail` heißt „hat technisch nicht geklappt, versuch es noch
einmal“. Ein BPMN-Fehler heißt „das ist ein gültiges Ergebnis, das im Modell einen eigenen
Weg hat“. Ein zweiter Versuch gegen einen nicht freigegebenen Host änderte nichts, also ist
er kein Fehlschlag, sondern eine Entscheidung.

### Error-Boundary im Modell

```xml
<bpmn:error id="Error_BelegUnbekannt" name="Beleg unbekannt" errorCode="HTTP_404" />
...
<bpmn:boundaryEvent id="BoundaryError_1" name="Unbekannt" attachedToRef="ServiceTask_Beleg">
  <bpmn:outgoing>Flow_Klaeren</bpmn:outgoing>
  <bpmn:errorEventDefinition id="ErrorEventDefinition_1" errorRef="Error_BelegUnbekannt" />
</bpmn:boundaryEvent>
```

Fängt das Boundary, wird der Service-Task unterbrochen, der Fehlerpfad läuft, und `status`
und `body` stehen dort im Prozesskontext. Fängt niemand, endet die Instanz als `Failed` mit
einer Begründung der Form `Unhandled BPMN error 'HTTP_404' at 'ServiceTask_Beleg'.`.

## E-Mail-Konnektor (`flowzer:email`)

Versand über **MailKit** (`MailKit` 4.18.0). Bewusst nicht `System.Net.Mail.SmtpClient`: Der
ist als veraltet gekennzeichnet und für einen unbeaufsichtigten Dienst zu schwach bei
TLS-Aushandlung und Serverfehlern.

### Konfiguration

| Einstellung | Bedeutung |
| --- | --- |
| `Connectors__Email__Enabled` | Default `false` |
| `Connectors__Email__From` | Absender aller Nachrichten. **Pflicht, wenn aktiviert** |
| `Connectors__Email__AllowedRecipientDomains__0` | Freigegebene Empfängerdomäne, optional mit führendem `*.`. **Ohne Eintrag wird nichts versendet** |
| `Connectors__Email__RetryBackoffSeconds` | Default 30 |
| `Connectors__Email__Smtp__Host` | **Pflicht, wenn aktiviert** |
| `Connectors__Email__Smtp__Port` | Default 587 |
| `Connectors__Email__Smtp__UseStartTls` | Default `true` |
| `Connectors__Email__Smtp__Username` | Leer heißt: ohne Anmeldung |
| `Connectors__Email__Smtp__PasswordSecretName` | Name hinter `FLOWZER_CONNECTOR_SECRET_` |
| `Connectors__Email__Smtp__TimeoutSeconds` | Default 30 |

Ein aktivierter Konnektor ohne Absender oder ohne SMTP-Server **startet die Installation
nicht**. Er würde sonst Aufträge übernehmen und wieder liegen lassen; das fiele erst am
ersten Vorgang auf.

Die leere Freigabeliste der Empfängerdomänen ist ebenfalls Absicht und der wichtigere
Schutz: Ein Testsystem trägt echte Vorgangsdaten. Ohne ausdrückliche Angabe soll es niemandem
draußen schreiben.

### Eingaben

| Name | Pflicht | Bedeutung |
| --- | --- | --- |
| `to` | ja | Adresse oder Liste von Adressen |
| `cc`, `bcc` | nein | wie `to` |
| `subject` | ja | Betreff |
| `text` | ja | Textfassung |
| `html` | nein | HTML-Fassung; die Textfassung bleibt Pflicht |

Der Absender kommt aus der Konfiguration, nicht aus dem Modell: Sonst entschiede ein Workflow,
in wessen Namen das Haus schreibt.

### Ergebnis

| Name | Bedeutung |
| --- | --- |
| `messageId` | Kennung der Nachricht |
| `acceptedRecipients` | Liste der angenommenen Adressen aus `to`, `cc` und `bcc` |

### Fehlerabbildung

| Ausgang | Rückmeldung |
| --- | --- |
| SMTP-Verbindungs-, TLS- oder Anmeldefehler | `Fail` mit Wartezeit |
| Empfängerdomäne nicht freigegeben, Adresse unbrauchbar | BPMN-Fehler `EMAIL_NOT_ALLOWED` mit `variables` `{ recipient }` |
| `to`, `subject` oder `text` fehlt, Adresse nicht lesbar | BPMN-Fehler `EMAIL_INVALID_REQUEST` |

### Modellierungsbeispiel

```xml
<bpmn:serviceTask id="ServiceTask_Info" name="Antragsteller informieren">
  <bpmn:extensionElements>
    <zeebe:taskDefinition type="flowzer:email" retries="3" />
    <zeebe:ioMapping>
      <zeebe:input source="=antragstellerMail" target="to" />
      <zeebe:input source="=&quot;Ihr Urlaubsantrag vom &quot; + antragsdatum" target="subject" />
      <zeebe:input source="=&quot;Ihr Antrag wurde genehmigt.&quot;" target="text" />
    </zeebe:ioMapping>
  </bpmn:extensionElements>
  <bpmn:incoming>Flow_1</bpmn:incoming>
  <bpmn:outgoing>Flow_2</bpmn:outgoing>
</bpmn:serviceTask>
```

## Betriebssicht

`GET /operations/diagnostics` enthält einen Abschnitt `connectors` mit einer Zeile je
Konnektor — auch für die abgeschalteten, denn „nicht aktiviert“ ist eine Aussage, „gar nicht
aufgeführt“ wäre keine:

```json
{
  "connectors": [
    { "name": "email", "jobType": "flowzer:email", "enabled": false,
      "lastRunAtUtc": null, "processedJobs": 0, "failedJobs": 0, "lastErrorMessage": null },
    { "name": "http", "jobType": "flowzer:http", "enabled": true,
      "lastRunAtUtc": "2026-09-19T12:00:05Z", "processedJobs": 18, "failedJobs": 1,
      "lastErrorMessage": "GET https://api.example.com/belege answered HTTP 500." }
  ]
}
```

Die Zähler laufen seit dem Start des Prozesses. Ein fachlicher BPMN-Fehler zählt als
verarbeitet, nicht als fehlgeschlagen: Das Modell hat dafür einen eigenen Weg. `lastErrorMessage`
ist immer eine vom Konnektor formulierte Meldung — rohe Ausnahmetexte bleiben im Log, weil sie
Adressen oder aufgelöste Werte tragen könnten. Adressen erscheinen ohne Query-Teil.

Die Betriebsseite der Konsole zeigt dieselben Angaben als eigene Kachel.

## Grenzen

- **Kein eingehender Webhook-Trigger.** Ein Prozess lässt sich damit nicht von außen starten
  oder fortsetzen.
- **Keine Anhänge** im E-Mail-Konnektor.
- **Keine OAuth-Flows** im HTTP-Konnektor. Ein fertiger Wert im `Authorization`-Header ja,
  ein Client-Credentials-Austausch nein.
- **Keine Konnektor-Vorlagen im Modeler.** Die Eingaben werden von Hand als `zeebe:input`
  deklariert.
- **Keine Idempotenz über einen Neuversuch hinweg.** Ein `Fail` nach einem bereits erfolgten
  Seiteneffekt — die Gegenstelle hat angenommen, die Antwort ging verloren — führt beim
  nächsten Versuch zu einem zweiten Aufruf. Wo das zählt, gehört ein Idempotenzschlüssel in
  die `headers` des Modells.
- Die Zähler der Diagnose leben im Prozess und beginnen nach einem Neustart wieder bei null.
