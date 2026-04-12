using core_engine.Expression;
using core_engine.Expression.Feelin;


namespace core_engine;

public class FlowzerConfig
{
    private static readonly Lazy<FlowzerConfig> DefaultConfig = new(() => CreateDefault());

    public required IExpressionHandler ExpressionHandler { get; init; }

    public static FlowzerConfig Default => DefaultConfig.Value;

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
