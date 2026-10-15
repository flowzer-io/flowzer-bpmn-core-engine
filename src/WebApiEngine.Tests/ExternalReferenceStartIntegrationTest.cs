using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Model;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Hostneutrale Herkunftsreferenz ohne Formularvariable oder Berechtigungswirkung.</summary>
[NonParallelizable]
public class ExternalReferenceStartIntegrationTest
{
    // Testzweck: Der echte HTTP-Start speichert nur die ausdrückliche Referenz intern;
    // Speichern/Neuladen erhält sie, öffentliche DTOs und Formularwerte geben sie nicht aus.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Start_ShouldPersistReferenceOutsideVariablesAndPublicProjection(bool isOperator)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var definition = await context.DeployAsync("assignee=\"bert\"");
        using var client = context.CreateClient(isOperator: isOperator);
        const string reference = "case:1234567";
        using var response = await client.PostAsJsonAsync(Path(definition.DefinitionId),
            new { expectedDefinitionId = definition.Id, externalReference = reference, variables = new { answer = "unchanged" } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain(reference).And.NotContain("externalReference");
        var result = JsonSerializer.Deserialize<ApiStatusResult<ProcessInstanceInfoDto>>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Result!;
        var stored = await context.Storage.InstanceStorage.GetProcessInstance(result.InstanceId);
        var master = stored.Tokens.Single(token => token.ParentTokenId is null);
        master.ExternalReference.Should().Be(reference);
        JsonSerializer.Serialize(master.Variables).Should().NotContain(reference).And.Contain("unchanged");
        master.Initiator.Should().NotBeNull();
    }

    // Testzweck: Derselbe persönliche Schlüssel darf weder eine andere Referenz noch
    // einen Altaufruf ohne Referenz übernehmen; der echte Replay erzeugt nur eine Instanz.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Reference_ShouldBelongToIdempotencyContent(bool removeReference)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var definition = await context.DeployAsync("assignee=\"bert\"");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "external-reference-once");
        var body = new { expectedDefinitionId = definition.Id, externalReference = "case:1234567" };
        using var first = await client.PostAsJsonAsync(Path(definition.DefinitionId), body);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using var same = await client.PostAsJsonAsync(Path(definition.DefinitionId), body);
        same.StatusCode.Should().Be(HttpStatusCode.OK);
        var a = await first.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        var b = await same.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        b!.Result!.InstanceId.Should().Be(a!.Result!.InstanceId);
        using var changed = removeReference
            ? await client.PostAsJsonAsync(Path(definition.DefinitionId), new { expectedDefinitionId = definition.Id })
            : await client.PostAsJsonAsync(Path(definition.DefinitionId), new { expectedDefinitionId = definition.Id, externalReference = "case:7654321" });
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: Leere, überlange und steuerzeichenhaltige Referenzen sind vor jeder
    // Mutation ungültig; der unverbrauchte persönliche Startkey bleibt verwendbar.
    [TestCase("empty")]
    [TestCase("blank")]
    [TestCase("long")]
    [TestCase("control")]
    public async Task InvalidReference_ShouldNotCreateInstanceOrConsumeKey(string invalid)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var definition = await context.DeployAsync("assignee=\"bert\"");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "invalid-reference");
        var reference = invalid switch { "empty" => "", "blank" => " ", "long" => new string('x', 129), _ => "case:private\nvalue" };
        using var denied = await client.PostAsJsonAsync(Path(definition.DefinitionId),
            new { expectedDefinitionId = definition.Id, externalReference = reference });
        denied.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await denied.Content.ReadAsStringAsync()).Should().NotContain("private");
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
        using var valid = await client.PostAsJsonAsync(Path(definition.DefinitionId),
            new { expectedDefinitionId = definition.Id, externalReference = "case:1234567" });
        valid.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ein erfundener Herkunftswert ist ausdrücklich kein Benutzer-/Betriebsrecht.
    [Test]
    public async Task Reference_ShouldNotAllowAnonymousStart()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var definition = await context.DeployAsync("assignee=\"bert\"");
        using var client = context.CreateAnonymousClient();
        using var response = await client.PostAsJsonAsync(Path(definition.DefinitionId),
            new { expectedDefinitionId = definition.Id, externalReference = "case:1234567" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
    }

    private static string Path(string id) => $"/definition/meta/{id}/instance";
}
