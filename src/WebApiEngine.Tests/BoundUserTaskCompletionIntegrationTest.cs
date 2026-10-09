using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Idempotency;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Atomare Aufgaben-/Versionsbindung und persönliche Wiederholung für eingebettete Hosts.</summary>
[NonParallelizable]
public sealed class BoundUserTaskCompletionIntegrationTest
{
    // Testzweck: Ein fremder oder leerer Versionswert darf weder Aufgabe noch Claim/Audit verändern;
    // eine abgelehnte Reservierung blockiert denselben Schlüssel für einen korrigierten Erstabschluss nicht.
    [TestCase(false)]
    [TestCase(true)]
    public async Task WrongDefinition_ShouldConflictWithoutMutation(bool empty)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval",
            """{"components":[{"type":"textfield","key":"answer"}]}""");
        var task = await context.StartAsync("candidateGroups=\"review\"");
        using var person = context.CreateClient();
        await ClaimAsync(person, task);
        person.DefaultRequestHeaders.Add("Idempotency-Key", "wrong-definition");
        var body = Bound(task);
        body["expectedDefinitionId"] = empty ? Guid.Empty : Guid.NewGuid();
        using var rejected = await person.PostAsJsonAsync("/usertask", body);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("user_task.binding_conflict");
        (await rejected.Content.ReadAsStringAsync()).Should().NotContain(task.DefinitionId.ToString());
        await context.AssertStillActiveAsync(task);
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id)).Should().ContainSingle();
        using var corrected = await person.PostAsJsonAsync("/usertask", Bound(task));
        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Migration behält Task, Token und Claimrevision bei; genau deshalb muss der
    // Abschluss die ursprünglich angezeigte Definition unter derselben Engine-Sperre prüfen.
    [TestCase("/usertask")]
    [TestCase("/form/result")]
    public async Task MigratedTask_ShouldRejectOldFormEvenWithMatchingClaimRevision(string route)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceMigrationScenarios.DeployAsync(provider, engine, new Model.Version(1, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: false));
        var instance = await InstanceMigrationScenarios.StartAsync(engine, "left");
        var original = (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId)).Single();
        using var person = context.CreateClient(isOperator: true);
        await ClaimAsync(person, original);
        var staleBody = Bound(original);
        var target = await InstanceMigrationScenarios.DeployAsync(provider, engine, new Model.Version(2, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: true));
        (await engine.MigrateInstances([instance.InstanceId], target.Id, AuthenticatedWorkflowTestContext.UserId))
            .Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();
        var migrated = (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId)).Single();
        migrated.Id.Should().Be(original.Id);
        migrated.Token.Id.Should().Be(original.Token.Id);
        (await context.Storage.UserTaskLifecycleStorage.Get(original.Id))!.Revision.Should().Be(1);
        person.DefaultRequestHeaders.Add("Idempotency-Key", "old-open-form");
        using var rejected = await person.PostAsJsonAsync(route, staleBody);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("user_task.binding_conflict");
        await context.AssertStillActiveAsync(migrated);
        using var refreshed = await person.PostAsJsonAsync(route, Bound(migrated));
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId)).Should().ContainSingle()
            .Which.Token.CurrentFlowNode!.Id.Should().Be("Approve");
    }

    // Testzweck: Instanz-/Tokenkoordinaten verleihen keine Berechtigung zum Abschluss einer
    // anderen Subscription; auch die leere erwartete Taskkennung ist kein Wildcard.
    [TestCase(false)]
    [TestCase(true)]
    public async Task WrongTask_ShouldRemainHiddenAndNotMutate(bool empty)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval",
            """{"components":[{"type":"textfield","key":"answer"}]}""");
        var task = await context.StartAsync("candidateGroups=\"review\"");
        using var person = context.CreateClient();
        await ClaimAsync(person, task);
        var body = Bound(task);
        body["expectedUserTaskId"] = empty ? Guid.Empty : Guid.NewGuid();
        using var rejected = await person.PostAsJsonAsync("/usertask", body);
        rejected.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await context.AssertStillActiveAsync(task);
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id)).Should().ContainSingle();
    }

    // Testzweck: Der Host kann echte Übernahme verlangen. Kandidaten- und Betriebsrecht
    // ersetzen sie nicht; nach dem protokollierten persönlichen Claim funktioniert derselbe Erstversuch.
    [TestCase(false)]
    [TestCase(true)]
    public async Task RequiredClaim_ShouldNotBeBypassedByCandidateOrOperator(bool isOperator)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval",
            """{"components":[{"type":"textfield","key":"answer"}]}""");
        var task = await context.StartAsync("candidateGroups=\"review\"");
        using var person = context.CreateClient(isOperator: isOperator, authorizedClientId: "synthetic-tt-demo");
        person.DefaultRequestHeaders.Add("Idempotency-Key", "claim-first");
        var body = Bound(task, revision: 0);
        using var rejected = await person.PostAsJsonAsync("/usertask", body);
        rejected.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await context.AssertStillActiveAsync(task);
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id)).Should().BeEmpty();
        await ClaimAsync(person, task);
        using var completed = await person.PostAsJsonAsync("/usertask", Bound(task));
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        var audit = await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id);
        audit.Select(item => item.Action).Should().Equal("claim", "complete");
        audit.Should().OnlyContain(item => item.ActorUserId == AuthenticatedWorkflowTestContext.UserId
            && item.AuthenticatedActor!.ClientId == "synthetic-tt-demo");
    }

    // Testzweck: Erfolgsbelege bleiben vor allen aktuellen Task-/Claimprüfungen wiederholbar;
    // eine nachträgliche Änderung von Version, Task oder Übernahmebedingung ist jedoch ein Konflikt.
    [TestCase("expectedDefinitionId")]
    [TestCase("expectedUserTaskId")]
    [TestCase("requireAssignedToCurrentUser")]
    public async Task BoundReplay_ShouldSucceedAfterRemovalButRejectChangedBinding(string property)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval",
            """{"components":[{"type":"textfield","key":"answer"}]}""");
        var task = await context.StartAsync("candidateGroups=\"review\"");
        using var person = context.CreateClient();
        await ClaimAsync(person, task);
        person.DefaultRequestHeaders.Add("Idempotency-Key", "bound-once");
        var body = Bound(task);
        using var completed = await person.PostAsJsonAsync("/usertask", body);
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        using var replay = await person.PostAsJsonAsync("/form/result", body);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        body[property] = property == "requireAssignedToCurrentUser" ? false : Guid.NewGuid();
        using var changed = await person.PostAsJsonAsync("/form/result", body);
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id)).Select(item => item.Action)
            .Should().Equal("claim", "complete");
        (await context.Storage.SubscriptionStorage.GetAllUserTasks(task.ProcessInstanceId!.Value)).Should().BeEmpty();
    }

    // Testzweck: Derselbe Schlüssel und Inhalt teilen einen Abschlussbeleg weder mit
    // einer anderen Person noch mit derselben Subjectkennung eines anderen Issuers.
    [TestCase(false)]
    [TestCase(true)]
    public async Task BoundReplay_ShouldRemainPersonal(bool differentIssuer)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval",
            """{"components":[{"type":"textfield","key":"answer"}]}""");
        var task = await context.StartAsync("candidateGroups=\"review\"");
        using var person = context.CreateClient();
        using var other = context.CreateClient(userId: differentIssuer ? null : Guid.NewGuid(),
            issuer: differentIssuer ? AuthenticatedWorkflowTestContext.Issuer + "-second" : null);
        await ClaimAsync(person, task);
        person.DefaultRequestHeaders.Add("Idempotency-Key", "personal-bound-once");
        other.DefaultRequestHeaders.Add("Idempotency-Key", "personal-bound-once");
        using var completed = await person.PostAsJsonAsync("/usertask", Bound(task));
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        using var hidden = await other.PostAsJsonAsync("/form/result", Bound(task));
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Betriebsrecht erlaubt normalerweise Zugriff auf fremde Claims. Der gebundene
    // Hostabschluss muss dennoch ausschließlich die wirklich zugewiesene Person akzeptieren.
    [Test]
    public async Task RequiredClaim_ShouldNotAllowOperatorToUseAnotherPersonsClaim()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval",
            """{"components":[{"type":"textfield","key":"answer"}]}""");
        var task = await context.StartAsync("candidateGroups=\"review\"");
        using var person = context.CreateClient();
        using var operation = context.CreateClient(isOperator: true, userId: Guid.NewGuid());
        await ClaimAsync(person, task);
        using var hidden = await operation.PostAsJsonAsync("/usertask", Bound(task));
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await context.AssertStillActiveAsync(task);
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id)).Should().ContainSingle();
    }

    // Testzweck: Ein schon abgeschlossener Quellschritt bleibt persönlich wiederholbar,
    // auch wenn die weiterlaufende Instanz inzwischen auf eine neue Definition migriert wurde.
    [Test]
    public async Task SuccessfulBoundReplay_ShouldPrecedeCurrentDefinitionCheckAfterMigration()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceMigrationScenarios.DeployAsync(provider, engine, new Model.Version(1, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: true));
        var instance = await InstanceMigrationScenarios.StartAsync(engine, "left");
        var review = (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId)).Single();
        using var person = context.CreateClient(isOperator: true);
        await ClaimAsync(person, review);
        person.DefaultRequestHeaders.Add("Idempotency-Key", "completed-before-migration");
        var body = Bound(review);
        using var completed = await person.PostAsJsonAsync("/usertask", body);
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        var next = (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId)).Single();
        var target = await InstanceMigrationScenarios.DeployAsync(provider, engine, new Model.Version(2, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: true));
        (await engine.MigrateInstances([instance.InstanceId], target.Id, AuthenticatedWorkflowTestContext.UserId))
            .Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();
        using var replay = await person.PostAsJsonAsync("/form/result", body);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var remaining = (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId)).Single();
        remaining.Id.Should().Be(next.Id);
        remaining.DefinitionId.Should().Be(target.Id);
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(review.Id)).Select(item => item.Action)
            .Should().Equal("claim", "complete");
    }

    // Testzweck: Ein bereits persistierter Altbeleg hat den exakt bisherigen Hash ohne neue
    // Null-/Defaultfelder. Ein neuer Deploymentstand darf ihn nicht in einen Konflikt umdeuten.
    [Test]
    public async Task LegacyCompletion_ShouldReplayAnExistingPreExtensionHash()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval",
            """{"components":[{"type":"textfield","key":"answer"}]}""");
        var task = await context.StartAsync("assignee=\"bert\"");
        var dto = new UserTaskResultDto
        {
            ProcessInstanceId = task.ProcessInstanceId, TokenId = task.Token.Id,
            FlowNodeId = task.Token.CurrentFlowNode!.Id
        };
        var model = dto.ToModel();
        var legacy = new { model.FlowNodeId, model.TokenId, model.ProcessInstanceId,
            model.ExpectedTaskRevision, model.ActionId, model.Data };
        var actor = new CurrentUserContext(AuthenticatedWorkflowTestContext.UserId, "test", false)
        {
            Identity = new AuthenticatedSubject(AuthenticatedWorkflowTestContext.Issuer,
                AuthenticatedWorkflowTestContext.UserId.ToString())
        };
        var http = new DefaultHttpContext();
        http.Request.Headers[HttpIdempotency.HeaderName] = "pre-extension";
        var request = HttpIdempotency.Create(http.Request, actor, "user-task-completion",
            $"{task.ProcessInstanceId:D}/{task.Token.Id:D}", legacy)!;
        await context.Storage.IdempotencyStorage.TryCreate(new IdempotencyRecord
        {
            ScopeHash = request.ScopeHash, RequestHash = request.RequestHash, Operation = request.Operation,
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.Add(HttpIdempotency.Retention)
        });
        await context.Storage.IdempotencyStorage.Complete(request.ScopeHash, null);
        using var person = context.CreateClient();
        person.DefaultRequestHeaders.Add("Idempotency-Key", "pre-extension");
        using var replay = await person.PostAsJsonAsync("/usertask", dto);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        await context.AssertStillActiveAsync(task); // Der synthetisch vorhandene Beleg verhindert neue Mutation.
    }

    private static Dictionary<string, object?> Bound(UserTaskSubscription task, long revision = 1) => new()
    {
        ["processInstanceId"] = task.ProcessInstanceId, ["tokenId"] = task.Token.Id,
        ["flowNodeId"] = task.Token.CurrentFlowNode!.Id, ["expectedTaskRevision"] = revision,
        ["expectedUserTaskId"] = task.Id, ["expectedDefinitionId"] = task.DefinitionId,
        ["requireAssignedToCurrentUser"] = true, ["data"] = new { answer = "yes" }
    };

    private static async Task ClaimAsync(HttpClient client, UserTaskSubscription task)
    {
        using var claimed = await client.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 });
        claimed.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
