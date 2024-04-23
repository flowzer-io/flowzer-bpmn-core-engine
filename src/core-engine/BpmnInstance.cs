using BPMN.Activities;
using BPMN.Common;
using BPMN.Events;
using BPMN.Process;

namespace core_engine;

public class BpmnInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required BpmnModel Model { get; set; }
    public required Process Process { get; set; }
    public List<Token> Tokens { get; private set; } = [];
    public List<ProcessActivity> ProcessActivities { get; private set; } = [];
    public Dictionary<string, object> ProcessVariables { get; set; } = new();
    
    //TODO: anm. v. Lukas: ich finde, es ist kein schöner stil, wenn eine Methode
    //einfach nur daten aus eingabedaten weiterleitet. Wofür?
    public List<CatchEvent> GetAdditionalCatchEvents(Token token)
    {
        return Model.GetCatchEvents(token.ActualNode);
    }

    public void Start(FlowNode? flowNode = null)
    {
        // Wenn flowNode null ist, wird der Prozess gestartet mit allen StartEvents etc.
        // Wenn flowNode nicht null ist, wird der Prozess an der Stelle gestartet
        
        throw new NotImplementedException();
    }
    
    public void CompleteActivity(Token token, Activity activity, Dictionary<string, object> variables)
    {
        // Die ProcessActivity wird abgeschlossen und die Variablen werden gesetzt
        
        throw new NotImplementedException();
    }
    
    public void HandleEvent(Token token, BPMN.Events.CatchEvent catchEvent, Dictionary<string, object> variables)
    //TODO: anm. v. Lukas: was ist denn ein "Event"?  Hier wird doch gar nix geworfen? Oder 
    // ist die Rückmeldung einer Aktivität gemeint?
    public void ThrowEvent(ThrowEvent throwEvent, Dictionary<string, object> variables)
    {
        // Hier wird ein Event verarbeitet, welches von Aussen "geworfen" wird.
        
        throw new NotImplementedException();
    }
    
    public void Cancel()
    {
        // Hier wird die Instance abgebrochen
    }
}