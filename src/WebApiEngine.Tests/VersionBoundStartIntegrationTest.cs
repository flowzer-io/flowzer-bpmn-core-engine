using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Model;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Versionsbindung zwischen angezeigtem Formular, Start und persönlichem Replay.</summary>
[NonParallelizable]
public class VersionBoundStartIntegrationTest
{
    // Testzweck: Ein Start mit der tatsächlich angezeigten deployten Version bleibt möglich.
    [Test]
    public async Task MatchingVersion_ShouldStartExactlyThatVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var definition = await context.DeployAsync("assignee=\"bert\"");
        using var client = context.CreateClient();
        using var response = await client.PostAsJsonAsync(Path(definition.DefinitionId),
            new { expectedDefinitionId = definition.Id });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        payload!.Result!.DefinitionId.Should().Be(definition.Id);
    }

    // Testzweck: Eine fremde oder leere Versionskennung darf weder Start noch Reservierung erzeugen.
    [TestCase(false)]
    [TestCase(true)]
    public async Task WrongVersion_ShouldConflictWithoutCreatingAnInstance(bool empty)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var definition = await context.DeployAsync("assignee=\"bert\"");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "wrong-version");
        using var response = await client.PostAsJsonAsync(Path(definition.DefinitionId),
            new { expectedDefinitionId = empty ? Guid.Empty : Guid.NewGuid() });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("workflow.definition_changed");
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
        // Derselbe noch unbenutzte Schlüssel darf nach einem expliziten neuen Formularabruf verwendet werden.
        using var retry = await client.PostAsJsonAsync(Path(definition.DefinitionId),
            new { expectedDefinitionId = definition.Id });
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Nach Deployment darf ein noch offenes Formular nicht still die neue Version starten.
    [Test]
    public async Task RedeployedVersion_ShouldRejectTheOldDisplayedVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var old = await context.DeployAsync("assignee=\"bert\"");
        var latest = await RedeployAsync(context, old);
        using var client = context.CreateClient();
        using var response = await client.PostAsJsonAsync(Path(old.DefinitionId),
            new { expectedDefinitionId = old.Id });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
        using var current = await client.PostAsJsonAsync(Path(latest.DefinitionId),
            new { expectedDefinitionId = latest.Id });
        current.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ein bereits erfolgter Start wird persönlich identisch wiedergegeben,
    // auch wenn inzwischen eine andere Fassung deployt wurde; kein zweiter Vorgang.
    [TestCase(false)]
    [TestCase(true)]
    public async Task IdenticalReplay_ShouldReturnOriginalInstanceAfterRedeployment(bool withReference)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var original = await context.DeployAsync("assignee=\"bert\"");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "bound-once");
        object body = withReference
            ? new { expectedDefinitionId = original.Id, externalReference = "case:1234567" }
            : new { expectedDefinitionId = original.Id };
        using var first = await client.PostAsJsonAsync(Path(original.DefinitionId), body);
        var started = await first.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        var latest = await RedeployAsync(context, original);
        using var replay = await client.PostAsJsonAsync(Path(original.DefinitionId), body);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var repeated = await replay.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        repeated!.Result!.InstanceId.Should().Be(started!.Result!.InstanceId);
        repeated.Result.DefinitionId.Should().Be(original.Id);
        using var changed = await client.PostAsJsonAsync(Path(latest.DefinitionId),
            new { expectedDefinitionId = latest.Id });
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: Auch ein Formularabruf bindet sich an den angezeigten Katalogstand,
    // statt aus einer inzwischen anderen Version ein unerwartetes Formular zu liefern.
    [Test]
    public async Task StartFormRead_ShouldCheckExpectedVersionBeforeReturningSchema()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var definition = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        using var client = context.CreateClient();
        var path = $"/definition/meta/{definition.DefinitionId}/start-form?expectedDefinitionId=";
        using var wrong = await client.GetAsync(path + Guid.NewGuid());
        wrong.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var correct = await client.GetAsync(path + definition.Id);
        correct.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Frei belegbare Legacy-Variablen dürfen keinen versionsgebundenen
    // Request imitieren; derselbe persönliche Schlüssel bleibt genau einem Inhalt zugeordnet.
    [TestCase(false)]
    [TestCase(true)]
    public async Task LegacyVariablesAndBoundRequest_ShouldNeverAliasTheSameRequestHash(bool boundFirst)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var definition = await context.DeployAsync("assignee=\"bert\"");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "no-cross-contract-alias");
        object bound = new { expectedDefinitionId = definition.Id };
        object legacy = new { variables = new { expectedDefinitionId = definition.Id, variables = (object?)null } };
        using var first = await client.PostAsJsonAsync(Path(definition.DefinitionId), boundFirst ? bound : legacy);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using var second = await client.PostAsJsonAsync(Path(definition.DefinitionId), boundFirst ? legacy : bound);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: Ein altes Formular erhält den expliziten Versionskonflikt auch,
    // wenn die neu deployte Fassung ausschließlich per Nachricht startet.
    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangedStartKind_ShouldReportVersionConflictBeforeResolvingTheNewProcess(bool formRead)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var previous = await context.DeployAsync("assignee=\"bert\"");
        await RedeployAsync(context, previous, messageOnly: true);
        using var client = context.CreateClient();
        using var response = formRead
            ? await client.GetAsync($"/definition/meta/{previous.DefinitionId}/start-form?expectedDefinitionId={previous.Id}")
            : await client.PostAsJsonAsync(Path(previous.DefinitionId), new { expectedDefinitionId = previous.Id });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("workflow.definition_changed");
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
    }

    private static string Path(string definitionId) => $"/definition/meta/{definitionId}/instance";

    /// <summary>Ein echter Versionswechsel, kein zweites Anlegen desselben Katalogeintrags.</summary>
    private static async Task<BpmnDefinition> RedeployAsync(AuthenticatedWorkflowTestContext context,
        BpmnDefinition previous, bool messageOnly = false)
    {
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(), DefinitionId = previous.DefinitionId, PreviousGuid = previous.Id,
            Hash = "synthetic-redeployed", SavedByUser = AuthenticatedWorkflowTestContext.UserId,
            SavedOn = DateTime.UtcNow, Version = new Model.Version(2, 0), IsActive = false
        };
        await context.Storage.DefinitionStorage.StoreDefinition(definition);
        var binary = await context.Storage.DefinitionStorage.GetBinary(previous.Id);
        if (messageOnly)
            binary = binary.Replace("<bpmn:process ", "<bpmn:message id=\"NoManualEntry\" name=\"MessageOnly\"/><bpmn:process ")
                .Replace("<bpmn:startEvent id=\"Start\">", "<bpmn:startEvent id=\"Start\"><bpmn:messageEventDefinition messageRef=\"NoManualEntry\"/>");
        await context.Storage.DefinitionStorage.StoreBinary(definition.Id, binary.Replace("assignee=\"bert\"", "assignee=\"anna\""));
        await context.Services.GetRequiredService<BpmnBusinessLogic>().DeployDefinition(definition);
        return definition;
    }
}
