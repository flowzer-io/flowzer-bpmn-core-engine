using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace WebApiEngine.Tests;

/// <summary>Ausführbare JSON-Vertragsregressionen am echten Testserver, ohne neue DTO-Typen im Testcode.</summary>
[NonParallelizable]
public sealed class WorkflowOutcomeHttpIntegrationTest
{
    // Testzweck: Laufende persönliche GET-/List-Projektionen liefern explizit Unknown, ohne Diagnoseinhalte.
    [Test]
    public async Task PersonalOverviewAndList_ShouldExposeUnknownWithoutTokens()
    {
        using var demo = new TickyTaskVacationTestContext();
        await demo.InitializeAsync(); await demo.StartAsync();
        using var owner = demo.Client();
        var overview = await owner.GetFromJsonAsync<JsonElement>($"/instance/{demo.InstanceId}");
        AssertOutcome(overview.GetProperty("result"), "Unknown");
        var list = await owner.GetFromJsonAsync<JsonElement>("/instance");
        AssertOutcome(list.GetProperty("result").EnumerateArray().Single(), "Unknown");
    }

    // Testzweck: Der bestehende HTTP-Startvertrag enthält Unknown, ohne eine neue Endpoint-/Typabhängigkeit.
    [Test]
    public async Task StartResponse_ShouldExposeUnknown()
    {
        using var demo = new TickyTaskVacationTestContext();
        await demo.InitializeAsync();
        using var owner = demo.Client();
        using var response = await owner.PostAsJsonAsync($"/definition/meta/{TickyTaskVacationTestContext.DefinitionKey}/instance",
            new { expectedDefinitionId = demo.DefinitionId, variables = demo.Application() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        AssertOutcome(json.GetProperty("result"), "Unknown");
    }

    // Testzweck: Technischer Abschluss ohne reviewte serverseitige Versionsbindung ist nie eine Genehmigung.
    [Test]
    public async Task UnconfiguredCompletedWorkflow_ShouldRemainUnknown()
    {
        using var demo = new TickyTaskVacationTestContext();
        await demo.InitializeAsync(); await demo.StartAsync();
        foreach (var node in new[] { "Task_Supervisor", "Task_Personnel", "Task_Substitute" })
            await demo.CompleteAsync(node);
        using var owner = demo.Client();
        var json = await owner.GetFromJsonAsync<JsonElement>($"/instance/{demo.InstanceId}");
        AssertOutcome(json.GetProperty("result"), "Unknown");
    }

    // Testzweck: Die additive Minimalprojektion erweitert keine Objektberechtigung.
    [Test]
    public async Task UnrelatedUser_ShouldStillGet404()
    {
        using var demo = new TickyTaskVacationTestContext();
        await demo.InitializeAsync(); await demo.StartAsync();
        using var stranger = demo.Client(Guid.NewGuid());
        using var response = await stranger.GetAsync($"/instance/{demo.InstanceId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void AssertOutcome(JsonElement result, string expected)
    {
        result.TryGetProperty("outcome", out var outcome).Should().BeTrue("das additive Outcome muss im echten JSON vorhanden sein");
        outcome.GetString().Should().Be(expected);
        result.GetProperty("canInspect").GetBoolean().Should().BeFalse();
        result.GetProperty("tokens").GetArrayLength().Should().Be(0);
        result.TryGetProperty("failureReason", out _).Should().BeFalse();
    }
}
