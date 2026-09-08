using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

[NonParallelizable]
public sealed class IdentityDirectoryStorageTest
{
    // Testzweck: Ein erfolgreicher Folgesnapshot bewahrt lokale Benutzer- und Gruppenkennungen,
    // deaktiviert fehlende historische Identitaeten und ersetzt Mitgliedschaften vollstaendig.
    [Test]
    public async Task PublishSnapshot_ShouldKeepStableIdsDeactivateMissingEntriesAndReplaceMemberships()
    {
        using var context = new IdentityDirectoryStorageTestContext();
        var first = CreateSnapshot("anna", "finance", "Finance", "/finance");
        await StartSync(context.Storage.IdentityDirectoryStorage, first);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(first);

        var initiallyPublished = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        var initialAnnaId = initiallyPublished.Users.Single().Id;
        var initialFinanceId = initiallyPublished.Groups.Single().Id;

        var second = CreateSnapshot("anna", "finance", "Accounting", "/finance");
        await StartSync(context.Storage.IdentityDirectoryStorage, second);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(second);

        var published = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        published.GenerationId.Should().Be(second.GenerationId);
        published.Users.Should().ContainSingle().Which.Id.Should().Be(initialAnnaId);
        published.Groups.Should().ContainSingle().Which.Id.Should().Be(initialFinanceId);
        published.Groups.Single().Name.Should().Be("Accounting");
        published.Memberships.Should().ContainSingle();
        published.Memberships.Single().UserId.Should().Be(initialAnnaId);
        published.Memberships.Single().GroupId.Should().Be(initialFinanceId);

        var withoutEntries = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(),
            Issuer = first.Issuer,
            CompletedAtUtc = first.CompletedAtUtc.AddMinutes(2)
        };
        await StartSync(context.Storage.IdentityDirectoryStorage, withoutEntries);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(withoutEntries);

        var withHistory = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        withHistory.Users.Should().ContainSingle().Which.IsActive.Should().BeFalse();
        withHistory.Groups.Should().ContainSingle().Which.IsActive.Should().BeFalse();
        withHistory.Memberships.Should().BeEmpty();
        var status = (await context.Storage.IdentityDirectoryStorage.GetSyncStatus())!;
        status.State.Should().Be(DirectorySyncState.Succeeded);
        status.ActiveGenerationId.Should().Be(withoutEntries.GenerationId);
        status.UserCount.Should().Be(0);
        status.GroupCount.Should().Be(0);
        status.MembershipCount.Should().Be(0);
    }

    // Testzweck: Gruppenhierarchien werden über temporäre Importkennungen auf persistente
    // lokale IDs abgebildet und behalten diese Elternbeziehung auch im Folgesnapshot.
    [Test]
    public async Task PublishSnapshot_ShouldResolveAndKeepStableGroupParents()
    {
        using var context = new IdentityDirectoryStorageTestContext();
        var first = CreateHierarchicalSnapshot();
        await StartSync(context.Storage.IdentityDirectoryStorage, first);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(first);
        var firstPublished = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        var firstRoot = firstPublished.Groups.Single(group => group.ExternalId == "root");
        var firstChild = firstPublished.Groups.Single(group => group.ExternalId == "child");
        firstChild.ParentId.Should().Be(firstRoot.Id);

        var second = CreateHierarchicalSnapshot();
        await StartSync(context.Storage.IdentityDirectoryStorage, second);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(second);
        var secondPublished = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        var secondRoot = secondPublished.Groups.Single(group => group.ExternalId == "root");
        var secondChild = secondPublished.Groups.Single(group => group.ExternalId == "child");
        secondRoot.Id.Should().Be(firstRoot.Id);
        secondChild.Id.Should().Be(firstChild.Id);
        secondChild.ParentId.Should().Be(secondRoot.Id);
    }

    // Testzweck: Ein fehlgeschlagener Lauf veraendert weder die aktive Generation noch deren
    // Identitaeten; der Status enthaelt nur begrenzte, zeilenbereinigte Fehlerinformationen.
    [Test]
    public async Task FailSync_ShouldKeepActiveSnapshotAndSanitizeFailure()
    {
        using var context = new IdentityDirectoryStorageTestContext();
        var snapshot = CreateSnapshot("anna", "finance", "Finance", "/finance");
        await StartSync(context.Storage.IdentityDirectoryStorage, snapshot);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);

        var failedGeneration = Guid.NewGuid();
        var failedAt = snapshot.CompletedAtUtc.AddMinutes(1);
        var failedStartedAt = failedAt.AddSeconds(-30);
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            snapshot.Issuer, failedGeneration, failedStartedAt, failedAt.AddMinutes(5))).Should().BeTrue();
        (await context.Storage.IdentityDirectoryStorage.FailSync(
            snapshot.Issuer, failedGeneration, " upstream\r\n failure ",
            "Authorization: Bearer secret-value\n" + new string('x', 600), failedAt)).Should().BeTrue();

        (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!.GenerationId.Should().Be(snapshot.GenerationId);
        var status = (await context.Storage.IdentityDirectoryStorage.GetSyncStatus())!;
        status.State.Should().Be(DirectorySyncState.Failed);
        status.ActiveGenerationId.Should().Be(snapshot.GenerationId);
        status.AttemptedAtUtc.Should().Be(failedStartedAt);
        status.FailedAtUtc.Should().Be(failedAt);
        status.ErrorCode.Should().Be("upstream failure");
        status.ErrorMessage.Should().NotContain("\r").And.NotContain("\n");
        status.ErrorMessage!.Length.Should().BeLessThanOrEqualTo(512);
        status.ErrorMessage.Should().NotContain("secret-value");
    }

    // Testzweck: Ein veralteter Fehlerstatus darf einen bereits erfolgreichen Snapshot nicht
    // nachtraeglich als fehlgeschlagen markieren, wenn kein Lauf mehr aktiv ist.
    [Test]
    public async Task FailSync_ShouldRejectFailureWithoutRunningGeneration()
    {
        using var context = new IdentityDirectoryStorageTestContext();
        var snapshot = CreateSnapshot("anna", "finance", "Finance", "/finance");
        await StartSync(context.Storage.IdentityDirectoryStorage, snapshot);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);

        var failed = await context.Storage.IdentityDirectoryStorage.FailSync(
            snapshot.Issuer, snapshot.GenerationId, "transient", "old failure", DateTime.UtcNow);
        failed.Should().BeFalse();
        (await context.Storage.IdentityDirectoryStorage.GetSyncStatus())!.State.Should().Be(DirectorySyncState.Succeeded);
    }

    // Testzweck: Bei einem Wechsel des konfigurierten Issuers deaktiviert der neue erfolgreiche
    // Snapshot die Identitaeten der bisherigen Quelle, statt sie weiterhin berechtigt zu lassen.
    [Test]
    public async Task PublishSnapshot_ShouldDeactivateHistoryWhenIssuerChanges()
    {
        using var context = new IdentityDirectoryStorageTestContext();
        var first = CreateSnapshot("anna", "finance", "Finance", "/finance");
        await StartSync(context.Storage.IdentityDirectoryStorage, first);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(first);

        var replacement = CreateSnapshot("bernd", "hr", "Human Resources", "/hr");
        replacement.Issuer = "https://keycloak.example/realms/replacement";
        replacement.Users.Single().Issuer = replacement.Issuer;
        replacement.Groups.Single().Issuer = replacement.Issuer;
        await StartSync(context.Storage.IdentityDirectoryStorage, replacement);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(replacement);

        var published = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        published.Users.Single(user => user.Subject == "anna").IsActive.Should().BeFalse();
        published.Groups.Single(group => group.ExternalId == "finance").IsActive.Should().BeFalse();
    }

    // Testzweck: Der atomar publizierte Dateidatensatz ueberlebt einen neuen Storage-Prozess;
    // ein bei Status Running verbliebener Folgelauf kann keinen alten Snapshot ueberschreiben.
    [Test]
    public async Task PublishSnapshot_ShouldSurviveRestartAndRejectOutdatedGeneration()
    {
        using var context = new IdentityDirectoryStorageTestContext();
        var first = CreateSnapshot("anna", "finance", "Finance", "/finance");
        await StartSync(context.Storage.IdentityDirectoryStorage, first);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(first);

        var newerGeneration = Guid.NewGuid();
        var newerStartedAt = first.CompletedAtUtc.AddMinutes(1);
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            first.Issuer, newerGeneration, newerStartedAt, newerStartedAt.AddMinutes(5))).Should().BeTrue();
        var oldPublish = () => context.Storage.IdentityDirectoryStorage.PublishSnapshot(first);
        await oldPublish.Should().ThrowAsync<InvalidOperationException>();

        var restarted = new Storage();
        (await restarted.IdentityDirectoryStorage.GetActiveSnapshot())!.GenerationId.Should().Be(first.GenerationId);
        (await restarted.IdentityDirectoryStorage.GetSyncStatus())!.State.Should().Be(DirectorySyncState.Running);
    }

    // Testzweck: Ein veralteter Prozess darf den Status einer neueren Generation weder als
    // fehlgeschlagen markieren noch deren anschliessende atomare Publikation verhindern.
    [Test]
    public async Task FailSync_ShouldCompareAndSwapTheRunningGeneration()
    {
        using var context = new IdentityDirectoryStorageTestContext();
        var oldGeneration = Guid.NewGuid();
        var newer = CreateSnapshot("anna", "finance", "Finance", "/finance");
        var oldStartedAt = newer.CompletedAtUtc.AddMinutes(-10);
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            newer.Issuer, oldGeneration, oldStartedAt, oldStartedAt.AddMinutes(1))).Should().BeTrue();
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            newer.Issuer, newer.GenerationId, newer.CompletedAtUtc, newer.CompletedAtUtc.AddMinutes(5))).Should().BeTrue();

        var staleFailure = await context.Storage.IdentityDirectoryStorage.FailSync(
            newer.Issuer, oldGeneration, "transient", "old run", newer.CompletedAtUtc);
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(newer);

        staleFailure.Should().BeFalse();
        (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!.GenerationId.Should().Be(newer.GenerationId);
        (await context.Storage.IdentityDirectoryStorage.GetSyncStatus())!.State.Should().Be(DirectorySyncState.Succeeded);
    }

    // Testzweck: Eine noch gueltige Lease verhindert einen zweiten Import, damit zwei API-
    // Prozesse nicht gleichzeitig dieselbe Verzeichnisgeneration ersetzen.
    [Test]
    public async Task TryStartSync_ShouldRejectASecondLiveLease()
    {
        using var context = new IdentityDirectoryStorageTestContext();
        var now = DateTime.UtcNow;
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            "https://keycloak.example/realms/flowzer", Guid.NewGuid(), now, now.AddMinutes(5))).Should().BeTrue();

        var second = await context.Storage.IdentityDirectoryStorage.TryStartSync(
            "https://keycloak.example/realms/flowzer", Guid.NewGuid(), now.AddSeconds(1), now.AddMinutes(6));

        second.Should().BeFalse();
    }

    // Testzweck: Zirkulaere Gruppenbeziehungen werden vor der Publikation abgewiesen; eine
    // Navigation durch Untergruppen darf spaeter nicht in einer Endlosschleife enden.
    [Test]
    public async Task PublishSnapshot_ShouldRejectCircularGroupHierarchy()
    {
        using var context = new IdentityDirectoryStorageTestContext();
        var snapshot = CreateHierarchicalSnapshot();
        var root = snapshot.Groups.Single(group => group.ExternalId == "root");
        var child = snapshot.Groups.Single(group => group.ExternalId == "child");
        root.ParentId = child.Id;
        await StartSync(context.Storage.IdentityDirectoryStorage, snapshot);

        var publish = () => context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);

        await publish.Should().ThrowAsync<ArgumentException>().WithMessage("*cycle*");
        (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot()).Should().BeNull();
    }

    private static DirectorySnapshot CreateHierarchicalSnapshot()
    {
        const string issuer = "https://keycloak.example/realms/flowzer";
        var root = new DirectoryGroup
        {
            Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
            Issuer = issuer, ExternalId = "root", Name = "Root", Path = "/root", IsActive = true
        };
        var child = new DirectoryGroup
        {
            Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
            Issuer = issuer, ExternalId = "child", Name = "Child", Path = "/root/child", ParentId = root.Id, IsActive = true
        };
        return new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(), Issuer = issuer, CompletedAtUtc = DateTime.UtcNow,
            Groups = [root, child]
        };
    }

    private static async Task StartSync(IIdentityDirectoryStorage storage, DirectorySnapshot snapshot)
    {
        var started = await storage.TryStartSync(
            snapshot.Issuer,
            snapshot.GenerationId,
            snapshot.CompletedAtUtc,
            snapshot.CompletedAtUtc.AddMinutes(5));
        started.Should().BeTrue();
    }

    private static DirectorySnapshot CreateSnapshot(string subject, string externalGroupId, string groupName, string groupPath)
    {
        var user = new DirectoryUser
        {
            Id = Guid.NewGuid(),
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = "https://keycloak.example/realms/flowzer",
            Subject = subject,
            DisplayName = "Anna Example",
            IsActive = true
        };
        var group = new DirectoryGroup
        {
            Id = Guid.NewGuid(),
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = user.Issuer,
            ExternalId = externalGroupId,
            Name = groupName,
            Path = groupPath,
            IsActive = true
        };
        return new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(),
            Issuer = user.Issuer,
            CompletedAtUtc = DateTime.UtcNow,
            Users = [user],
            Groups = [group],
            Memberships = [new DirectoryMembership { UserId = user.Id, GroupId = group.Id }]
        };
    }

    private sealed class IdentityDirectoryStorageTestContext : IDisposable
    {
        private readonly string? _previousStorageRoot;

        public IdentityDirectoryStorageTestContext()
        {
            Root = Path.Combine(Path.GetTempPath(), "flowzer-directory-storage", Guid.NewGuid().ToString("N"));
            _previousStorageRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, Root);
            Storage = new Storage();
        }

        public string Root { get; }
        public Storage Storage { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousStorageRoot);
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
