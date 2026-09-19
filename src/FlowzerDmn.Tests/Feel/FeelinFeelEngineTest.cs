using FluentAssertions;

namespace FlowzerDmn.Tests.Feel;

/// <summary>
/// Der Adapter zwischen dem FEEL-Handler der Engine und der Schnittstelle der
/// DMN-Bibliothek. Hier steht, was die Bibliothek von FEEL erwarten darf.
/// </summary>
public class FeelinFeelEngineTest : FeelBackedTest
{
    // Testzweck: Ausdruecke werden gerechnet und Variablen aus dem Kontext gelesen. Ohne
    // das waere eine Ausgabespalte nur ein Text.
    [Test]
    public void Evaluate_ShouldComputeExpressionsAndReadVariables()
    {
        FeelEngine.Evaluate("1 + 1", new Dictionary<string, object?>()).Should().Be(2);
        FeelEngine.Evaluate("season", new Dictionary<string, object?> { ["season"] = "Winter" })
            .Should().Be("Winter");
        FeelEngine.Evaluate("\"Spareribs\"", new Dictionary<string, object?>()).Should().Be("Spareribs");
    }

    // Testzweck: Ein unbekannter Name liefert null statt einer Ausnahme — eine Tabelle darf
    // nach einer Variablen fragen, die es in diesem Durchlauf nicht gibt.
    [Test]
    public void Evaluate_ShouldReturnNullForAnUnknownName()
    {
        FeelEngine.Evaluate("diesenNamenGibtEsNicht", new Dictionary<string, object?>()).Should().BeNull();
    }

    // Testzweck: Der entscheidende Punkt des Adapters — libfeelin liest den Eingabewert
    // eines Unary-Tests aus dem Kontext unter dem Schluessel "?". Ein Vergleich ohne
    // linke Seite ("> 10") bezieht sich genau darauf.
    [Test]
    public void UnaryTest_ShouldCompareAgainstTheInputValuePassedUnderQuestionMark()
    {
        var context = new Dictionary<string, object?>();

        FeelEngine.UnaryTest("> 10", 20, context).Should().BeTrue();
        FeelEngine.UnaryTest("> 10", 5, context).Should().BeFalse();
    }

    // Testzweck: Derselbe Schluessel traegt auch den ausdruecklich genannten Eingabewert.
    // Ein Test darf "?" schreiben, etwa fuer "? > limit".
    [Test]
    public void UnaryTest_ShouldAlsoSupportTheExplicitQuestionMark()
    {
        FeelEngine.UnaryTest("? > 10", 20, new Dictionary<string, object?>()).Should().BeTrue();
        FeelEngine.UnaryTest("? > limit", 20, new Dictionary<string, object?> { ["limit"] = 30 })
            .Should().BeFalse();
    }

    // Testzweck: Die Schreibweisen, die in Entscheidungstabellen wirklich vorkommen —
    // Werteliste, Bereich, Negation und der Vergleich mit einer Variablen.
    [Test]
    public void UnaryTest_ShouldSupportTheUsualNotationsOfADecisionTable()
    {
        var empty = new Dictionary<string, object?>();

        FeelEngine.UnaryTest("\"Winter\",\"Fall\"", "Fall", empty).Should().BeTrue();
        FeelEngine.UnaryTest("\"Winter\",\"Fall\"", "Summer", empty).Should().BeFalse();
        FeelEngine.UnaryTest("[1..10]", 5, empty).Should().BeTrue();
        FeelEngine.UnaryTest("[1..10]", 11, empty).Should().BeFalse();
        FeelEngine.UnaryTest("not(5)", 6, empty).Should().BeTrue();
        FeelEngine.UnaryTest("limit", 5, new Dictionary<string, object?> { ["limit"] = 5 }).Should().BeTrue();
    }

    // Testzweck: Ein Kontext ohne Eingabewert oder mit null laesst den Test scheitern,
    // reisst aber nichts mit. Eine Bedingung, die nicht wahr ist, ist falsch.
    [Test]
    public void UnaryTest_ShouldBeFalseForANullInputValue()
    {
        var test = () => FeelEngine.UnaryTest("> 10", null, new Dictionary<string, object?>());

        test.Should().NotThrow();
        test().Should().BeFalse();
    }
}
