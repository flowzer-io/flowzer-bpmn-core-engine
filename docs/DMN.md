# DMN – Entscheidungstabellen in Flowzer

Stand: 2026-09-19

Flowzer bekommt Entscheidungstabellen wie Camunda. Dieses Dokument beschreibt den
**Kern**: die eigenständige Bibliothek `src/FlowzerDmn/`, die DMN-Dateien liest und
Entscheidungen auswertet. Die Anbindung an den Business-Rule-Task, an die API und an die
Konsole kommt in späteren Paketen; dieser Baustein ist bewusst so geschnitten, dass er
dafür bereitliegt.

## Was es jetzt gibt

| Baustein | Ort |
|---|---|
| Modell (`DmnDefinitions`, `DmnDecision`, `DmnDecisionTable`, …) | `src/FlowzerDmn/Model/` |
| Parser `DmnModelParser.Parse(string xml)` | `src/FlowzerDmn/Parsing/` |
| Auswertung `DmnDecisionEvaluator` und `IFeelEngine` | `src/FlowzerDmn/Evaluation/` |
| Fehler (`DmnParseException`, `DmnUnsupportedException`, …) | `src/FlowzerDmn/Exceptions/` |
| Tests samt DMN-Beispieldateien | `src/FlowzerDmn.Tests/` |

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

Der Adapter auf den FEEL-Handler der Engine liegt vorerst im Testprojekt
(`src/FlowzerDmn.Tests/Feel/FeelinFeelEngine.cs`). Er zeigt den entscheidenden Punkt:

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
- Die produktive Brücke von `FeelinExpressionHandler` auf `IFeelEngine` liegt noch im
  Testprojekt, nicht im `core-engine`-Paket.

## Was als Nächstes kommt

1. **Business-Rule-Task binden.** Wie in Camunda 8 über `zeebe:calledDecision` mit
   `decisionId` und `resultVariable`: Der Task wertet die genannte Decision mit den
   Prozessvariablen aus und schreibt `DmnDecisionResult.Value` unter `resultVariable`
   zurück. Dafür wandert der FEEL-Adapter aus dem Testprojekt in `core-engine`.
2. **Verwaltung und Deployment über die API.** DMN-Dateien ablegen, versionieren und
   ausliefern wie BPMN-Modelle, dazu ein Trockenlauf-Endpunkt, der eine Decision mit
   gegebenen Variablen durchrechnet, ohne eine Instanz anzufassen – dafür ist die
   Bibliothek bereits geschnitten.
3. **Editor in der Konsole.** `dmn-js` für Entscheidungstabellen, analog zum
   BPMN-Editor.
4. **DMN TCK.** Die offiziellen Testfälle anbinden, um den Abdeckungsgrad ehrlich
   beziffern zu können.
