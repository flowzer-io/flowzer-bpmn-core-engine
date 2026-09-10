using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>Öffentlicher, objektberechtigter Vertrag für persistente Task-Meldungen.</summary>
[NonParallelizable]
public sealed class UserTaskNotificationIntegrationTest
{
    // Testzweck: Der öffentliche Task-Vertrag liefert nur die serverseitig gebundene
    // UTC-Fälligkeit; ein nicht unterstützter Ausdruck bleibt explizit unausgewertet.
    [Test]
    public async Task TaskView_ShouldExposeBoundAndUnsupportedSchedules()
    {
        using var boundContext = new AuthenticatedWorkflowTestContext();
        await boundContext.StartAsync("", taskScheduleXml: "<zeebe:taskSchedule dueDate=\"PT2H\" />");
        using var boundClient = boundContext.CreateClient();
        var bound = (await GetTasks(boundClient)).Single().GetProperty("deadline");
        bound.GetProperty("scheduleState").GetString().Should().Be("resolved");
        bound.GetProperty("dueAtUtc").GetDateTimeOffset().Should()
            .BeAfter(DateTimeOffset.UtcNow.AddHours(1.9));

        using var unsupportedContext = new AuthenticatedWorkflowTestContext();
        var unsupportedTask = await unsupportedContext.StartAsync(
            "", taskScheduleXml: "<zeebe:taskSchedule dueDate=\"=now()\" />");
        (await unsupportedContext.Storage.UserTaskDeadlineStorage.Get(unsupportedTask.Id))!
            .RawDueDate.Should().Be("=now()");
        using var unsupportedClient = unsupportedContext.CreateClient();
        var unsupported = (await GetTasks(unsupportedClient)).Single().GetProperty("deadline");
        unsupported.GetProperty("scheduleState").GetString().Should().Be("unsupported");
        unsupported.TryGetProperty("dueAtUtc", out _).Should().BeFalse();
    }

    // Testzweck: Der Scheduler holt eine fällige Aufgabe nach und beide Kandidaten haben
    // getrennte Lesezustände; nach Claim verliert der andere Kandidat auch die Meldung.
    [Test]
    public async Task DueNotification_ShouldBePersistentPerUserAndFollowTaskOwnership()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync(
            "candidateGroups=\"review\"",
            taskScheduleXml: "<zeebe:taskSchedule dueDate=\"PT0S\" />");
        var deadlineService = context.Services.GetRequiredService<UserTaskDeadlineService>();
        (await deadlineService.ProcessDueAsync(DateTimeOffset.UtcNow.AddSeconds(1), 100, default))
            .Should().Be(1);
        using var bert = context.CreateClient(username: "bert");
        using var anna = context.CreateClient(userId: Guid.NewGuid(), username: "anna");

        var bertFeed = await GetFeed(bert);
        var annaFeed = await GetFeed(anna);
        bertFeed.Should().ContainSingle();
        annaFeed.Should().ContainSingle();
        var notificationId = bertFeed[0].GetProperty("id").GetGuid();

        (await bert.PostAsync($"/notifications/{notificationId}/read", null)).EnsureSuccessStatusCode();
        (await GetFeed(bert)).Single().GetProperty("readAtUtc").ValueKind.Should()
            .Be(JsonValueKind.String);
        (await GetFeed(anna)).Single().TryGetProperty("readAtUtc", out var annaRead).Should().BeFalse();

        (await bert.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }))
            .EnsureSuccessStatusCode();
        (await GetFeed(anna)).Should().BeEmpty();
        (await anna.PostAsync($"/notifications/{notificationId}/read", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Eine wiederholte Nachholverarbeitung erzeugt wegen des persistenten
    // Meilensteinstands und Deduplizierungsschlüssels keine zweite Meldung.
    [Test]
    public async Task SchedulerReplay_ShouldNotDuplicateNotification()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.StartAsync("", taskScheduleXml: "<zeebe:taskSchedule dueDate=\"PT0S\" />");
        var deadlines = context.Services.GetRequiredService<UserTaskDeadlineService>();
        var now = DateTimeOffset.UtcNow.AddSeconds(1);

        var first = await deadlines.ProcessDueAsync(now, 100, default);
        var replay = await deadlines.ProcessDueAsync(now.AddMinutes(1), 100, default);

        first.Should().Be(1);
        replay.Should().Be(0);
    }

    // Testzweck: Ein Task-Abschluss gewinnt über dieselbe Subscription-Sperre; danach
    // darf ein Scheduler-Nachlauf keine Meldung für die entfernte Aufgabe erzeugen.
    [Test]
    public async Task CompletionBeforeScheduler_ShouldPreventLateNotification()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("", taskScheduleXml: "<zeebe:taskSchedule dueDate=\"PT0S\" />");
        using var client = context.CreateClient();
        (await client.PostAsJsonAsync("/usertask", new WebApiEngine.Shared.UserTaskResultDto
        {
            ProcessInstanceId = task.ProcessInstanceId,
            TokenId = task.Token.Id,
            FlowNodeId = task.Token.CurrentFlowNode!.Id,
            ExpectedTaskRevision = 0
        })).EnsureSuccessStatusCode();

        var created = await context.Services.GetRequiredService<UserTaskDeadlineService>()
            .ProcessDueAsync(DateTimeOffset.UtcNow.AddMinutes(1), 100, default);

        created.Should().Be(0);
        (await GetFeed(client)).Should().BeEmpty();
    }

    private static async Task<JsonElement[]> GetFeed(HttpClient client)
    {
        using var response = await client.GetAsync("/notifications");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("result").EnumerateArray().ToArray();
    }

    private static async Task<JsonElement[]> GetTasks(HttpClient client)
    {
        using var response = await client.GetAsync("/usertask");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("result").EnumerateArray().ToArray();
    }
}
