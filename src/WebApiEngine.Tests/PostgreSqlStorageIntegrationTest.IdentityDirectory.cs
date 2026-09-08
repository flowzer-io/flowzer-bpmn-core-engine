using FluentAssertions;
using PostgreSqlStorageSystem;
using StorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: PostgreSQL bewahrt lokale IDs zwischen erfolgreichen Verzeichnis-Snapshots,
    // deaktiviert fehlende Historie und verwirft Mitgliedschaften aus dem vorherigen Lauf.
    [Test]
    public async Task IdentityDirectoryStorage_ShouldKeepStableIdsAndDeactivateHistoricalEntries()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var first = CreateDirectorySnapshot("anna", "finance", "Finance");
        await StartDirectorySync(storage.IdentityDirectoryStorage, first);
        await storage.IdentityDirectoryStorage.PublishSnapshot(first);
        var firstPublished = (await storage.IdentityDirectoryStorage.GetActiveSnapshot())!;

        var second = CreateDirectorySnapshot("anna", "finance", "Accounting");
        await StartDirectorySync(storage.IdentityDirectoryStorage, second);
        await storage.IdentityDirectoryStorage.PublishSnapshot(second);

        var published = (await storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        published.Users.Single().Id.Should().Be(firstPublished.Users.Single().Id);
        published.Groups.Single().Id.Should().Be(firstPublished.Groups.Single().Id);
        published.Memberships.Should().ContainSingle();

        var empty = new DirectorySnapshot { GenerationId = Guid.NewGuid(), Issuer = first.Issuer, CompletedAtUtc = DateTime.UtcNow };
        await StartDirectorySync(storage.IdentityDirectoryStorage, empty);
        await storage.IdentityDirectoryStorage.PublishSnapshot(empty);
        var historical = (await storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        historical.Users.Should().ContainSingle().Which.IsActive.Should().BeFalse();
        historical.Groups.Should().ContainSingle().Which.IsActive.Should().BeFalse();
        historical.Memberships.Should().BeEmpty();
    }

    // Testzweck: Der PostgreSQL-Statuswechsel ist transaktional: ohne Commit bleibt der aktive
    // Snapshot unveraendert, nach Commit ist der neue Status auch aus einer neuen Storage-Sicht lesbar.
    [Test]
    public async Task IdentityDirectoryStorage_ShouldRollbackUncommittedPublicationAndPersistCommittedStatus()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var reader = new PostgreSqlStorage(_dataSource!, Schema);
        var snapshot = CreateDirectorySnapshot("anna", "finance", "Finance");

        using (var writer = provider.GetTransactionalStorage())
        {
            await StartDirectorySync(writer.IdentityDirectoryStorage, snapshot);
            await writer.IdentityDirectoryStorage.PublishSnapshot(snapshot);
        }
        (await reader.IdentityDirectoryStorage.GetActiveSnapshot()).Should().BeNull();

        using (var writer = provider.GetTransactionalStorage())
        {
            await StartDirectorySync(writer.IdentityDirectoryStorage, snapshot);
            await writer.IdentityDirectoryStorage.PublishSnapshot(snapshot);
            writer.CommitChanges();
        }
        (await reader.IdentityDirectoryStorage.GetActiveSnapshot())!.GenerationId.Should().Be(snapshot.GenerationId);
        (await reader.IdentityDirectoryStorage.GetSyncStatus())!.State.Should().Be(DirectorySyncState.Succeeded);
    }

    // Testzweck: PostgreSQL verknuepft Fehlerstatus und Publikation mit der laufenden
    // Generation; ein verspaeteter Prozess kann einen neueren Lauf nicht abbrechen.
    [Test]
    public async Task IdentityDirectoryStorage_ShouldRejectStaleFailureForNewerGeneration()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var snapshot = CreateDirectorySnapshot("anna", "finance", "Finance");
        var oldGeneration = Guid.NewGuid();
        var oldStart = snapshot.CompletedAtUtc.AddMinutes(-10);
        (await storage.IdentityDirectoryStorage.TryStartSync(
            snapshot.Issuer, oldGeneration, oldStart, oldStart.AddMinutes(1))).Should().BeTrue();
        (await storage.IdentityDirectoryStorage.TryStartSync(
            snapshot.Issuer, snapshot.GenerationId, snapshot.CompletedAtUtc, snapshot.CompletedAtUtc.AddMinutes(5))).Should().BeTrue();

        (await storage.IdentityDirectoryStorage.FailSync(
            snapshot.Issuer, oldGeneration, "transient", "old run", snapshot.CompletedAtUtc)).Should().BeFalse();
        await storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);

        (await storage.IdentityDirectoryStorage.GetActiveSnapshot())!.GenerationId.Should().Be(snapshot.GenerationId);
        (await storage.IdentityDirectoryStorage.GetSyncStatus())!.State.Should().Be(DirectorySyncState.Succeeded);
    }

    // Testzweck: Zwei API-Prozesse, die denselben PostgreSQL-Stand gleichzeitig reservieren,
    // erhalten durch Row-Lock und Status-CAS genau eine aktive Synchronisations-Lease.
    [Test]
    public async Task IdentityDirectoryStorage_ShouldGrantOnlyOneConcurrentLease()
    {
        using var firstStorage = new PostgreSqlStorage(_dataSource!, Schema);
        using var secondStorage = new PostgreSqlStorage(_dataSource!, Schema);
        var startedAt = DateTime.UtcNow;

        var attempts = await Task.WhenAll(
            firstStorage.IdentityDirectoryStorage.TryStartSync(
                "https://issuer.example/realms/flowzer", Guid.NewGuid(), startedAt, startedAt.AddMinutes(5)),
            secondStorage.IdentityDirectoryStorage.TryStartSync(
                "https://issuer.example/realms/flowzer", Guid.NewGuid(), startedAt, startedAt.AddMinutes(5)));

        attempts.Should().ContainSingle(started => started);
        attempts.Should().ContainSingle(started => !started);
    }

    private static DirectorySnapshot CreateDirectorySnapshot(string subject, string externalGroupId, string groupName)
    {
        var user = new DirectoryUser
        {
            Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
            Issuer = "https://keycloak.example/realms/flowzer", Subject = subject, DisplayName = "Anna Example", IsActive = true
        };
        var group = new DirectoryGroup
        {
            Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
            Issuer = user.Issuer, ExternalId = externalGroupId, Name = groupName, Path = "/finance", IsActive = true
        };
        return new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(), Issuer = user.Issuer, CompletedAtUtc = DateTime.UtcNow,
            Users = [user], Groups = [group], Memberships = [new DirectoryMembership { UserId = user.Id, GroupId = group.Id }]
        };
    }

    private static async Task StartDirectorySync(IIdentityDirectoryStorage storage, DirectorySnapshot snapshot)
    {
        var started = await storage.TryStartSync(
            snapshot.Issuer,
            snapshot.GenerationId,
            snapshot.CompletedAtUtc,
            snapshot.CompletedAtUtc.AddMinutes(5));
        started.Should().BeTrue();
    }
}
