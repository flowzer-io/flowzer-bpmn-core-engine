using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Tests;

/// <summary>Servergrenze fuer neu geschriebene stabile Ordnerzuweisungen.</summary>
public sealed class FolderDirectoryAssignmentValidatorTest
{
    // Testzweck: Legacy-Freitext bleibt auch ohne eingerichtetes Directory speicherbar.
    [Test]
    public void Validate_ShouldLeaveTextAssignmentsIndependentOfDirectory()
    {
        var assignment = new FolderAssignment
        {
            SubjectKind = FolderSubjectKind.User,
            Subject = "anna",
            Role = FolderRole.Editor
        };

        var action = () => FolderDirectoryAssignmentValidator.ValidateAndProject([assignment], null);

        action.Should().NotThrow();
    }

    // Testzweck: Nur eine aktive, arttreue Referenz wird akzeptiert; ihr Anzeigename stammt
    // aus dem autoritativen Snapshot und nicht aus dem Request.
    [Test]
    public void Validate_ShouldResolveActiveReferenceAndReplaceDisplayName()
    {
        var id = Guid.NewGuid();
        var assignment = DirectoryAssignment(DirectorySubjectKind.User, id);
        assignment.DisplayName = "Manipuliert";
        var snapshot = Snapshot(new DirectoryUser
        {
            Id = id,
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = "issuer-a",
            Subject = "subject-anna",
            DisplayName = "Anna Autoritativ",
            IsActive = true
        });

        FolderDirectoryAssignmentValidator.ValidateAndProject([assignment], snapshot);

        assignment.DisplayName.Should().Be("Anna Autoritativ");
    }

    // Testzweck: Fehlender Snapshot sowie unbekannte oder deaktivierte IDs muessen vor dem
    // Speichern fehlschlagen, statt spaeter per Text oder Anzeigename Zugriff zu geben.
    [Test]
    public void Validate_ShouldRejectUnavailableOrInactiveReference()
    {
        var id = Guid.NewGuid();
        var assignment = DirectoryAssignment(DirectorySubjectKind.User, id);
        var inactive = Snapshot(new DirectoryUser
        {
            Id = id,
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = "issuer-a",
            Subject = "subject-anna",
            DisplayName = "Anna",
            IsActive = false
        });

        Action unavailable = () => FolderDirectoryAssignmentValidator.ValidateAndProject([assignment], null);
        Action rejected = () => FolderDirectoryAssignmentValidator.ValidateAndProject([assignment], inactive);

        unavailable.Should().Throw<InvalidOperationException>();
        rejected.Should().Throw<ArgumentException>();
    }

    private static FolderAssignment DirectoryAssignment(DirectorySubjectKind kind, Guid id) => new()
    {
        AssignmentMode = FolderAssignmentMode.Directory,
        SubjectKind = kind == DirectorySubjectKind.User ? FolderSubjectKind.User : FolderSubjectKind.Group,
        Subject = id.ToString(),
        DirectorySubject = new SubjectRef(kind, id),
        Role = FolderRole.Editor
    };

    private static DirectorySnapshot Snapshot(DirectoryUser user) => new()
    {
        GenerationId = Guid.NewGuid(),
        Issuer = "issuer-a",
        CompletedAtUtc = DateTime.UtcNow,
        Users = [user]
    };
}
