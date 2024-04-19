namespace core_engine;

/// <summary>
/// Das Ergebnis der Ausführung eines Prozessschrittes des BPMN-Cores
/// </summary>
public class EventResult
{
   

    /// <summary>
    /// Die zu abonnierenden Events (durch Services) die nach diesem Prozessschritt aktiv sein sollen.
    /// Diese soll mindestens die Abonnierung eines Services enthalten, oder den Service "AUTO", um
    /// mitzuteilen, dass der nächste Prozessschritt unmittelbar erfolgen soll (z.B. bei einem Gate).
    /// </summary>
    public required Subscription[] ServiceSubscriptions { get; set; }
    
    /// <summary>
    /// Gibt an, ob der Flow insgesamt abgeschlossen ist
    /// </summary>
    public required bool IsDone { get; set; }
}