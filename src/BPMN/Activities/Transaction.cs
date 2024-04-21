using BPMN.Common;

namespace BPMN.Activities;

public class Transaction : SubProcess
{
    public required string Method { get; set; }
    public required string Protocol { get; set; }
}