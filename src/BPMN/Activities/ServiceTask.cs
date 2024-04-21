using BPMN.Common;

namespace BPMN.Activities;

public class ServiceTask : Task
{
    public required string Implementation { get; set; }
}