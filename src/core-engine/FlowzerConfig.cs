using core_engine.Expression;
using core_engine.Expression.Feelin;


namespace core_engine;

public class FlowzerConfig
{
    public required IExpressionHandler ExpressionHandler { get; init; }

    private static FlowzerConfig? _default;
    
    public static FlowzerConfig Default 
    {
        get
        {
            if (_default != null)
                return _default;
                
            try
            {
                return new FlowzerConfig
                {
                    ExpressionHandler = new FeelinExpressionHandler()
                };
            }
            catch (Exception ex) when (ex is System.DllNotFoundException || 
                                       ex.Message.Contains("ClearScriptV8") ||
                                       ex.InnerException is System.DllNotFoundException)
            {
                // Fallback für Test-Umgebungen ohne V8 native libraries
                return new FlowzerConfig
                {
                    ExpressionHandler = new SimpleExpressionHandler()
                };
            }
        }
    }
    
    /// <summary>
    /// Setzt eine benutzerdefinierte FlowzerConfig für Tests
    /// </summary>
    public static void SetDefault(FlowzerConfig config)
    {
        _default = config;
    }
    
    /// <summary>
    /// Setzt die Standard-Konfiguration zurück
    /// </summary>
    public static void ResetDefault()
    {
        _default = null;
    }
}