using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Model;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Human-Task-Demo ohne Worker, Urlaubssystem oder weitere externe Effekte.</summary>
[NonParallelizable]
public sealed class TickyTaskVacationIntegrationTest
{
    private static readonly string[] ReviewNodes = ["Task_Supervisor", "Task_Personnel", "Task_Substitute"];

    // Testzweck: Eine zweite Rückgabe ersetzt den Korrekturhinweis, behält aber beide
    // historischen Prüfungen. Der vorige Hinweis darf nicht als neue Rückfrage erscheinen.
    [Test]
    public async Task RepeatedReturn_ShouldShowLatestReasonAndRetainBothReviewHistories()
    {
        using var demo = await CreateAsync();
        await demo.CompleteAsync("Task_Supervisor", "return", new() { ["begruendung"] = "Erste Rückfrage" });
        var firstCorrection = (await demo.TasksAsync()).Single();
        await demo.CompleteAsync("Task_Correction", null, demo.Application());
        await demo.CompleteAsync("Task_Personnel", "return", new() { ["begruendung"] = "Zweite Rückfrage" });
        var secondCorrection = (await demo.TasksAsync()).Single();
        secondCorrection.Id.Should().NotBe(firstCorrection.Id);
        using var owner = demo.Client();
        var displayed = await owner.GetFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto>>($"/usertask/{secondCorrection.Id}");
        ((IDictionary<string, object?>)displayed!.Result!.Token.Variables!)["rueckfrage"].Should().Be("Zweite Rückfrage");
        var history = (await demo.InstanceAsync()).Tokens
            .Where(token => ReviewNodes.Contains(token.CurrentBaseElement.Id) && token.State == FlowNodeState.Completed)
            .Select(token => ((IDictionary<string, object?>)token.OutputData!)["begruendung"]);
        history.Should().BeEquivalentTo(new[] { "Erste Rückfrage", "Zweite Rückfrage" });
    }

    // Testzweck: Die Korrektur zeigt genau die Rückfrage der zurückgebenden Rolle,
    // nicht den Kommentar einer früheren Zustimmung und keine privaten Rundenvariablen.
    [TestCase("Task_Supervisor")]
    [TestCase("Task_Personnel")]
    [TestCase("Task_Substitute")]
    public async Task Correction_ShouldShowReadOnlyReturnReasonWithoutOtherReviewComments(string returningNode)
    {
        using var demo = await CreateAsync();
        await demo.CompleteAsync(ReviewNodes.First(node => node != returningNode), "approve",
            new() { ["begruendung"] = "Private frühere Zustimmung" });
        var reason = "Bitte Zeitraum korrigieren: " + returningNode;
        await demo.CompleteAsync(returningNode, "return", new() { ["begruendung"] = reason });
        var correction = (await demo.TasksAsync()).Single();
        using var owner = demo.Client();
        var task = await owner.GetFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto>>($"/usertask/{correction.Id}");
        var values = (IDictionary<string, object?>)task!.Result!.Token.Variables!;
        values.Should().ContainKey("rueckfrage").WhoseValue.Should().Be(reason);
        values.Keys.Should().BeEquivalentTo("von", "bis", "arbeitstage", "vertretung", "bemerkung", "rueckfrage");
        values.Values.Should().NotContain("Private frühere Zustimmung");
        var form = await owner.GetFromJsonAsync<ApiStatusResult<FormDto>>($"/usertask/{correction.Id}/form");
        using var schema = JsonDocument.Parse(form!.Result!.FormData!);
        schema.RootElement.GetProperty("components").EnumerateArray()
            .Single(field => field.GetProperty("key").GetString() == "rueckfrage")
            .GetProperty("disabled").GetBoolean().Should().BeTrue();
        var root = (IDictionary<string, object?>)(await demo.InstanceAsync()).Tokens.Single(token => token.ParentTokenId is null).Variables!;
        root.Keys.Should().NotContain(new[] { "supervisorBegruendung", "personnelBegruendung", "substituteBegruendung" });
    }

    // Testzweck: Der lokale DMN-Schritt liefert genau die konfigurierte stabile Person;
    // drei echte Human Tasks ersetzen Worker, Textnamen und echte Urlaubsbuchungen.
    [Test]
    public async Task Start_ShouldResolveSupervisorAndCreateThreeDistinctDirectoryTasksWithoutJobs()
    {
        using var demo = await CreateAsync();
        var tasks = await demo.TasksAsync();
        tasks.Select(task => task.Token.CurrentFlowNode!.Id).Should().BeEquivalentTo(ReviewNodes);
        tasks.Single(task => task.Token.CurrentFlowNode!.Id == "Task_Supervisor").DirectoryAssigneeUserId.Should().Be(demo.SupervisorId);
        tasks.Single(task => task.Token.CurrentFlowNode!.Id == "Task_Substitute").DirectoryAssigneeUserId.Should().Be(demo.SubstituteId);
        tasks.Single(task => task.Token.CurrentFlowNode!.Id == "Task_Personnel").DirectoryCandidateGroupIds.Should().Equal(demo.PersonnelGroupId);
        (await demo.Storage.ServiceTaskStorage.GetJobs()).Should().BeEmpty();
    }

    // Testzweck: Jede Reihenfolge verlangt alle drei Zustimmungen; zwei Stimmen reichen
    // nie. Persönlicher Abschlussaudit und Formularentscheidung bleiben in der Historie.
    [TestCase("012")]
    [TestCase("021")]
    [TestCase("102")]
    [TestCase("120")]
    [TestCase("201")]
    [TestCase("210")]
    public async Task ThreeApprovals_ShouldCompleteOnlyAfterAllThree(string order)
    {
        using var demo = await CreateAsync();
        for (var index = 0; index < order.Length; index++)
        {
            await demo.CompleteAsync(ReviewNodes[order[index] - '0']);
            (await demo.InstanceAsync()).IsFinished.Should().Be(index == 2);
        }
        (await demo.TasksAsync()).Should().BeEmpty();
        var instance = await demo.InstanceAsync();
        instance.State.Should().Be(ProcessInstanceState.Completed);
        instance.Tokens.Should().Contain(token => token.CurrentBaseElement.Id == "End_Approved" && token.State == FlowNodeState.Completed);
        var root = (IDictionary<string, object?>)instance.Tokens.Single(token => token.ParentTokenId is null).Variables!;
        root["urlaubsentscheidung"].Should().Be("genehmigt");
        root.Keys.Should().NotContain(new[] { "vorgesetztenEntscheidung", "personalEntscheidung", "vertretungsEntscheidung",
            "supervisorBegruendung", "personnelBegruendung", "substituteBegruendung" });
        foreach (var node in ReviewNodes)
        {
            var token = instance.Tokens.Single(token => token.CurrentBaseElement.Id == node);
            token.CompletedByActor!.Identity.Subject.Should().Be(demo.Actor(node)!.Value.ToString());
            token.CompletedByActor!.ClientId.Should().Be("synthetic-tickytask-demo");
            ((IDictionary<string, object?>)token.OutputData!)["entscheidung"].Should().Be("genehmigt");
        }
    }

    // Testzweck: Jede der drei Rollen kann ablehnen; schon vorhandene Zustimmung darf
    // keine Genehmigung erzeugen. Die anderen Tasks verschwinden in derselben Mutation.
    [TestCase("Task_Supervisor")]
    [TestCase("Task_Personnel")]
    [TestCase("Task_Substitute")]
    public async Task Rejection_ShouldCancelOtherReviewsImmediately(string rejectingNode)
    {
        using var demo = await CreateAsync();
        await demo.CompleteAsync(ReviewNodes.First(node => node != rejectingNode));
        await demo.CompleteAsync(rejectingNode, "reject");
        (await demo.TasksAsync()).Should().BeEmpty();
        var instance = await demo.InstanceAsync();
        instance.State.Should().Be(ProcessInstanceState.Completed);
        instance.Tokens.Should().Contain(token => token.CurrentBaseElement.Id == "End_Rejected" && token.State == FlowNodeState.Completed);
        instance.Tokens.Should().NotContain(token => token.CurrentBaseElement.Id == "End_Approved");
        instance.Tokens.Should().Contain(token => ReviewNodes.Contains(token.CurrentBaseElement.Id) && token.State == FlowNodeState.Withdrawn);
    }

    // Testzweck: Rückgabe unterbricht den ganzen parallelen Scope. Korrektur gehört dem
    // echten Initiator, neue Task-IDs und neue Vertretung gelten nur für die neue Runde.
    [TestCase("Task_Supervisor")]
    [TestCase("Task_Personnel")]
    [TestCase("Task_Substitute")]
    public async Task ReturnAndCorrection_ShouldRequireThreeFreshVotesAndRetainOldHistory(string returningNode)
    {
        using var demo = await CreateAsync();
        var oldTasks = await demo.TasksAsync();
        var approvingNode = ReviewNodes.First(node => node != returningNode);
        await demo.CompleteAsync(approvingNode);
        await demo.CompleteAsync(returningNode, "return");
        var correction = (await demo.TasksAsync()).Should().ContainSingle().Subject;
        correction.Token.CurrentFlowNode!.Id.Should().Be("Task_Correction");
        correction.DirectoryAssigneeUserId.Should().Be(demo.InitiatorId);
        var changedApplication = demo.Application(demo.SupervisorId);
        changedApplication["von"] = "2026-10-19";
        changedApplication["bis"] = "2026-10-23";
        changedApplication["bemerkung"] = "Korrigierter Zeitraum";
        await demo.CompleteAsync("Task_Correction", null, changedApplication);
        var newTasks = await demo.TasksAsync();
        newTasks.Should().HaveCount(3);
        newTasks.Select(task => task.Id).Should().NotIntersectWith(oldTasks.Select(task => task.Id));
        newTasks.Single(task => task.Token.CurrentFlowNode!.Id == "Task_Substitute").DirectoryAssigneeUserId.Should().Be(demo.SupervisorId);
        using var newSupervisor = demo.Client(TickyTaskVacationTestContext.SupervisorSubject);
        var displayed = await newSupervisor.GetFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto>>(
            $"/usertask/{newTasks.Single(task => task.Token.CurrentFlowNode!.Id == "Task_Supervisor").Id}");
        var newApplication = (IDictionary<string, object?>)displayed!.Result!.Token.Variables!;
        newApplication["von"].Should().Be("2026-10-19");
        newApplication["bis"].Should().Be("2026-10-23");
        newApplication["bemerkung"].Should().Be("Korrigierter Zeitraum");
        var history = await demo.InstanceAsync();
        var oldVote = history.Tokens.Single(token => token.Id == oldTasks.Single(task => task.Token.CurrentFlowNode!.Id == approvingNode).Token.Id);
        oldVote.State.Should().Be(FlowNodeState.Completed);
        ((IDictionary<string, object?>)oldVote.OutputData!)["entscheidung"].Should().Be("genehmigt");
        (await demo.Storage.UserTaskLifecycleStorage.GetEvents(oldTasks.Single(task => task.Token.CurrentFlowNode!.Id == approvingNode).Id))
            .Select(item => item.Action).Should().Equal("claim", "complete");
        // Die gewählte Vertretung ist jetzt die Vorgesetzten-Person: Demo-Selbstfreigabe ist erlaubt.
        foreach (var node in ReviewNodes.Take(2)) await demo.CompleteAsync(node);
        (await demo.InstanceAsync()).IsFinished.Should().BeFalse();
        var substituteTask = (await demo.TasksAsync()).Should().ContainSingle().Subject;
        using var supervisor = demo.Client(TickyTaskVacationTestContext.SupervisorSubject);
        await demo.ClaimAsync(supervisor, substituteTask);
        using var finish = await supervisor.PostAsJsonAsync("/usertask", demo.Bound(substituteTask, "approve", new() { ["begruendung"] = "Neue Runde" }));
        finish.StatusCode.Should().Be(HttpStatusCode.OK);
        (await demo.InstanceAsync()).State.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Zurückziehen verwendet denselben verifizierten Initiator und beendet
    // sowohl Prüfrunden als auch Korrekturarbeit ohne Rollback oder Urlaubssystemeffekt.
    [TestCase(false)]
    [TestCase(true)]
    public async Task InitiatorWithdrawal_ShouldCancelEveryOpenHumanTask(bool duringCorrection)
    {
        using var demo = await CreateAsync();
        if (duringCorrection) await demo.CompleteAsync("Task_Supervisor", "return");
        using var owner = demo.Client();
        using var response = await owner.PostAsync($"/instance/{demo.InstanceId}/withdraw", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await demo.TasksAsync()).Should().BeEmpty();
        (await demo.InstanceAsync()).State.Should().Be(ProcessInstanceState.Terminated);
    }

    // Testzweck: Unvollständige Korrektur kann privat gespeichert und erneut geladen
    // werden, schließt aber ohne fachliche Pflichtfelder keine neue Prüfrunde auf.
    [Test]
    public async Task IncompleteCorrectionDraft_ShouldRestoreWithoutProcessProgress()
    {
        using var demo = await CreateAsync();
        await demo.CompleteAsync("Task_Personnel", "return");
        var correction = (await demo.TasksAsync()).Should().ContainSingle().Subject;
        using var owner = demo.Client();
        await demo.ClaimAsync(owner, correction);
        using var saved = await owner.PutAsJsonAsync($"/usertask/{correction.Id}/draft", new { expectedRevision = 0, data = new { bemerkung = "Noch unvollständig" } });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        using var reopened = demo.Client();
        var draft = await reopened.GetFromJsonAsync<System.Text.Json.JsonElement>($"/usertask/{correction.Id}/draft");
        draft.GetProperty("result").GetProperty("data").GetProperty("bemerkung").GetString().Should().Be("Noch unvollständig");
        using var incomplete = await reopened.PostAsJsonAsync("/usertask", demo.Bound(correction, null, new() { ["bemerkung"] = "Noch unvollständig" }));
        incomplete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await demo.TasksAsync()).Should().ContainSingle().Which.Id.Should().Be(correction.Id);
    }

    private static async Task<TickyTaskVacationTestContext> CreateAsync()
    {
        var demo = new TickyTaskVacationTestContext();
        try { await demo.InitializeAsync(); await demo.StartAsync(); return demo; }
        catch { demo.Dispose(); throw; }
    }
}
