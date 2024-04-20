namespace core_engine;

/// <summary>
/// The result of executing a process step of the BPMN core.
/// </summary>
public class ExecutionResult
{
    /// <summary>
    /// Indicates whether the flow is completed overall.
    /// </summary>
    public required bool IsDone { get; set; }
}