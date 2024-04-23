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
    public List<ProcessActivity> Activities { get; private set; } = [];
    public Dictionary<string, object> PricessVariables { get; set; } = new();
    
    
    //TODO: anm. v. Lukas: ich finde, es ist kein schöner stil, wenn eine Methode
    //einfach nur daten aus eingabedaten weiterleitet. Wofür?
    public List<FlowNode> GetPossibleInteractionFlowNodes(Token token)
    {
        return Model.GetPossibleInteractionFlowNodes(token);
    }

    //TODO: anm. v. Lukas: was genau bedeutet denn "Start"? Was soll denn hier passieren?
    public void Start(FlowNode? flowNode = null)
    {
        // Wenn flowNode null ist, wird der Prozess gestartet mit allen StartEvents etc.
        // Wenn flowNode nicht null ist, wird der Prozess an der Stelle gestartet
        
        throw new NotImplementedException();
    }
    
    public void CompleteActivity(Activity aktivity, Dictionary<string, object> variables)
    {
        // Die ProcessAktivity wird abgeschlossen und die Variablen werden gesetzt
        
        throw new NotImplementedException();
    }
    
    //TODO: anm. v. Lukas: was ist denn ein "Event"?  Hier wird doch gar nix geworfen? Oder 
    // ist die Rückmeldung einer Aktivität gemeint?
    public void ThrowEvent(ThrowEvent throwEvent, Dictionary<string, object> variables)
    {
        // Hier wird ein Event geworfen
        
        throw new NotImplementedException();
    }
}