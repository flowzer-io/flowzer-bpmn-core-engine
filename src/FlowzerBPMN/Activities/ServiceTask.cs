using BPMN.Common;

namespace BPMN.Activities;

public record ServiceTask : Task
{
    public required string Implementation { get; init; }
    
    public int FlowzerRetries { get; init; }
}