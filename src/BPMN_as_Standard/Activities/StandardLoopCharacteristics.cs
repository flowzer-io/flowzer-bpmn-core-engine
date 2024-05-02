namespace BPMN.Activities;

public record StandardLoopCharacteristics : LoopCharacteristics
{
    public bool TestBefore { get; init; }
}