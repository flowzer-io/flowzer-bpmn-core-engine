using core_engine.Dmn;
using core_engine.Expression;
using core_engine.Expression.Feelin;
using FlowzerDmn.Evaluation;


namespace core_engine;

public class FlowzerConfig
{
    private static readonly Lazy<FlowzerConfig> DefaultConfig = new(() => CreateDefault());

    /// <summary>
    /// Der FEEL-Zugang fuer DMN wird erst beim ersten Zugriff gebaut. Sonst wuerde schon das
    /// Erzeugen der Konfiguration ueber einen fehlenden FEEL-Handler stolpern — und eine
    /// Umgebung ohne V8 waere gar nicht mehr startbar.
    /// </summary>
    private readonly Lazy<IFeelEngine> _feelEngine;

    public FlowzerConfig()
    {
        _feelEngine = new Lazy<IFeelEngine>(CreateFeelEngine);
    }

    public required IExpressionHandler ExpressionHandler { get; init; }

    /// <summary>
    /// Die FEEL-Engine, mit der Entscheidungstabellen gerechnet werden. Sie passt immer zum
    /// konfigurierten <see cref="ExpressionHandler"/>: Derselbe FEEL-Kern, der die Ausdruecke
    /// im Prozess auswertet, wertet auch die Tabelle aus.
    /// </summary>
    /// <remarks>
    /// Ohne FEEL-faehigen Handler steht hier ein Platzhalter, dessen <b>Benutzung</b> eine
    /// <see cref="Exceptions.FlowzerDmnUnavailableException"/> wirft. Das Lesen dieser
    /// Eigenschaft bleibt bewusst harmlos.
    /// </remarks>
    public IFeelEngine FeelEngine => _feelEngine.Value;

    public static FlowzerConfig Default => DefaultConfig.Value;

    private IFeelEngine CreateFeelEngine() => ExpressionHandler switch
    {
        FeelinExpressionHandler feelinExpressionHandler => new FeelinFeelEngine(feelinExpressionHandler),
        // Insbesondere der SimpleExpressionHandler: Er deckt die Ausdruecke des Testbestands
        // ab, ist aber kein FEEL. Eine Tabelle darauf zu rechnen ergaebe stille Fehlurteile.
        var handler => new UnsupportedFeelEngine(handler)
    };

    /// <summary>
    /// Erstellt die Standardkonfiguration. Wenn die native V8-/ClearScript-Abhängigkeit
    /// in der aktuellen Umgebung nicht verfügbar ist, fällt die Engine kontrolliert auf
    /// den einfachen Ausdrucks-Handler zurück. Dadurch bleiben CI und Nicht-V8-Umgebungen
    /// funktionsfähig, ohne produktive V8-Setups zu blockieren.
    /// </summary>
    internal static FlowzerConfig CreateDefault(Func<IExpressionHandler>? expressionHandlerFactory = null)
    {
        var handlerFactory = expressionHandlerFactory ?? (() => new FeelinExpressionHandler());

        try
        {
            return new FlowzerConfig
            {
                ExpressionHandler = handlerFactory()
            };
        }
        catch (Exception exception) when (ShouldFallbackToSimpleExpressionHandler(exception))
        {
            return CreateForTests();
        }
    }

    /// <summary>
    /// Liefert eine testfreundliche Konfiguration ohne native V8-Abhängigkeit.
    /// </summary>
    public static FlowzerConfig CreateForTests()
    {
        return new FlowzerConfig
        {
            ExpressionHandler = new SimpleExpressionHandler()
        };
    }

    private static bool ShouldFallbackToSimpleExpressionHandler(Exception exception)
    {
        return EnumerateExceptionChain(exception).Any(IsMissingClearScriptDependency);
    }

    private static IEnumerable<Exception> EnumerateExceptionChain(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException!)
        {
            yield return current;

            if (current.InnerException == null)
            {
                yield break;
            }
        }
    }

    private static bool IsMissingClearScriptDependency(Exception exception)
    {
        return exception switch
        {
            DllNotFoundException => true,
            FileNotFoundException => true,
            PlatformNotSupportedException => true,
            _ => exception.Message.Contains("ClearScript", StringComparison.OrdinalIgnoreCase)
        };
    }
}
