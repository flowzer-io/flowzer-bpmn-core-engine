using System.Dynamic;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

/// <summary>Die gewünschte Host-Origin muss in der Installations-Allowlist enthalten sein.</summary>
public sealed class CreateFormEmbedLinkRequestDto
{
    public required string HostOrigin { get; init; }
}

/// <summary>Ein persönlicher Fragment-Einstieg; nicht loggen oder dauerhaft speichern.</summary>
public sealed class FormEmbedLinkDto
{
    public required string Url { get; init; }
    /// <summary>Nur die Einlösefrist, ausdrücklich keine Bearbeitungsfrist.</summary>
    public required DateTimeOffset RedeemBeforeUtc { get; init; }
}

/// <summary>Das Read-only-Secret wird im Körper übertragen, nie im Pfad oder in der Query.</summary>
public sealed class RedeemFormEmbedLinkRequestDto
{
    public required string Secret { get; init; }
}

/// <summary>Datensparsamer Formularsnapshot ohne Tokens, Instanzdiagnose oder Mutationsfreigabe.</summary>
public sealed class FormEmbedSnapshotDto
{
    public required Guid UserTaskId { get; init; }
    public required string HostOrigin { get; init; }
    public required long TaskRevision { get; init; }
    public required FormDto Form { get; init; }
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public required ExpandoObject Context { get; init; }
    public required UserTaskDraftDto Draft { get; init; }
}
