using FlowzerDmn.Evaluation;
using FlowzerDmn.Exceptions;
using FlowzerDmn.Model;
using FlowzerDmn.Parsing;
using FluentAssertions;

namespace FlowzerDmn.Tests;

/// <summary>
/// Die Auswertung einer Entscheidungstabelle, Trefferregel fuer Trefferregel. Gerechnet
/// wird mit dem echten FEEL der Engine.
/// </summary>
public class DmnDecisionEvaluatorTest : FeelBackedTest
{
    // Testzweck: Der Normalfall einer UNIQUE-Tabelle — genau eine Regel passt, und das
    // Ergebnis nennt die Ausgabespalte beim Namen.
    [Test]
    public void Evaluate_ShouldReturnTheOutputOfTheSingleMatchingRule()
    {
        var definitions = DmnFixtures.Parse("dish.dmn");

        var result = Evaluator.Evaluate(definitions, "dish",
            Variables(("season", "Fall"), ("guestCount", 5)));

        result.DecisionId.Should().Be("dish");
        result.MatchedRules.Should().Equal("dishRuleSpareribs");
        SingleOutput(result).Should().Equal(new Dictionary<string, object?> { ["desiredDish"] = "Spareribs" });
    }

    // Testzweck: Auch bei genau einer Ausgabespalte bleibt das Ergebnis ein Objekt und wird
    // nicht zum blossen Wert ausgepackt. Wer spaeter eine zweite Spalte ergaenzt, aendert
    // damit nicht die Form des Ergebnisses.
    [Test]
    public void Evaluate_ShouldWrapASingleOutputInAnObject()
    {
        var definitions = DmnFixtures.Parse("dish.dmn");

        var result = Evaluator.Evaluate(definitions, "dish",
            Variables(("season", "Winter"), ("guestCount", 4)));

        result.Value.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>();
        SingleOutput(result)["desiredDish"].Should().Be("Roastbeef");
    }

    // Testzweck: Ein Strich in der Eingabe passt immer — im Sommer ist die Gaestezahl egal.
    [Test]
    public void Evaluate_ShouldTreatTheWildcardEntryAsAlwaysMatching()
    {
        var definitions = DmnFixtures.Parse("dish.dmn");

        var result = Evaluator.Evaluate(definitions, "dish",
            Variables(("season", "Summer"), ("guestCount", 250)));

        result.MatchedRules.Should().Equal("dishRuleSalad");
        SingleOutput(result)["desiredDish"].Should().Be("Light Salad and a nice Steak");
    }

    // Testzweck: Eine Werteliste in der Bedingung trifft, wenn einer der Werte passt.
    [Test]
    public void Evaluate_ShouldMatchAListOfValuesInARuleCondition()
    {
        var definitions = DmnFixtures.Parse("dish.dmn");

        var result = Evaluator.Evaluate(definitions, "dish",
            Variables(("season", "Winter"), ("guestCount", 20)));

        result.MatchedRules.Should().Equal("dishRuleStew");
    }

    // Testzweck: Passt keine Regel, ist das Ergebnis null und die Trefferliste leer —
    // das ist kein Fehler, sondern eine Tabelle ohne Antwort.
    [Test]
    public void Evaluate_ShouldReturnNullWhenNoRuleMatches()
    {
        var definitions = DmnFixtures.Parse("anyAndUnique.dmn");

        var result = Evaluator.Evaluate(definitions, "uniqueOverlap", Variables(("amount", 5000)));

        result.Value.Should().BeNull();
        result.MatchedRules.Should().BeEmpty();
    }

    // Testzweck: Ein Eingabewert ausserhalb der inputValues bricht ab. Bewusste Abweichung
    // von Camunda: sonst faellt ein Tippfehler in der Saison still durch die Tabelle.
    [Test]
    public void Evaluate_ShouldRejectAnInputValueOutsideTheAllowedValues()
    {
        var definitions = DmnFixtures.Parse("dish.dmn");

        var evaluate = () => Evaluator.Evaluate(definitions, "dish",
            Variables(("season", "Autumn"), ("guestCount", 5)));

        var exception = evaluate.Should().Throw<DmnInputOutOfRangeException>().Which;
        exception.DecisionId.Should().Be("dish");
        exception.InputId.Should().Be("dishInputSeason");
        exception.InputExpression.Should().Be("season");
        exception.Value.Should().Be("Autumn");
        exception.AllowedValues.Should().Be("\"Fall\",\"Winter\",\"Spring\",\"Summer\"");
    }

    // Testzweck: UNIQUE mit mehreren Treffern ist ein Modellfehler; die Ausnahme nennt alle
    // beteiligten Regeln, damit die Ueberlappung auffindbar ist.
    [Test]
    public void Evaluate_ShouldRejectSeveralMatchesUnderUnique()
    {
        var definitions = DmnFixtures.Parse("anyAndUnique.dmn");

        var evaluate = () => Evaluator.Evaluate(definitions, "uniqueOverlap", Variables(("amount", 10)));

        var exception = evaluate.Should().Throw<DmnHitPolicyViolationException>().Which;
        exception.HitPolicy.Should().Be(DmnHitPolicy.Unique);
        exception.RuleIds.Should().Equal("uniqueOverlapSmall", "uniqueOverlapTiny");
    }

    // Testzweck: ANY erlaubt mehrere Treffer, solange sie dasselbe sagen.
    [Test]
    public void Evaluate_ShouldAcceptSeveralMatchesUnderAnyWhenTheyAgree()
    {
        var definitions = DmnFixtures.Parse("anyAndUnique.dmn");

        var result = Evaluator.Evaluate(definitions, "anyAgrees", Variables(("amount", 10)));

        result.MatchedRules.Should().Equal("anyAgreesSmall", "anyAgreesTiny");
        SingleOutput(result)["approval"].Should().Be("Automatisch");
    }

    // Testzweck: ANY mit widerspruechlichen Ausgaben ist ein Modellfehler.
    [Test]
    public void Evaluate_ShouldRejectContradictingMatchesUnderAny()
    {
        var definitions = DmnFixtures.Parse("anyAndUnique.dmn");

        var evaluate = () => Evaluator.Evaluate(definitions, "anyConflicts", Variables(("amount", 10)));

        var exception = evaluate.Should().Throw<DmnHitPolicyViolationException>().Which;
        exception.HitPolicy.Should().Be(DmnHitPolicy.Any);
        exception.RuleIds.Should().Equal("anyConflictsSmall", "anyConflictsTiny");
    }

    // Testzweck: FIRST nimmt den ersten Treffer in Dokumentreihenfolge und nennt auch nur
    // diesen — dass weiter unten noch etwas gepasst haette, ist hier gewollt.
    [Test]
    public void Evaluate_ShouldTakeTheFirstMatchUnderFirst()
    {
        var definitions = DmnFixtures.Parse("firstAndRuleOrder.dmn");

        var result = Evaluator.Evaluate(definitions, "firstDecision", Variables(("weight", 5)));

        result.MatchedRules.Should().Equal("firstExpress");
        SingleOutput(result)["shipping"].Should().Be("Express");
    }

    // Testzweck: RULE ORDER sammelt alle Treffer in Regelreihenfolge.
    [Test]
    public void Evaluate_ShouldCollectAllMatchesInRuleOrder()
    {
        var definitions = DmnFixtures.Parse("firstAndRuleOrder.dmn");

        var result = Evaluator.Evaluate(definitions, "ruleOrderDecision", Variables(("weight", 5)));

        result.MatchedRules.Should().Equal("ruleOrderExpress", "ruleOrderStandard", "ruleOrderFreight");
        OutputList(result).Select(output => output["shipping"])
            .Should().Equal("Express", "Standard", "Freight");
    }

    // Testzweck: PRIORITY waehlt nicht die erste, sondern die hoechstrangige Regel. Die
    // Rangfolge steht in outputValues, nicht in der Regelreihenfolge.
    [Test]
    public void Evaluate_ShouldTakeTheHighestRankedMatchUnderPriority()
    {
        var definitions = DmnFixtures.Parse("priority.dmn");

        var result = Evaluator.Evaluate(definitions, "priorityDecision", Variables(("score", 150)));

        SingleOutput(result)["verdict"].Should().Be("Refer");
        result.MatchedRules.Should().Equal("priorityRefer", "priorityDecline", "priorityAccept");
    }

    // Testzweck: OUTPUT ORDER liefert alle Treffer, aber nach der Rangfolge aus outputValues
    // sortiert statt in Regelreihenfolge.
    [Test]
    public void Evaluate_ShouldSortAllMatchesByOutputValuesUnderOutputOrder()
    {
        var definitions = DmnFixtures.Parse("priority.dmn");

        var result = Evaluator.Evaluate(definitions, "outputOrderDecision", Variables(("score", 150)));

        OutputList(result).Select(output => output["verdict"])
            .Should().Equal("Refer", "Decline", "Accept");
    }

    // Testzweck: COLLECT ohne Verdichtung liefert die Liste aller Treffer.
    [Test]
    public void Evaluate_ShouldCollectAllMatchesAsAList()
    {
        var definitions = DmnFixtures.Parse("collect.dmn");

        var result = Evaluator.Evaluate(definitions, "collectList",
            Variables(("customerType", "Business"), ("orderSize", 20)));

        result.MatchedRules.Should().Equal("collectListBusiness", "collectListLargeOrder");
        OutputList(result).Select(output => output["discount"]).Should().Equal(5d, 3d);
    }

    // Testzweck: Die Verdichtungen von COLLECT liefern eine einzelne Zahl statt einer Liste.
    [TestCase("collectSum", 8d)]
    [TestCase("collectMin", 3d)]
    [TestCase("collectMax", 5d)]
    public void Evaluate_ShouldAggregateTheCollectedMatches(string decisionId, double expected)
    {
        var definitions = DmnFixtures.Parse("collect.dmn");

        var result = Evaluator.Evaluate(definitions, decisionId,
            Variables(("customerType", "Business"), ("orderSize", 20)));

        result.Value.Should().Be(expected);
    }

    // Testzweck: COUNT zaehlt die Treffer und ist damit die einzige Verdichtung, die auch
    // ohne Treffer eine sinnvolle Antwort hat.
    [Test]
    public void Evaluate_ShouldCountTheMatchesUnderCollectCount()
    {
        var definitions = DmnFixtures.Parse("collect.dmn");

        Evaluator.Evaluate(definitions, "collectCount",
            Variables(("customerType", "Business"), ("orderSize", 20))).Value.Should().Be(2);

        Evaluator.Evaluate(definitions, "collectCount",
            Variables(("customerType", "Andere"), ("orderSize", 1))).Value.Should().Be(0);
    }

    // Testzweck: Ohne Treffer bleibt eine Sammeltabelle leer und eine Summe ohne Antwort.
    [Test]
    public void Evaluate_ShouldReturnAnEmptyListAndNoSumWhenNothingMatches()
    {
        var definitions = DmnFixtures.Parse("collect.dmn");
        var variables = Variables(("customerType", "Andere"), ("orderSize", 1));

        OutputList(Evaluator.Evaluate(definitions, "collectList", variables)).Should().BeEmpty();
        Evaluator.Evaluate(definitions, "collectSum", variables).Value.Should().BeNull();
    }

    // Testzweck: Eine leere Ausgabe liefert null statt eines Fehlers; ein Strich in der
    // Eingabe faengt den Rest ab.
    [Test]
    public void Evaluate_ShouldReturnNullForAnEmptyOutputEntry()
    {
        var definitions = DmnFixtures.Parse("types.dmn");

        var result = Evaluator.Evaluate(definitions, "typedOutputs", Variables(("kind", "anders")));

        result.MatchedRules.Should().Equal("typedRuleSparse");
        var outputs = SingleOutput(result);
        outputs["text"].Should().Be("rest");
        outputs["count"].Should().BeNull();
        outputs["ratio"].Should().BeNull();
        outputs["flag"].Should().BeNull();
    }

    // Testzweck: Das typeRef der Ausgabespalte bestimmt den .NET-Typ des Werts. Ohne die
    // Umsetzung kaeme aus V8 je nach Zahl ein Int32 oder ein Single zurueck.
    [Test]
    public void Evaluate_ShouldConvertOutputsToTheDeclaredTypes()
    {
        var definitions = DmnFixtures.Parse("types.dmn");

        var outputs = SingleOutput(Evaluator.Evaluate(definitions, "typedOutputs", Variables(("kind", "voll"))));

        outputs["text"].Should().BeOfType<string>().And.Be("eins");
        outputs["count"].Should().BeOfType<int>().And.Be(3);
        outputs["ratio"].Should().BeOfType<double>().And.Be(1.5d);
        outputs["flag"].Should().BeOfType<bool>().And.Be(true);
    }

    // Testzweck: Eine Kette benoetigter Decisions wird von unten nach oben ausgewertet; jede
    // Stufe sieht das Ergebnis der vorigen unter dem Namen ihrer variable.
    [Test]
    public void Evaluate_ShouldEvaluateRequiredDecisionsFirst()
    {
        var definitions = DmnFixtures.Parse("requiredDecision.dmn");

        var result = Evaluator.Evaluate(definitions, "finalPremium", Variables(("applicantAge", 20)));

        SingleOutput(result)["premium"].Should().Be(250d);
        result.RequiredResults.Keys.Should().BeEquivalentTo("riskFactor", "riskLevel");
        result.RequiredResults["riskLevel"].Value.Should().Be("high");
        result.RequiredResults["riskFactor"].Value.Should().Be(2d);
    }

    // Testzweck: Dieselbe Kette mit anderen Variablen nimmt den anderen Zweig — die
    // Zwischenergebnisse sind nicht aus einem frueheren Lauf zwischengespeichert.
    [Test]
    public void Evaluate_ShouldFollowTheOtherBranchOfTheChainForOtherVariables()
    {
        var definitions = DmnFixtures.Parse("requiredDecision.dmn");

        var result = Evaluator.Evaluate(definitions, "finalPremium", Variables(("applicantAge", 40)));

        SingleOutput(result)["premium"].Should().Be(100d);
        result.RequiredResults["riskLevel"].Value.Should().Be("low");
    }

    // Testzweck: Ein literalExpression ist selbst eine Entscheidungslogik. Sein Ergebnis
    // wird nicht in ein Objekt gepackt, sondern direkt geliefert.
    [Test]
    public void Evaluate_ShouldReturnTheValueOfALiteralExpression()
    {
        var definitions = DmnFixtures.Parse("literalExpression.dmn");

        var result = Evaluator.Evaluate(definitions, "netAmount", Variables(("grossAmount", 119)));

        result.MatchedRules.Should().BeEmpty();
        result.Value.Should().BeOfType<double>().Which.Should().BeApproximately(100d, 1e-9);
    }

    // Testzweck: Eine DMN-1.1-Datei wird nicht nur gelesen, sondern genauso ausgewertet.
    [Test]
    public void Evaluate_ShouldEvaluateALegacyDmn11File()
    {
        var definitions = DmnFixtures.Parse("legacyDmn11.dmn");

        SingleOutput(Evaluator.Evaluate(definitions, "legacyDecision", Variables(("years", 7))))["bonus"]
            .Should().Be(500d);
    }

    // Testzweck: Eine Datei mit Diagrammteil wertet genauso aus wie eine ohne.
    [Test]
    public void Evaluate_ShouldEvaluateAFileWithADiagramPart()
    {
        var definitions = DmnFixtures.Parse("withDmndi.dmn");

        SingleOutput(Evaluator.Evaluate(definitions, "diagramDecision", Variables(("speed", 80))))["signal"]
            .Should().Be("rot");
    }

    // Testzweck: Eine Decision, die es nicht gibt, ist ein Aufruferfehler und wird als
    // solcher benannt.
    [Test]
    public void Evaluate_ShouldRejectAnUnknownDecisionId()
    {
        var definitions = DmnFixtures.Parse("dish.dmn");

        var evaluate = () => Evaluator.Evaluate(definitions, "gibtEsNicht", Variables());

        evaluate.Should().Throw<DmnEvaluationException>().WithMessage("*gibtEsNicht*");
    }

    // Testzweck: Ueber Texte laesst sich nicht summieren. Das soll gesagt werden, statt
    // still eine Null zu liefern.
    [Test]
    public void Evaluate_ShouldRejectAnAggregationOverValuesThatAreNotNumbers()
    {
        var definitions = DmnModelParser.Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="d" namespace="urn:test">
              <decision id="textSum">
                <decisionTable hitPolicy="COLLECT" aggregation="SUM">
                  <output id="o" name="value" typeRef="string"/>
                  <rule id="r"><outputEntry><text>"kein Wert"</text></outputEntry></rule>
                </decisionTable>
              </decision>
            </definitions>
            """);

        var evaluate = () => Evaluator.Evaluate(definitions, "textSum", Variables());

        evaluate.Should().Throw<DmnEvaluationException>().WithMessage("*keine Zahl*");
    }

    // Testzweck: Eine einzelne Ausgabespalte ohne Namen bekommt den Schluessel "result" —
    // so bleibt die Ergebnisform auch dann ein Objekt.
    [Test]
    public void Evaluate_ShouldNameASingleUnnamedOutputResult()
    {
        var definitions = DmnModelParser.Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="d" namespace="urn:test">
              <decision id="unnamed">
                <decisionTable hitPolicy="UNIQUE">
                  <output id="o" typeRef="number"/>
                  <rule id="r"><outputEntry><text>42</text></outputEntry></rule>
                </decisionTable>
              </decision>
            </definitions>
            """);

        SingleOutput(Evaluator.Evaluate(definitions, "unnamed", Variables()))["result"].Should().Be(42d);
    }
}
