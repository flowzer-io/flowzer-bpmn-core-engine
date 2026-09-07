using FluentAssertions;
using Model;
using WebApiEngine.Auth;

namespace WebApiEngine.Tests;

/// <summary>
/// Die Vererbungsregel der Ordnerrechte, geprueft ohne Ablage und ohne HTTP: Eine Zuweisung
/// gilt fuer ihren Ordner und alles darunter, und nach unten kann sie nur staerker werden.
/// </summary>
public class FolderAccessTest
{
    private static readonly Guid FinanzenId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BeschaffungId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RahmenId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PersonalId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static WorkflowFolder Folder(Guid id, string name, Guid? parentId, params FolderAssignment[] assignments) =>
        new()
        {
            Id = id,
            Name = name,
            ParentId = parentId,
            Assignments = assignments.ToList()
        };

    private static FolderAssignment User(string subject, FolderRole role) =>
        new() { SubjectKind = FolderSubjectKind.User, Subject = subject, Role = role };

    private static FolderAssignment Group(string subject, FolderRole role) =>
        new() { SubjectKind = FolderSubjectKind.Group, Subject = subject, Role = role };

    private static UserTaskIdentity Identity(string[] names, string[] groups) => new(names, groups);

    // Testzweck: Eine Zuweisung an einem Ordner gilt auch fuer dessen Unterordner. Ohne
    // Vererbung muesste jede Fachverantwortung in jedem Unterordner wiederholt werden — und
    // ein neuer Unterordner waere fuer die Zustaendigen sofort gesperrt.
    [Test]
    public void ResolveRoles_ShouldInheritDownTheTree()
    {
        WorkflowFolder[] folders =
        [
            Folder(FinanzenId, "Finanzen", null, User("anna", FolderRole.Editor)),
            Folder(BeschaffungId, "Beschaffung", FinanzenId),
            Folder(RahmenId, "Rahmenvertraege", BeschaffungId),
            Folder(PersonalId, "Personal", null)
        ];

        var roles = FolderAccess.ResolveRoles(folders, Identity(["anna"], []));

        roles.Should().ContainKeys(FinanzenId, BeschaffungId, RahmenId);
        roles[RahmenId].Should().Be(FolderRole.Editor);
        roles.Should().NotContainKey(PersonalId, "ein Nachbarast erbt nichts");
    }

    // Testzweck: Die staerkere Rolle gewinnt, egal auf welcher Ebene sie steht. Sonst haenge
    // das Ergebnis daran, in welcher Reihenfolge die Ordner gelesen wurden.
    [Test]
    public void ResolveRoles_ShouldPreferTheStrongerRole()
    {
        WorkflowFolder[] folders =
        [
            Folder(FinanzenId, "Finanzen", null, User("anna", FolderRole.Editor)),
            Folder(BeschaffungId, "Beschaffung", FinanzenId, User("anna", FolderRole.Steward))
        ];

        var roles = FolderAccess.ResolveRoles(folders, Identity(["anna"], []));

        roles[FinanzenId].Should().Be(FolderRole.Editor);
        roles[BeschaffungId].Should().Be(FolderRole.Steward);
    }

    // Testzweck: Eine geerbte Fachverantwortung bleibt Fachverantwortung, auch wenn weiter
    // unten nur ein Bearbeitungsrecht steht. Ein Recht darf nach unten nicht schrumpfen —
    // sonst liesse es sich durch Anlegen eines Unterordners aushebeln.
    [Test]
    public void ResolveRoles_ShouldNotWeakenAnInheritedRole()
    {
        WorkflowFolder[] folders =
        [
            Folder(FinanzenId, "Finanzen", null, User("anna", FolderRole.Steward)),
            Folder(BeschaffungId, "Beschaffung", FinanzenId, User("anna", FolderRole.Editor))
        ];

        FolderAccess.ResolveRoles(folders, Identity(["anna"], []))[BeschaffungId]
            .Should().Be(FolderRole.Steward);
    }

    // Testzweck: Gruppen werden wie im BPMN-Modell abgeglichen — Keycloak liefert Pfade,
    // eine Zuweisung nennt oft nur den Namen. Ein Teilstring darf nie reichen.
    [Test]
    public void ResolveRoles_ShouldMatchGroupsLikeTheModelDoes()
    {
        WorkflowFolder[] folders = [Folder(FinanzenId, "Finanzen", null, Group("einkauf", FolderRole.Editor))];

        FolderAccess.ResolveRoles(folders, Identity([], ["/abteilungen/einkauf"]))
            .Should().ContainKey(FinanzenId);
        FolderAccess.ResolveRoles(folders, Identity([], ["/abteilungen/einkauf-archiv"]))
            .Should().BeEmpty();
    }

    // Testzweck: Eine Zuweisung an eine Gruppe darf nicht auf eine gleichnamige Person
    // passen und umgekehrt. Sonst genuegte ein passend gewaehlter Benutzername.
    [Test]
    public void ResolveRoles_ShouldNotConfuseUsersAndGroups()
    {
        WorkflowFolder[] folders = [Folder(FinanzenId, "Finanzen", null, Group("anna", FolderRole.Steward))];

        FolderAccess.ResolveRoles(folders, Identity(["anna"], [])).Should().BeEmpty();
    }

    // Testzweck: Ein durch einen Fehler entstandener Ring darf die Aufloesung nicht aufhaengen.
    [Test]
    public void ResolveRoles_ShouldSurviveACycle()
    {
        WorkflowFolder[] folders =
        [
            Folder(FinanzenId, "Finanzen", BeschaffungId, User("anna", FolderRole.Editor)),
            Folder(BeschaffungId, "Beschaffung", FinanzenId)
        ];

        var roles = FolderAccess.ResolveRoles(folders, Identity(["anna"], []));

        roles.Should().ContainKeys(FinanzenId, BeschaffungId);
    }

    // Testzweck: Die geerbten Zuweisungen eines Ordners stammen ausschliesslich von oberhalb;
    // die eigenen gehoeren nicht dazu, sonst staende jede Zeile im Dialog doppelt.
    [Test]
    public void CollectInherited_ShouldOnlyReportAncestors()
    {
        var finanzen = Folder(FinanzenId, "Finanzen", null, User("anna", FolderRole.Steward));
        var beschaffung = Folder(BeschaffungId, "Beschaffung", FinanzenId, User("bert", FolderRole.Editor));

        var inherited = FolderAccess.CollectInherited(beschaffung, [finanzen, beschaffung]);

        inherited.Should().HaveCount(1);
        inherited[0].Source.Should().Be(finanzen);
        inherited[0].Assignment.Subject.Should().Be("anna");
    }

    // Testzweck: Der Pfad traegt die Brotkrumenleiste und laeuft von oben nach unten.
    [Test]
    public void BuildPath_ShouldRunFromTheRootDown()
    {
        WorkflowFolder[] folders =
        [
            Folder(FinanzenId, "Finanzen", null),
            Folder(BeschaffungId, "Beschaffung", FinanzenId),
            Folder(RahmenId, "Rahmenvertraege", BeschaffungId)
        ];

        FolderAccess.BuildPath(RahmenId, folders).Select(folder => folder.Id)
            .Should().Equal(FinanzenId, BeschaffungId, RahmenId);
    }

    // Testzweck: Ein Ordner darf nicht unter sich selbst geschoben werden — der Ast waere
    // danach vom Baum abgeschnitten und aus der Oberflaeche nicht mehr erreichbar.
    [Test]
    public void WouldCreateCycle_ShouldRejectMovingIntoTheOwnSubtree()
    {
        WorkflowFolder[] folders =
        [
            Folder(FinanzenId, "Finanzen", null),
            Folder(BeschaffungId, "Beschaffung", FinanzenId),
            Folder(RahmenId, "Rahmenvertraege", BeschaffungId),
            Folder(PersonalId, "Personal", null)
        ];

        FolderAccess.WouldCreateCycle(FinanzenId, RahmenId, folders).Should().BeTrue();
        FolderAccess.WouldCreateCycle(FinanzenId, FinanzenId, folders).Should().BeTrue();
        FolderAccess.WouldCreateCycle(FinanzenId, PersonalId, folders).Should().BeFalse();
        FolderAccess.WouldCreateCycle(FinanzenId, null, folders).Should().BeFalse("die oberste Ebene ist immer gueltig");
    }
}
