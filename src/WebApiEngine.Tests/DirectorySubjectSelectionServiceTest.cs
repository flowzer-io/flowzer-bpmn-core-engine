using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Tests;

[TestFixture]
public sealed class DirectorySubjectSelectionServiceTest
{
    // Testzweck: Gleichnamige Identitäten bleiben durch typisierte stabile IDs und eindeutige
    // Zusatzinformationen unterscheidbar; inaktive Einträge fehlen in der Standardsuche.
    [Test]
    public async Task SearchAsync_ShouldReturnDistinctActiveSubjectsInStableOrder()
    {
        var snapshot = CreateSnapshot();
        var service = new DirectorySubjectSelectionService(new SnapshotStorage(snapshot));

        var result = await service.SearchAsync(
            "muster",
            DirectorySubjectSearchKind.All,
            10,
            DirectorySubjectSelectionPolicy.WorkflowModeling);
        var groups = await service.SearchAsync(
            "team",
            DirectorySubjectSearchKind.Group,
            10,
            DirectorySubjectSelectionPolicy.WorkflowModeling);
        var limited = await service.SearchAsync(
            "muster",
            DirectorySubjectSearchKind.User,
            1,
            DirectorySubjectSelectionPolicy.WorkflowModeling);

        result.Should().NotBeNull();
        result!.GenerationId.Should().Be(snapshot.GenerationId);
        result.Items.Should().HaveCount(2);
        result.Items.Select(item => item.Subject.Kind).Should().Equal(
            DirectorySubjectKind.User,
            DirectorySubjectKind.User);
        result.Items.Select(item => item.Subject.Id).Should().OnlyHaveUniqueItems();
        result.Items.Select(item => item.Detail).Should().Equal("subject-anna-a", "subject-anna-b");
        result.Items.Should().NotContain(item => item.Detail == "subject-inactive");
        groups!.Items.Select(item => item.Detail).Should().Equal(
            "/Organisation/Team",
            "/Organisation/Team/Untergruppe");
        groups.Items.Select(item => item.Subject.Id).Should().OnlyHaveUniqueItems();
        groups.Items.Should().NotContain(item => item.Detail == "/Archiv/Team");
        limited!.Items.Should().ContainSingle().Which.Detail.Should().Be("subject-anna-a");
    }

    // Testzweck: Auflösung lehnt unbekannte, inaktive und mit falscher Art bezeichnete IDs ab,
    // statt über Anzeigenamen oder einen anderen Identitätstyp zurückzufallen.
    [Test]
    public async Task ResolveAsync_ShouldRejectUnknownInactiveAndWrongKindReferences()
    {
        var snapshot = CreateSnapshot();
        var service = new DirectorySubjectSelectionService(new SnapshotStorage(snapshot));
        var activeUser = snapshot.Users.Single(user => user.Subject == "subject-anna-a");
        var inactiveUser = snapshot.Users.Single(user => user.Subject == "subject-inactive");
        var group = snapshot.Groups.Single(entry => entry.ExternalId == "group-root");

        (await service.ResolveAsync(
            new SubjectRef(DirectorySubjectKind.User, activeUser.Id),
            DirectorySubjectSelectionPolicy.WorkflowModeling)).Should().NotBeNull();
        (await service.ResolveAsync(
            new SubjectRef(DirectorySubjectKind.User, inactiveUser.Id),
            DirectorySubjectSelectionPolicy.WorkflowModeling)).Should().BeNull();
        (await service.ResolveAsync(
            new SubjectRef(DirectorySubjectKind.User, group.Id),
            DirectorySubjectSelectionPolicy.WorkflowModeling)).Should().BeNull();
        (await service.ResolveAsync(
            new SubjectRef(DirectorySubjectKind.Group, Guid.NewGuid()),
            DirectorySubjectSelectionPolicy.WorkflowModeling)).Should().BeNull();
    }

    // Testzweck: Die reine Anzeigeauflösung erhält deaktivierte Identitäten mit
    // stabilem Namen, markiert sie aber ausdrücklich als nicht erneut auswählbar.
    [Test]
    public async Task ResolveForDisplayAsync_ShouldPreserveInactiveSubjectsWithoutMakingThemSelectable()
    {
        var snapshot = CreateSnapshot();
        var service = new DirectorySubjectSelectionService(new SnapshotStorage(snapshot));
        var activeUser = snapshot.Users.Single(user => user.Subject == "subject-anna-a");
        var inactiveUser = snapshot.Users.Single(user => user.Subject == "subject-inactive");

        var result = await service.ResolveForDisplayAsync(
        [
            new SubjectRef(DirectorySubjectKind.User, inactiveUser.Id),
            new SubjectRef(DirectorySubjectKind.User, activeUser.Id),
            new SubjectRef(DirectorySubjectKind.Group, Guid.NewGuid())
        ], DirectorySubjectSelectionPolicy.WorkflowModeling,
        new HashSet<SubjectRef>
        {
            new(DirectorySubjectKind.User, inactiveUser.Id),
            new(DirectorySubjectKind.User, activeUser.Id)
        });

        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
        result.Items.Should().ContainSingle(item => item.Subject.Id == inactiveUser.Id)
            .Which.Should().Match<DirectorySubjectResult>(item => !item.IsActive && !item.IsSelectable);
        result.Items.Should().ContainSingle(item => item.Subject.Id == activeUser.Id)
            .Which.Should().Match<DirectorySubjectResult>(item => item.IsActive && item.IsSelectable);
    }

    // Testzweck: Auch eine existierende stabile UUID gibt ohne Bindung an den
    // fachlichen Kontext keinerlei Directory-Projektion preis.
    [Test]
    public async Task ResolveForDisplayAsync_ShouldIgnoreExistingButUnboundSubject()
    {
        var snapshot = CreateSnapshot();
        var service = new DirectorySubjectSelectionService(new SnapshotStorage(snapshot));
        var known = snapshot.Users.Single(user => user.Subject == "subject-inactive");

        var result = await service.ResolveForDisplayAsync(
            [new SubjectRef(DirectorySubjectKind.User, known.Id)],
            DirectorySubjectSelectionPolicy.WorkflowModeling,
            new HashSet<SubjectRef>());

        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
    }

    // Testzweck: Der Modeler muss bereits gespeicherte aktive Referenzen nach einem erneuten
    // Öffnen eindeutig darstellen können, ohne dafür ein ungeschütztes Vollverzeichnis zu laden.
    [Test]
    public async Task SearchAsync_ShouldResolveAnActiveSubjectByItsStableId()
    {
        var snapshot = CreateSnapshot();
        var service = new DirectorySubjectSelectionService(new SnapshotStorage(snapshot));
        var activeUser = snapshot.Users.Single(user => user.Subject == "subject-anna-a");

        var result = await service.SearchAsync(
            activeUser.Id.ToString(),
            DirectorySubjectSearchKind.User,
            10,
            DirectorySubjectSelectionPolicy.WorkflowModeling);

        result!.Items.Should().ContainSingle().Which.Subject.Should().Be(
            new SubjectRef(DirectorySubjectKind.User, activeUser.Id));
    }

    // Testzweck: Eine Gruppenbeschränkung wird serverseitig ausgewertet; Untergruppen erweitern
    // die erlaubten Benutzer nur nach ausdrücklicher Freigabe und nie durch Browserparameter.
    [Test]
    public async Task SearchAsync_ShouldApplyMemberGroupRestrictionAndExplicitDescendants()
    {
        var snapshot = CreateSnapshot();
        var service = new DirectorySubjectSelectionService(new SnapshotStorage(snapshot));
        var rootGroup = snapshot.Groups.Single(group => group.ExternalId == "group-root");
        var directOnly = new DirectorySubjectSelectionPolicy
        {
            AllowUsers = true,
            AllowGroups = false,
            UserMemberOfGroupIds = new HashSet<Guid> { rootGroup.Id },
            IncludeSubgroups = false
        };
        var withDescendants = new DirectorySubjectSelectionPolicy
        {
            AllowUsers = true,
            AllowGroups = false,
            UserMemberOfGroupIds = new HashSet<Guid> { rootGroup.Id },
            IncludeSubgroups = true
        };

        var withoutChildMembers = await service.SearchAsync("muster", DirectorySubjectSearchKind.User, 10, directOnly);
        var includingChildMembers = await service.SearchAsync("muster", DirectorySubjectSearchKind.User, 10, withDescendants);

        withoutChildMembers!.Items.Should().BeEmpty();
        includingChildMembers!.Items.Select(item => item.Detail).Should().Equal("subject-anna-a", "subject-anna-b");
    }

    private static DirectorySnapshot CreateSnapshot()
    {
        const string issuer = "https://issuer.example/realms/flowzer";
        var root = new DirectoryGroup
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = issuer,
            ExternalId = "group-root",
            Name = "Team",
            Path = "/Organisation/Team",
            IsActive = true
        };
        var child = new DirectoryGroup
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = issuer,
            ExternalId = "group-child",
            Name = "Team",
            Path = "/Organisation/Team/Untergruppe",
            ParentId = root.Id,
            IsActive = true
        };
        var inactiveGroup = new DirectoryGroup
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = issuer,
            ExternalId = "group-inactive",
            Name = "Team",
            Path = "/Archiv/Team",
            IsActive = false
        };
        var first = User("20000000-0000-0000-0000-000000000001", "subject-anna-a", true);
        var second = User("20000000-0000-0000-0000-000000000002", "subject-anna-b", true);
        var inactive = User("20000000-0000-0000-0000-000000000003", "subject-inactive", false);
        return new DirectorySnapshot
        {
            GenerationId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Issuer = issuer,
            CompletedAtUtc = DateTime.Parse("2026-09-08T10:00:00Z").ToUniversalTime(),
            Users = [first, second, inactive],
            Groups = [root, child, inactiveGroup],
            Memberships =
            [
                new DirectoryMembership { UserId = first.Id, GroupId = child.Id },
                new DirectoryMembership { UserId = second.Id, GroupId = child.Id },
                new DirectoryMembership { UserId = inactive.Id, GroupId = child.Id }
            ]
        };

        DirectoryUser User(string id, string subject, bool active) => new()
        {
            Id = Guid.Parse(id),
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = issuer,
            Subject = subject,
            DisplayName = "Anna Muster",
            IsActive = active
        };
    }

    private sealed class SnapshotStorage(DirectorySnapshot? snapshot) : IIdentityDirectoryStorage
    {
        public Task<DirectorySnapshot?> GetActiveSnapshot() => Task.FromResult(snapshot);
        public Task<DirectorySyncStatus?> GetSyncStatus() => Task.FromResult<DirectorySyncStatus?>(null);
        public Task<bool> TryStartSync(string issuer, Guid generationId, DateTime startedAtUtc, DateTime leaseExpiresAtUtc) =>
            throw new NotSupportedException();
        public Task<bool> FailSync(string issuer, Guid generationId, string errorCode, string errorMessage, DateTime failedAtUtc) =>
            throw new NotSupportedException();
        public Task PublishSnapshot(DirectorySnapshot published) => throw new NotSupportedException();
    }
}
