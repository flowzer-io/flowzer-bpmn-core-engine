using FluentAssertions;
using Model;
using Npgsql;
using PostgreSqlStorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Ein Startgrant benötigt weder Instanz noch Aufgabe. Einlösung und
    // Rollback funktionieren zwischen unabhängigen PostgreSQL-Sessions genau einmal.
    [Test]
    public async Task StartFormGrant_ShouldConsumeOnceAndHonorRollbackAcrossSessions()
    {
        using var first = new PostgreSqlStorage(_dataSource!, Schema);
        using var second = new PostgreSqlStorage(_dataSource!, Schema);
        var now = DateTimeOffset.UtcNow;
        var grant = StartGrant(Guid.NewGuid(), new string('A', 64), now.AddMinutes(5));
        await first.StartFormEmbedGrantStorage.Create(grant, now);
        (await second.StartFormEmbedGrantStorage.Find(grant.SecretHash, now)).Should().NotBeNull();
        using (var transaction = new PostgreSqlTransactionalStorage(_dataSource!, Schema))
        {
            (await transaction.StartFormEmbedGrantStorage.TryTake(grant.SecretHash, now)).Should().NotBeNull();
            transaction.RollbackTransaction();
        }
        var results = await Task.WhenAll(first.StartFormEmbedGrantStorage.TryTake(grant.SecretHash, now),
            second.StartFormEmbedGrantStorage.TryTake(grant.SecretHash, now));
        results.Should().ContainSingle(g => g != null);
        (await second.StartFormEmbedGrantStorage.TryTake(grant.SecretHash, now)).Should().BeNull();
        (await first.InstanceStorage.GetAllInstances()).Should().BeEmpty();
        (await first.SubscriptionStorage.GetAllUserTasksExtended(Guid.Empty)).Should().BeEmpty();
    }

    // Testzweck: Gleichzeitige Ausgaben über getrennte Sessions halten atomar die
    // Vierer-Obergrenze je Person/Version. Andere Personen und Versionen bleiben unberührt.
    [Test]
    public async Task StartFormGrant_ShouldBoundConcurrentIssuancePerOwnerAndVersion()
    {
        var now = DateTimeOffset.UtcNow;
        var version = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            using var storage = new PostgreSqlStorage(_dataSource!, Schema);
            await storage.StartFormEmbedGrantStorage.Create(StartGrant(version, i.ToString("x64"), now.AddMinutes(5)), now);
        }));
        using var other = new PostgreSqlStorage(_dataSource!, Schema);
        await other.StartFormEmbedGrantStorage.Create(StartGrant(Guid.NewGuid(), new string('B', 64), now.AddMinutes(5)), now);
        await other.StartFormEmbedGrantStorage.Create(StartGrant(version, new string('C', 64), now.AddMinutes(5), new string('D', 64)), now);
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var count = new NpgsqlCommand($"SELECT count(*) FROM {Schema}.start_form_embed_grants WHERE owner_key = @owner AND definition_id = @version", connection);
        count.Parameters.AddWithValue("owner", new string('A', 64)); count.Parameters.AddWithValue("version", version);
        Convert.ToInt64(await count.ExecuteScalarAsync()).Should().Be(4);
        (await other.StartFormEmbedGrantStorage.Find(new string('B', 64), now)).Should().NotBeNull();
        (await other.StartFormEmbedGrantStorage.Find(new string('C', 64), now)).Should().NotBeNull();
        await other.StartFormEmbedGrantStorage.CleanupExpired(now.AddMinutes(5));
        Convert.ToInt64(await count.ExecuteScalarAsync()).Should().Be(0);
    }

    private static StartFormEmbedGrant StartGrant(Guid version, string hash, DateTimeOffset expires, string? ownerKey = null) => new()
    {
        SecretHash = hash, OwnerKey = ownerKey ?? new string('A', 64), OwnerUserId = Guid.NewGuid(),
        Owner = new AuthenticatedSubject("https://synthetic.test", "subject"), DefinitionId = version,
        RelatedDefinitionId = "synthetic-workflow", HostOrigin = "https://host.test", ExpiresAtUtc = expires
    };
}
