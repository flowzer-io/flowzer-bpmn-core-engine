using BPMN.HumanInteraction;
using FluentAssertions;
using StorageSystem;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Tests;

public sealed class DirectoryTaskAssignmentValidatorTest
{
    private static readonly Guid AnnaId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid BertId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid InactiveUserId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid FinanceId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    // Testzweck: Reine Freitextmodelle bleiben auch ohne konfiguriertes Verzeichnis deploybar.
    [Test]
    public void Validate_ShouldNotRequireSnapshotForTextAssignments()
    {
        var task = CreateTask(UserTaskAssignmentMode.Text);

        var action = () => DirectoryTaskAssignmentValidator.Validate([task], null);

        action.Should().NotThrow();
    }

    // Testzweck: Alle aktiven Benutzer- und Gruppenreferenzen werden typgerecht akzeptiert.
    [Test]
    public void Validate_ShouldAcceptActiveReferencesOfTheExpectedKind()
    {
        var task = CreateTask(UserTaskAssignmentMode.Directory) with
        {
            FlowzerDirectoryAssigneeUserId = AnnaId,
            FlowzerDirectoryCandidateUserIds = [BertId],
            FlowzerDirectoryCandidateGroupIds = [FinanceId]
        };

        var action = () => DirectoryTaskAssignmentValidator.Validate([task], CreateSnapshot());

        action.Should().NotThrow();
    }

    // Testzweck: Ohne vollständig veröffentlichten Snapshot darf ein Directory-Deployment
    // nicht aktiviert werden, während Textmodelle weiterhin unabhängig bleiben.
    [Test]
    public void Validate_ShouldRejectDirectoryAssignmentsWithoutSnapshot()
    {
        var task = CreateTask(UserTaskAssignmentMode.Directory) with
        {
            FlowzerDirectoryAssigneeUserId = AnnaId
        };

        var action = () => DirectoryTaskAssignmentValidator.Validate([task], null);

        action.Should().Throw<InvalidOperationException>().WithMessage("*directory snapshot*");
    }

    // Testzweck: Unbekannte, inaktive oder typfalsch einsortierte IDs werden vor Aktivierung
    // abgewiesen; eine UUID allein beweist weder Existenz noch Identitätsart.
    [TestCase("unknown-user")]
    [TestCase("inactive-user")]
    [TestCase("group-as-user")]
    [TestCase("user-as-group")]
    [TestCase("inactive-group")]
    public void Validate_ShouldRejectUnavailableOrWrongKindReferences(string scenario)
    {
        var snapshot = CreateSnapshot();
        var task = CreateTask(UserTaskAssignmentMode.Directory);
        task = scenario switch
        {
            "unknown-user" => task with { FlowzerDirectoryAssigneeUserId = Guid.NewGuid() },
            "inactive-user" => task with { FlowzerDirectoryAssigneeUserId = InactiveUserId },
            "group-as-user" => task with { FlowzerDirectoryAssigneeUserId = FinanceId },
            "user-as-group" => task with { FlowzerDirectoryCandidateGroupIds = [AnnaId] },
            "inactive-group" => task with
            {
                FlowzerDirectoryCandidateGroupIds = [snapshot.Groups.Single(group => !group.IsActive).Id]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var action = () => DirectoryTaskAssignmentValidator.Validate([task], snapshot);

        action.Should().Throw<InvalidOperationException>().WithMessage("*Task_Approval*");
    }

    private static UserTask CreateTask(UserTaskAssignmentMode mode) => new()
    {
        Id = "Task_Approval",
        Name = "Approval",
        Implementation = "ApprovalForm",
        FlowzerAssignmentMode = mode
    };

    private static DirectorySnapshot CreateSnapshot()
    {
        const string issuer = "https://issuer.test/realms/flowzer";
        return new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(),
            Issuer = issuer,
            CompletedAtUtc = DateTime.UtcNow,
            Users =
            [
                new DirectoryUser
                {
                    Id = AnnaId, SourceKind = DirectorySourceKind.Keycloak, Issuer = issuer,
                    Subject = "anna-subject", DisplayName = "Anna", IsActive = true
                },
                new DirectoryUser
                {
                    Id = BertId, SourceKind = DirectorySourceKind.Keycloak, Issuer = issuer,
                    Subject = "bert-subject", DisplayName = "Bert", IsActive = true
                },
                new DirectoryUser
                {
                    Id = InactiveUserId, SourceKind = DirectorySourceKind.Keycloak, Issuer = issuer,
                    Subject = "former-subject", DisplayName = "Former", IsActive = false
                }
            ],
            Groups =
            [
                new DirectoryGroup
                {
                    Id = FinanceId, SourceKind = DirectorySourceKind.Keycloak, Issuer = issuer,
                    ExternalId = "finance", Name = "Finance", Path = "/finance", IsActive = true
                },
                new DirectoryGroup
                {
                    Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                    SourceKind = DirectorySourceKind.Keycloak, Issuer = issuer, ExternalId = "former",
                    Name = "Former", Path = "/former", IsActive = false
                }
            ]
        };
    }
}
