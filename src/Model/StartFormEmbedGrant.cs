namespace Model;

/// <summary>
/// Persönlicher, kurzlebiger Startformular-Einstieg ohne Aufgabe oder Instanz.
/// Nur der Secret-Hash und stabile Bindungen werden persistiert, niemals Formulareingaben.
/// </summary>
public sealed class StartFormEmbedGrant
{
    public required string SecretHash { get; init; }
    public required string OwnerKey { get; init; }
    public required Guid OwnerUserId { get; init; }
    public required AuthenticatedSubject Owner { get; init; }
    public required string RelatedDefinitionId { get; init; }
    public required Guid DefinitionId { get; init; }
    public required string HostOrigin { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
