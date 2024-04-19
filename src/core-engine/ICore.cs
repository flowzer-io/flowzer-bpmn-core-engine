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
    /// <param name="interactionResult">Die Daten des Events</param>
    /// <returns>Das Ergebnis der Ausführung des Events</returns>
    public Task<EventResult> HandleInteraction(InstanceData instanceData, InteractionResult interactionResult);

    /// <summary>
    /// Event, dass aufgerufen wird, wenn ein Prozessschritt abgeschlossen wurde
    /// </summary>
    public event EventHandler<InstanceData> InteractionFinished;
}