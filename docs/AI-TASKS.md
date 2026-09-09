# Versionierter KI-Aufgabenvertrag

**Stand: 9. September 2026 · #242 / PR #243, #244 / PR #245 und #246 / PR #247**

Flowzer modelliert eine KI-Aufgabe weiterhin als normalen BPMN-Service-Task. Die
Flowzer-Erweiterung beschreibt ausschließlich den fachlichen Auftrag; sie führt keinen
zweiten Prozessstandard und keine Abhängigkeit von einer einbettenden Anwendung ein.

## BPMN-Vertrag

```xml
<bpmn:serviceTask id="Ai_Classify">
  <bpmn:extensionElements>
    <zeebe:taskDefinition type="flowzer.ai.v1" retries="2" />
    <flowzer:aiTask contractVersion="1"
                     connectionId="118adeb6-65a4-4e57-a03b-d3b0a3300ac9"
                     model="optional-model-override"
                     instructionVersion="1"
                     maxInputTokens="4096"
                     maxOutputTokens="1024"
                     timeoutSeconds="60">
      <flowzer:instruction>Classify the request.</flowzer:instruction>
      <flowzer:resultSchema>{"type":"object","properties":{},"additionalProperties":false}</flowzer:resultSchema>
    </flowzer:aiTask>
    <zeebe:ioMapping>
      <zeebe:input source="=request" target="request" />
      <zeebe:output source="=result" target="classification" />
    </zeebe:ioMapping>
  </bpmn:extensionElements>
</bpmn:serviceTask>
```

- `connectionId` ist die stabile ID einer aktiven, einsatzbereiten KI-Verbindung.
- `model` ist freiwillig; ohne Wert gilt das Standardmodell der Verbindung.
- `instructionVersion` macht fachliche Promptänderungen nachvollziehbar.
- Mindestens eine vollständige Ein- und Ausgangszuordnung ist Pflicht. Es wird nicht
  stillschweigend der gesamte Prozesskontext an ein Modell gegeben.
- Das Ergebnisschema muss dem portablen Profil `flowzer.ai-result-schema/1` entsprechen
  und an der Wurzel `type: "object"` verwenden.
- Grenzen sind verbindlich: 1–128.000 Eingabetokens, 1–32.768 Ausgabetokens und
  1–300 Sekunden.

Unbekannte Attribute oder Kinder werden abgelehnt. Das gilt ausdrücklich auch für
`secretReference`: Weder Secret-Werte noch Secret-Referenzen gehören in BPMN, Exporte,
Prompts oder Browserantworten.

## Prüfung und aktueller Ausführungsstatus

Parser, Autorenprüfung und spätere Runtime verwenden denselben serverseitigen Vertrag.
Die Autorenprüfung kontrolliert zusätzlich, dass die referenzierte Verbindung existiert,
aktiv ist und ihr Secret serverseitig verfügbar ist. Browserfilter oder manipuliertes XML
können diese Bindung nicht erweitern.

Der Vertrag ist in diesem Slice **modellierbar und speicherbar, aber noch nicht
deploybar**. `serviceTask.aiTask` steht deshalb im Fähigkeitsvertrag als nicht ausführbar.
Die Vorabprüfung erhält mit `deployment=true` denselben Blocker wie das echte Deployment.
Damit kann kein produktiver Vorgang an einer nur vorgetäuschten KI-Runtime hängenbleiben.
Die Provider- und Schema-Schicht aus #244 / PR #245 ist intern bereits vorhanden. Das Deployment
bleibt dennoch blockiert, bis ein persistenter KI-Lauf den Provideraufruf, Recovery und
den Engine-Fortschritt als eine nachvollziehbare Zustandsmaschine verbindet.

## Portables Ergebnisschema

Flowzer akzeptiert bewusst nicht den gesamten JSON-Schema-Standard. Das Profil unterstützt:

- die Typen `object`, `array`, `string`, `number`, `integer`, `boolean` und `null`;
- `properties`, `required` und boolesches `additionalProperties` für Objekte;
- `items`, `minItems` und `maxItems` für Listen;
- `minLength`/`maxLength`, `minimum`/`maximum` sowie typgleiche `enum`-Werte;
- optionale kurze `title`- und `description`-Texte.

Externe `$ref`-Ziele, kombinierende Konstrukte und unbekannte Schlüsselwörter werden
schon beim Speichern abgelehnt. Schema und Antwort sind in Größe, Tiefe, Eigenschafts-
und Enumanzahl begrenzt. Auch wenn ein Provider strukturierte Ausgabe zusagt, parst und
prüft Flowzer das Ergebnis erneut serverseitig. Doppelte oder zusätzliche Eigenschaften,
fehlende Pflichtwerte und Typ-/Bereichsverletzungen erhalten stabile interne Fehlercodes.

## Providerneutrale Aufrufschicht

#244 / PR #245 ergänzt einen internen Gateway-Vertrag und drei Adapter:

- OpenAI verwendet ausschließlich die feste Responses-API mit `store: false`;
- Anthropic verwendet ausschließlich die feste Messages-API;
- OpenAI-kompatible Verbindungen verwenden nur ihre administrativ gespeicherte API-Wurzel
  und den Pfad `chat/completions`.

Alle Adapter fordern strukturiertes JSON an. Die Registry prüft diese Fähigkeit explizit
und kennt keine Fallback-Kette. Ein unbekanntes oder vom Ziel nicht unterstütztes Modell
wird als Providerablehnung behandelt und löst keinen Wechsel zu einem anderen Modell,
Provider oder Cloudziel aus. Persistierte Ziele und aktuelle Installations-Opt-ins werden
unmittelbar vor dem Aufruf erneut geprüft.

Secrets werden erst direkt vor dem HTTP-Aufruf aufgelöst und danach entsorgt. Provider-
Header sind für das .NET-HTTP-Logging redigiert; Weiterleitungen und automatische Retries
sind abgeschaltet. Der Aufgaben-Timeout sowie eine 2-MiB-Grenze für Provider-Envelopes
gelten unabhängig vom Anbieter. Authentifizierung, Rate Limit, Timeout, Transport,
Providerablehnung und ungültige Antwort werden ohne fremden Rohinhalt stabil klassifiziert.
Ein Adapter muss Ein- und Ausgabetokens melden; andernfalls kann Flowzer die gebundenen
Budgets nicht nachweisen und verwirft die Antwort als unvollständig.

## Persistenter Laufzustand

#246 / PR #247 ergänzt vor der eigentlichen Ausführung einen dauerhaften Zustandsvertrag. Genau ein
Lauf gehört zu einem Prozessinstanz-/Tokenpaar. Sein unveränderlicher Snapshot bindet
Verbindungsrevision, Modell, Anweisungsversion, deklarierte Eingaben, Ergebnisschema und
Limits, enthält aber weder Secret-Wert noch Secret-Referenz.

Provider- und Ergebnis-Claims sind getrennt, revisionsgeschützt und jeweils an einen
kurzlebigen Lease-Inhaber gebunden. PostgreSQL vergibt sie atomar mit Zeilensperren und
`SKIP LOCKED`; die Dateiablage serialisiert sie nur innerhalb eines Entwicklungsprozesses.
Ein verlorener Lease vor dem markierten Provideraufruf wird erneut freigegeben. Ist der
Aufruf bereits als begonnen gespeichert oder ging der Engine-Commit unklar aus, entsteht
statt eines blinden Retries eine Störung. Ein bereits validiertes Ergebnis samt Modell- und
Tokenmessung bleibt für die spätere Fortsetzung erhalten.

Dieser Slice startet noch keinen Hintergrund-Executor und ändert den Deployment-Blocker
nicht. Die nächste Stufe verbindet den gespeicherten Lauf mit Gateway und Engine.

Die Requestformen orientieren sich an den offiziellen Verträgen der
[OpenAI Responses API](https://platform.openai.com/docs/api-reference/responses/create),
der [OpenAI Structured Outputs](https://platform.openai.com/docs/guides/structured-outputs)
und der [Anthropic Messages API](https://docs.anthropic.com/en/api/messages). Normale Tests
verwenden ausschließlich simulierte HTTP-Handler und lösen keine abrechenbaren Aufrufe aus.

## Oberflächen

- Das Diagramm besitzt eine eigene **KI-Aufgabe**-Kachel. Technisch erzeugt sie einen
  `bpmn:serviceTask` mit dem oben beschriebenen Vertrag.
- Ein bestehender Service-Task kann zwischen **Worker** und **KI** wechseln. Der freie
  Worker-Text bleibt vollständig erhalten; beim bewussten Wechsel werden unvereinbare
  KI-Felder entfernt.
- Diagramm und Gliederung pflegen Verbindung, Modell, Anweisung, Schema, Limits sowie
  Ein-/Ausgangszuordnungen. Beide serialisieren denselben XML-Vertrag.

## Noch offen

1. Hintergrund-Executor, der persistente Läufe mit Provider und Engine verbindet.
2. Typisierte Werkzeugregistry, parametergebundene Freigaben und Ausführungsjournal.
3. Administrativer Verbindungstest und fachlicher Testmodus ohne Außenwirkungen.
4. Nachvollziehbare Laufzeit-/Tokenhistorie und Kosten nur mit versionierter Preisgrundlage.
