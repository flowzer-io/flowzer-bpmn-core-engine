using StorageSystem;
using System.Security.Cryptography;
using WebApiEngine.Idempotency;

namespace WebApiEngine.BusinessLogic;

/// <summary>Gemeinsame Reservierungs- und Replay-Logik innerhalb der Fachtransaktion.</summary>
internal static class IdempotencyExecution
{
    internal static async Task<IdempotencyAcquisition> Acquire(IStorageSystem storage, IdempotencyRequest? request)
    {
        if (request is null) return IdempotencyAcquisition.None;
        var now = DateTime.UtcNow;
        await storage.IdempotencyStorage.DeleteExpired(now);
        var existing = await storage.IdempotencyStorage.Get(request.ScopeHash);
        if (existing is not null) return Existing(existing, request);

        var record = new IdempotencyRecord
        {
            ScopeHash = request.ScopeHash, RequestHash = request.RequestHash, Operation = request.Operation,
            CreatedAt = now, ExpiresAt = now.Add(HttpIdempotency.Retention)
        };
        if (await storage.IdempotencyStorage.TryCreate(record)) return new(record, true, false);
        existing = await storage.IdempotencyStorage.Get(request.ScopeHash)
            ?? throw new IdempotencyConflictException("The idempotent request is currently being established.");
        return Existing(existing, request);
    }

    internal static async Task Abandon(IStorageSystem storage, IdempotencyAcquisition acquisition)
    {
        if (acquisition.IsOwned) await storage.IdempotencyStorage.Remove(acquisition.Record!.ScopeHash);
    }

    private static IdempotencyAcquisition Existing(IdempotencyRecord existing, IdempotencyRequest request)
    {
        if (existing.Operation != request.Operation)
            throw new IdempotencyConflictException("The Idempotency-Key belongs to another operation.");
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(existing.RequestHash),
                System.Text.Encoding.ASCII.GetBytes(request.RequestHash)))
            throw new IdempotencyConflictException("The Idempotency-Key was already used with different request content.");
        if (!existing.IsCompleted)
            throw new IdempotencyConflictException("The idempotent request is still in progress.");
        return new(existing, false, true);
    }
}

internal sealed record IdempotencyAcquisition(IdempotencyRecord? Record, bool IsOwned, bool IsReplay)
{
    internal static IdempotencyAcquisition None { get; } = new(null, false, false);
}
