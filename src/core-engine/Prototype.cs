namespace core_engine;

/// Gedanken: Ich glaube ich möchte es noch ein wenig mehr vereinfachen und auch die Interactions
/// rausnehmen, sondern nur noch mit Tokens arbeiten. Wir sollten das Klassendiagramm genau so gestalten,
/// wie es der BPMN-Standard vorgibt. Das heißt, dass wir die Interactions rausnehmen und nur noch mit
/// Tokens arbeiten. Die Interactions sind dann nur noch ein Teil des Tokens. Über den aktuellen Token wissen
/// wir ja genau, welche möglichen Interactions es gibt bzw. für mögliche Interactions, die mit einer Activity
/// verbunden sind, können wir ja entsprechend viele Tokens erstellen. Wir müssen wir natürlich dann nur, wenn
/// ein Token eine Activity wieder verlässt, die anderen damit verknüpften Tokens löschen.
///  
/// Tokens wiederrum sollten halt mit aller Art von Nodes verknüpft werden können. So auch z.B. mit Sequenzflows
/// etc. Dann können wir immer die gleiche logik verwenden und die Engine kann Schritt für Schritt durchgehen bis 
/// sie wieder an einem Punkt ankommt, an dem sie auf ein Event warten muss, sich beendet und die Tokens zurückgibt. 


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
    /// <param name="instanceData">Die Daten der Instanz</param>
    /// <param name="eventData">Die Daten des Events</param>
    /// <returns>Das Ergebnis der Ausführung des Events</returns>
    public Task<EventResult> HandleEvent(Instance instanceData, EventData eventData);

    /// <summary>
    /// Event, dass aufgerufen wird, wenn ein Prozessschritt abgeschlossen wurde
    /// </summary>
    public event EventHandler<Instance> InteractionFinished;
}

public class Subscription
{
    /// <summary>
    /// Die ID des Service, die für dieses Event zuständig sein soll. z.B. Chron/Webhook etc....
    /// </summary>
    public required string ServiceId { get; set; }
    
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

public class Instance
{
    /// <summary>
    /// Die ID der Instanz
    /// </summary>
    public required Guid InstanceId { get; set; }
    
    /// <summary>
    /// Die globalen Variablen der Instanz
    /// </summary>
    public required Dictionary<string, object> InstanceData { get; set; }
    
    /// <summary>
    /// Resultierenden Tokens aus diesem Prozessschritt
    /// </summary>
    public required Token[] Tokens { get; set; }

    /// <summary>
    /// Die Interaktionen, die in diesem Prozessschritt möglich sind
    /// </summary>
    public required Interaction[] PossibleInteractions { get; set; }
}

public abstract class Interaction
{
    public string Name { get; set; }
    public string NodeId { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; }
    // public dict<string processSource, object interactionTarget> InputDataLinking { get; set; }
    // public dict<string interactionSource, object processTarget> OutputDataLinking { get; set; }
    // public InteractionData InteractionData { get; set; }
}

public class UserTask : Interaction
{
    public string FormId { get; set; }
}

public class ServiceTask : Interaction
{

}

public class Timer : Interaction
{
    public DateTime TimeToFire { get; set; }
}

public class SendMessage : Interaction
{
    public string MessageId { get; set; }
}

public class ReceiveMessage : Interaction
{
    public string MessageId { get; set; }
}

public class ThrowSignal : Interaction
{
    public string SignalId { get; set; }
}

public class CatchSignal : Interaction
{
    public string SignalId { get; set; }
}

public class ThrowError : Interaction
{
    public string ErrorDescription { get; set; }
}

public class CatchError : Interaction
{
}

public class EventData
{
   
    /// <summary>
    /// Die BPMN-Node an die Daten übergeben werden sollen
    /// </summary>
    public required string BpmnNodeId { get; set; }
    
    /// <summary>
    /// Die ID der Instance des BPMN-Flows auf die sich das Event bezieht.
    /// Sollte ein Service ein Ereignis auslösen, dass sich auf keine InstanzId bezieht, so
    /// muss dieses Feld mit einer frisch erzeugten Guid gefüllt werden, um somit eine neue Instanz
    /// zu starten
    /// </summary>
    public required Guid InstanceId { get; set; }
}

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
