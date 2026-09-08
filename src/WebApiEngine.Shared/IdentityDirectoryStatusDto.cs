namespace WebApiEngine.Shared;

/// <summary>
/// Datensparsame Betriebsansicht des externen Identitaetsverzeichnisses. Sie enthaelt weder
/// Subjects und Gruppen noch Provideradresse, Zugangstoken oder Client-Secret.
/// </summary>
public sealed class IdentityDirectoryStatusDto
{
    public required bool Enabled { get; set; }
    public required string State { get; set; }
    public Guid? ActiveGenerationId { get; set; }
    public Guid? RunningGenerationId { get; set; }
    public DateTime? AttemptedAtUtc { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? SucceededAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public int UserCount { get; set; }
    public int GroupCount { get; set; }
    public int MembershipCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
