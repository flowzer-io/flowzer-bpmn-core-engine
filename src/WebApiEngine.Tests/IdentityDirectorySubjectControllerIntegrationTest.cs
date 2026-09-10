using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

[NonParallelizable]
public sealed class IdentityDirectorySubjectControllerIntegrationTest
{
    // Testzweck: Ein Modellierer erhält ausschließlich aktive, typisierte Treffer des konkreten
    // Workflows; unbekannte Query-Parameter können den serverseitigen Filter nicht erweitern.
    [Test]
    public async Task Search_ShouldReturnActiveSubjectsForEditableWorkflowOnly()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var active = await SeedDirectoryAsync(context.Storage);
        await context.Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            { DefinitionId = "workflow-editable", Name = "Editable" });
        using var client = context.CreateClient(isModeler: true);

        var response = await client.GetAsync(
            "/identity-directory/workflows/workflow-editable/subjects?query=anna&kind=user&limit=10&includeInactive=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<DirectorySubjectSearchResultDto>>();
        payload!.Successful.Should().BeTrue();
        payload.Result!.GenerationId.Should().Be(active.GenerationId);
        payload.Result.Items.Should().ContainSingle();
        payload.Result.Items.Single().Subject.Kind.Should().Be("user");
        payload.Result.Items.Single().Subject.Id.Should().Be(active.Users.Single(user => user.IsActive).Id);
        payload.Result.Items.Single().Detail.Should().Be("subject-active");
    }

    // Testzweck: Eine delegierte Ordnerbearbeiterin darf im Kontext ihres Workflows suchen,
    // obwohl sie keine globale Modelliererrolle besitzt.
    [Test]
    public async Task Search_ShouldAllowDelegatedFolderEditor()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await SeedDirectoryAsync(context.Storage);
        var folder = new WorkflowFolder
        {
            Id = Guid.NewGuid(),
            Name = "Team",
            CreatedOn = DateTime.UtcNow,
            CreatedByUser = AuthenticatedWorkflowTestContext.UserId,
            Assignments =
            [
                new FolderAssignment
                {
                    SubjectKind = FolderSubjectKind.Group,
                    Subject = "/team/review",
                    Role = FolderRole.Editor
                }
            ]
        };
        await context.Storage.FolderStorage.StoreFolder(folder);
        await context.Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            { DefinitionId = "workflow-delegated", Name = "Delegated", FolderId = folder.Id });
        using var client = context.CreateClient();

        var response = await client.GetAsync(
            "/identity-directory/workflows/workflow-delegated/subjects?query=anna&kind=all");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Die Ordnerauswahl darf nur im Kontext eines vorhandenen Ordners mit
    // Delegationsrecht suchen; fremde und erfundene Ordner bleiben identisch verborgen.
    [Test]
    public async Task FolderSearch_ShouldRequireDelegationRightAndHideForeignFolders()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await SeedDirectoryAsync(context.Storage);
        var own = new WorkflowFolder
        {
            Id = Guid.NewGuid(), Name = "Own", CreatedOn = DateTime.UtcNow,
            CreatedByUser = Guid.NewGuid(),
            Assignments =
            [
                new FolderAssignment
                {
                    SubjectKind = FolderSubjectKind.Group,
                    Subject = "/team/review",
                    Role = FolderRole.Steward
                }
            ]
        };
        var foreign = new WorkflowFolder
        {
            Id = Guid.NewGuid(), Name = "Foreign", CreatedOn = DateTime.UtcNow,
            CreatedByUser = Guid.NewGuid()
        };
        await context.Storage.FolderStorage.StoreFolder(own);
        await context.Storage.FolderStorage.StoreFolder(foreign);
        using var client = context.CreateClient();

        var allowed = await client.GetAsync($"/identity-directory/folders/{own.Id}/subjects?query=anna&kind=all");
        var hidden = await client.GetAsync($"/identity-directory/folders/{foreign.Id}/subjects?query=anna&kind=user");
        var unknown = await client.GetAsync($"/identity-directory/folders/{Guid.NewGuid()}/subjects?query=anna&kind=user");

        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadFromJsonAsync<ProblemDetails>())!.Title.Should()
            .Be((await unknown.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    // Testzweck: Eine deaktivierte direkte Ordnerzuweisung bleibt für die berechtigte
    // Pflege sichtbar; eine nur im Request ergänzte bekannte UUID bleibt ohne Treffer.
    [Test]
    public async Task FolderResolution_ShouldOnlyProjectStoredDirectoryAssignments()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var snapshot = await SeedDirectoryAsync(context.Storage);
        var inactive = snapshot.Users.Single(user => !user.IsActive);
        var manipulated = snapshot.Users.Single(user => user.IsActive);
        var folder = new WorkflowFolder
        {
            Id = Guid.NewGuid(),
            Name = "Own",
            CreatedOn = DateTime.UtcNow,
            CreatedByUser = Guid.NewGuid(),
            Assignments =
            [
                new FolderAssignment
                {
                    SubjectKind = FolderSubjectKind.Group,
                    Subject = "/team/review",
                    Role = FolderRole.Steward
                },
                new FolderAssignment
                {
                    AssignmentMode = FolderAssignmentMode.Directory,
                    SubjectKind = FolderSubjectKind.User,
                    Subject = inactive.Id.ToString(),
                    DirectorySubject = new SubjectRef(DirectorySubjectKind.User, inactive.Id),
                    DisplayName = inactive.DisplayName,
                    Role = FolderRole.Editor
                }
            ]
        };
        await context.Storage.FolderStorage.StoreFolder(folder);
        using var client = context.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/identity-directory/folders/{folder.Id}/subjects/resolve",
            new
            {
                subjects = new[]
                {
                    new { kind = "user", id = inactive.Id },
                    new { kind = "user", id = manipulated.Id }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("result").GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("subject").GetProperty("id").GetGuid().Should().Be(inactive.Id);
        items[0].GetProperty("isSelectable").GetBoolean().Should().BeFalse();
    }

    // Testzweck: Fremde und unbekannte Workflow-Kontexte antworten identisch mit 404, damit
    // die Suche weder die Existenz des Workflows noch Verzeichnisdaten offenlegt.
    [Test]
    public async Task Search_ShouldHideForeignAndUnknownWorkflowsWithTheSameResponse()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await SeedDirectoryAsync(context.Storage);
        var folder = new WorkflowFolder
        {
            Id = Guid.NewGuid(),
            Name = "Foreign",
            CreatedOn = DateTime.UtcNow,
            CreatedByUser = Guid.NewGuid()
        };
        await context.Storage.FolderStorage.StoreFolder(folder);
        await context.Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            { DefinitionId = "workflow-foreign", Name = "Foreign", FolderId = folder.Id });
        using var client = context.CreateClient();

        var foreign = await client.GetAsync(
            "/identity-directory/workflows/workflow-foreign/subjects?query=anna&kind=user");
        var unknown = await client.GetAsync(
            "/identity-directory/workflows/workflow-unknown/subjects?query=anna&kind=user");

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var foreignProblem = await foreign.Content.ReadFromJsonAsync<ProblemDetails>();
        var unknownProblem = await unknown.Content.ReadFromJsonAsync<ProblemDetails>();
        foreignProblem.Should().NotBeNull();
        unknownProblem.Should().NotBeNull();
        foreignProblem!.Status.Should().Be(unknownProblem!.Status);
        foreignProblem.Title.Should().Be(unknownProblem.Title);
        foreignProblem.Detail.Should().Be(unknownProblem.Detail);
        foreignProblem.Type.Should().Be(unknownProblem.Type);
    }

    // Testzweck: Ungültige Suchart, zu kurzer Suchtext und übergroße Ergebnismengen werden vor
    // dem Verzeichniszugriff als dokumentierte Problem-Details-Fehler abgewiesen.
    [TestCase("query=a&kind=user", TestName = "Search_ShouldRejectShortQuery")]
    [TestCase("query=anna&kind=role", TestName = "Search_ShouldRejectUnknownKind")]
    [TestCase("query=anna&kind=user&limit=51", TestName = "Search_ShouldRejectExcessiveLimit")]
    public async Task Search_ShouldValidatePublicQueryContract(string query)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await SeedDirectoryAsync(context.Storage);
        await context.Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            { DefinitionId = "workflow-editable", Name = "Editable" });
        using var client = context.CreateClient(isModeler: true);

        var response = await client.GetAsync($"/identity-directory/workflows/workflow-editable/subjects?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    // Testzweck: Eine berechtigte Modellierungsansicht kann aktive und historische
    // stabile Referenzen exakt auflösen; deaktivierte Treffer bleiben nicht auswählbar.
    [Test]
    public async Task Resolve_ShouldReturnInactiveSubjectAsDisplayOnly()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var snapshot = await SeedDirectoryAsync(context.Storage);
        var active = snapshot.Users.Single(user => user.IsActive);
        var inactive = snapshot.Users.Single(user => !user.IsActive);
        await StoreWorkflowWithDirectorySubjectsAsync(context.Storage, inactive.Id, active.Id);
        using var client = context.CreateClient(isModeler: true);

        var response = await client.PostAsJsonAsync(
            "/identity-directory/workflows/workflow-editable/subjects/resolve",
            new
            {
                subjects = new[]
                {
                    new { kind = "user", id = inactive.Id },
                    new { kind = "user", id = active.Id }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = payload.GetProperty("result").GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("subject").GetProperty("id").GetGuid().Should().Be(inactive.Id);
        items[0].GetProperty("displayName").GetString().Should().Be("Anna Muster");
        items[0].GetProperty("isActive").GetBoolean().Should().BeFalse();
        items[0].GetProperty("isSelectable").GetBoolean().Should().BeFalse();
        items[1].GetProperty("isActive").GetBoolean().Should().BeTrue();
        items[1].GetProperty("isSelectable").GetBoolean().Should().BeTrue();
    }

    // Testzweck: Die exakte Anzeigeauflösung wird nicht zu einem unbeschränkten
    // Verzeichniszugriff; fremde Kontexte und mehr als 50 IDs werden abgewiesen.
    [Test]
    public async Task Resolve_ShouldHideForeignWorkflowAndRejectOversizedBatch()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var snapshot = await SeedDirectoryAsync(context.Storage);
        var folder = new WorkflowFolder
        {
            Id = Guid.NewGuid(), Name = "Foreign", CreatedOn = DateTime.UtcNow,
            CreatedByUser = Guid.NewGuid()
        };
        await context.Storage.FolderStorage.StoreFolder(folder);
        await context.Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            { DefinitionId = "workflow-foreign", Name = "Foreign", FolderId = folder.Id });
        await StoreWorkflowWithDirectorySubjectsAsync(
            context.Storage, snapshot.Users.Single(user => user.IsActive).Id);
        using var client = context.CreateClient(isModeler: true);
        using var regularClient = context.CreateClient();
        var subjects = Enumerable.Range(0, 51)
            .Select(_ => new { kind = "user", id = Guid.NewGuid() })
            .ToArray();

        var hidden = await regularClient.PostAsJsonAsync(
            "/identity-directory/workflows/workflow-foreign/subjects/resolve",
            new { subjects = subjects[..1] });
        var oversized = await client.PostAsJsonAsync(
            "/identity-directory/workflows/workflow-editable/subjects/resolve",
            new { subjects });
        var manipulated = await client.PostAsJsonAsync(
            "/identity-directory/workflows/workflow-editable/subjects/resolve",
            new
            {
                subjects = new[]
                {
                    new { kind = "user", id = snapshot.Users.Single(user => !user.IsActive).Id }
                }
            });

        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        oversized.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        oversized.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        manipulated.StatusCode.Should().Be(HttpStatusCode.OK);
        var manipulatedPayload = await manipulated.Content.ReadFromJsonAsync<JsonElement>();
        manipulatedPayload.GetProperty("result").GetProperty("items").GetArrayLength().Should().Be(0);
    }

    private static async Task StoreWorkflowWithDirectorySubjectsAsync(
        FilesystemStorageSystem.Storage storage,
        Guid assigneeId,
        Guid? candidateId = null)
    {
        const string definitionId = "workflow-editable";
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(),
            DefinitionId = definitionId,
            Hash = "directory-resolution-test",
            SavedByUser = AuthenticatedWorkflowTestContext.UserId,
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0),
            IsActive = false
        };
        await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            { DefinitionId = definitionId, Name = "Editable" });
        await storage.DefinitionStorage.StoreDefinition(definition);
        var candidates = candidateId is null ? "" : $" candidateUserIds=\"{candidateId}\"";
        await storage.DefinitionStorage.StoreBinary(definition.Id, $$"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                id="{{definitionId}}" targetNamespace="test">
              <bpmn:process id="Process" isExecutable="true">
                <bpmn:startEvent id="Start"><bpmn:outgoing>ToTask</bpmn:outgoing></bpmn:startEvent>
                <bpmn:sequenceFlow id="ToTask" sourceRef="Start" targetRef="Task" />
                <bpmn:userTask id="Task">
                  <bpmn:extensionElements>
                    <zeebe:formDefinition formKey="Approval" />
                    <flowzer:taskAssignment mode="directory" assigneeId="{{assigneeId}}"{{candidates}} />
                  </bpmn:extensionElements>
                  <bpmn:incoming>ToTask</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
                </bpmn:userTask>
                <bpmn:sequenceFlow id="ToEnd" sourceRef="Task" targetRef="End" />
                <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
              </bpmn:process>
            </bpmn:definitions>
            """);
    }

    private static async Task<DirectorySnapshot> SeedDirectoryAsync(FilesystemStorageSystem.Storage storage)
    {
        const string issuer = AuthenticatedWorkflowTestContext.Issuer;
        var activeUser = new DirectoryUser
        {
            Id = Guid.NewGuid(),
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = issuer,
            Subject = "subject-active",
            DisplayName = "Anna Muster",
            IsActive = true
        };
        var inactiveUser = new DirectoryUser
        {
            Id = Guid.NewGuid(),
            SourceKind = DirectorySourceKind.Keycloak,
            Issuer = issuer,
            Subject = "subject-inactive",
            DisplayName = "Anna Muster",
            IsActive = false
        };
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(),
            Issuer = issuer,
            CompletedAtUtc = DateTime.UtcNow,
            Users = [activeUser, inactiveUser]
        };
        (await storage.IdentityDirectoryStorage.TryStartSync(
            issuer,
            snapshot.GenerationId,
            snapshot.CompletedAtUtc,
            snapshot.CompletedAtUtc.AddMinutes(5))).Should().BeTrue();
        await storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
        return (await storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
    }
}
