using System.Dynamic;
using core_engine.Expression.Feelin;
using FlowzerDmn.Evaluation;

namespace core_engine.Dmn;

/// <summary>
/// Schliesst den FEEL-Handler der Engine (libfeelin ueber ClearScript/V8) an die
/// Schnittstelle <see cref="IFeelEngine"/> der DMN-Bibliothek an.
/// </summary>
/// <remarks>
/// <para>
/// Das ist die produktive Bruecke: Dieselbe V8-Instanz, die Ausdruecke im Prozess rechnet,
/// rechnet damit auch die Entscheidungstabelle. Ein zweiter FEEL-Kern wuerde sonst genau
/// dieselben Ausdruecke anders auswerten koennen.
/// </para>
/// <para>
/// <b>Wie der Eingabewert eines Unary-Tests uebergeben wird:</b> libfeelin liest ihn aus
/// dem Kontext unter dem Schluessel <c>?</c>. In <c>bundle.js</c> steht dazu
/// <c>const value = context['?'] !== undefined ? context['?'] : null;</c>, und der
/// Grammatik-Knoten <c>'?'</c> greift ueber <c>getFromContext('?', context)</c> auf
/// denselben Schluessel zu. Der Adapter legt den Eingabewert deshalb unter <c>?</c> in
/// die Variablen, bevor er <see cref="FeelinExpressionHandler.MatchExpression"/> ruft.
/// Damit funktionieren sowohl <c>&gt; 10</c> (impliziter Vergleich mit dem Eingabewert)
/// als auch <c>? &gt; 10</c> (ausdrueckliche Nennung).
/// </para>
/// <para>
/// Beide Methoden setzen ein <c>=</c> vor den Ausdruck, weil der Handler daran erkennt,
/// dass etwas auszuwerten ist — ohne das Zeichen gibt er den Text unveraendert zurueck.
/// </para>
/// </remarks>
public sealed class FeelinFeelEngine : IFeelEngine
{
    /// <summary>Der Schluessel, unter dem libfeelin den Eingabewert eines Unary-Tests erwartet.</summary>
    public const string InputValueKey = "?";

    private readonly FeelinExpressionHandler _handler;

    /// <summary>Erzeugt den Adapter und damit eine V8-Instanz mit geladenem libfeelin.</summary>
    public FeelinFeelEngine()
    {
        _handler = new FeelinExpressionHandler();
    }

    /// <summary>
    /// Erzeugt den Adapter ueber einem bereits vorhandenen Handler. Das ist der Normalfall in
    /// der Laufzeit: Die Engine hat ihre V8-Instanz schon; eine zweite waere teuer und koennte
    /// denselben Ausdruck anders rechnen.
    /// </summary>
    /// <param name="handler">Der FEEL-Handler der Engine.</param>
    public FeelinFeelEngine(FeelinExpressionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <inheritdoc/>
    public object? Evaluate(string expression, IReadOnlyDictionary<string, object?> context) =>
        _handler.GetValue(ToVariables(context), "=" + expression);

    /// <inheritdoc/>
    public bool UnaryTest(string test, object? inputValue, IReadOnlyDictionary<string, object?> context)
    {
        var variables = ToVariables(context);
        ((IDictionary<string, object?>)variables)[InputValueKey] = inputValue;
        return _handler.MatchExpression(variables, "=" + test);
    }

    /// <summary>
    /// Baut aus dem Kontext das Variablenobjekt, das die Engine erwartet (ein
    /// <see cref="ExpandoObject"/>, siehe <c>Variables</c> in core-engine).
    /// </summary>
    private static ExpandoObject ToVariables(IReadOnlyDictionary<string, object?> context)
    {
        var variables = new ExpandoObject();
        var target = (IDictionary<string, object?>)variables;

        foreach (var (key, value) in context)
        {
            target[key] = value;
        }

        return variables;
    }
}
