namespace Model;

/// <summary>
/// Privater, revisionsgeschuetzter Bearbeitungsstand einer offenen User-Task. Der OwnerKey
/// wird serverseitig aus der authentifizierten Identitaet abgeleitet und nie an Clients gegeben.
/// Die zusaetzlichen Bindungen verhindern, dass ein verwaister Entwurf an einem anderen Token
/// oder einer anderen Definition wiederverwendet wird.
/// </summary>
public sealed class UserTaskDraft
{
    public required Guid UserTaskId { get; init; }
    public required string OwnerKey { get; init; }
    public required Guid OwnerUserId { get; init; }
    public required Guid TokenId { get; init; }
    public required Guid ProcessInstanceId { get; init; }
    public required Guid DefinitionId { get; init; }
    public required long Revision { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Kanonisches JSON-Objekt mit ausschliesslich beschreibbaren Formularfeldern.</summary>
    public required string DataJson { get; init; }
}
