namespace BPMN.Flowzer;

public record FlowzwerLoopCharacteristics
{
    public required string InputCollection { get; set; }
    public string? InputElement { get; set; }
    public string? OutputCollection { get; set; }
    public string? OutputElement { get; set; }
}