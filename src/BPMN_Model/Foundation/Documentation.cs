namespace BPMN_Model.Foundation;

public record Documentation : BaseElement
{
    public required string Text { get; init; }
    public string? TextFormat { get; init; }
}