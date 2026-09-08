using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>End-to-End-Rechte für stabile User-Task-Zuweisungen über alle öffentlichen Pfade.</summary>
[NonParallelizable]
public sealed class DirectoryTaskAssignmentEndpointTest
{
    // Testzweck: Nur das exakte Directory-Subject darf Liste, Formular, Vorgangsübersicht und
    // Abschluss nutzen; ein gleich benannter Nutzer erhält überall dieselbe 404-Semantik.
    [Test]
    public async Task ExactDirectoryAssignee_ShouldHaveConsistentAccessAcrossTaskRoutes()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directory = await PublishDirectoryAsync(context, includeMembership: false);
        var task = await context.StartAsync("", assignmentExtensionXml: DirectoryAssignment(assigneeId: directory.UserId));
        using var exactUser = context.CreateClient();
        using var sameName = context.CreateClient(userId: Guid.NewGuid(), username: "bert");

        (await exactUser.GetFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>("/usertask"))!
            .Result.Should().ContainSingle(item => item.Id == task.Id);
        (await exactUser.GetAsync($"/usertask/{task.Id}/form")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await exactUser.GetAsync($"/instance/{task.ProcessInstanceId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await sameName.GetFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>("/usertask"))!
            .Result.Should().BeEmpty();
        (await sameName.GetAsync($"/usertask/{task.Id}/form")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await sameName.GetAsync($"/instance/{task.ProcessInstanceId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await sameName.PostAsJsonAsync("/usertask", Result(task))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await exactUser.PostAsJsonAsync("/form/result", Result(task))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Eine Kandidatengruppe berechtigt über die aktuelle stabile Mitgliedschaft;
    // sie wird weder im Modell noch im API-Vertrag in einzelne Benutzer expandiert.
    [Test]
    public async Task DirectoryCandidateGroup_ShouldAuthorizeCurrentMemberWithoutExpandingReference()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directory = await PublishDirectoryAsync(context, includeMembership: true);
        var task = await context.StartAsync("", assignmentExtensionXml: DirectoryAssignment(groupId: directory.GroupId));
        using var client = context.CreateClient();

        var list = await client.GetFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>("/usertask");

        var dto = list!.Result.Should().ContainSingle().Subject;
        dto.AssignmentMode.Should().Be("directory");
        dto.DirectoryCandidateGroups.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new SubjectRefDto { Kind = "group", Id = directory.GroupId!.Value });
        dto.DirectoryCandidateUsers.Should().BeEmpty();
        (await client.PostAsJsonAsync("/usertask", Result(task))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Eine nach Aufgabenstart deaktivierte Identität verliert sofort alle normalen
    // Bearbeitungsrechte; die bestehende Operator-Ausnahme bleibt für Störungsbehebung erhalten.
    [Test]
    public async Task DeactivatedDirectoryUser_ShouldLoseAccessWhileOperatorKeepsRecoveryAccess()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directory = await PublishDirectoryAsync(context, includeMembership: false);
        var task = await context.StartAsync("", assignmentExtensionXml: DirectoryAssignment(assigneeId: directory.UserId));
        await PublishReplacementWithoutCurrentUserAsync(context);
        using var formerUser = context.CreateClient();
        using var operatorClient = context.CreateClient(isOperator: true);

        (await formerUser.GetFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>("/usertask"))!
            .Result.Should().BeEmpty();
        (await formerUser.GetAsync($"/usertask/{task.Id}/form")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await formerUser.GetAsync($"/instance/{task.ProcessInstanceId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await formerUser.PostAsJsonAsync("/form/result", Result(task))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await operatorClient.GetAsync($"/usertask/{task.Id}/form")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await operatorClient.PostAsJsonAsync("/usertask", Result(task))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ein manipuliertes Deployment mit unbekannter Directory-ID wird vor der
    // Aktivierung abgelehnt und hinterlässt weder eine Version noch ersetzt es den Altstand.
    [Test]
    public async Task HttpDeployment_ShouldRejectUnknownDirectoryReferenceWithoutReplacingActiveVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var original = await context.DeployAsync("assignee=\"bert\"");
        await PublishDirectoryAsync(context, includeMembership: false);
        using var client = context.CreateClient(isModeler: true);
        var xml = WorkflowXml(DirectoryAssignment(assigneeId: Guid.NewGuid()));

        using var response = await client.PostAsync("/definition/deploy",
            new StringContent(xml, Encoding.UTF8, "application/xml"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var stored = (await context.Storage.DefinitionStorage.GetAllDefinitions()).ToArray();
        stored.Should().ContainSingle().Which.Id.Should().Be(original.Id);
        stored.Single().IsActive.Should().BeTrue();
        (await context.Storage.DefinitionStorage.GetDeployedDefinition(original.DefinitionId))!.Id
            .Should().Be(original.Id);
    }

    private static string DirectoryAssignment(Guid? assigneeId = null, Guid? groupId = null)
    {
        var assignee = assigneeId.HasValue ? $" assigneeId=\"{assigneeId}\"" : "";
        var group = groupId.HasValue ? $" candidateGroupIds=\"{groupId}\"" : "";
        return $"<flowzer:taskAssignment mode=\"directory\"{assignee}{group} />";
    }

    private static string WorkflowXml(string assignment) => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
            id="Definitions_Completion" targetNamespace="test">
          <bpmn:process id="Process_Completion" isExecutable="true">
            <bpmn:startEvent id="Start"><bpmn:outgoing>ToReview</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="ToReview" sourceRef="Start" targetRef="Review" />
            <bpmn:userTask id="Review" name="Review">
              <bpmn:extensionElements>
                <zeebe:formDefinition formKey="Approval" />
                {{assignment}}
              </bpmn:extensionElements>
              <bpmn:incoming>ToReview</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
            </bpmn:userTask>
            <bpmn:sequenceFlow id="ToEnd" sourceRef="Review" targetRef="End" />
            <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static UserTaskResultDto Result(UserTaskSubscription task) => new()
    {
        ProcessInstanceId = task.ProcessInstanceId,
        TokenId = task.Token.Id,
        FlowNodeId = task.Token.CurrentFlowNode!.Id
    };

    private static async Task<(Guid UserId, Guid? GroupId)> PublishDirectoryAsync(
        AuthenticatedWorkflowTestContext context,
        bool includeMembership)
    {
        var importUserId = Guid.NewGuid();
        var importGroupId = Guid.NewGuid();
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(), Issuer = AuthenticatedWorkflowTestContext.Issuer,
            CompletedAtUtc = DateTime.UtcNow,
            Users =
            [
                new DirectoryUser
                {
                    Id = importUserId, SourceKind = DirectorySourceKind.Keycloak,
                    Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    Subject = AuthenticatedWorkflowTestContext.UserId.ToString(),
                    DisplayName = "Bert", IsActive = true
                }
            ],
            Groups = includeMembership
                ?
                [
                    new DirectoryGroup
                    {
                        Id = importGroupId, SourceKind = DirectorySourceKind.Keycloak,
                        Issuer = AuthenticatedWorkflowTestContext.Issuer, ExternalId = "review",
                        Name = "Review", Path = "/team/review", IsActive = true
                    }
                ]
                : [],
            Memberships = includeMembership
                ? [new DirectoryMembership { UserId = importUserId, GroupId = importGroupId }]
                : []
        };
        await PublishAsync(context, snapshot);
        var published = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        return (
            published.Users.Single(user => user.Subject == AuthenticatedWorkflowTestContext.UserId.ToString()).Id,
            includeMembership ? published.Groups.Single(group => group.ExternalId == "review").Id : null);
    }

    private static async Task PublishReplacementWithoutCurrentUserAsync(AuthenticatedWorkflowTestContext context)
    {
        var replacement = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(), Issuer = AuthenticatedWorkflowTestContext.Issuer,
            CompletedAtUtc = DateTime.UtcNow.AddSeconds(1),
            Users =
            [
                new DirectoryUser
                {
                    Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
                    Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    Subject = Guid.NewGuid().ToString(), DisplayName = "Replacement", IsActive = true
                }
            ]
        };
        await PublishAsync(context, replacement);
    }

    private static async Task PublishAsync(
        AuthenticatedWorkflowTestContext context,
        DirectorySnapshot snapshot)
    {
        var started = snapshot.CompletedAtUtc.AddSeconds(-1);
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            snapshot.Issuer, snapshot.GenerationId, started, started.AddMinutes(5))).Should().BeTrue();
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
    }
}
