namespace BPMN.Flowzer;

public record FlowzwerLoopCharacteristics
{
    public required object InputCollection { get; init; }
    public string? InputElement { get; init; }
    public string? OutputCollection { get; init; }
    [DoNotTranslate]
    public string? OutputElement { get; init; }
}

