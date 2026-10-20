using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PostgreSqlStorageSystem;
using StorageSystem;
using WebApiEngine.IdentityDirectory;

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

    // Testzweck: Ein echter PostgreSQL-Stand kann vom realmweiten auf den Gruppen-Scope
    // wechseln, bewahrt historische IDs und entzieht bei Mitgliedschaftsverlust auch aus
    // einer neuen Session alle aktuellen Mitgliedschaften ohne falsche Löschung der Historie.
    [Test]
    public async Task IdentityDirectoryScope_ShouldNarrowAndRevokeWithoutLosingHistoricIds()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var initial = CreateDirectorySnapshot("anna", "finance", "Finance");
        var outside = new DirectoryUser { Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
            Issuer = initial.Issuer, Subject = "outside-person", DisplayName = "Other", IsActive = true };
        var outsideGroup = new DirectoryGroup { Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
            Issuer = initial.Issuer, ExternalId = "outside-group", Name = "Other", Path = "/Other", IsActive = true };
        initial.Users.Add(outside); initial.Groups.Add(outsideGroup);
        initial.Memberships.Add(new DirectoryMembership { UserId = outside.Id, GroupId = outsideGroup.Id });
        await StartDirectorySync(storage.IdentityDirectoryStorage, initial);
        await storage.IdentityDirectoryStorage.PublishSnapshot(initial);
        var previous = (await storage.IdentityDirectoryStorage.GetActiveSnapshot())!;

        var options = KeycloakDirectoryScopeTest.Options(); options.RootGroupId = "finance"; options.Issuer = initial.Issuer;
        var root = new KeycloakDirectoryGroup("finance", "Finance", "/finance", null);
        var client = new ScopeSourceClient(new KeycloakDirectorySnapshot(
            [new KeycloakDirectoryUser("anna", true, "anna", null, null, ["finance"])], [root]));
        var synchronizer = new IdentityDirectorySynchronizer(client, storage.IdentityDirectoryStorage, Options.Create(options),
            TimeProvider.System, NullLogger<IdentityDirectorySynchronizer>.Instance);
        (await synchronizer.SynchronizeAsync(CancellationToken.None)).Should().Be(IdentityDirectorySynchronizer.SynchronizationOutcome.Succeeded);

        using var reader = new PostgreSqlStorage(_dataSource!, Schema);
        var scoped = (await reader.IdentityDirectoryStorage.GetActiveSnapshot())!;
        scoped.Users.Single(user => user.Subject == "anna").Id.Should().Be(previous.Users.Single(user => user.Subject == "anna").Id);
        scoped.Groups.Single(group => group.ExternalId == "finance").Id.Should().Be(previous.Groups.Single(group => group.ExternalId == "finance").Id);
        scoped.Users.Single(user => user.Subject == "outside-person").IsActive.Should().BeFalse();
        scoped.Groups.Single(group => group.ExternalId == "outside-group").IsActive.Should().BeFalse();
        scoped.Memberships.Should().ContainSingle();

        client.Source = new KeycloakDirectorySnapshot([], [root]);
        (await synchronizer.SynchronizeAsync(CancellationToken.None)).Should().Be(IdentityDirectorySynchronizer.SynchronizationOutcome.Succeeded);
        var revoked = (await reader.IdentityDirectoryStorage.GetActiveSnapshot())!;
        revoked.Users.Should().OnlyContain(user => !user.IsActive);
        revoked.Users.Single(user => user.Subject == "anna").Id.Should().Be(scoped.Users.Single(user => user.Subject == "anna").Id);
        revoked.Memberships.Should().BeEmpty();
    }

    private sealed class ScopeSourceClient(KeycloakDirectorySnapshot source) : IKeycloakAdminClient
    {
        internal KeycloakDirectorySnapshot Source { get; set; } = source;
        public Task<KeycloakDirectorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(Source);
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
