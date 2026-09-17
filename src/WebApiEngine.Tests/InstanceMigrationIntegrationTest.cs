using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Der HTTP-Vertrag der Instanzmigration: Betriebsrecht, Statuscodes und die Wirkung, die ein
/// Betreiber danach in der Konsole sieht — Formular, Entwurf, Verlauf und Diagramm.
/// </summary>
[NonParallelizable]
public sealed class InstanceMigrationIntegrationTest
{
    private const string OtherWorkflowId = "Definitions_Migration_Other";

    // Testzweck: Der vollstaendige Weg ueber HTTP — uebernommene Aufgabe bleibt uebernommen,
    // Formular und Diagramm kommen aus der Zielversion, der Verlauf bleibt vollstaendig, und
    // die Instanz folgt danach dem neuen Modell.
    [Test]
    public async Task Migration_ShouldLiftAClaimedInstanceAndFollowTheTargetModel()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceMigrationScenarios.DeployAsync(
            provider, engine, new Model.Version(1, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: false));
        var instance = await InstanceMigrationScenarios.StartAsync(engine, "left");
        var review = (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId)).Single();

        using var client = context.CreateClient(isOperator: true);
        using var claimed = await client.PostAsJsonAsync(
            $"/usertask/{review.Id}/claim", new UserTaskClaimRequestDto { ExpectedRevision = 0 });
        claimed.StatusCode.Should().Be(HttpStatusCode.OK);

        // Die Zielversion bindet ein anderes Formular und haengt eine zweite Aufgabe an.
        var target = await InstanceMigrationScenarios.DeployAsync(
            provider, engine, new Model.Version(2, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.SecondForm, withApprove: true),
            additionalForms: [InstanceMigrationScenarios.SecondForm]);

        using var previewResponse = await client.PostAsJsonAsync("/instance/migration/preview",
            new InstanceMigrationPreviewRequestDto { InstanceIds = [instance.InstanceId] });
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = (await previewResponse.Content
            .ReadFromJsonAsync<ApiStatusResult<InstanceMigrationPreviewDto>>())!.Result!;
        preview.RelatedDefinitionId.Should().Be(InstanceMigrationScenarios.MetaDefinitionId);
        preview.RelatedDefinitionName.Should().Be("Migration");
        preview.SourceVersion.Should().Be(new VersionDto(1, 0));
        preview.TargetDefinitionId.Should().Be(target.Id);
        preview.TargetVersion.Should().Be(new VersionDto(2, 0));
        preview.Instances.Should().ContainSingle().Which.Migratable.Should().BeTrue();
        preview.Instances[0].Notices.Select(notice => notice.Code)
            .Should().Contain(InstanceMigrationCodes.UserTaskFormChanged);

        using var migrateResponse = await client.PostAsJsonAsync("/instance/migration",
            new InstanceMigrationRequestDto
            {
                InstanceIds = [instance.InstanceId], TargetDefinitionId = target.Id
            });
        migrateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await migrateResponse.Content
            .ReadFromJsonAsync<ApiStatusResult<InstanceMigrationResultDto>>())!.Result!;
        result.TargetVersion.Should().Be(new VersionDto(2, 0));
        result.Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();

        var migratedInstance = (await client.GetFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>(
            $"/instance/{instance.InstanceId}"))!.Result!;
        migratedInstance.DefinitionId.Should().Be(target.Id);
        migratedInstance.DefinitionVersion.Should().Be(new VersionDto(2, 0));

        var task = (await client.GetFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto>>(
            $"/usertask/{review.Id}"))!.Result!;
        task.Id.Should().Be(review.Id);
        task.DefinitionVersion.Should().Be(new VersionDto(2, 0));
        task.WorkState.Claimed.Should().BeTrue("the migration must not release the task");
        task.WorkState.IsAssignedToCurrentUser.Should().BeTrue();

        // Das Formular wird wie immer ueber Version und Form-Key aufgeloest und stammt damit
        // aus der Zielversion; sonst bearbeitete die Aufgabe weiter das alte Schema.
        var form = (await client.GetFromJsonAsync<ApiStatusResult<FormDto>>(
            $"/usertask/{review.Id}/form"))!.Result!;
        form.FormData.Should().Contain("other");

        var data = new ExpandoObject();
        ((IDictionary<string, object?>)data)["other"] = "fertig";
        using var completed = await client.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            ProcessInstanceId = instance.InstanceId, TokenId = review.Token.Id,
            FlowNodeId = "Review", Data = data
        });
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId))
            .Should().ContainSingle().Which.Token.CurrentFlowNode!.Id.Should().Be("Approve");
        (await InstanceMigrationScenarios.InstanceAsync(provider, instance.InstanceId))
            .Migrations.Should().ContainSingle();

        var diagram = (await client.GetFromJsonAsync<ApiStatusResult<RuntimeDiagramDto>>(
            $"/instance/{instance.InstanceId}/runtime-diagram"))!.Result!;
        diagram.DefinitionId.Should().Be(target.Id);
        diagram.DiagramXml.Should().Contain("Approve", "the diagram is the one of the target version");
        diagram.Events.Should().Contain(item =>
            item.FlowNodeId == "Review" && item.State == FlowNodeStateDto.Completed);
        diagram.Events.Should().OnlyHaveUniqueItems(item =>
            new { item.FlowNodeId, item.State, item.OccurredAtUtc });
    }

    // Testzweck: Was vor dem Umzug geschah, bleibt im Laufzeitdiagramm sichtbar. Der bereits
    // abgeschlossene Schritt wurde ausschliesslich unter der Quellversion festgehalten; ein
    // Filter auf die aktuelle Version allein liesse die halbe Geschichte verschwinden.
    [Test]
    public async Task Migration_ShouldKeepTheHistoryOfEarlierVersionsInTheRuntimeDiagram()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceMigrationScenarios.DeployAsync(
            provider, engine, new Model.Version(1, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: true));
        var instance = await InstanceMigrationScenarios.StartAsync(engine, "left");
        var review = (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId)).Single();

        using var client = context.CreateClient(isOperator: true);
        var data = new ExpandoObject();
        ((IDictionary<string, object?>)data)["answer"] = "erledigt";
        using var completed = await client.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            ProcessInstanceId = instance.InstanceId, TokenId = review.Token.Id,
            FlowNodeId = "Review", Data = data
        });
        completed.StatusCode.Should().Be(HttpStatusCode.OK);

        var target = await InstanceMigrationScenarios.DeployAsync(
            provider, engine, new Model.Version(2, 0),
            InstanceMigrationScenarios.Xml(
                InstanceMigrationScenarios.FirstForm, withApprove: true, secondNodeId: "SecondRenamed"));
        using var migrated = await client.PostAsJsonAsync("/instance/migration",
            new InstanceMigrationRequestDto
            {
                InstanceIds = [instance.InstanceId], TargetDefinitionId = target.Id
            });
        migrated.StatusCode.Should().Be(HttpStatusCode.OK);

        var diagram = (await client.GetFromJsonAsync<ApiStatusResult<RuntimeDiagramDto>>(
            $"/instance/{instance.InstanceId}/runtime-diagram"))!.Result!;

        diagram.Events.Should().Contain(item =>
                item.FlowNodeId == "Review" && item.State == FlowNodeStateDto.Active,
            "this fact was only ever written under the source version");
        diagram.Events.Should().Contain(item =>
            item.FlowNodeId == "Approve" && item.State == FlowNodeStateDto.Active);
        diagram.Events.Should().OnlyHaveUniqueItems(item =>
            new { item.FlowNodeId, item.State, item.OccurredAtUtc });
    }

    // Testzweck: Bleibt die Formularbindung gleich, ist der private Entwurf danach unveraendert
    // lesbar — die Bindungspruefung des Entwurfs darf nicht in einen 500 laufen.
    [Test]
    public async Task Migration_ShouldKeepAnIdenticalDraftReadableOverHttp()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceMigrationScenarios.DeployAsync(
            provider, engine, new Model.Version(1, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: false));
        var instance = await InstanceMigrationScenarios.StartAsync(engine, "left");
        var review = (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId)).Single();

        using var client = context.CreateClient(isOperator: true);
        using var saved = await client.PutAsJsonAsync($"/usertask/{review.Id}/draft", new
        {
            expectedRevision = 0,
            data = new { answer = "halb fertig" }
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);

        var target = await InstanceMigrationScenarios.DeployAsync(
            provider, engine, new Model.Version(2, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: true));
        using var migrated = await client.PostAsJsonAsync("/instance/migration",
            new InstanceMigrationRequestDto
            {
                InstanceIds = [instance.InstanceId], TargetDefinitionId = target.Id
            });
        migrated.StatusCode.Should().Be(HttpStatusCode.OK);

        using var draft = await client.GetAsync($"/usertask/{review.Id}/draft");
        draft.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        payload.GetProperty("revision").GetInt64().Should().Be(1);
        payload.GetProperty("data").GetProperty("answer").GetString().Should().Be("halb fertig");
    }

    // Testzweck: Nennt der Aufrufer eine inzwischen ueberholte Zielversion, ist das ein
    // Zustandskonflikt — und es wird nichts veraendert.
    [Test]
    public async Task Migration_ShouldReturnConflict_WhenTheNamedTargetIsStale()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        var source = await InstanceMigrationScenarios.DeployAsync(
            provider, engine, new Model.Version(1, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: false));
        var instance = await InstanceMigrationScenarios.StartAsync(engine, "left");
        var stale = await InstanceMigrationScenarios.DeployAsync(
            provider, engine, new Model.Version(2, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: true));
        await InstanceMigrationScenarios.DeployAsync(
            provider, engine, new Model.Version(3, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: true));

        using var client = context.CreateClient(isOperator: true);
        using var response = await client.PostAsJsonAsync("/instance/migration",
            new InstanceMigrationRequestDto
            {
                InstanceIds = [instance.InstanceId], TargetDefinitionId = stale.Id
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await InstanceMigrationScenarios.InstanceAsync(provider, instance.InstanceId))
            .DefinitionId.Should().Be(source.Id);
    }

    // Testzweck: Unbrauchbare Zusammenstellungen werden als Ganzes abgelehnt, bevor irgendetwas
    // geprueft oder veraendert wird.
    [TestCase("empty")]
    [TestCase("duplicate")]
    [TestCase("mixed-workflows")]
    [TestCase("mixed-versions")]
    public async Task MigrationPreview_ShouldReturnUnprocessableEntity_ForUnusableRequests(string kind)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceMigrationScenarios.DeployAsync(
            provider, engine, new Model.Version(1, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: false));
        var first = await InstanceMigrationScenarios.StartAsync(engine, "left");

        Guid[] instanceIds;
        switch (kind)
        {
            case "empty":
                instanceIds = [];
                break;
            case "duplicate":
                instanceIds = [first.InstanceId, first.InstanceId];
                break;
            case "mixed-workflows":
                await InstanceMigrationScenarios.DeployAsync(
                    provider, engine, new Model.Version(1, 0),
                    InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: false),
                    metaDefinitionId: OtherWorkflowId);
                var other = await InstanceMigrationScenarios.StartAsync(engine, "left", OtherWorkflowId);
                instanceIds = [first.InstanceId, other.InstanceId];
                break;
            default:
                await InstanceMigrationScenarios.DeployAsync(
                    provider, engine, new Model.Version(2, 0),
                    InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: true));
                var newer = await InstanceMigrationScenarios.StartAsync(engine, "left");
                instanceIds = [first.InstanceId, newer.InstanceId];
                break;
        }

        using var client = context.CreateClient(isOperator: true);
        using var response = await client.PostAsJsonAsync("/instance/migration/preview",
            new InstanceMigrationPreviewRequestDto { InstanceIds = instanceIds });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    // Testzweck: Eine unbekannte Instanz ist 404 — wie ueberall sonst an dieser Ressource.
    [Test]
    public async Task MigrationPreview_ShouldReturnNotFound_ForUnknownInstances()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateClient(isOperator: true);

        using var response = await client.PostAsJsonAsync("/instance/migration/preview",
            new InstanceMigrationPreviewRequestDto { InstanceIds = [Guid.NewGuid()] });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    // Testzweck: Der Versionswechsel greift in fremde Vorgaenge ein und verlangt wie der
    // Instanzabbruch eine Anmeldung und das Betriebsrecht.
    [TestCase("/instance/migration/preview")]
    [TestCase("/instance/migration")]
    public async Task Migration_ShouldRequireAnOperator(string route)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var withoutRole = context.CreateClient(isOperator: false);

        using var response = await withoutRole.PostAsJsonAsync(route, new InstanceMigrationRequestDto
        {
            InstanceIds = [Guid.NewGuid()], TargetDefinitionId = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Testzweck: Ohne aufgeloesten Benutzerkontext bleibt der Endpunkt verschlossen.
    [TestCase("/instance/migration/preview")]
    [TestCase("/instance/migration")]
    public async Task Migration_ShouldReturnUnauthorized_WithoutUserContext(string route)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateAnonymousClient();

        using var response = await client.PostAsJsonAsync(route, new InstanceMigrationRequestDto
        {
            InstanceIds = [Guid.NewGuid()], TargetDefinitionId = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Das Wegstueck "migration" darf nicht als Instanzkennung gelesen werden; sonst
    // beantwortete eine unbeteiligte Route den Aufruf.
    [Test]
    public async Task MigrationRoute_ShouldNotBeReadAsAnInstanceId()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateClient(isOperator: true);

        using var byRoute = await client.PostAsJsonAsync("/instance/migration/preview",
            new InstanceMigrationPreviewRequestDto { InstanceIds = [] });
        using var cancel = await client.PostAsync("/instance/migration/cancel", content: null);

        // Die Trockenlaufroute antwortet fachlich, nicht mit einem Routingfehler.
        byRoute.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        // "migration" ist keine Instanzkennung; der Abbruchweg findet nichts.
        cancel.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }
}
