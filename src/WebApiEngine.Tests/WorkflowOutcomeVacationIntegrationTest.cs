using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Fachlicher Ergebnisvertrag am aktuellen Originaldemo: echte Stimmen statt Variablenheuristik.</summary>
[NonParallelizable]
public sealed class WorkflowOutcomeVacationIntegrationTest
{
    // Testzweck: Keine Einzel-/Doppelstimme reicht; GET und List liefern erst nach drei Zustimmungen Approved.
    [Test]
    public async Task ThreeApprovals_ShouldExposeApprovedOnlyAfterLastVote()
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync();
        await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Unknown);
        foreach (var node in new[] { "Task_Supervisor", "Task_Personnel" })
        {
            await context.Demo.CompleteAsync(node);
            await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Unknown);
        }
        await context.Demo.CompleteAsync("Task_Substitute");
        await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Approved);
        var instance = await context.Demo.InstanceAsync();
        instance.State.Should().Be(Model.ProcessInstanceState.Completed);
    }

    // Testzweck: Jede Rolle kann ablehnen. Unterbrochene innere Prüftokens dürfen das reguläre Rootende nicht verbergen.
    [TestCase("Task_Supervisor")]
    [TestCase("Task_Personnel")]
    [TestCase("Task_Substitute")]
    public async Task EachRoleRejection_ShouldExposeRejected(string node)
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync();
        await context.Demo.CompleteAsync(node, "reject");
        await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Rejected);
    }

    // Testzweck: Alte Zustimmungen zählen nicht nach Rückgabe; Korrektur und neue Runde bleiben Unknown bis zum Rootabschluss.
    [TestCase("Task_Supervisor")]
    [TestCase("Task_Personnel")]
    [TestCase("Task_Substitute")]
    public async Task ReturnCorrectionAndNewRound_ShouldExposeOnlyCurrentRootOutcome(string node)
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync();
        await context.Demo.CompleteAsync(node, "return");
        await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Unknown);
        await context.Demo.CompleteAsync("Task_Correction", null, context.Demo.Application());
        await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Unknown);
        await context.ApproveAsync();
        await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Approved);
    }

    // Testzweck: Persönlicher Rückzug und Betriebsabbruch sind technische Beendigungen, niemals fachliche Ablehnung.
    [TestCase(false)]
    [TestCase(true)]
    public async Task WithdrawalOrOperatorCancel_ShouldRemainUnknown(bool cancel)
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync();
        using var actor = context.Demo.Client(isOperator: cancel);
        using var response = await actor.PostAsync($"/instance/{context.Demo.InstanceId}/{(cancel ? "cancel" : "withdraw")}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Unknown);
    }

    // Testzweck: Geänderter Inhalt unter derselben GUID ist keine reviewte Version; XML-Hashmismatch darf nie positiv bleiben.
    [Test]
    public async Task ReplacedBinaryUnderSameGuid_ShouldExposeUnknown()
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Approved);
        var xml = await context.Demo.Storage.DefinitionStorage.GetBinary(context.Demo.DefinitionId);
        await context.Demo.Storage.DefinitionStorage.StoreBinary(context.Demo.DefinitionId, xml + "\n");
        await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Unknown);
    }

    // Testzweck: Fehlende gespeicherte Version oder BPMN-Binary ist Unknown; die Versionskennung wird nicht geraten.
    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingBoundDefinitionOrBinary_ShouldRemainUnknown(bool deleteBinary)
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        if (deleteBinary) await context.Demo.Storage.DefinitionStorage.DeleteBinary(context.Demo.DefinitionId);
        else await context.Demo.Storage.DefinitionStorage.DeleteDefinition(context.Demo.DefinitionId);
        await AssertHttpAsync(context, ProcessInstanceOutcomeDto.Unknown);
    }

    private static async Task AssertHttpAsync(WorkflowOutcomeTestContext context, ProcessInstanceOutcomeDto expected)
    {
        using var owner = context.Demo.Client();
        var overview = await owner.GetFromJsonAsync<JsonElement>($"/instance/{context.Demo.InstanceId}");
        var list = await owner.GetFromJsonAsync<JsonElement>("/instance");
        foreach (var result in new[] { overview.GetProperty("result"), list.GetProperty("result").EnumerateArray().Single() })
        {
            result.GetProperty("outcome").GetString().Should().Be(expected.ToString());
            result.GetProperty("canInspect").GetBoolean().Should().BeFalse();
            result.GetProperty("tokens").GetArrayLength().Should().Be(0);
            result.TryGetProperty("failureReason", out _).Should().BeFalse();
            result.TryGetProperty("variables", out _).Should().BeFalse();
        }
    }
}
