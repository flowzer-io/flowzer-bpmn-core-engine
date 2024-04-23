using BPMN.Activities;
using BPMN.Common;

namespace core_engine;

public class ProcessActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required FlowNode ActualNode { get; set; }
    public int RemainingRetries { get; set; } = 3;
    
    //TODO: anm. v. Lukas: das ist m.M.n aufgabe der "App",
    //da es von der implementierung der "außeren" umgebung abhängt,
    //ob locking notwendig ist oder nicht. das muss also teil des Datenbankmodells werden
    public DateTime? LockUntil { get; set; } // Wenn gesetzt, ist die Aktivity gesperrt, da sie bearbeitet wird
    
    public ActivityState State { get; set; } = ActivityState.Ready;
    public required Dictionary<string, object> InputVariables { get; set; }
    
    //TODO: anm. v. Lukas: output macht hier m.M.n. keinen sinn, da die ja teil
    // der rückmeldung einer aktivität sind. das mapping ansich sollte der core machen
    public required Dictionary<string, object> OutputVariables { get; set; }
    
    public required Token CallingToken { get; set; }
    
    //TODO: anm. v. Lukas: was genau macht das hier?
    public Dictionary<DateTime, ActivityState> TimeLog { get; init; } = [];
}