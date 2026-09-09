# Versionierter KI-Aufgabenvertrag

**Stand: 9. September 2026 · Slice #242 / PR #243**

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
      <flowzer:resultSchema>{"type":"object","properties":{}}</flowzer:resultSchema>
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
- Das Ergebnisschema muss valides JSON Schema mit `type: "object"` sein.
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
Provideradapter, persistente KI-Läufe, Ergebnisschema-Prüfung zur Laufzeit, Werkzeuge und
Freigaben folgen in getrennten, getesteten Slices.

## Oberflächen

- Das Diagramm besitzt eine eigene **KI-Aufgabe**-Kachel. Technisch erzeugt sie einen
  `bpmn:serviceTask` mit dem oben beschriebenen Vertrag.
- Ein bestehender Service-Task kann zwischen **Worker** und **KI** wechseln. Der freie
  Worker-Text bleibt vollständig erhalten; beim bewussten Wechsel werden unvereinbare
  KI-Felder entfernt.
- Diagramm und Gliederung pflegen Verbindung, Modell, Anweisung, Schema, Limits sowie
  Ein-/Ausgangszuordnungen. Beide serialisieren denselben XML-Vertrag.

## Noch offen

1. Provideradapter mit expliziter Fähigkeitenprüfung und ohne Cloud-Fallback.
2. Dauerhafte, nach Neustart fortsetzbare KI-Läufe mit Lease-Verlängerung.
3. Validierung realer Modellantworten gegen das gebundene Ergebnisschema.
4. Typisierte Werkzeugregistry, parametergebundene Freigaben und Ausführungsjournal.
5. Testmodus ohne Außenwirkung und nachvollziehbare Laufzeit-/Tokenmessung.
