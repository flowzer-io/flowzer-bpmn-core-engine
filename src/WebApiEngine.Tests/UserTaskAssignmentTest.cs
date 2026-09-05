using BPMN.Common;
using BPMN.HumanInteraction;
using FluentAssertions;
using Model;
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
}
