namespace core_engine;

/// <summary>
/// Der Token gibt an, an welcher stelle sich gerade die Ausführung der Prozessinstanz befindet
/// </summary>
public class Token
{
    /// <summary>
    /// die eindeutige ID des Tokens
    /// </summary>
    public required Guid Id { get; set; }
    
    /// <summary>
    /// der Zeitpunkt, zu dem der Token die Node erreicht hat
    /// </summary>
    public required DateTime Time { get; set; }
    
    /// <summary>
    /// Die id der Node, an der sich der Token gerade befindet
    /// </summary>
    public required string BpmnNodeId { get; set; }
}