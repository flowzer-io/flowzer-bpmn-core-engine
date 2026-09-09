# Versionierter KI-Aufgabenvertrag

**Stand: 9. September 2026 · #242–#254**

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
      <flowzer:tool id="flowzer.directory.lookup" version="1" approval="automatic" />
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
- Werkzeugreferenzen verwenden ausschließlich eine registrierte ID, eine positive
  Version und `automatic`, `human` oder `preApproved` als Freigabemodus. Pro Task
  sind höchstens 20 eindeutige Werkzeug-IDs erlaubt.

Unbekannte Attribute oder Kinder werden abgelehnt. Das gilt ausdrücklich auch für
`secretReference`: Weder Secret-Werte noch Secret-Referenzen gehören in BPMN, Exporte,
Prompts oder Browserantworten.

## Prüfung und aktueller Ausführungsstatus

Parser, Autorenprüfung und Runtime verwenden denselben serverseitigen Vertrag.
Die Autorenprüfung kontrolliert zusätzlich, dass die referenzierte Verbindung existiert,
aktiv ist und ihr Secret serverseitig verfügbar ist. Browserfilter oder manipuliertes XML
können diese Bindung nicht erweitern.

Der Vertrag ist **modellierbar, speicherbar und deploybar**. Vorabprüfung und echtes
Deployment erzwingen dieselben Regeln. Das Deployment löst die aktuelle Verbindung genau
einmal auf und speichert ihre Revision sowie das effektive Modell als unveränderlichen
Definitions-Snapshot. Spätere administrative Änderungen wirken daher nur auf neue
Workflowversionen und verändern deren gebundene Ausführungskonfiguration nicht. Der aktuelle
Aktivstatus bleibt davon getrennt ein administrativer Kill-Switch und stoppt auch alte
Bindungen vor Secret- oder Netzwerkzugriff.

### Werkzeugverträge in Autorenständen

#254 ergänzt eine geschlossene `IAiTool`-Registry. Jede Installation stellt damit einen
rein lesbaren Katalog stabiler Werkzeug-IDs und -Versionen mit Ein-/Ausgabeschema,
Außenwirkung und Vertragshash bereit. Es gibt keine dynamisch aus BPMN, Prompt oder
Modellantwort erzeugten Handler, Zieladressen, Shell- oder SQL-Aufrufe.

Eine Verbindung enthält eine administrativ gepflegte Allowlist konkreter Werkzeugversionen.
Der Autorenvertrag darf nur die Schnittmenge aus Registry und Verbindung auswählen. Eine
automatische Ausführung ist ausschließlich für als `ReadOnly` registrierte Werkzeuge
modellierbar. `preApproved` verlangt zusätzlich, dass sowohl der Werkzeugvertrag als auch
die Verbindung diese Möglichkeit ausdrücklich erlauben. Beim Deployment werden Version,
Vertragshash, Außenwirkung und Freigabemodus unveränderlich gebunden.

Werkzeugreferenzen sind in diesem Slice bewusst **nur modellier- und speicherbar**. Solange
das persistente Aktionsjournal und parametergebundene Freigaben fehlen, antwortet die
Deploymentprüfung stabil mit `bpmn.ai_task.tools_runtime_unavailable`. KI-Aufgaben ohne
Werkzeugreferenz bleiben unverändert ausführbar. Damit entsteht keine nur scheinbar sichere
Außenwirkung. Die mitgelieferte Installation registriert noch kein konkretes Werkzeug;
der Katalog ist daher bis zu einer expliziten Erweiterung leer.

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

Die Ablage wird durch #250 / PR #251 vom optionalen Hintergrund-Executor bis zum dauerhaft
validierten Providerergebnis verwendet. #252 / PR #253 verbindet denselben Laufvertrag mit der
BPMN-Engine und entfernt erst damit den früheren Deployment-Blocker.

## Provider-Executor

#250 / PR #251 claimt wartende beziehungsweise fällige Retry-Läufe atomar und markiert den möglichen
Beginn des externen Aufrufs vor Secret- oder Netzwerkzugriff. Der unveränderliche Snapshot
wird einschließlich der exakten Verbindungsrevision erneut geprüft. Eine inzwischen geänderte
Verbindung, ein deaktivierter Eintrag oder ein fehlendes Secret beendet den Lauf ohne Fallback.

Lange Aufrufe verlängern ihre Lease besitzer- und revisionsgebunden. Geht sie verloren, wird
der Aufruf abgebrochen und kein verspätetes Ergebnis gespeichert. Erfolgreiche, erneut gegen
das Ergebnisschema geprüfte Antworten werden mit Modell- und Tokenmessung als `ResultReady`
persistiert. Nur eine feste Allowlist temporärer Fehlercodes darf innerhalb des gebundenen
Versuchslimits einen exponentiell begrenzten Retrytermin erzeugen; alle anderen oder
erschöpften Fehler werden datenarm als `Incident` angehalten. Vor jedem Takt läuft die
konservative Recovery des Laufzustands.

Der Hintergrunddienst ist standardmäßig deaktiviert und verarbeitet ausschließlich intern
persistierte Läufe. Nach dem Providerdurchgang claimt er bereitliegende Ergebnisse getrennt
für den Engine-Commit. Ein deaktivierter Dienst lässt deployte Prozesse nachvollziehbar am
KI-Token warten, statt unkontrolliert Netzwerkaufrufe auszulösen.

Die Requestformen orientieren sich an den offiziellen Verträgen der
[OpenAI Responses API](https://platform.openai.com/docs/api-reference/responses/create),
der [OpenAI Structured Outputs](https://platform.openai.com/docs/guides/structured-outputs)
und der [Anthropic Messages API](https://docs.anthropic.com/en/api/messages). Normale Tests
verwenden ausschließlich simulierte HTTP-Handler und lösen keine abrechenbaren Aufrufe aus.

## Engine-Anbindung und Atomizität

#252 / PR #253 schließt den Laufzustand vertikal an die BPMN-Runtime an:

- Jeder aktive KI-Token erzeugt genau einen internen `AiRun`; ein KI-Task erscheint niemals
  als Auftrag in der externen Worker-API.
- Der Lauf verwendet ausschließlich die deklarierten Eingabezuordnungen und bindet
  Definitions-ID, Token, Verbindung, Verbindungsrevision, Modell, Anweisung, Schema und
  Grenzen unveränderlich. Semantisch gleiche JSON-Objekte bleiben trotz anderer
  Eigenschaftsreihenfolge derselbe Snapshot.
- Datei- und PostgreSQL-Ablage behalten historische Verbindungsrevisionen. Der Gateway löst
  exakt die beim Deployment gebundene Revision und deren Secret-Referenz auf; die öffentliche
  API liefert diese Referenz weiterhin nicht aus.
- Ein `ResultReady` wird geleast, auf die aktive Definition und den aktiven Token geprüft,
  über das deklarierte Output-Mapping angewendet und zusammen mit Instanz, Subscriptions,
  Historie und Laufstatus in **einer PostgreSQL-Transaktion** committed.
- PostgreSQL verwendet pro Prozessinstanz einen transaktionsgebundenen Advisory Lock. Zwei
  API-Prozesse können dadurch parallele Ergebnisse derselben Instanz nicht aus veralteten
  Snapshots überschreiben. Ein Engine-Batch claimt höchstens ein Ergebnis je Instanz; reine
  Instanzansichten benötigen den Schreib-Lock nicht und bleiben währenddessen lesbar.
- Verlässt ein Token seinen KI-Schritt durch Abbruch oder Fehler, werden offene Läufe samt
  Lease storniert. Eine verspätete Providerantwort darf den Prozess danach nicht fortsetzen.
- Technische KI-Abschlüsse erhalten keinen erfundenen menschlichen Akteur.

Die dateibasierte Ablage bleibt Entwicklungsbetrieb: Sie serialisiert innerhalb eines
Prozesses, kann aber Instanz-, Historien- und Laufdateien nicht gemeinsam zurückrollen.

## Oberflächen

- Das Diagramm besitzt eine eigene **KI-Aufgabe**-Kachel. Technisch erzeugt sie einen
  `bpmn:serviceTask` mit dem oben beschriebenen Vertrag.
- Ein bestehender Service-Task kann zwischen **Worker** und **KI** wechseln. Der freie
  Worker-Text bleibt vollständig erhalten; beim bewussten Wechsel werden unvereinbare
  KI-Felder entfernt.
- Diagramm und Gliederung pflegen Verbindung, Modell, Anweisung, Schema, Limits sowie
  Ein-/Ausgangszuordnungen. Beide serialisieren denselben XML-Vertrag.
- Die Verbindungsverwaltung pflegt die erlaubten Werkzeugversionen. Der Modeler zeigt
  anschließend nur diese Schnittmenge und lässt veraltete beziehungsweise entzogene
  Referenzen ausdrücklich entfernen, statt sie still umzudeuten.

## Noch offen

1. Persistentes Werkzeug-Aktionsjournal, parametergebundene Freigaben und tatsächliche
   Ausführung der bereits typisiert gebundenen Verträge.
2. Administrativer Verbindungstest und fachlicher Testmodus ohne Außenwirkungen.
3. Bedienbares Störungszentrum sowie detaillierte Laufzeit-/Tokenhistorie.
4. Kostenanzeige ausschließlich mit versionierter, nachvollziehbarer Preisgrundlage.
