using core_engine.Exceptions;
using core_engine.Expression.Feelin;
using FluentAssertions;

namespace core_engine_tests;

/// <summary>
/// Der FEEL-Zugang der Konfiguration. Er entscheidet, ob Entscheidungstabellen gerechnet
/// werden koennen — und muss dabei eine Umgebung ohne V8 weiterhin startbar lassen.
/// </summary>
public class FlowzerConfigFeelEngineTest
{
    // Testzweck: Ohne FEEL-faehigen Handler darf das Erzeugen der Konfiguration und das Lesen
    // des Zugangs nicht scheitern — sonst waere die Engine ohne V8 gar nicht mehr startbar.
    // Erst die Benutzung meldet klar, woran es liegt, und nennt den gesetzten Handler.
    [Test]
    public void FeelEngine_WithSimpleExpressionHandler_ShouldFailOnlyWhenItIsUsed()
    {
        var config = FlowzerConfig.CreateForTests();

        var feelEngine = config.FeelEngine;

        var evaluate = () => feelEngine.Evaluate("1 + 1", new Dictionary<string, object?>());
        var unaryTest = () => feelEngine.UnaryTest("> 10", 20, new Dictionary<string, object?>());

        evaluate.Should().Throw<FlowzerDmnUnavailableException>()
            .WithMessage("*SimpleExpressionHandler*");
        unaryTest.Should().Throw<FlowzerDmnUnavailableException>()
            .WithMessage("*SimpleExpressionHandler*");
    }

    // Testzweck: Mit dem FEEL-Handler der Engine rechnet derselbe Zugang wirklich FEEL —
    // die produktive Bruecke zwischen Engine und DMN-Kern haengt genau daran.
    [Test]
    public void FeelEngine_WithFeelinExpressionHandler_ShouldEvaluateFeel()
    {
        FlowzerConfig config;
        try
        {
            config = new FlowzerConfig { ExpressionHandler = new FeelinExpressionHandler() };
        }
        catch (Exception)
        {
            Assert.Ignore("Ohne native V8-Bibliothek nicht pruefbar.");
            return;
        }

        config.FeelEngine.Evaluate("1 + 1", new Dictionary<string, object?>()).Should().Be(2);
    }
}
