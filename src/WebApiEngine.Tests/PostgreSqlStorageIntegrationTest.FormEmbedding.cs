using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using Microsoft.Extensions.Options;
using Npgsql;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.FormEmbedding;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Zwei unabhängige PostgreSQL-Sessions liefern den Einstieg genau einmal;
    // Verbrauch und Ablauf überleben neue Sessions, Aufgabenlöschung räumt Freigaben mit auf.
    [Test]
    public async Task FormEmbedGrant_ShouldRedeemOnceAcrossSessionsAndCascade()
    {
        using var first = new PostgreSqlStorage(_dataSource!, Schema);
        using var second = new PostgreSqlStorage(_dataSource!, Schema);
        var task = await AddDraftUserTaskAsync(first);
        var now = DateTimeOffset.UtcNow;
        var grant = Grant(task.Id, new string('A', 64), now.AddMinutes(5));
        await first.FormEmbedGrantStorage.Create(grant, now);
        (await first.FormEmbedGrantStorage.Find(grant.SecretHash, now)).Should().NotBeNull();
        (await second.FormEmbedGrantStorage.Find(grant.SecretHash, now)).Should().NotBeNull();
        var winners = await Task.WhenAll(first.FormEmbedGrantStorage.TryTake(grant.SecretHash, now),
            second.FormEmbedGrantStorage.TryTake(grant.SecretHash, now));
        winners.Should().ContainSingle(item => item != null);
        (await second.FormEmbedGrantStorage.TryTake(grant.SecretHash, now)).Should().BeNull();
        (await second.FormEmbedGrantStorage.Find(grant.SecretHash, now)).Should().BeNull();
        var expired = Grant(task.Id, new string('B', 64), now);
        await first.FormEmbedGrantStorage.Create(expired, now.AddMinutes(-1));
        (await second.FormEmbedGrantStorage.TryTake(expired.SecretHash, now)).Should().BeNull();
        var cancelled = Grant(task.Id, new string('C', 64), now.AddMinutes(5));
        await first.FormEmbedGrantStorage.Create(cancelled, now);
        await first.SubscriptionStorage.RemoveUserTaskSubscription(task.Id);
        (await second.FormEmbedGrantStorage.TryTake(cancelled.SecretHash, now)).Should().BeNull();
    }

    // Testzweck: Ein Rollback gibt einen verbrauchten Einstieg nicht dauerhaft frei;
    // nur der Commit der vollständigen Lese-/Rechteprüfung beendet die Einlösung atomar.
    [Test]
    public async Task FormEmbedGrant_ShouldParticipateInTransaction()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var task = await AddDraftUserTaskAsync(storage);
        var now = DateTimeOffset.UtcNow;
        var grant = Grant(task.Id, new string('D', 64), now.AddMinutes(5));
        await storage.FormEmbedGrantStorage.Create(grant, now);
        using (var transaction = new PostgreSqlTransactionalStorage(_dataSource!, Schema))
        {
            (await transaction.FormEmbedGrantStorage.TryTake(grant.SecretHash, now)).Should().NotBeNull();
            transaction.RollbackTransaction();
        }
        (await storage.FormEmbedGrantStorage.TryTake(grant.SecretHash, now)).Should().NotBeNull();
    }

    // Testzweck: Der echte Service darf bei parallelem Aufgabenabschluss nicht erst die
    // Grant-Zeile und dann die Aufgabe sperren. PostgreSQL würde sonst den Cascade-Abschluss
    // als Deadlock abbrechen. Die wartende Einlösung erhält nach Abschluss keinen Snapshot.
    [Test]
    public async Task FormEmbedService_ShouldLockTaskBeforeGrantAcrossSessions()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var task = await AddDraftUserTaskAsync(storage);
        const string secret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(secret)));
        await storage.FormEmbedGrantStorage.Create(Grant(task.Id, hash, DateTimeOffset.UtcNow.AddMinutes(5)), DateTimeOffset.UtcNow);
        var links = new FormEmbedLinkService(
            new BpmnBusinessLogic(new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema)), storage,
            new AnonymousEmbedActor(), Options.Create(new FormEmbeddingOptions
            {
                Enabled = true, PublicOrigin = "https://flowzer.test", AllowedHostOrigins = ["https://host.test"]
            }), TimeProvider.System);
        using var completing = new PostgreSqlTransactionalStorage(_dataSource!, Schema);
        (await completing.UserTaskLifecycleStorage.LockTask(task.Id)).Should().BeTrue();
        var redemption = links.RedeemAsync(secret);
        try
        {
            // Ein DB-beobachtetes Wait statt einer geratenen Pause beweist die Konkurrenz.
            await using var observer = await _dataSource!.OpenConnectionAsync();
            var waiting = false;
            for (var attempt = 0; attempt < 200 && !waiting; attempt++)
            {
                await using var probe = new NpgsqlCommand("""
                    SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
                    AND wait_event_type = 'Lock' AND query LIKE '%user_task_subscriptions%')
                    """, observer);
                waiting = (bool)(await probe.ExecuteScalarAsync())!;
                if (!waiting) await Task.Delay(25);
            }
            waiting.Should().BeTrue("redemption must be blocked on the task, not consume its grant first");
            await completing.SubscriptionStorage.RemoveUserTaskSubscription(task.Id);
            completing.CommitChanges();
        }
        finally { completing.RollbackTransaction(); }
        (await redemption).Should().BeNull();
    }

    // Testzweck: Neue Einsteige sind je Aufgabe begrenzt und abgelaufene Personenbindungen
    // werden unabhängig von neuen Ausgaben bereinigt; andere aktive Aufgaben bleiben bestehen.
    [Test]
    public async Task FormEmbedGrant_ShouldBoundOutstandingLinksAndCleanIndependently()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var task = await AddDraftUserTaskAsync(storage);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 6; i++)
            await storage.FormEmbedGrantStorage.Create(Grant(task.Id, new string((char)('A' + i), 64), now.AddMinutes(i + 1)), now);
        (await storage.FormEmbedGrantStorage.Find(new string('A', 64), now)).Should().BeNull();
        (await storage.FormEmbedGrantStorage.Find(new string('F', 64), now)).Should().NotBeNull();
        await storage.FormEmbedGrantStorage.CleanupExpired(now.AddMinutes(10));
        (await storage.FormEmbedGrantStorage.Find(new string('F', 64), now)).Should().BeNull();
    }

    private sealed class AnonymousEmbedActor : ICurrentUserContextAccessor
    {
        public CurrentUserContext GetCurrentUser() => throw new InvalidOperationException("Anonymous redemption must use the stored binding.");
    }

    private static FormEmbedGrant Grant(Guid taskId, string hash, DateTimeOffset expiry) => new()
    {
        SecretHash = hash, UserTaskId = taskId, DefinitionId = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(),
        Owner = new AuthenticatedSubject("https://synthetic.test", "synthetic-subject"),
        HostOrigin = "https://host.test", TaskRevision = 0, ExpiresAtUtc = expiry
    };
}
