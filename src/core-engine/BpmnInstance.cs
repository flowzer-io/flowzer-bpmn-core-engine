using BPMN.Activities;
using BPMN.Common;
using BPMN.Events;
using BPMN.Process;
using CatchEvent = BPMN.Common.CatchEvent;

namespace core_engine;

public class BpmnInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required BpmnModel Model { get; set; }
    public required Process Process { get; set; }
    public List<Token> Tokens { get; private set; } = [];
    public List<ProcessActivity> ProcessActivities { get; private set; } = [];
    public Dictionary<string, object> ProcessVariables { get; set; } = new();
    
    public List<CatchEvent> GetAdditionalCatchEvents(Token token)
    {
        return Model.GetCatchEvents(token.ActualNode);
    }

    public void Start(CatchEvent? flowNode = null)
    {
        // Wenn flowNode null ist, wird der Prozess gestartet mit allen StartEvents etc.
        // Wenn flowNode nicht null ist, wird der Prozess an der Stelle gestartet
        
        throw new NotImplementedException();
    }
    
    public void CompleteActivity(Guid tokenId, Activity activity, Dictionary<string, object> variables)
    {
        // Die ProcessAktivity wird abgeschlossen und die Variablen werden gesetzt
        
        throw new NotImplementedException();
    }
    
    public void HandleEvent(Guid tokenId, BPMN.Events.CatchEvent catchEvent, Dictionary<string, object> variables)
    {
        // Hier wird ein Event verarbeitet, welches von Aussen "geworfen" wird.
        
        throw new NotImplementedException();
    }
}