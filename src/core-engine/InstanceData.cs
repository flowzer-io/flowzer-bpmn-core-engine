namespace core_engine;

public class InstanceData
{
    /// <summary>
    /// Die ID der Instanz
    /// </summary>
    public required Guid InstanceId { get; set; }
    
    /// <summary>
    /// Die globalen Variablen der Instanz
    /// </summary>
    public required Dictionary<string, object> Data { get; set; }
    
    /// <summary>
    /// Resultierenden Tokens aus diesem Prozessschritt
    /// </summary>
    public required Token[] Tokens { get; set; }

    /// <summary>
    /// Die Interaktionen, die in diesem Prozessschritt möglich sind
    /// </summary>
    public required InteractionRequest[] PossibleInteractions { get; set; }
}