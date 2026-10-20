namespace Model;

/// <summary>
/// Persönliche, kurzlebige Anzeige-Freigabe. Persistiert wird ausschließlich der Hash des
/// zufälligen Secrets, niemals das Secret selbst oder ein Benutzerzugriffstoken.
/// </summary>
public sealed class FormEmbedGrant
{
    public required string SecretHash { get; init; }
    public required Guid UserTaskId { get; init; }
    public required Guid DefinitionId { get; init; }
    public required Guid OwnerUserId { get; init; }
    public required AuthenticatedSubject Owner { get; init; }
    public required string HostOrigin { get; init; }
    public required long TaskRevision { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
