using core_engine.Exceptions;
using core_engine.Expression;
using FlowzerDmn.Evaluation;

namespace core_engine.Dmn;

/// <summary>
/// Der Platzhalter fuer eine Engine ohne FEEL: Jede Benutzung endet mit einer klaren
/// Ausnahme, die den tatsaechlich gesetzten Ausdrucks-Handler nennt.
/// </summary>
/// <remarks>
/// Erzeugen darf man ihn immer. Das ist wichtig: Ohne V8 laeuft die Engine weiter, nur eben
/// ohne Entscheidungstabellen. Wuerde schon das Erzeugen der Konfiguration scheitern, waere
/// eine Umgebung ohne V8 gar nicht mehr startbar.
/// </remarks>
/// <param name="expressionHandler">Der Handler, der statt eines FEEL-Handlers konfiguriert ist.</param>
internal sealed class UnsupportedFeelEngine(IExpressionHandler expressionHandler) : IFeelEngine
{
    /// <inheritdoc/>
    public object? Evaluate(string expression, IReadOnlyDictionary<string, object?> context) => throw Unavailable();

    /// <inheritdoc/>
    public bool UnaryTest(string test, object? inputValue, IReadOnlyDictionary<string, object?> context) =>
        throw Unavailable();

    private FlowzerDmnUnavailableException Unavailable() => new(
        $"DMN decisions require a FEEL capable expression handler, but the engine is configured with "
        + $"'{expressionHandler.GetType().Name}'. Run the engine with the V8 based "
        + $"'{nameof(Expression.Feelin.FeelinExpressionHandler)}' to evaluate decisions.");
}
