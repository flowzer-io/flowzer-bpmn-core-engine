namespace WebApiEngine.Shared;

public enum AiProviderKindDto
{
    OpenAi,
    OpenAiCompatible,
    Anthropic
}

public enum AiProcessingLocationDto
{
    Cloud,
    Local
}

/// <summary>
/// Sichere Browser- und Modellierprojektion einer KI-Verbindung. Weder Secret-Wert noch
/// Secret-Referenz sind Teil dieses Antwortvertrags.
/// </summary>
public sealed record AiConnectionDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required AiProviderKindDto Provider { get; init; }
    public required AiProcessingLocationDto Location { get; init; }
    public string? BaseAddress { get; init; }
    public required string DefaultModel { get; init; }
    public required bool Enabled { get; init; }
    public required bool Ready { get; init; }
    public required long Revision { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record CreateAiConnectionRequestDto
{
    public required string Name { get; init; }
    public required AiProviderKindDto Provider { get; init; }
    public required AiProcessingLocationDto Location { get; init; }
    public string? BaseAddress { get; init; }
    public required string DefaultModel { get; init; }
    public required string SecretReference { get; init; }
}

public sealed record UpdateAiConnectionRequestDto
{
    public required long ExpectedRevision { get; init; }
    public required string Name { get; init; }
    public required AiProviderKindDto Provider { get; init; }
    public required AiProcessingLocationDto Location { get; init; }
    public string? BaseAddress { get; init; }
    public required string DefaultModel { get; init; }

    /// <summary>Leer beziehungsweise nicht gesetzt behaelt die bestehende Referenz.</summary>
    public string? SecretReference { get; init; }
}

public sealed record SetAiConnectionEnabledRequestDto
{
    public required long ExpectedRevision { get; init; }
    public required bool Enabled { get; init; }
}
