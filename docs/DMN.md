# DMN – Entscheidungstabellen in Flowzer

Stand: 2026-09-19

Flowzer hat Entscheidungstabellen wie Camunda. Dieses Dokument beschreibt beides: den
**Kern** — die eigenständige Bibliothek `src/FlowzerDmn/`, die DMN-Dateien liest und
Entscheidungen auswertet — und seine **Anbindung** an den Business-Rule-Task, an den
Entscheidungskatalog der API und an die Konsole.

## Was es jetzt gibt

| Baustein | Ort |
|---|---|
| Modell (`DmnDefinitions`, `DmnDecision`, `DmnDecisionTable`, …) | `src/FlowzerDmn/Model/` |
| Parser `DmnModelParser.Parse(string xml)` | `src/FlowzerDmn/Parsing/` |
| Auswertung `DmnDecisionEvaluator` und `IFeelEngine` | `src/FlowzerDmn/Evaluation/` |
| Fehler (`DmnParseException`, `DmnUnsupportedException`, …) | `src/FlowzerDmn/Exceptions/` |
| Tests samt DMN-Beispieldateien | `src/FlowzerDmn.Tests/` |
| Produktive FEEL-Brücke `FeelinFeelEngine` | `src/core-engine/Dmn/` |
| Bereitstellung am Token (`PendingDecision`) | `src/core-engine/InstanceEngine/Decisions.cs` |
| Auswertung in der Transaktion | `src/WebApiEngine/BusinessLogic/BpmnBusinessLogic.Decisions.cs` |
| Katalog und Trockenlauf | `src/WebApiEngine/Controller/DecisionController.cs` |
| Seite „Entscheidungen" mit dmn-js | `src/FlowzerConsole/src/pages/DecisionsPage.tsx` |

Die Bibliothek hat **keine** Abhängigkeiten: weder auf `core-engine` noch auf
ClearScript/V8, weder auf NuGet-Pakete für DMN noch auf Dateisystem oder Netz. Sie ist
deterministisch und ohne Ein- und Ausgabe. Damit taugt derselbe Code später für die
Engine **und** für einen Trockenlauf-Endpunkt, der eine Tabelle nur durchrechnet.

## Umfang

**Unterstützte Namensräume** – DMN 1.1, 1.2, 1.3 und 1.4:

| Fassung | Namensraum |
|---|---|
| DMN 1.1 | `http://www.omg.org/spec/DMN/20151101/dmn.xsd` |
| DMN 1.2 | `https://www.omg.org/spec/DMN/20180521/MODEL/` |
| DMN 1.3 | `https://www.omg.org/spec/DMN/20191111/MODEL/` |
| DMN 1.4 | `https://www.omg.org/spec/DMN/20211108/MODEL/` |

Der Parser arbeitet über die **lokalen Elementnamen** und ist damit gegenüber Präfixen
und Namensräumen gleichgültig. Die Fassung wird nur erkannt und in
`DmnDefinitions.Version` abgelegt; ausgewertet werden alle Fassungen gleich. Eine Datei
mit unbekanntem Namensraum wird trotzdem gelesen und bekommt `DmnVersion.Unknown`.

**Entscheidungslogik** – in dieser Ausbaustufe genau zwei Arten:

- `decisionTable` – die Entscheidungstabelle,
- `literalExpression` – ein einzelner FEEL-Ausdruck.

Jede andere boxed expression (`context`, `invocation`, `relation`, `list`,
`functionDefinition`, …) führt beim Parsen zu einer `DmnUnsupportedException`, die
Element **und** Decision-Id nennt. Das ist Absicht: Eine Decision, die still nichts
liefert, fällt sonst erst in der Produktion auf.

**Ignoriert, aber toleriert**: der Diagrammteil `DMNDI`, `inputData`,
`businessKnowledgeModel`, `extensionElements` und alles Weitere, was die Bibliothek nicht
kennt.

**Abhängigkeiten zwischen Decisions**: `informationRequirement` wird gelesen, sowohl
`requiredDecision` als auch `requiredInput`. Benötigte Decisions werden vor der
aufrufenden ausgewertet; ihr Ergebnis steht im Kontext unter dem Namen ihrer `variable`
(ersatzweise ihrem Namen, ersatzweise ihrer Id). Ein Zyklus ist ein **Parse-Fehler** –
beim Auswerten wäre er eine Endlosschleife.

## Hit-Policies

| Policy | Ergebnis | Besonderheiten |
|---|---|---|
| `UNIQUE` | ein Objekt | mehrere Treffer → `DmnHitPolicyViolationException` |
| `FIRST` | ein Objekt | erster Treffer in Dokumentreihenfolge; `MatchedRules` nennt nur ihn |
| `PRIORITY` | ein Objekt | Rangfolge aus `outputValues`; `MatchedRules` in Rangfolge |
| `ANY` | ein Objekt | mehrere Treffer erlaubt, solange sie dasselbe liefern – sonst `DmnHitPolicyViolationException` |
| `COLLECT` | Liste | ohne Verdichtung |
| `COLLECT` + `SUM`/`MIN`/`MAX`/`COUNT` | ein Zahlenwert | nur über genau eine Ausgabespalte |
| `RULE ORDER` | Liste | in Regelreihenfolge |
| `OUTPUT ORDER` | Liste | nach `outputValues` sortiert |

Ohne Treffer: `UNIQUE`/`FIRST`/`PRIORITY`/`ANY` liefern `null`, die Listen-Policies eine
leere Liste. Bei Verdichtung liefert `COUNT` die `0`, `SUM`/`MIN`/`MAX` liefern `null` –
eine Summe über nichts hat keine sinnvolle Zahl, eine Anzahl schon.

`MIN` und `MAX` liefern den Wert der jeweiligen Regel **im deklarierten Typ**; `SUM`
rechnet in `double`; `COUNT` liefert ein `int`. Liegt unter `SUM`/`MIN`/`MAX` ein Wert,
der keine Zahl ist, bricht die Auswertung mit einer `DmnEvaluationException` ab.

## Ergebnisform

`DmnDecisionEvaluator.Evaluate(definitions, decisionId, variables)` liefert ein
`DmnDecisionResult`:

- `DecisionId` – die ausgewertete Decision,
- `MatchedRules` – die Regel-Ids in der Reihenfolge, in der sie das Ergebnis geprägt haben,
- `Value` – das Ergebnis gemäß Hit-Policy,
- `RequiredResults` – die Ergebnisse der benötigten Decisions, **auch die transitiven**,
  nach Decision-Id. Damit lässt sich eine Kette im Nachhinein nachvollziehen.

`Value` ist bei den Einzeltreffer-Policies ein
`IReadOnlyDictionary<string, object?>` mit den Ausgabespalten, bei den Listen-Policies
eine `IReadOnlyList<IReadOnlyDictionary<string, object?>>`, bei Verdichtung eine Zahl und
bei einem `literalExpression` der blanke Wert des Ausdrucks.

## Bewusste Entscheidungen

### 1. Auch eine einzelne Ausgabespalte wird umhüllt

Eine Tabelle mit genau einer Ausgabespalte liefert **trotzdem** ein Objekt,
`{ "desiredDish": "Spareribs" }`, nicht den blanken Wert. Der Grund ist die Form des
Vertrags: Wer später eine zweite Spalte ergänzt, ändert damit nicht die Gestalt des
Ergebnisses und bricht keinen laufenden Prozess. Hat die eine Spalte keinen Namen – DMN
erlaubt das nur bei genau einer –, heißt der Schlüssel `result`. Die Id der Spalte ist
bewusst **kein** Rückfall: Editoren vergeben sie automatisch (`OutputClause_0x1`), und
ein Business-Rule-Task soll nicht danach greifen müssen.

Bei mehreren Ausgabespalten verlangt der Parser einen Namen je Spalte; sonst ließe sich
das Ergebnis nicht benennen.

### 2. `inputValues` wird geprüft, nicht nur angezeigt

Liegt ein Eingabewert außerhalb der `inputValues` seiner Spalte, bricht die Auswertung
mit einer `DmnInputOutOfRangeException` ab, die Spalte, Wert und erlaubte Werte nennt.

Camunda behandelt `inputValues` nur als Hinweis für den Editor und rechnet stillschweigend
weiter. Wir weichen ab, weil ein Wert außerhalb des erlaubten Bereichs sonst leise in die
falsche Zeile fällt oder gar nicht trifft – und das Ergebnis dann so aussieht, als hätte
die Tabelle nichts zu sagen gehabt. Ein Tippfehler in der Saison (`"Autumn"` statt
`"Fall"`) soll auffallen, nicht durchrutschen. `outputValues` bleibt dagegen reine
Rangfolge und wird nicht als Prüfung verwendet.

### 3. Typumsetzung ist nachsichtig

`typeRef` wird für `string`, `number`, `double`, `integer`, `long`, `boolean`, `date`,
`time`, `dateTime` und `dayTimeDuration` in den passenden .NET-Typ umgesetzt – mit oder
ohne Präfix (`feel:string`, `xs:date`). Passt der Wert nicht zum Typ, oder ist das
`typeRef` unbekannt, bleibt der Wert **roh**. Eine Tabelle soll nicht daran scheitern,
dass jemand ein Feld anders deklariert hat, als FEEL es liefert.

Ohne diese Umsetzung wäre das Ergebnis vom Zufall abhängig: ClearScript liefert aus V8 je
nach Zahl ein `Int32` oder ein `Single`.

`yearMonthDuration` bleibt roh. .NET hat keinen Typ für Jahre und Monate, und ein
`TimeSpan` wäre eine Lüge über die Länge eines Monats.

### 4. Leer heißt leer

Ein leerer `inputEntry` und der Strich `-` passen immer. Ein leerer `outputEntry` liefert
`null` – das ist kein Fehler, sondern ein bewusstes „nichts“.

### 5. FEEL kommt von außen

Die Bibliothek rechnet nichts selbst, sondern fragt über `IFeelEngine`:

```csharp
public interface IFeelEngine
{
    object? Evaluate(string expression, IReadOnlyDictionary<string, object?> context);
    bool UnaryTest(string test, object? inputValue, IReadOnlyDictionary<string, object?> context);
}
```

Damit bleibt `FlowzerDmn` frei von ClearScript und V8, und ein Trockenlauf darf eine
andere Engine einsetzen als die Laufzeit.

### 6. Wie der Eingabewert an libfeelin übergeben wird

Der Adapter auf den FEEL-Handler der Engine liegt in `src/core-engine/Dmn/FeelinFeelEngine.cs`.
Er zeigt den entscheidenden Punkt:

**libfeelin liest den Eingabewert eines Unary-Tests aus dem Kontext unter dem Schlüssel
`?`.** In `src/core-engine/Expression/Feelin/bundle.js` steht dazu

```js
function unaryTest(expression, context = {}) {
    const value = context['?'] !== undefined ? context['?'] : null;
    const { root } = interpreter.unaryTest(expression, context);
    const test = root(context);
    return test(value);
}
```

und der Grammatik-Knoten `'?'` greift über `getFromContext('?', context)` auf denselben
Schlüssel zu. Der Adapter legt den Eingabewert deshalb unter `?` in die Variablen, bevor
er `FeelinExpressionHandler.MatchExpression` ruft. Damit funktionieren beide
Schreibweisen: `> 10` (impliziter Vergleich mit dem Eingabewert) und `? > 10`
(ausdrückliche Nennung). Beide sind in
`src/FlowzerDmn.Tests/Feel/FeelinFeelEngineTest.cs` festgehalten.

Beide Adaptermethoden setzen ein `=` vor den Ausdruck, weil der Handler daran erkennt,
dass etwas auszuwerten ist.

### 7. Ohne FEEL kein DMN — und das laut gesagt

`FlowzerConfig.FeelEngine` liefert immer die Brücke, die zum konfigurierten
`ExpressionHandler` passt: Derselbe FEEL-Kern, der die Ausdrücke im Prozess rechnet, rechnet
auch die Tabelle. Steht dort kein FEEL-fähiger Handler — insbesondere der
`SimpleExpressionHandler`, den CI und Umgebungen ohne V8 benutzen —, dann steht an seiner
Stelle ein Platzhalter, dessen **Benutzung** eine `FlowzerDmnUnavailableException` wirft.

Das Lesen der Eigenschaft bleibt bewusst harmlos und der Zugriff ist verzögert: Eine
Installation ohne V8 soll weiterhin starten und alles andere tun können. Erst wer eine
Entscheidung auswerten will, bekommt die klare Ansage statt eines stillen Fehlurteils. Eine
Tabelle auf dem `SimpleExpressionHandler` zu rechnen wäre genau das — er deckt die Ausdrücke
des Testbestands ab, ist aber kein FEEL.

## Der Entscheidungskatalog

Eine *Entscheidungsdatei* ist ein DMN-Dokument. Sie liegt unter einer Katalogkennung
(`DecisionDefinitionId`) und trägt fortlaufend nummerierte Versionen; jede Version hält das
XML, den Zeitpunkt, die Person und die Liste der enthaltenen `decisionId`s samt Namen fest.
Die Kennung kommt aus `dmn:definitions/@id` oder wird vom Server vergeben und folgt
denselben Regeln wie eine BPMN-Katalogkennung (`DefinitionIdRules`) — sie wird in der
Dateiablage zum Dateinamen, und eine Kennung wie `../../x` zeigte sonst aus dem Ablageordner
heraus.

**Immer gilt genau die jüngste Version.** In dieser Stufe gibt es bewusst keine Entwürfe:
Speichern heißt neue Version, und die wirkt sofort. Das ist die einfachste Regel, die
funktioniert; ein Entwurfsstand mit eigener Freigabe ist eine spätere Stufe und keine
Lücke, die hier still gelassen wurde.

| Endpunkt | Rolle | Zweck |
|---|---|---|
| `GET /decision` | Zugang | Katalog mit jüngster Version und enthaltenen `decisionId`s |
| `POST /decision` | modeler | neue Datei aus XML |
| `PUT /decision/{id}` | modeler | neue Version aus XML |
| `GET /decision/{id}` | Zugang | jüngste Version mit XML |
| `GET /decision/{id}/versions` | Zugang | Versionsliste |
| `GET /decision/{id}/versions/{version}` | Zugang | eine bestimmte Version mit XML |
| `DELETE /decision/{id}` | modeler | löschen, nur wenn unbenutzt |
| `POST /decision/{id}/evaluate` | operator oder modeler | Trockenlauf |

Beim Speichern parst der Server das XML mit `DmnModelParser`. Ein Parse-Fehler antwortet mit
**422** und der Meldung des Kerns — eine Datei, die Flowzer nicht rechnen kann, soll gar
nicht erst im Katalog stehen.

Gelöscht wird nur, was kein deployter Workflow benutzt. Geprüft wird über die deployten
BPMN-XML nach `zeebe:calledDecision`; sonst antwortet **409** und nennt die Workflows. Der
Grund ist derselbe wie beim Formular: Eine gelöschte Entscheidung ließe jede laufende
Instanz an ihrem Business-Rule-Task mit `DECISION_NOT_FOUND` stehen.

Der **Trockenlauf** rechnet eine Entscheidung mit gegebenen Variablen durch, ohne eine
Instanz anzufassen. Er liefert dasselbe `DmnDecisionResult` wie die Laufzeit — Wert,
getroffene Regeln und Zwischenergebnisse. Dafür war die Bibliothek von Anfang an ohne Ein-
und Ausgabe geschnitten.

## Bindung an den Business-Rule-Task

Wie in Camunda 8 über `zeebe:calledDecision`:

```xml
<bpmn:businessRuleTask id="Rabatt" name="Rabatt ermitteln">
  <bpmn:extensionElements>
    <zeebe:calledDecision decisionId="rabatt" resultVariable="rabattErgebnis" />
  </bpmn:extensionElements>
</bpmn:businessRuleTask>
```

Beide Angaben sind Pflicht; ohne sie lehnt die Veröffentlichung mit
`bpmn.business_rule_task.decision_required` beziehungsweise
`bpmn.business_rule_task.result_variable_required` ab. Ein Task mit `zeebe:taskDefinition`
statt `calledDecision` ist dagegen ein Auftrag für einen externen Worker und läuft über
`IFlowzerWorkerTask` denselben Weg wie ein Service-Task.

Der Ablauf folgt genau dem Muster der Aufruf-Aktivität, und zwar aus demselben Grund: **Die
Engine kennt den Katalog nicht.** Sie stellt am Token eine `PendingDecision` bereit; die
Geschäftslogik holt sie ab, sucht die Entscheidung, rechnet sie und schließt den Token
wieder — alles in derselben Transaktion wie das Speichern der Instanz. Scheitert ein
Schritt, scheitert die ganze Mutation.

Gesucht wird die **jüngste Version jeder Entscheidungsdatei**, die diese `decisionId`
enthält. Findet sie sich in mehreren Dateien, ist das kein Zufall, den man stillschweigend
auflösen dürfte, sondern ein Modellfehler: `DECISION_AMBIGUOUS`.

## Variablenfluss

**Hinein:** Liegt am Task ein `zeebe:ioMapping`-Eingang, gilt genau diese Auswahl. **Ohne
Zuordnung bekommt die Entscheidung alle Prozessvariablen** — hier bewusst anders als bei der
ausgehenden Nachricht und der Aufruf-Aktivität, die ohne Zuordnung nichts mitnehmen. Der
Unterschied ist die Reichweite: Die Tabelle läuft lokal in derselben Transaktion, es
verlässt nichts den Server. Eine Tabelle, die nach einer Variablen fragt, soll sie finden,
statt an einer vergessenen Zuordnung leer auszugehen.

**Heraus:** Der Wert aus `DmnDecisionResult.Value` steht unter `resultVariable` im
Prozesskontext. Dabei wird er in die Form umgesetzt, die die Engine für Variablen führt:
ein Objekt der Ausgabespalten wird zu `Variables`, eine Liste zu einer Liste, ein Skalar
bleibt ein Skalar — rekursiv. Ohne diese Umsetzung stünde das Ergebnis zwar im Prozess, wäre
aber in keinem FEEL-Ausdruck erreichbar: `rabattErgebnis.rabatt` an einer Gateway-Bedingung
liefe ins Leere. Ein `zeebe:ioMapping`-Ausgang greift zusätzlich und kann daraus einzelne
Werte an anderer Stelle ablegen.

Weil eine Tabelle mit genau einer Ausgabespalte trotzdem ein Objekt liefert (siehe
*Bewusste Entscheidungen*, Punkt 1), heißt der Zugriff auch dann `rabattErgebnis.rabatt` und
nicht `rabattErgebnis`. Wer später eine zweite Spalte ergänzt, bricht damit keinen laufenden
Prozess.

## Fehlercodes

Alle drei sind BPMN-Fehler am Business-Rule-Task und damit von einem Error-Boundary fangbar
— genauso wie die Fehler einer Aufruf-Aktivität.

| Code | Wann |
|---|---|
| `DECISION_NOT_FOUND` | Keine deployte Entscheidungsdatei enthält diese `decisionId`. |
| `DECISION_AMBIGUOUS` | Mehrere Dateien enthalten sie; die Meldung nennt sie. |
| `DECISION_EVALUATION_FAILED` | Die Auswertung ist gescheitert: `DmnHitPolicyViolationException`, `DmnInputOutOfRangeException` oder `DmnEvaluationException`. Die Meldung des Kerns wird durchgereicht. |

Dass eine Hit-Policy-Verletzung und ein Wert ausserhalb der `inputValues` hier als fangbarer
BPMN-Fehler ankommen und nicht als Serverfehler, ist der Zweck der strengen Prüfung aus
*Bewusste Entscheidungen*, Punkt 2: Der Tippfehler in der Saison fällt auf, und der Prozess
kann selbst entscheiden, was dann geschehen soll.

## Grenzen

- Nur `decisionTable` und `literalExpression`. Kein `context`, `invocation`, `relation`,
  `list`, `functionDefinition`.
- Keine Decision Services, keine Business Knowledge Models, keine DRD-Grafik.
- Kein `import` über Dateigrenzen hinweg: `requiredDecision` muss in derselben Datei
  auflösbar sein.
- Die **offizielle DMN TCK** (github.com/dmn-tck/tck) ist noch nicht angebunden; die
  Beispieldateien unter `src/FlowzerDmn.Tests/Fixtures/` sind eigene. Die TCK-Anbindung
  bleibt offen.
- Die Datums- und Zeitumsetzung ist über den Text der FEEL-Werte gelöst und deshalb nur
  so gut wie deren Schreibweise.
- **Ohne FEEL-fähigen Ausdrucks-Handler ist DMN nicht benutzbar.** Der
  `SimpleExpressionHandler` kann keine Tabelle rechnen; der Zugriff meldet eine
  `FlowzerDmnUnavailableException`, statt still ein Fehlurteil zu liefern.
- Im Katalog gibt es **keine Entwürfe**: Speichern heißt neue Version, und die jüngste
  Version gilt sofort.
- **Keine Versionsbindung am Task** (`versionTag`). Es gilt immer die jüngste Version der
  Entscheidungsdatei.
- Keine Ordner und keine Rechte je Entscheidungsdatei; es gelten die Rollen der API.
- Kein Import aus Camunda 7 (`camunda:decisionRef`).
- Multi-Instance am Business-Rule-Task ist ungeprüft.
- Die Konsole bindet `dmn-js` ein; der Editor selbst ist nur mit einem Rauchtest abgedeckt,
  weil `dmn-js` in jsdom nicht montierbar ist (diagram-js misst über
  `SVGGraphicsElement.getBBox()`).

## Was als Nächstes kommt

1. **DMN TCK.** Die offiziellen Testfälle (github.com/dmn-tck/tck) anbinden, um den
   Abdeckungsgrad ehrlich beziffern zu können. Das ist der größte offene Punkt: Solange die
   TCK fehlt, beruht jede Aussage über die Abdeckung auf eigenen Beispieldateien.
2. **Entwürfe und Freigabe im Katalog.** Heute wirkt jedes Speichern sofort. Ein
   Entwurfsstand mit eigener Freigabe würde dem Weg der Formulare folgen
   ([FORM-AUTHORING.md](FORM-AUTHORING.md)).
3. **Versionsbindung am Task.** `versionTag` wie in Camunda 8, damit ein veröffentlichter
   Workflow an genau der Tabelle hängt, gegen die er geprüft wurde.
4. **Weitere boxed expressions.** `context` und `invocation` sind die nächsten, die in
   echten Modellen vorkommen.
