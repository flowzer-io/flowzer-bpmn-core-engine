using BPMN.Common;
using BPMN.HumanInteraction;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.Auth;

namespace WebApiEngine.Tests;

/// <summary>
/// Auswertung der BPMN-Zuweisungen (<c>assignee</c>, <c>candidateUsers</c>, <c>candidateGroups</c>).
/// Bisher sah jede angemeldete Person jede Aufgabe; die Angaben im Modell wurden gelesen,
/// aber nie ausgewertet.
/// </summary>
public class UserTaskAssignmentTest
{
    // Testzweck: Eine Aufgabe ohne jede Zuweisung bleibt fuer alle sichtbar; sonst waeren
    // bestehende Modelle nach dem Update fuer niemanden mehr bearbeitbar.
    [Test]
    public void UnassignedTask_ShouldStayVisibleToEveryone()
    {
        var task = CreateTask();

        UserTaskAssignment.IsVisibleTo(task, Identity("anna"), seeAll: false).Should().BeTrue();
    }

    // Testzweck: Eine namentlich zugewiesene Aufgabe sieht nur die genannte Person.
    [Test]
    public void AssignedTask_ShouldBeVisibleOnlyToTheAssignee()
    {
        var task = CreateTask(assignee: "anna");

        UserTaskAssignment.IsVisibleTo(task, Identity("anna"), seeAll: false).Should().BeTrue();
        UserTaskAssignment.IsVisibleTo(task, Identity("bert"), seeAll: false).Should().BeFalse();
    }

    // Testzweck: Der Abgleich laeuft ueber alle Kennungen der Person. Keycloak liefert je nach
    // Mapper Benutzername, E-Mail oder die technische Id; ein Modell darf jede davon nennen.
    [Test]
    public void Assignee_ShouldMatchAnyKnownIdentifierOfThePerson()
    {
        var task = CreateTask(assignee: "anna@maass.it");
        var identity = Identity("anna", "anna@maass.it", "8f14e45f-ceea-467a-9a26-1b1ab2d1f0d0");

        UserTaskAssignment.IsVisibleTo(task, identity, seeAll: false).Should().BeTrue();
    }

    // Testzweck: Gross- und Kleinschreibung darf ueber die Sichtbarkeit nicht entscheiden.
    [Test]
    public void Matching_ShouldIgnoreCasingAndSurroundingSpaces()
    {
        var task = CreateTask(assignee: " Anna@Maass.IT ");

        UserTaskAssignment.IsVisibleTo(task, Identity("anna@maass.it"), seeAll: false).Should().BeTrue();
    }

    // Testzweck: Kandidatenlisten sind kommagetrennt und wirken wie eine Oder-Verknuepfung.
    [Test]
    public void CandidateUsersAndGroups_ShouldBeReadAsCommaSeparatedLists()
    {
        var task = CreateTask(candidateUsers: "bert, carla", candidateGroups: "buchhaltung,einkauf");

        UserTaskAssignment.IsVisibleTo(task, Identity("carla"), seeAll: false).Should().BeTrue();
        UserTaskAssignment.IsVisibleTo(task, Identity("dora", groups: ["/einkauf"]), seeAll: false).Should().BeTrue();
        UserTaskAssignment.IsVisibleTo(task, Identity("dora", groups: ["/vertrieb"]), seeAll: false).Should().BeFalse();
    }

    // Testzweck: Keycloak liefert Gruppen als Pfade; ein Modell nennt aber den Gruppennamen.
    [Test]
    public void GroupMatching_ShouldAcceptKeycloakPathsAndPlainNames()
    {
        var task = CreateTask(candidateGroups: "buchhaltung");

        UserTaskAssignment.IsVisibleTo(task, Identity("dora", groups: ["/abteilungen/buchhaltung"]), seeAll: false).Should().BeTrue();
        UserTaskAssignment.IsVisibleTo(task, Identity("dora", groups: ["buchhaltung"]), seeAll: false).Should().BeTrue();
        UserTaskAssignment.IsVisibleTo(task, Identity("dora", groups: ["/buchhaltung-archiv"]), seeAll: false).Should().BeFalse();
    }

    // Testzweck: Nennt das Modell einen vollstaendigen Pfad, muss dieser genau stimmen. Sonst
    // oeffnete /extern/buchhaltung auch die Aufgaben von /intern/buchhaltung.
    [Test]
    public void GroupMatching_ShouldCompareFullPathsExactly_WhenTheModelNamesAPath()
    {
        var task = CreateTask(candidateGroups: "/intern/buchhaltung");

        UserTaskAssignment.IsVisibleTo(task, Identity("dora", groups: ["/intern/buchhaltung"]), seeAll: false).Should().BeTrue();
        UserTaskAssignment.IsVisibleTo(task, Identity("dora", groups: ["/extern/buchhaltung"]), seeAll: false).Should().BeFalse();
    }

    // Testzweck: Aufgaben aus der Zeit vor dieser Auswertung tragen die Felder nicht; sie werden
    // aus dem Modellelement im Token nachgezogen, statt fuer alle sichtbar zu bleiben.
    [Test]
    public void EnsureAssignmentFromModel_ShouldFillMissingFieldsFromTheStoredUserTask()
    {
        var userTask = new UserTask
        {
            Id = "UserTask_1",
            Name = "Freigabe",
            Implementation = "Formular",
            FlowzerAssignee = "anna",
            FlowzerCandidateGroups = "buchhaltung"
        };
        var task = CreateTask();
        task.Token = new Token
        {
            ProcessInstanceId = Guid.NewGuid(),
            CurrentBaseElement = userTask,
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Active
        };

        UserTaskAssignment.EnsureAssignmentFromModel(task);

        task.Assignee.Should().Be("anna");
        task.CandidateGroups.Should().BeEquivalentTo(["buchhaltung"]);
        UserTaskAssignment.IsVisibleTo(task, Identity("bert"), seeAll: false).Should().BeFalse();
        UserTaskAssignment.IsVisibleTo(task, Identity("anna"), seeAll: false).Should().BeTrue();
    }

    // Testzweck: Wer den Betrieb verantwortet, sieht alles; sonst waere eine haengende Instanz
    // nicht diagnostizierbar.
    [Test]
    public void Operators_ShouldSeeEveryTask()
    {
        var task = CreateTask(assignee: "anna");

        UserTaskAssignment.IsVisibleTo(task, Identity("bert"), seeAll: true).Should().BeTrue();
    }

    // Testzweck: Ein zugewiesener Name, den niemand traegt, macht die Aufgabe nicht fuer alle
    // sichtbar; sie bleibt fuer den Betrieb sichtbar und sonst verborgen.
    [Test]
    public void UnknownAssignee_ShouldNotFallBackToEveryone()
    {
        var task = CreateTask(assignee: "ehemalige.person");

        UserTaskAssignment.IsVisibleTo(task, Identity("anna"), seeAll: false).Should().BeFalse();
    }

    // Testzweck: Eine stabile Benutzerreferenz berechtigt ausschließlich die im aktiven
    // Snapshot über exakt denselben Issuer und Subject aufgelöste Person.
    [Test]
    public void DirectoryAssignee_ShouldMatchExactAuthenticatedSubject()
    {
        var (snapshot, annaId, _, _) = Directory();
        var task = CreateDirectoryTask(assigneeUserId: annaId);

        UserTaskAssignment.IsVisibleTo(task, CurrentUser("issuer-a", "anna-subject"), snapshot, seeAll: false)
            .Should().BeTrue();
        UserTaskAssignment.IsVisibleTo(task, CurrentUser("issuer-b", "anna-subject"), snapshot, seeAll: false)
            .Should().BeFalse();
    }

    // Testzweck: Gleiche Anzeigenamen, E-Mails oder Claim-Gruppen dürfen im Directory-Modus
    // niemals auf den Legacy-Textmatcher zurückfallen.
    [Test]
    public void DirectoryAssignment_ShouldNotFallBackToNamesOrClaimGroups()
    {
        var (snapshot, annaId, _, financeId) = Directory();
        var task = CreateDirectoryTask(assigneeUserId: annaId, candidateGroupIds: [financeId]);
        var impostor = CurrentUser("issuer-a", "impostor-subject") with
        {
            Names = ["Anna", "anna@example.test"],
            Groups = ["/finance"]
        };

        UserTaskAssignment.IsVisibleTo(task, impostor, snapshot, seeAll: false).Should().BeFalse();
    }

    // Testzweck: Kandidatenbenutzer und Kandidatengruppen werden über lokale IDs und eine
    // aktuelle direkte Mitgliedschaft ausgewertet; eine Gruppenreferenz bleibt dabei Gruppe.
    [Test]
    public void DirectoryCandidates_ShouldUseStableUserAndMembershipIds()
    {
        var (snapshot, _, bertId, financeId) = Directory();
        var byUser = CreateDirectoryTask(candidateUserIds: [bertId]);
        var byGroup = CreateDirectoryTask(candidateGroupIds: [financeId]);

        UserTaskAssignment.IsVisibleTo(byUser, CurrentUser("issuer-a", "bert-subject"), snapshot, seeAll: false)
            .Should().BeTrue();
        UserTaskAssignment.IsVisibleTo(byGroup, CurrentUser("issuer-a", "bert-subject"), snapshot, seeAll: false)
            .Should().BeTrue();
        byGroup.DirectoryCandidateUserIds.Should().BeEmpty();
    }

    // Testzweck: Ein fehlender Snapshot, eine fehlende Authentifizierungsidentität oder ein
    // inzwischen deaktivierter Benutzer schließt den Zugriff; nur der Operator darf retten.
    [Test]
    public void DirectoryAssignment_ShouldFailClosedForUnavailableIdentityState()
    {
        var (snapshot, annaId, _, _) = Directory();
        var task = CreateDirectoryTask(assigneeUserId: annaId);
        var user = CurrentUser("issuer-a", "anna-subject");
        snapshot.Users.Single(entry => entry.Id == annaId).IsActive = false;

        UserTaskAssignment.IsVisibleTo(task, user, null, seeAll: false).Should().BeFalse();
        UserTaskAssignment.IsVisibleTo(task, user with { Identity = null }, snapshot, seeAll: false).Should().BeFalse();
        UserTaskAssignment.IsVisibleTo(task, user, snapshot, seeAll: false).Should().BeFalse();
        UserTaskAssignment.IsVisibleTo(task, user, null, seeAll: true).Should().BeTrue();
    }

    // Testzweck: Eine historisch ohne neue Subscription-Felder gespeicherte Directory-Aufgabe
    // zieht genau die stabilen Modellreferenzen nach und nicht etwa die Textzuweisung.
    [Test]
    public void EnsureAssignmentFromModel_ShouldRestoreDirectoryContract()
    {
        var assigneeId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var userTask = new UserTask
        {
            Id = "UserTask_1", Name = "Freigabe", Implementation = "Formular",
            FlowzerAssignmentMode = UserTaskAssignmentMode.Directory,
            FlowzerDirectoryAssigneeUserId = assigneeId,
            FlowzerDirectoryCandidateGroupIds = [groupId]
        };
        var task = CreateTask();
        task.Token = new Token
        {
            ProcessInstanceId = Guid.NewGuid(), CurrentBaseElement = userTask,
            ActiveBoundaryEvents = [], State = FlowNodeState.Active
        };

        UserTaskAssignment.EnsureAssignmentFromModel(task);

        task.AssignmentMode.Should().Be(UserTaskAssignmentMode.Directory);
        task.DirectoryAssigneeUserId.Should().Be(assigneeId);
        task.DirectoryCandidateGroupIds.Should().Equal(groupId);
        task.Assignee.Should().BeNull();
    }

    private static UserTaskIdentity Identity(params string[] names) => new(names, []);

    private static UserTaskIdentity Identity(string name, IReadOnlyCollection<string> groups) => new([name], groups);

    private static ExtendedUserTaskSubscription CreateTask(
        string? assignee = null,
        string? candidateUsers = null,
        string? candidateGroups = null)
    {
        return new ExtendedUserTaskSubscription
        {
            Id = Guid.NewGuid(),
            Name = "Freigabe",
            Token = null!,
            MetaDefinitionId = "catalog",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_1",
            Assignee = assignee,
            CandidateUsers = UserTaskAssignment.SplitList(candidateUsers),
            CandidateGroups = UserTaskAssignment.SplitList(candidateGroups)
        };
    }

    private static ExtendedUserTaskSubscription CreateDirectoryTask(
        Guid? assigneeUserId = null,
        IReadOnlyCollection<Guid>? candidateUserIds = null,
        IReadOnlyCollection<Guid>? candidateGroupIds = null)
    {
        var task = CreateTask();
        task.AssignmentMode = UserTaskAssignmentMode.Directory;
        task.DirectoryAssigneeUserId = assigneeUserId;
        task.DirectoryCandidateUserIds = candidateUserIds?.ToList() ?? [];
        task.DirectoryCandidateGroupIds = candidateGroupIds?.ToList() ?? [];
        return task;
    }

    private static CurrentUserContext CurrentUser(string issuer, string subject) =>
        new(Guid.NewGuid(), "test", false)
        {
            Identity = new AuthenticatedSubject(issuer, subject), Names = ["irrelevant"], Groups = ["/irrelevant"]
        };

    private static (DirectorySnapshot Snapshot, Guid AnnaId, Guid BertId, Guid FinanceId) Directory()
    {
        var annaId = Guid.NewGuid();
        var bertId = Guid.NewGuid();
        var financeId = Guid.NewGuid();
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(), Issuer = "issuer-a", CompletedAtUtc = DateTime.UtcNow,
            Users =
            [
                new DirectoryUser
                {
                    Id = annaId, SourceKind = DirectorySourceKind.Keycloak, Issuer = "issuer-a",
                    Subject = "anna-subject", DisplayName = "Anna", IsActive = true
                },
                new DirectoryUser
                {
                    Id = bertId, SourceKind = DirectorySourceKind.Keycloak, Issuer = "issuer-a",
                    Subject = "bert-subject", DisplayName = "Bert", IsActive = true
                }
            ],
            Groups =
            [
                new DirectoryGroup
                {
                    Id = financeId, SourceKind = DirectorySourceKind.Keycloak, Issuer = "issuer-a",
                    ExternalId = "finance", Name = "Finance", Path = "/finance", IsActive = true
                }
            ],
            Memberships = [new DirectoryMembership { UserId = bertId, GroupId = financeId }]
        };
        return (snapshot, annaId, bertId, financeId);
    }
}
