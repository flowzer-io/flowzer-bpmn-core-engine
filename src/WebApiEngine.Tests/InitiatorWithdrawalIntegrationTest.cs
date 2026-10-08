using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StorageSystem;
using FilesystemStorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Persönliches Zurückziehen ohne Betriebsrechte, Fremdzugriff oder automatische Kompensation.</summary>
[NonParallelizable]
public class InitiatorWithdrawalIntegrationTest
{
    // Testzweck: Technische Ablagefehler sind kein definitiver Fachabschlusskonflikt;
    // der Host muss 500 erkennen und darf daraus keinen angeblichen Endzustand ableiten.
    [Test]
    public async Task TechnicalStorageFailure_ShouldNotBecomeFinishedConflict()
    {
        var provider = new FaultingProvider();
        using var context = new AuthenticatedWorkflowTestContext(configureServices: services =>
        {
            services.RemoveAll<ITransactionalStorageProvider>();
            services.AddSingleton<ITransactionalStorageProvider>(provider);
        });
        using var owner = context.CreateClient();
        var id = await StartOwnAsync(context, owner);
        provider.Fail = true;
        using var response = await owner.PostAsync($"/instance/{id}/withdraw", null);
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await context.Storage.InstanceStorage.GetProcessInstance(id)).IsFinished.Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("synthetic storage failure");
    }

    // Testzweck: Nur der tatsächliche Initiator darf laufende Arbeit zurückziehen;
    // aktive Aufgaben verschwinden und der verifizierte Akteur bleibt intern auditiert.
    [Test]
    public async Task OwnWithdrawal_ShouldCancelTasksAndPersistPersonalAuditWithoutPublicIdentity()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var owner = context.CreateClient();
        var id = await StartOwnAsync(context, owner);
        using var response = await owner.PostAsync($"/instance/{id}/withdraw", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = body.GetProperty("result");
        result.GetProperty("state").GetInt32().Should().Be((int)ProcessInstanceStateDto.Terminated);
        result.GetProperty("wasWithdrawn").GetBoolean().Should().BeTrue();
        result.GetProperty("tokens").GetArrayLength().Should().Be(0);
        (await context.Storage.SubscriptionStorage.GetAllUserTasks(id)).Should().BeEmpty();
        var audit = WithdrawalAudit(await context.Storage.InstanceStorage.GetProcessInstance(id));
        audit.GetProperty("ActorUserId").GetGuid().Should().Be(AuthenticatedWorkflowTestContext.UserId);
        audit.GetProperty("Actor").GetProperty("Issuer").GetString().Should().Be(AuthenticatedWorkflowTestContext.Issuer);
        audit.GetProperty("Actor").GetProperty("Subject").GetString().Should().Be(AuthenticatedWorkflowTestContext.UserId.ToString());
        audit.GetProperty("WithdrawnAtUtc").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15));
        result.TryGetProperty("withdrawal", out _).Should().BeFalse();
    }

    // Testzweck: Namen, aktueller Aufgabenbezug und selbst Operatorrecht ersetzen
    // beim persönlichen Rückzug niemals die gespeicherte Issuer-/Subject-Bindung.
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task ForeignActor_ShouldSeeSameNotFoundAsMissingAndNeverCancel(bool isOperator, bool otherIssuer)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var owner = context.CreateClient();
        var id = await StartOwnAsync(context, owner);
        using var foreign = context.CreateClient(isOperator: isOperator,
            userId: otherIssuer ? null : Guid.NewGuid(),
            issuer: otherIssuer ? AuthenticatedWorkflowTestContext.Issuer + "-second" : null);
        using var denied = await foreign.PostAsync($"/instance/{id}/withdraw", null);
        using var missing = await foreign.PostAsync($"/instance/{Guid.NewGuid()}/withdraw", null);
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var a = await denied.Content.ReadFromJsonAsync<JsonElement>();
        var b = await missing.Content.ReadFromJsonAsync<JsonElement>();
        a.GetProperty("title").GetString().Should().Be(b.GetProperty("title").GetString());
        a.GetProperty("detail").GetString().Should().Be(b.GetProperty("detail").GetString());
        (await context.Storage.InstanceStorage.GetProcessInstance(id)).IsFinished.Should().BeFalse();
    }

    // Testzweck: Ein neuer Anzeigename derselben stabilen Person bleibt berechtigt;
    // wiederholter oder konkurrierender Rückzug erzeugt keinen zweiten Auditfakt.
    [Test]
    public async Task RenamedOwnerAndConcurrentRepeat_ShouldReturnSameTerminalResultAndKeepFirstAudit()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var owner = context.CreateClient();
        var id = await StartOwnAsync(context, owner);
        using var renamed = context.CreateClient(username: "renamed");
        var responses = await Task.WhenAll(owner.PostAsync($"/instance/{id}/withdraw", null),
            renamed.PostAsync($"/instance/{id}/withdraw", null));
        foreach (var response in responses) { response.StatusCode.Should().Be(HttpStatusCode.OK); response.Dispose(); }
        var firstAudit = WithdrawalAudit(await context.Storage.InstanceStorage.GetProcessInstance(id)).GetRawText();
        using var again = await renamed.PostAsync($"/instance/{id}/withdraw", null);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        WithdrawalAudit(await context.Storage.InstanceStorage.GetProcessInstance(id)).GetRawText().Should().Be(firstAudit);
    }

    // Testzweck: Ein bereits fachlich abgeschlossener Antrag wird nicht nachträglich
    // als zurückgezogen umetikettiert; seine Entscheidung und Historie bleiben erhalten.
    [Test]
    public async Task CompletedInstance_ShouldConflictWithoutChangingItsResult()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var owner = context.CreateClient();
        var id = await StartOwnAsync(context, owner);
        var task = (await context.Storage.SubscriptionStorage.GetAllUserTasks(id)).Single();
        using var completion = await owner.PostAsJsonAsync("/usertask", new
        {
            flowNodeId = task.Token.CurrentFlowNode!.Id, tokenId = task.Token.Id,
            // Das veröffentlichte Fixture ist ausdrücklich leer; keine undeclarierten Felder erfinden.
            processInstanceId = id, expectedTaskRevision = 0, data = new { }
        });
        completion.EnsureSuccessStatusCode();
        using var response = await owner.PostAsync($"/instance/{id}/withdraw", null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await context.Storage.InstanceStorage.GetProcessInstance(id)).State.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Historische oder technische Starts ohne verifizierten Initiator
    // werden nicht über einen Namens-/Operator-Fallback persönlich zurückziehbar.
    [Test]
    public async Task StartWithoutInitiator_ShouldNotPermitPersonalWithdrawalEvenForOperator()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"bert\"");
        using var client = context.CreateClient(isOperator: true);
        using var response = await client.PostAsync($"/instance/{task.ProcessInstanceId}/withdraw", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await context.Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value)).IsFinished.Should().BeFalse();
    }

    private static JsonElement WithdrawalAudit(StorageSystem.ProcessInstanceInfo instance) =>
        JsonSerializer.SerializeToElement(instance.Tokens.Single(token => token.ParentTokenId is null).Withdrawal);

    private static async Task<Guid> StartOwnAsync(AuthenticatedWorkflowTestContext context, HttpClient client)
    {
        var definition = await context.DeployAsync("assignee=\"bert\"");
        using var response = await client.PostAsJsonAsync($"/definition/meta/{definition.DefinitionId}/instance",
            new { expectedDefinitionId = definition.Id });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        return payload!.Result!.InstanceId;
    }
    /// <summary>Explizite technische Störung nur im isolierten HTTP-Test, keine Produktoption.</summary>
    private sealed class FaultingProvider : ITransactionalStorageProvider
    {
        public bool Fail { get; set; }
        public ITransactionalStorage GetTransactionalStorage() => Fail
            ? throw new InvalidOperationException("synthetic storage failure")
            : new FileSystemTransactionalStorageProvider().GetTransactionalStorage();
    }
}
