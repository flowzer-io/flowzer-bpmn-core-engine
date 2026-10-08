namespace Model;

/// <summary>
/// Persönlicher Rückzug am Master-Token: verifizierte Identität und Zeitpunkt,
/// keine Formulardaten. Nicht in gewöhnlichen öffentlichen Tokenprojektionen.
/// </summary>
public sealed record ProcessWithdrawal(AuthenticatedSubject Actor, Guid ActorUserId, DateTimeOffset WithdrawnAtUtc);
