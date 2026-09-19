namespace BPMN.Flowzer.Events;

/// <summary>
/// Ein Zwischenereignis, das auf eine Nachricht wartet.
///
/// Die Nutzdaten der eintreffenden Nachricht wandern wie bei einer Empfangsaufgabe in den
/// Prozesskontext — mit <c>zeebe:ioMapping</c>-Ausgang gezielt, ohne Zuordnung vollständig.
/// Ohne diesen Weg käme eine Nachricht an, ohne dass der Prozess ihren Inhalt je zu sehen bekäme.
/// </summary>
public record FlowzerIntermediateMessageCatchEvent : IntermediateThrowEvent, IFlowzerOutputMapping
{
    public required MessageDefinition MessageDefinition { get; init; }

    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}
