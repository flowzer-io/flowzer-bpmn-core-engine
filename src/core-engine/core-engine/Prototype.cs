namespace core_engine;


/// <summary>
/// Das Interface für die eigentliche BPMN-Ausführung-Implementierung.
/// Es soll für die Implementierung unabhängig sein, ob sie in einer dauerhaft laufenden Anwendung
/// läuft, oder in einem MiroService oder Azure/Aws - Function oder in einem Test.
/// Die Daten von externen Diensten (Webhook/UserTask/Zeitsteuerung etc.) werden über die HandleEvent - Methode
/// in den Core "injected". Sofern der Dienst "AUTO" abonniert wird, wird der nächste Prozess Schritt unmittelbar ausgeführt.
/// Die Ausführung des Prozesses erfolgt in Einzelschritten. Jeder Einzelschritt wird durch das Rufen der
/// HandleEvent-Methode ausgelöst und as Ergebnis der Ausführung zurückgeliefert.
/// </summary>
public interface ICore
{
    /// <summary>
    /// Lädt die BPMN Definition (xml) aus dem angegeben Stream und Prüft diese ggf. auf Richtigkeit
    /// </summary>
    /// <param name="xmlDataStream">die XML Daten des BPMN</param>
    /// <param name="verify">Gibt an, ob die Daten aus syntaktische und logische
    /// Richtigkeit geprüft werden soll. </param>
    public Task LoadBpmnFile(Stream xmlDataStream, bool verify);
    
    /// <summary>
    /// Ruf die Startpunkte bzw. Anfangs-Service-Abonements (Subscriptions) des Diagrams ab
    /// </summary>
    /// <returns>Gibt die Anfangs-Service-Abonnements (Subscriptions) des Diagrams zurück</returns>
    public Task<Subscription[]> GetInitialSubscriptions();
    
    /// <summary>
    /// Gibt der Engine ein externes Event eines Services. Die Engine gibt dann den Token an die nächste
    /// BPMN-Node aus und gibt das Ergebnis der Ausführung zurück
    /// </summary>
    /// <param name="data">Die Context-Daten, die die Node braucht. Diese sind *nicht*
    /// die Eingangsdaten der Node</param>
    /// <returns>Das Ergebnis der Ausführung des Events</returns>
    public Task<EventResult> HandleEvent(EventData data);
}

public class Subscription
{
    /// <summary>
    /// Die ID des Service, die für dieses Event zuständig sein soll. z.B. Chron/Webhook etc....
    /// </summary>
    public required string ServiceId { get; set; }
    
    /// <summary>
    /// Daten, die dem Service Context-Informationen geben. Beim Webhook-Service z.B. die URL auf
    /// der er hören soll
    /// </summary>
    public required string Data { get; set; }
    
    /// <summary>
    /// Die BPMN-Node an die Daten bei einem Event des Services übergeben werden sollen
    /// </summary>
    public required string BpmnNodeId { get; set; }
    
    /// <summary>
    /// Die ID der Instance des BPMN-Flows, an die die Daten bei einem Event übergeben werden soll.
    /// Null, wenn eine neue Instanz gestartet werden soll
    /// </summary>
    public required Guid? InstanceId { get; set; }
}

public class EventData
{
    /// <summary>
    /// Die ID des Services von der die Daten kommen
    /// </summary>
    public required string ServiceId { get; set; }
    
    /// <summary>
    /// Die BPMN-Node an die Daten übergeben werden sollen
    /// </summary>
    public required string BpmnNodeId { get; set; }
    
    /// <summary>
    /// Die Context-Daten, die bei der Abonnierung des Events an den Service übergeben wurden
    /// </summary>
    public required string Data { get; set; }
    
    /// <summary>
    /// Die ID der Instance des BPMN-Flows auf die sich das Event bezieht.
    /// Sollte ein Service ein Ereignis auslösen, dass sich auf keine InstanzId bezieht, so
    /// muss dieses Feld mit einer frisch erzeugten Guid gefüllt werden, um somit eine neue Instanz
    /// zu starten
    /// </summary>
    public required Guid InstanceId { get; set; }
    
    /// <summary>
    /// Die globalen Variablen der Instanz des BPMN Flows
    /// </summary>
    public required Dictionary<string, object> InstanceData { get; set; }
}

/// <summary>
/// Das Ergebnis der Ausführung eines Prozessschrittes des BPMN-Cores
/// </summary>
public class EventResult
{
    /// <summary>
    /// Resultierenden Tokens aus diesem Prozessschritt
    /// </summary>
    public required Token[] Tokens { get; set; }
    
    /// <summary>
    /// Die zu abonnierenden Events (durch Services) die nach diesem Prozessschritt aktiv sein sollen.
    /// Diese soll mindestens die Abonnierung eines Services enthalten, oder den Service "AUTO", um
    /// mitzuteilen, dass der nächste Prozessschritt unmittelbar erfolgen soll (z.B. bei einem Gate).
    /// </summary>
    public required Subscription[] ServiceSubscriptions { get; set; }
    
    /// <summary>
    /// Die *geänderten* globalen Variablen der Instance des BPMN Flows
    /// </summary>
    public required Dictionary<string, object> DataChanges { get; set; }
    
    /// <summary>
    /// Gibt an, ob der Flow insgesamt abgeschlossen ist
    /// </summary>
    public required bool IsDone { get; set; }
}


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
