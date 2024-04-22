using BPMN.Activities;
using BPMN.Common;

namespace core_engine;

public class ProcessActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required CatchEvent ActualNode { get; set; }
    public int RemainingRetries { get; set; } = 3;
    public DateTime? LockUntil { get; set; } // Wenn gesetzt, ist die Aktivity gesperrt, da sie bearbeitet wird
    public ActivityState State { get; set; } = ActivityState.Ready;
    public required Dictionary<string, object> InputVariables { get; set; }
    public required Dictionary<string, object> OutputVariables { get; set; }
    public required Token CallingToken { get; set; }
    public Dictionary<DateTime, ActivityState> TimeLog { get; init; } = [];
}