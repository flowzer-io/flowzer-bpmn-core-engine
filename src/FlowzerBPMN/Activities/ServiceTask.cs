namespace BPMN.Activities;

public record ServiceTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string Implementation { get; init; }

    /// <summary>
    /// Gesetzt, wenn der normale BPMN-Service-Task den versionierten Flowzer-KI-Vertrag trägt.
    /// Die Engine behält damit die BPMN-Semantik eines Service-Tasks; Providerdetails bleiben
    /// eine klar abgegrenzte Erweiterung.
    /// </summary>
    [DoNotTranslate]
    public Flowzer.AiTaskDefinition? FlowzerAiTask { get; init; }

    public int FlowzerRetries { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}
