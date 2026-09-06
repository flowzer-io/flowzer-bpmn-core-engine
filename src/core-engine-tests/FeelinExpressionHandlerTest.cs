using System.Dynamic;
using core_engine.Expression.Feelin;
using FluentAssertions;

namespace core_engine_tests;

/// <summary>
/// Der FEEL-Handler laeuft ueber V8 und braucht die native ClearScript-Bibliothek. Wo die
/// fehlt — im Linux-Container der Produktion etwa — springt der einfache Handler ein; die
/// Tests hier laufen deshalb nur, wo V8 tatsaechlich vorhanden ist.
/// </summary>
public class FeelinExpressionHandlerTest
{
    private static FeelinExpressionHandler? CreateHandler()
    {
        try
        {
            return new FeelinExpressionHandler();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Testzweck: Ein Ausdruck, den V8 nicht auswerten kann, liefert "nicht wahr" statt die
    // Instanz zu beenden. Zuvor lief hier .ToString() auf null: Eine einzelne kaputte
    // Bedingung riss den ganzen Prozess mit, statt den Standardfluss nehmen zu lassen.
    [Test]
    public void MatchExpression_ShouldBeFalse_WhenTheExpressionCannotBeEvaluated()
    {
        var handler = CreateHandler();
        if (handler is null)
        {
            Assert.Ignore("Ohne native V8-Bibliothek nicht pruefbar.");
            return;
        }

        var variables = new ExpandoObject();

        var match = () => handler.MatchExpression(variables, "=diesenNamenGibtEsNicht");

        match.Should().NotThrow();
        match().Should().BeFalse();
    }

    // Testzweck: Ohne Variablen darf der Handler nicht ueber null stolpern — eine Bedingung
    // am Anfang eines Prozesses trifft genau diesen Fall.
    [Test]
    public void MatchExpression_ShouldBeFalse_WhenThereAreNoVariables()
    {
        var handler = CreateHandler();
        if (handler is null)
        {
            Assert.Ignore("Ohne native V8-Bibliothek nicht pruefbar.");
            return;
        }

        var match = () => handler.MatchExpression(null!, "=irgendetwas = \"ja\"");

        match.Should().NotThrow();
        match().Should().BeFalse();
    }
}
