using BPMN.Activities;

namespace FlowzerBPMN;

public record FlowzerServiceTask : ServiceTask
{
    public int Retrys { get; init; }
}