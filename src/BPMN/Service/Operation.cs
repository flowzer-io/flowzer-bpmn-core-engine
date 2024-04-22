using System.ComponentModel.DataAnnotations;
using BPMN.Common;
using BPMN.Data;

namespace BPMN.Service;

public record Operation
{
    public required string Name { get; init; }
    public string? ImplementationRef { get; init; }
    
    public required Message InMessageRef { get; init; }
    public Message? OutMessageRef { get; init; }
    public List<InputOutputBinding> IoBindings { get; init; } = [];
}