# Eingehende Auslöser

**Stand:** 19. September 2026

Ein Ticketsystem, ein Shop oder ein Formulardienst soll einen Workflow starten oder einer
wartenden Instanz eine Nachricht zustellen können. Diese Systeme haben keine OIDC-Sitzung und
kein Bearer-Token; sie können einen Webhook aufrufen und ihn signieren. Genau dafür gibt es den
**Auslöser**: eine eigene Adresse mit einem eigenen Geheimnis, die ohne Anmeldung erreichbar ist.

Ein Auslöser ist bewusst schmal. Er entscheidet nichts: Er startet einen benannten Workflow oder
stellt eine benannte Nachricht zu, mit genau den Werten, die der Betrieb freigegeben hat.

## Zweck und Abgrenzung

| Art | Was passiert |
| --- | --- |
| `start` | Startet eine Instanz des hinterlegten Workflows — immer in der aktuell deployten Version. Ein Auslöser bindet sich nicht an eine Fassung. |
| `message` | Stellt einer wartenden Instanz eine BPMN-Nachricht zu. Welche Instanz gemeint ist, sagt ein Wert aus dem Aufruf. |

Nicht dafür gedacht: OAuth oder JWT für Auslöser, Signale, Dateiuploads und Vorlagen im Modeler.
Ein `message`-Auslöser stellt außerdem nur an **wartende** Instanzen zu; einen Workflow über ein
Nachrichten-Startereignis zu starten ist die Aufgabe eines Auslösers der Art `start`.

## Sicherheitsmodell

### Das Geheimnis wird genau einmal gezeigt

Die Installation erzeugt Schlüssel und Geheimnis selbst. Der **Schlüssel** steht in der Adresse
(`POST /trigger/{key}`), ist kein Geheimnis und verrät auch nichts über den Workflow dahinter.
Das **Geheimnis** — 256 Bit Zufall — steht ausschließlich in der Antwort auf das Anlegen und auf
das Erneuern. `GET /inbound-trigger` liefert es nie; ein zweiter Leseweg wäre ein zweiter Weg, an
ein einmal vergebenes Geheimnis zu kommen.

Wer es verliert, erneuert es (`rotate-secret`). Das alte gilt ab dem Commit nicht mehr — eine
Übergangszeit mit zwei gültigen Geheimnissen gibt es nicht, weil sie das Zurückziehen eines
verlorenen Geheimnisses aufschieben würde.

### Warum das Geheimnis versiegelt und nicht gehasht liegt

Der Aufrufer weist sich mit **HMAC-SHA256** über Zeitstempel und Körper aus. HMAC ist
symmetrisch: Wer eine Signatur prüfen will, muss denselben Schlüssel besitzen, mit dem sie
gebildet wurde. Aus einer Einwegableitung — PBKDF2, Argon2, gepfefferte Hashes — lässt sich keine
Signatur nachrechnen. Ein Geheimnis nur als Hash zu speichern **und** trotzdem HMAC zu prüfen,
geht nicht; eines von beidem muss weichen.

Weichen darf nicht die Signatur. Ohne sie müsste das fremde System das Geheimnis selbst als
Kopfzeile mitschicken, und jede Zwischenstelle, jedes Zugriffsprotokoll und jeder Fehlerbericht
sähe es im Klartext. Also bleibt die Signatur, und das Geheimnis liegt **versiegelt** in der
Ablage: AES-256-GCM unter einem installationsweiten Schlüssel, der nicht in der Datenbank steht.
Die Kennung des Auslösers geht als zusätzlich authentifizierte Angabe mit ein, damit ein
umkopierter Eintrag sich nicht unter fremder Kennung öffnen lässt. Das Format trägt seine Fassung
im Wert (`aesgcm-v1$…`), damit eine spätere Installation vorhandene Einträge erkennt.

**Die Grenze dieses Verfahrens:** Wer nur die Datenbank liest — Sicherung, Dump, fremder
Lesezugriff —, bekommt die Geheimnisse nicht. Wer zusätzlich die Konfiguration der Anwendung
liest, bekommt sie. Für einen stärkeren Schutz müsste der Schlüssel in ein HSM oder einen
externen Secret-Store wandern; der Weg dorthin ändert nur `InboundTriggerOptions`, nicht das
Format.

### Signatur, Zeitstempel und Replay-Fenster

Jeder Aufruf trägt zwei Kopfzeilen:

| Kopfzeile | Inhalt |
| --- | --- |
| `X-Flowzer-Timestamp` | Unix-Sekunden, als Text |
| `X-Flowzer-Signature` | `sha256=<hex>` |

Signiert wird `"{timestamp}.{rawBody}"` — der Zeitstempel, ein Punkt, dann der Körper **Byte für
Byte so, wie er gesendet wird**. Wer den Körper vor dem Signieren umformatiert, bekommt eine
andere Signatur.

Der Zeitstempel gehört mit in die Signatur: Ohne ihn wäre ein einmal mitgelesener Aufruf beliebig
oft wiederholbar. Er darf **±5 Minuten** von der Serverzeit abweichen. Ein Aufruf außerhalb
dieses Fensters wird abgelehnt, auch wenn die Signatur zu ihm passt. Verglichen wird
konstant-zeitig; ein Vergleich, der beim ersten abweichenden Zeichen abbricht, ließe die Signatur
Zeichen für Zeichen erraten.

Das Fenster ersetzt keine Aufzeichnung bereits gesehener Signaturen. Innerhalb der fünf Minuten
ist ein Aufruf wiederholbar — wer das ausschließen will, sendet einen `Idempotency-Key`
(siehe unten).

### 404 statt 403

Ein unbekannter und ein abgeschalteter Schlüssel antworten **identisch** mit 404. Ein
Unterschied verriete, welche Schlüssel es gibt und welche Anbindung gerade ruht. Auch die
Größenprüfung läuft, **bevor** überhaupt nachgesehen wird, ob es den Schlüssel gibt — sonst
verriete der Unterschied zwischen 413 und 404 dasselbe.

### Datensparsamkeit der Variablen

Der Standard ist `fields` mit leerer Liste: **keine Variablen**. Ein fremdes System schickt in der
Regel seinen ganzen Datensatz — Kundennummern, Freitexte, Anhänge als Text. Ohne Grenze läge das
alles dauerhaft in den Prozessvariablen und damit in jeder Sicht auf die Instanz.

| Modus | Ergebnis |
| --- | --- |
| `fields` | Nur die in `allowedFields` genannten **obersten** Felder, jedes als eigene Variable. |
| `body` | Der ganze Körper als **eine** Variable `payload`. |

`body` ist bequem, aber weitreichend. `fields` hat außerdem einen zweiten Nutzen: Im Modus `body`
kann ein fremdes System keine beliebigen Prozessvariablen überschreiben, weil alles unter
`payload` landet — im Modus `fields` entscheidet der Betrieb, welche Namen überhaupt entstehen.

Der Körper wird vor der Auswahl genauso in Prozesswerte übersetzt wie die Werte eines
Startformulars. Ein eigener Konverter würde dieselben Daten anders abbilden als ein Start über die
Konsole — eine Zahl einmal als Ganzzahl und einmal als Text, und eine Bedingung im Modell träfe je
nach Startweg anders zu.

### Kontingent

Zwei Fenster greifen nacheinander:

1. Das allgemeine Kontingent je Aufrufer (`RateLimiting:*`). Für einen anonymen Aufruf ist das die
   Adresse — hinter einem Reverse Proxy die echte, wenn die Weiterleitungsheader konfiguriert sind.
2. Ein eigenes Fenster **je Auslöser** (`InboundTriggers:PermitLimit`, Standard 60 je Minute).

Beide sind verkettet, nicht alternativ. Ohne das zweite fielen alle anonymen Aufrufe in dieselbe
Adress-Partition: Ein einzelner Auslöser könnte darin das Kontingent aller verbrauchen, und
umgekehrt verdeckte die gemeinsame Partition, welcher Auslöser hämmert.

### Wer die Instanz gestartet hat

Ein Aufruf von außen hat keine angemeldete Person hinter sich. Instanzen, die so entstehen, tragen
als Initiator die technische Identität `urn:flowzer:inbound-trigger` mit der Kennung des
Auslösers als Subject — **keinen menschlichen Initiator**. Über die Initiatorregel der
Zugriffsprüfung sind sie damit für niemanden sichtbar; wer sie sehen soll, braucht die
Betriebsrolle oder eine Aufgabe darin.

Im Subject steht die Kennung des Auslösers, nicht sein Name: Namen ändern sich, und eine
Identität, die sich beim Umbenennen mitändert, wäre weder als Initiator noch als Idempotenzbereich
stabil.

### Was protokolliert wird

Jeder erfolgreiche Aufruf zählt `useCount` hoch und setzt `lastUsedAt`. Jede Ablehnung setzt
`lastFailureAt` und einen kurzen, festen `lastFailureReason`:

| Grund | Bedeutung |
| --- | --- |
| `disabled` | Der Auslöser ist abgeschaltet. |
| `timestamp` | Kein oder ein zu alter Zeitstempel. |
| `signature` | Keine oder eine falsche Signatur. |
| `payload` | Der Körper ist kein JSON-Objekt. |
| `correlation-key` | Am konfigurierten Pfad steht kein einzelner Wert. |
| `not-deployed` | Der Workflow hat keine deployte Version. |

**Daten des Aufrufers gehören nicht dazu.** Sonst wäre ein offen erreichbarer Endpunkt ein Weg,
beliebigen Text in die Ablage zu schreiben.

Zählerstand und Fehlerfelder werden getrennt von den verwalteten Angaben geschrieben. Ein
Umbenennen darf den Nutzungsstand nicht auf den Stand zurücksetzen, den die Oberfläche zufällig
geladen hatte, und zwei gleichzeitige Aufrufe dürfen nicht als einer zählen.

## Der Aufruf

```http
POST /trigger/{key}
Content-Type: application/json
X-Flowzer-Timestamp: 1789800000
X-Flowzer-Signature: sha256=3f1c…
Idempotency-Key: bestellung-4711        (optional)

{ "orderId": "4711", "amount": 42 }
```

Der Körper muss ein **JSON-Objekt** sein und darf höchstens **256 KiB** groß sein.

### Antworten

| Status | Körper | Wann |
| --- | --- | --- |
| 202 | `{ "instanceId": "…" }` | Art `start`, Instanz gestartet |
| 202 | `{ "correlated": true }` | Art `message`, zugestellt |
| 202 | `{ "correlated": false }` | Art `message`, es wartete niemand |
| 400 | Problem Details | Körper kein JSON-Objekt, oder am Korrelationspfad steht kein einzelner Wert |
| 401 | Problem Details | Signatur fehlt/falsch oder Zeitstempel außerhalb des Fensters |
| 404 | Problem Details | Schlüssel unbekannt **oder** abgeschaltet |
| 413 | Problem Details | Körper größer als 256 KiB |
| 422 | Problem Details | Der Workflow hat keine deployte Version |
| 429 | `ApiStatusResult` | Kontingent überschritten, mit `Retry-After` |

`correlated: false` ist **kein Fehler**. Wartete keine Instanz, wäre eine Fehlermeldung für einen
Aufrufer ein Weg herauszufinden, welche Vorgänge es in dieser Installation gibt.

Die Antworten des Aufrufs tragen bewusst nicht den Hausumschlag `ApiStatusResult`: Ein fremdes
System soll `{ "instanceId": … }` lesen können, ohne den Vertrag dieser API zu kennen. Die
Ausnahme ist 429, die aus der allgemeinen Kontingent-Middleware kommt.

### Korrelation bei der Art `message`

`correlationKeyPath` ist ein Pfad in Punktnotation über dem Körper, zum Beispiel `order.id`. Sein
Wert wird zum Korrelationsschlüssel. Er muss ein einzelner Wert sein — Zeichenkette, Zahl oder
Wahrheitswert; ein Objekt, ein Feld oder `null` zählt nicht.

Gesucht wird dann die Instanz, die auf diese Nachricht mit genau diesem Schlüssel wartet. Im
Modell steht der Schlüssel als Ausdruck über den Prozessvariablen:

```xml
<bpmn:message id="Message_OrderPaid" name="OrderPaid">
  <bpmn:extensionElements>
    <zeebe:subscription correlationKey="=orderId" />
  </bpmn:extensionElements>
</bpmn:message>
```

Wartet die Instanz mit `orderId = "4711"`, korreliert ein Aufruf mit `{"order":{"id":"4711"}}` und
dem Pfad `order.id`.

### Idempotenz

Der optionale Header `Idempotency-Key` wird an den vorhandenen Mechanismus durchgereicht (siehe
[IDEMPOTENCY.md](IDEMPOTENCY.md)). Der Bereich ist an den **Auslöser** gebunden, nicht an eine
Person: Zwei Auslöser mit demselben Schlüssel kollidieren nicht. Eine identische Wiederholung
liefert dieselbe `instanceId`, ohne eine zweite Instanz zu starten; derselbe Schlüssel mit anderem
Inhalt antwortet mit 409.

**Nur für die Art `start`.** Nachrichtenzustellung kennt keine Idempotenz — dort wirkt allein das
Replay-Fenster von fünf Minuten.

## Beispiele

### curl

```sh
BODY='{"orderId":"4711","amount":42}'
TS=$(date +%s)
SECRET='<das einmal gezeigte Geheimnis>'
SIG=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -r | cut -d' ' -f1)

curl -X POST 'https://flowzer.example/trigger/<key>' \
  -H 'Content-Type: application/json' \
  -H "X-Flowzer-Timestamp: $TS" \
  -H "X-Flowzer-Signature: sha256=$SIG" \
  -d "$BODY"
```

### Node

```js
import { createHmac } from 'node:crypto';

const base = 'https://flowzer.example';
const key = '<key>';
const secret = '<das einmal gezeigte Geheimnis>';

const body = JSON.stringify({ orderId: '4711', amount: 42 });
const timestamp = Math.floor(Date.now() / 1000);
const signature = createHmac('sha256', secret).update(`${timestamp}.${body}`).digest('hex');

const response = await fetch(`${base}/trigger/${key}`, {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-Flowzer-Timestamp': String(timestamp),
    'X-Flowzer-Signature': `sha256=${signature}`,
  },
  // Denselben String senden, der signiert wurde — nicht das Objekt erneut serialisieren.
  body,
});
console.log(response.status, await response.json());
```

### Python

```python
import hashlib, hmac, json, time, urllib.request

base = "https://flowzer.example"
key = "<key>"
secret = "<das einmal gezeigte Geheimnis>"

body = json.dumps({"orderId": "4711", "amount": 42}, separators=(",", ":"))
timestamp = str(int(time.time()))
signature = hmac.new(
    secret.encode(), f"{timestamp}.{body}".encode(), hashlib.sha256
).hexdigest()

request = urllib.request.Request(
    f"{base}/trigger/{key}",
    data=body.encode(),
    headers={
        "Content-Type": "application/json",
        "X-Flowzer-Timestamp": timestamp,
        "X-Flowzer-Signature": f"sha256={signature}",
    },
    method="POST",
)
with urllib.request.urlopen(request) as response:
    print(response.status, response.read().decode())
```

## Verwaltung

Alle Endpunkte unter `/inbound-trigger` verlangen die Betriebsrolle. Wer hier anlegt, öffnet eine
Adresse, die ohne Anmeldung Workflows startet — das darf keine bloße Zugangsrolle können.

| Aufruf | Bedeutung |
| --- | --- |
| `GET /inbound-trigger` | Alle Auslöser, **ohne** Geheimnis und ohne dessen Ableitung |
| `POST /inbound-trigger` | Anlegen; die Antwort enthält Schlüssel **und Geheimnis im Klartext** |
| `PUT /inbound-trigger/{id}` | Name, Aktivzustand, Ziel, Variablenmodus, Felder |
| `POST /inbound-trigger/{id}/rotate-secret` | Neues Geheimnis, einmal im Klartext |
| `DELETE /inbound-trigger/{id}` | Endgültig; die Adresse ist danach ein 404 |

Die **Art** lässt sich nachträglich nicht ändern: Ein bereits verteilter Schlüssel darf nicht
still von „startet einen Workflow" zu „stellt eine Nachricht zu" werden. Wer das Ziel wechseln
will, legt einen neuen Auslöser an.

Ein Auslöser der Art `start` darf nur auf einen Workflow zeigen, den es im Katalog gibt. Sonst
entstünde ein Eintrag, der erst beim ersten Aufruf von außen auffällt — und dann als 422 beim
fremden System, nicht beim Betrieb.

In der Konsole liegt das unter **Betrieb → Auslöser** (`/triggers`).

## Konfiguration

| Einstellung | Bedeutung |
| --- | --- |
| `InboundTriggers__SecretKey` | Der installationsweite Schlüssel, unter dem die Geheimnisse versiegelt liegen. **Ohne ihn nimmt die Installation keinen Auslöser an.** Mindestens 32 Zeichen; ein kürzerer Wert hält den Start an. |
| `InboundTriggers__PermitLimit` | Aufrufe je Auslöser und Fenster, Standard 60 |
| `InboundTriggers__WindowSeconds` | Länge des Fensters, Standard 60 |

Der fehlende Standardwert ist Absicht — wie die leere Freigabeliste ausgehender Webhooks: Ein
eingebauter Schlüssel stünde im Quelltext und wäre damit keiner.

**Geht der Schlüssel verloren**, lassen sich vorhandene Auslöser nicht mehr prüfen; jeder Aufruf
antwortet dann mit 401. Die Auslöser selbst und ihre Adressen bleiben bestehen — sie brauchen
über `rotate-secret` ein neues Geheimnis, und das fremde System muss es übernehmen. Der Schlüssel
gehört deshalb in die gesicherten Zugangsdaten der Installation, nicht nur in eine Compose-Datei.

Die Adresse muss am Gateway ankommen: `trigger` und `inbound-trigger` stehen in der
Weiterleitungsliste in `deploy/console/entrypoint.sh`; `tests/ui-smoke/check-gateway-routes.sh`
prüft das.

## Ablage

PostgreSQL-Migration `018_inbound_triggers.sql` legt `inbound_triggers` an. Die verwalteten
Angaben stehen als JSON in `body`; Zählerstand, Aktivzustand und letzter Fehler liegen in eigenen
Spalten, damit ein Aufruf in einem Statement zählen kann, ohne eine gleichzeitige Änderung der
Verwaltung zu überschreiben. Der Schlüssel ist eindeutig indiziert.

Die Dateiablage bildet denselben Vertrag ab, hält Eindeutigkeit und Hochzählen aber nur innerhalb
eines Prozesses zusammen. Sie ist für lokale Entwicklung gedacht; mehrere API-Prozesse brauchen
PostgreSQL.

## Grenzen

- Kein OAuth und kein JWT für Auslöser, keine Signale, keine Dateiuploads, keine Vorlagen im
  Modeler.
- Ein `message`-Auslöser stellt nur an wartende Instanzen zu, nicht an Nachrichten-Startereignisse.
- Gibt es mehrere wartende Instanzen mit demselben Korrelationsschlüssel, bekommt eine davon die
  Nachricht. Eindeutigkeit ist Sache des Modells.
- Die Suche nach der wartenden Instanz liest die Nachrichtenanmeldungen und filtert im Speicher.
  Für die heutigen Bestände genügt das; ein eigener Suchvertrag in der Ablage wäre der nächste
  Schritt.
- Innerhalb des Replay-Fensters von fünf Minuten ist ein Aufruf ohne `Idempotency-Key`
  wiederholbar. Gesehene Signaturen werden nicht aufgezeichnet.
- Der installationsweite Schlüssel liegt in der Konfiguration, nicht in einem HSM.
