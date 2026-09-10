using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace WebApiEngine.Tests;

/// <summary>Objektberechtigter Detailvertrag für hostneutrale Human-Task-Einbettungen.</summary>
[NonParallelizable]
public sealed class UserTaskEmbeddingContractTest
{
    // Testzweck: Eine berechtigte Host-Oberfläche kann genau eine Aufgabe per stabiler ID
    // laden und erhält dabei dieselbe datensparsame Formularprojektion wie in der Liste.
    [Test]
    public async Task GetTask_ShouldReturnTheVisibleTaskWithProjectedContext()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", """
            {
              "flowzer": { "contractVersion": 1 },
              "components": [
                { "type": "textfield", "key": "visible", "label": "Sichtbar", "input": true }
              ]
            }
            """);
        dynamic variables = new ExpandoObject();
        variables.visible = "freigegeben";
        variables.secret = "nicht freigegeben";
        var task = await context.StartAsync("assignee=\"bert\"", variables);
        using var client = context.CreateClient(username: "bert");

        using var response = await client.GetAsync($"/usertask/{task.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        result.GetProperty("id").GetGuid().Should().Be(task.Id);
        result.GetProperty("workState").GetProperty("canWork").GetBoolean().Should().BeTrue();
        var projected = result.GetProperty("token").GetProperty("variables");
        projected.GetProperty("visible").GetString().Should().Be("freigegeben");
        projected.TryGetProperty("secret", out _).Should().BeFalse();
    }

    // Testzweck: Fremde und unbekannte Task-IDs liefern denselben 404-Problem-Details-
    // Vertrag, sodass der Detailendpunkt keine Existenz fremder Aufgaben offenlegt.
    [Test]
    public async Task GetTask_ShouldHideForeignAndMissingTasksIdentically()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"bert\"");
        using var foreign = context.CreateClient(userId: Guid.NewGuid(), username: "anna");

        using var forbidden = await foreign.GetAsync($"/usertask/{task.Id}");
        using var missing = await foreign.GetAsync($"/usertask/{Guid.NewGuid()}");

        forbidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forbidden.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var forbiddenProblem = await forbidden.Content.ReadFromJsonAsync<JsonElement>();
        var missingProblem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        forbiddenProblem.GetProperty("title").GetString().Should().Be("User task unavailable");
        missingProblem.GetProperty("title").GetString().Should().Be("User task unavailable");
        forbiddenProblem.GetProperty("detail").GetString().Should()
            .Be(missingProblem.GetProperty("detail").GetString());
    }

    // Testzweck: Nach einem Claim verschwindet der Task-Deep-Link für frühere
    // Gruppenkandidaten, während der tatsächliche Bearbeiter ihn weiter laden kann.
    [Test]
    public async Task GetTask_ShouldFollowTheExclusiveClaimState()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("candidateGroups=\"review\"");
        using var bert = context.CreateClient(username: "bert");
        using var anna = context.CreateClient(userId: Guid.NewGuid(), username: "anna");
        (await bert.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }))
            .EnsureSuccessStatusCode();

        using var formerCandidate = await anna.GetAsync($"/usertask/{task.Id}");
        using var actualWorker = await bert.GetAsync($"/usertask/{task.Id}");

        formerCandidate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        actualWorker.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await actualWorker.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        result.GetProperty("workState").GetProperty("isAssignedToCurrentUser")
            .GetBoolean().Should().BeTrue();
    }
}
