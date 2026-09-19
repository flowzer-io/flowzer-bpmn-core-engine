using FlowzerDmn.Exceptions;
using FlowzerDmn.Model;
using FlowzerDmn.Parsing;
using FluentAssertions;

namespace FlowzerDmn.Tests;

/// <summary>
/// Der Parser liest DMN-Dateien in das Modell der Bibliothek ein. Die Tests hier zeigen,
/// was er aus einer Datei herausholt und woran er sich weigert.
/// </summary>
public class DmnModelParserTest
{
    // Testzweck: Die Kopfdaten einer DMN-1.3-Datei landen vollstaendig im Modell, samt
    // erkannter Fassung. Die Fassung wird nur gespeichert, nicht unterschiedlich behandelt.
    [Test]
    public void Parse_ShouldReadTheHeaderOfTheDefinitions()
    {
        var definitions = DmnFixtures.Parse("dish.dmn");

        definitions.Id.Should().Be("dishDefinitions");
        definitions.Name.Should().Be("Dish");
        definitions.Namespace.Should().Be("http://flowzer.maass.it/dmn/dish");
        definitions.ModelNamespace.Should().Be("https://www.omg.org/spec/DMN/20191111/MODEL/");
        definitions.Version.Should().Be(DmnVersion.Dmn13);
        definitions.Decisions.Should().ContainSingle();
    }

    // Testzweck: Spalten, Regeln und Beschriftungen einer Entscheidungstabelle stehen
    // spaltenweise in derselben Reihenfolge im Modell wie in der Datei.
    [Test]
    public void Parse_ShouldReadTheColumnsAndRulesOfADecisionTable()
    {
        var decision = DmnFixtures.Parse("dish.dmn").FindDecision("dish")!;

        decision.Name.Should().Be("Dish");
        decision.OutputVariableName.Should().Be("desiredDish");
        decision.OutputTypeRef.Should().Be("string");

        var table = decision.DecisionTable!;
        table.HitPolicy.Should().Be(DmnHitPolicy.Unique);
        table.Aggregation.Should().Be(DmnAggregation.None);

        table.Inputs.Should().HaveCount(2);
        table.Inputs[0].Label.Should().Be("Saison");
        table.Inputs[0].Expression.Should().Be("season");
        table.Inputs[0].TypeRef.Should().Be("string");
        table.Inputs[1].Expression.Should().Be("guestCount");

        table.Outputs.Should().ContainSingle();
        table.Outputs[0].Name.Should().Be("desiredDish");
        table.Outputs[0].Label.Should().Be("Gericht");
        table.Outputs[0].TypeRef.Should().Be("string");

        table.Rules.Should().HaveCount(6);
        table.Rules[0].Id.Should().Be("dishRuleSpareribs");
        table.Rules[0].Description.Should().Be("Herbst, kleine Runde");
        table.Rules[0].InputEntries.Should().Equal("\"Fall\"", "<= 8");
        table.Rules[0].OutputEntries.Should().Equal("\"Spareribs\"");
    }

    // Testzweck: Der Strich der DMN-Notation bleibt als Strich stehen. Erst die Auswertung
    // deutet ihn als "trifft immer"; der Parser soll ihn nicht wegwerfen.
    [Test]
    public void Parse_ShouldKeepTheWildcardEntryAsItIs()
    {
        var table = DmnFixtures.Parse("dish.dmn").FindDecision("dish")!.DecisionTable!;

        table.Rules.Single(rule => rule.Id == "dishRuleSalad").InputEntries[1].Should().Be("-");
    }

    // Testzweck: inputValues werden sowohl im Original als auch zerlegt abgelegt. Der
    // Originaltext dient der Pruefung, die Zerlegung der Rangfolge.
    [Test]
    public void Parse_ShouldReadInputValuesAsTextAndAsEntries()
    {
        var table = DmnFixtures.Parse("dish.dmn").FindDecision("dish")!.DecisionTable!;

        var inputValues = table.Inputs[0].InputValues!;
        inputValues.Text.Should().Be("\"Fall\",\"Winter\",\"Spring\",\"Summer\"");
        inputValues.Entries.Should().Equal("\"Fall\"", "\"Winter\"", "\"Spring\"", "\"Summer\"");
        table.Inputs[1].InputValues.Should().BeNull();
    }

    // Testzweck: outputValues geben die Rangfolge vor; ihre Reihenfolge muss erhalten bleiben,
    // sonst waere PRIORITY beliebig.
    [Test]
    public void Parse_ShouldKeepTheOrderOfOutputValues()
    {
        var table = DmnFixtures.Parse("priority.dmn").FindDecision("priorityDecision")!.DecisionTable!;

        table.HitPolicy.Should().Be(DmnHitPolicy.Priority);
        table.Outputs[0].OutputValues!.Entries.Should().Equal("\"Refer\"", "\"Decline\"", "\"Accept\"");
    }

    // Testzweck: Die Trefferregeln mit Leerzeichen im Namen werden erkannt — "RULE ORDER"
    // und "OUTPUT ORDER" stehen so in der Datei.
    [Test]
    public void Parse_ShouldReadHitPoliciesThatContainASpace()
    {
        DmnFixtures.Parse("firstAndRuleOrder.dmn").FindDecision("ruleOrderDecision")!
            .DecisionTable!.HitPolicy.Should().Be(DmnHitPolicy.RuleOrder);

        DmnFixtures.Parse("priority.dmn").FindDecision("outputOrderDecision")!
            .DecisionTable!.HitPolicy.Should().Be(DmnHitPolicy.OutputOrder);
    }

    // Testzweck: Jede Verdichtung von COLLECT wird erkannt; ohne Angabe bleibt es bei None.
    [TestCase("collectList", DmnAggregation.None)]
    [TestCase("collectSum", DmnAggregation.Sum)]
    [TestCase("collectMin", DmnAggregation.Min)]
    [TestCase("collectMax", DmnAggregation.Max)]
    [TestCase("collectCount", DmnAggregation.Count)]
    public void Parse_ShouldReadTheAggregationOfACollectTable(string decisionId, DmnAggregation expected)
    {
        var table = DmnFixtures.Parse("collect.dmn").FindDecision(decisionId)!.DecisionTable!;

        table.HitPolicy.Should().Be(DmnHitPolicy.Collect);
        table.Aggregation.Should().Be(expected);
    }

    // Testzweck: Ein literalExpression ist eine vollwertige Entscheidungslogik und keine Tabelle.
    [Test]
    public void Parse_ShouldReadALiteralExpressionAsDecisionLogic()
    {
        var decision = DmnFixtures.Parse("literalExpression.dmn").FindDecision("netAmount")!;

        decision.DecisionTable.Should().BeNull();
        decision.LiteralExpression!.Text.Should().Be("grossAmount / 1.19");
        decision.LiteralExpression.ExpressionLanguage.Should().Be("feel");
        decision.OutputTypeRef.Should().Be("number");
    }

    // Testzweck: Abhaengigkeiten auf andere Decisions und auf Input-Data-Knoten werden
    // unterschieden; aus dem href wird die blosse Id.
    [Test]
    public void Parse_ShouldReadInformationRequirements()
    {
        var definitions = DmnFixtures.Parse("requiredDecision.dmn");

        definitions.FindDecision("riskFactor")!.RequiredDecisionIds.Should().Equal("riskLevel");

        var finalPremium = definitions.FindDecision("finalPremium")!;
        finalPremium.RequiredDecisionIds.Should().Equal("riskFactor");
        finalPremium.RequiredInputIds.Should().Equal("coverageInput");
        finalPremium.Requirements.Should().HaveCount(2);
    }

    // Testzweck: Eine Datei im alten DMN-1.1-Namensraum mit Praefix wird genauso gelesen
    // wie eine aus 1.3 — nur die erkannte Fassung unterscheidet sich.
    [Test]
    public void Parse_ShouldAcceptTheOldDmn11Namespace()
    {
        var definitions = DmnFixtures.Parse("legacyDmn11.dmn");

        definitions.Version.Should().Be(DmnVersion.Dmn11);
        definitions.FindDecision("legacyDecision")!.DecisionTable!.Rules.Should().HaveCount(2);
    }

    // Testzweck: Der Diagrammteil DMNDI wird toleriert und nicht ausgewertet. Editoren
    // speichern ihn immer mit; er darf das Einlesen nicht stoeren.
    [Test]
    public void Parse_ShouldTolerateTheDiagramPart()
    {
        var definitions = DmnFixtures.Parse("withDmndi.dmn");

        definitions.Decisions.Should().ContainSingle();
        definitions.FindDecision("diagramDecision")!.DecisionTable!.Rules.Should().HaveCount(2);
    }

    // Testzweck: Eine Entscheidungslogik, die diese Ausbaustufe nicht auswerten kann, faellt
    // beim Parsen auf — mit Element- und Decision-Id. Sonst laege der Fehler erst zur Laufzeit.
    [Test]
    public void Parse_ShouldRejectAnUnsupportedDecisionLogic()
    {
        var parse = () => DmnFixtures.Parse("unsupportedLogic.dmn");

        var exception = parse.Should().Throw<DmnUnsupportedException>().Which;
        exception.ElementName.Should().Be("invocation");
        exception.DecisionId.Should().Be("invocationDecision");
    }

    // Testzweck: Decisions, die einander brauchen, wuerden beim Auswerten endlos kreisen.
    // Der Zyklus muss deshalb schon beim Parsen auffallen.
    [Test]
    public void Parse_ShouldRejectACycleBetweenDecisions()
    {
        var parse = () => DmnFixtures.Parse("cycle.dmn");

        parse.Should().Throw<DmnParseException>()
            .WithMessage("*im Kreis*");
    }

    // Testzweck: Ohne id laesst sich eine Decision weder ansprechen noch in einem Ergebnis
    // nennen; sie wird abgelehnt statt still uebergangen.
    [Test]
    public void Parse_ShouldRejectADecisionWithoutId()
    {
        var parse = () => DmnFixtures.Parse("missingDecisionId.dmn");

        parse.Should().Throw<DmnParseException>().WithMessage("*ohne id*");
    }

    // Testzweck: Kaputtes XML kommt als DmnParseException zurueck, nicht als XmlException.
    // Wer DMN einbindet, soll nur einen Fehlerbaum kennen muessen.
    [Test]
    public void Parse_ShouldWrapBrokenXml()
    {
        var parse = () => DmnModelParser.Parse("<definitions><decision></definitions>");

        parse.Should().Throw<DmnParseException>().WithInnerException<System.Xml.XmlException>();
    }

    // Testzweck: Ein falsches Wurzelelement ist kein DMN. Das soll klar gesagt werden,
    // statt eine Datei ohne Decisions zu liefern.
    [Test]
    public void Parse_ShouldRejectAWrongRootElement()
    {
        var parse = () => DmnModelParser.Parse("<definitions2 />");

        parse.Should().Throw<DmnParseException>().WithMessage("*definitions*");
    }

    // Testzweck: Eine unbekannte Trefferregel wird nicht stillschweigend zu UNIQUE.
    [Test]
    public void Parse_ShouldRejectAnUnknownHitPolicy()
    {
        var parse = () => DmnModelParser.Parse(Inline("""
            <decision id="d">
              <decisionTable hitPolicy="SOMETIMES">
                <output id="o" name="value"/>
                <rule id="r"><outputEntry><text>1</text></outputEntry></rule>
              </decisionTable>
            </decision>
            """));

        parse.Should().Throw<DmnParseException>().WithMessage("*SOMETIMES*");
    }

    // Testzweck: Eine Regel mit zu wenigen Eintraegen wuerde beim Auswerten in den
    // Indexfehler laufen; der Parser zaehlt die Spalten deshalb selbst nach.
    [Test]
    public void Parse_ShouldRejectARuleWithTheWrongNumberOfEntries()
    {
        var parse = () => DmnModelParser.Parse(Inline("""
            <decision id="d">
              <decisionTable hitPolicy="UNIQUE">
                <input id="i"><inputExpression id="ie"><text>a</text></inputExpression></input>
                <input id="i2"><inputExpression id="ie2"><text>b</text></inputExpression></input>
                <output id="o" name="value"/>
                <rule id="r">
                  <inputEntry><text>1</text></inputEntry>
                  <outputEntry><text>1</text></outputEntry>
                </rule>
              </decisionTable>
            </decision>
            """));

        parse.Should().Throw<DmnParseException>().WithMessage("*2 Eingabespalten*");
    }

    // Testzweck: PRIORITY ohne outputValues haette keine Rangfolge und wuerde still zur
    // Regelreihenfolge verkommen.
    [Test]
    public void Parse_ShouldRejectPriorityWithoutOutputValues()
    {
        var parse = () => DmnModelParser.Parse(Inline("""
            <decision id="d">
              <decisionTable hitPolicy="PRIORITY">
                <input id="i"><inputExpression id="ie"><text>a</text></inputExpression></input>
                <output id="o" name="value"/>
                <rule id="r">
                  <inputEntry><text>1</text></inputEntry>
                  <outputEntry><text>1</text></outputEntry>
                </rule>
              </decisionTable>
            </decision>
            """));

        parse.Should().Throw<DmnParseException>().WithMessage("*outputValues*");
    }

    // Testzweck: Verdichtet wird ueber genau eine Spalte. Mehrere Ausgabespalten und eine
    // Summe passen nicht zusammen.
    [Test]
    public void Parse_ShouldRejectAnAggregationOverSeveralOutputs()
    {
        var parse = () => DmnModelParser.Parse(Inline("""
            <decision id="d">
              <decisionTable hitPolicy="COLLECT" aggregation="SUM">
                <output id="o" name="a"/>
                <output id="o2" name="b"/>
                <rule id="r">
                  <outputEntry><text>1</text></outputEntry>
                  <outputEntry><text>2</text></outputEntry>
                </rule>
              </decisionTable>
            </decision>
            """));

        parse.Should().Throw<DmnParseException>().WithMessage("*Ausgabespalten*");
    }

    // Testzweck: Eine Verdichtung ohne COLLECT ist ein Modellfehler; sonst bliebe unklar,
    // worueber summiert werden soll.
    [Test]
    public void Parse_ShouldRejectAnAggregationWithoutCollect()
    {
        var parse = () => DmnModelParser.Parse(Inline("""
            <decision id="d">
              <decisionTable hitPolicy="UNIQUE" aggregation="SUM">
                <output id="o" name="value"/>
                <rule id="r"><outputEntry><text>1</text></outputEntry></rule>
              </decisionTable>
            </decision>
            """));

        parse.Should().Throw<DmnParseException>().WithMessage("*COLLECT*");
    }

    // Testzweck: Mehrere Ausgabespalten brauchen Namen, sonst liesse sich das Ergebnisobjekt
    // nicht benennen.
    [Test]
    public void Parse_ShouldRejectSeveralOutputsWithoutNames()
    {
        var parse = () => DmnModelParser.Parse(Inline("""
            <decision id="d">
              <decisionTable hitPolicy="UNIQUE">
                <output id="o"/>
                <output id="o2"/>
                <rule id="r">
                  <outputEntry><text>1</text></outputEntry>
                  <outputEntry><text>2</text></outputEntry>
                </rule>
              </decisionTable>
            </decision>
            """));

        parse.Should().Throw<DmnParseException>().WithMessage("*Namen*");
    }

    // Testzweck: Eine Abhaengigkeit auf eine Decision, die es in der Datei nicht gibt,
    // faellt beim Parsen auf statt erst mitten in der Auswertung.
    [Test]
    public void Parse_ShouldRejectARequiredDecisionThatDoesNotExist()
    {
        var parse = () => DmnModelParser.Parse(Inline("""
            <decision id="d">
              <informationRequirement id="ir">
                <requiredDecision href="#gibtEsNicht"/>
              </informationRequirement>
              <literalExpression id="le"><text>1</text></literalExpression>
            </decision>
            """));

        parse.Should().Throw<DmnParseException>().WithMessage("*gibtEsNicht*");
    }

    // Testzweck: Eine Decision ohne Entscheidungslogik liefert nie ein Ergebnis; das soll
    // beim Einlesen gesagt werden.
    [Test]
    public void Parse_ShouldRejectADecisionWithoutDecisionLogic()
    {
        var parse = () => DmnModelParser.Parse(Inline("""
            <decision id="d"><variable name="v"/></decision>
            """));

        parse.Should().Throw<DmnParseException>().WithMessage("*keine Entscheidungslogik*");
    }

    // Testzweck: Regeln ohne id bekommen eine aus ihrer Position. Ohne Id waere in einem
    // Ergebnis nicht zu sehen, welche Zeile gegriffen hat.
    [Test]
    public void Parse_ShouldGiveRulesWithoutIdAnIdFromTheirPosition()
    {
        var definitions = DmnModelParser.Parse(Inline("""
            <decision id="d">
              <decisionTable hitPolicy="UNIQUE">
                <output id="o" name="value"/>
                <rule><outputEntry><text>1</text></outputEntry></rule>
                <rule><outputEntry><text>2</text></outputEntry></rule>
              </decisionTable>
            </decision>
            """));

        definitions.FindDecision("d")!.DecisionTable!.Rules
            .Select(rule => rule.Id).Should().Equal("d-rule-1", "d-rule-2");
    }

    // Testzweck: Die Zerlegung der Wertelisten achtet auf Zeichenketten und Klammern —
    // ein Komma in einem Text oder in einer Funktion trennt nicht.
    [Test]
    public void Parse_ShouldSplitValueListsOnlyAtTopLevelCommas()
    {
        var definitions = DmnModelParser.Parse(Inline("""
            <decision id="d">
              <decisionTable hitPolicy="UNIQUE">
                <input id="i">
                  <inputExpression id="ie"><text>a</text></inputExpression>
                  <inputValues id="iv"><text>"rot,gruen", [1..10], not(5)</text></inputValues>
                </input>
                <output id="o" name="value"/>
                <rule id="r">
                  <inputEntry><text>-</text></inputEntry>
                  <outputEntry><text>1</text></outputEntry>
                </rule>
              </decisionTable>
            </decision>
            """));

        definitions.FindDecision("d")!.DecisionTable!.Inputs[0].InputValues!.Entries
            .Should().Equal("\"rot,gruen\"", "[1..10]", "not(5)");
    }

    // Testzweck: Zwei Decisions mit derselben Id machen die Auswertung mehrdeutig.
    [Test]
    public void Parse_ShouldRejectDuplicateDecisionIds()
    {
        var parse = () => DmnModelParser.Parse(Inline("""
            <decision id="d"><literalExpression id="le"><text>1</text></literalExpression></decision>
            <decision id="d"><literalExpression id="le2"><text>2</text></literalExpression></decision>
            """));

        parse.Should().Throw<DmnParseException>().WithMessage("*mehrfach*");
    }

    /// <summary>Baut eine kleine DMN-1.3-Datei um den uebergebenen Rumpf herum.</summary>
    private static string Inline(string body) =>
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/"
                      id="inlineDefinitions" name="Inline" namespace="http://flowzer.maass.it/dmn/inline">
         {body}
         </definitions>
         """;
}
