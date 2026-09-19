using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Die Auswertungen entstehen aus der vorhandenen Laufzeithistorie: echte Vorgaenge fuer die
/// Anzahlen, gezielt gespeicherte Ereignisse fuer nachrechenbare Kennzahlen. Die Engine setzt
/// ihre Zeitstempel selbst (<c>DateTime.UtcNow</c> im Tokenzustand) und ist nicht steuerbar;
/// Dauern mit bekannten Werten werden deshalb als Ereignisse abgelegt.
/// </summary>
[NonParallelizable]
public sealed class WorkflowAnalyticsIntegrationTest
{
    private const string WorkflowId = "Definitions_Completion";
    private const string Overview = "/operations/analytics/workflows";
    private static readonly string Detail = $"{Overview}/{WorkflowId}";

    // Testzweck: Die Auswertung beschreibt fremde Vorgaenge in ihrer Gesamtheit; das ist eine
    // Betriebssicht und bleibt ohne die Betriebsrolle gesperrt — auf beiden Wegen.
    [Test]
    public async Task WorkflowAnalytics_ShouldRequireTheOperatorRole()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.StartAsync("assignee=\"anna\"");
        using var user = context.CreateClient();
        using var operatorClient = context.CreateClient(isOperator: true);

        using var deniedOverview = await user.GetAsync(Overview);
        using var deniedDetail = await user.GetAsync(Detail);
        using var allowed = await operatorClient.GetAsync(Overview);

        deniedOverview.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deniedDetail.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ein echter Durchlauf zaehlt nach seinem Ausgang, und die Durchlaufzeit entsteht
    // aus Start und Ende der Instanz — ohne dass dafuer etwas zusaetzlich erfasst wird.
    [Test]
    public async Task WorkflowAnalytics_ShouldCountOutcomesOfRealRuns()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"bert\"");
        using var worker = context.CreateClient();
        using var completion = await worker.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            ProcessInstanceId = task.ProcessInstanceId,
            TokenId = task.Token.Id,
            FlowNodeId = "Review",
            ExpectedTaskRevision = 0
        });
        completion.EnsureSuccessStatusCode();
        using var client = context.CreateClient(isOperator: true);

        var payload = await client.GetFromJsonAsync<ApiStatusResult<WorkflowAnalyticsOverviewDto>>(Overview);

        var workflow = payload!.Result!.Workflows.Single(item => item.MetaDefinitionId == WorkflowId);
        workflow.Name.Should().Be("Review");
        workflow.TotalCount.Should().Be(1);
        workflow.CompletedCount.Should().Be(1);
        workflow.RunningCount.Should().Be(0);
        workflow.CancelledCount.Should().Be(0);
        workflow.FailedCount.Should().Be(0);
        workflow.CycleTime.Should().NotBeNull();
        workflow.CycleTime!.SampleCount.Should().Be(1);
        workflow.CycleTime.MedianSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    // Testzweck: Ein Abbruch ist ein regulaerer Ausgang und keine Stoerung. Er zaehlt in seiner
    // eigenen Spalte, nicht unter „gescheitert“, und liefert keine Durchlaufzeit — dieselbe
    // Einteilung wie im Betriebsbild.
    [Test]
    public async Task WorkflowAnalytics_ShouldCountACancelledInstanceSeparatelyFromAFailure()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient(isOperator: true);
        using var cancellation = await client.PostAsync($"/instance/{task.ProcessInstanceId}/cancel", null);
        cancellation.EnsureSuccessStatusCode();

        var payload = await client.GetFromJsonAsync<ApiStatusResult<WorkflowAnalyticsOverviewDto>>(Overview);

        var workflow = payload!.Result!.Workflows.Single(item => item.MetaDefinitionId == WorkflowId);
        workflow.CancelledCount.Should().Be(1);
        workflow.FailedCount.Should().Be(0);
        workflow.CompletedCount.Should().Be(0);
        workflow.RunningCount.Should().Be(0);
        workflow.CycleTime.Should().BeNull("ein Abbruch sagt nichts ueber die uebliche Dauer");
    }

    // Testzweck: Eine laufende Instanz zaehlt als laufend, hat noch keine Durchlaufzeit, und ihr
    // wartendes Token erscheint am richtigen Schritt — samt dessen Namen aus der deployten Version.
    [Test]
    public async Task WorkflowAnalyticsDetail_ShouldCountWaitingTokensAndNameTheStep()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient(isOperator: true);

        var payload = await client.GetFromJsonAsync<ApiStatusResult<WorkflowAnalyticsDetailDto>>(Detail);

        payload!.Result!.Summary.RunningCount.Should().Be(1);
        payload.Result.Summary.CompletedCount.Should().Be(0);
        payload.Result.Summary.CycleTime.Should().BeNull("ohne Abschluss gibt es keine Durchlaufzeit");
        var review = payload.Result.Nodes.Single(node => node.FlowNodeId == "Review");
        review.WaitingTokenCount.Should().Be(1);
        review.Name.Should().Be("Review");
        payload.Result.NamingDefinitionId.Should().NotBeNull();
    }

    // Testzweck: Die Wartezeit eines Schritts ist die Spanne zwischen dem festgehaltenen Active
    // und dem naechsten Completed desselben Tokens. Median und p90 der bekannten Stichprobe
    // (10/20/30/100 s) muessen exakt herauskommen, und die Engpaesse stehen vorn.
    [Test]
    public async Task WorkflowAnalyticsDetail_ShouldMeasureNodeWaitTimesAndSortBottlenecksFirst()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        var instanceId = task.ProcessInstanceId!.Value;
        var origin = DateTimeOffset.UtcNow.AddMinutes(-30);
        foreach (var seconds in new[] { 10, 20, 30, 100 })
        {
            await AppendWaitAsync(context, instanceId, task.DefinitionId, "Slow", origin, seconds);
        }

        foreach (var seconds in new[] { 1, 2 })
        {
            await AppendWaitAsync(context, instanceId, task.DefinitionId, "Fast", origin, seconds);
        }

        using var client = context.CreateClient(isOperator: true);

        var payload = await client.GetFromJsonAsync<ApiStatusResult<WorkflowAnalyticsDetailDto>>(Detail);

        var slow = payload!.Result!.Nodes.Single(node => node.FlowNodeId == "Slow");
        slow.ExecutionCount.Should().Be(4);
        slow.WaitTime.Should().NotBeNull();
        slow.WaitTime!.SampleCount.Should().Be(4);
        slow.WaitTime.MedianSeconds.Should().BeApproximately(25d, 1e-6);
        slow.WaitTime.P90Seconds.Should().BeApproximately(79d, 1e-6);
        slow.WaitTime.MeanSeconds.Should().BeApproximately(40d, 1e-6);
        var fast = payload.Result.Nodes.Single(node => node.FlowNodeId == "Fast");
        fast.WaitTime!.MedianSeconds.Should().BeApproximately(1.5d, 1e-6);

        var order = payload.Result.Nodes.Select(node => node.FlowNodeId).ToArray();
        order.Should().StartWith(["Slow", "Fast"], "Engpaesse gehoeren nach oben");
        payload.Result.Nodes.Should().Contain(node => node.FlowNodeId == "Review");
    }

    // Testzweck: Eine Instanz gilt als im Zeitraum, wenn sie darin gestartet wurde. Ein Zeitraum,
    // der vor ihrem Start endet, darf sie in keiner Kennzahl mitzaehlen.
    [Test]
    public async Task WorkflowAnalytics_ShouldExcludeInstancesStartedOutsideThePeriod()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient(isOperator: true);
        var from = DateTimeOffset.UtcNow.AddDays(-10).ToString("O");
        var to = DateTimeOffset.UtcNow.AddDays(-5).ToString("O");

        var payload = await client.GetFromJsonAsync<ApiStatusResult<WorkflowAnalyticsOverviewDto>>(
            $"{Overview}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        var workflow = payload!.Result!.Workflows.Single(item => item.MetaDefinitionId == WorkflowId);
        workflow.TotalCount.Should().Be(0, "die Instanz startete nach dem Zeitraum");
        workflow.CycleTime.Should().BeNull();
    }

    // Testzweck: Die Versionswahl schraenkt auf die Instanzen ein, die genau diese Version
    // tragen; eine fremde Version liefert eine leere, aber gueltige Auswertung.
    [Test]
    public async Task WorkflowAnalyticsDetail_ShouldRestrictToTheChosenVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient(isOperator: true);

        var bound = await client.GetFromJsonAsync<ApiStatusResult<WorkflowAnalyticsDetailDto>>(
            $"{Detail}?definitionId={task.DefinitionId}");
        var foreign = await client.GetFromJsonAsync<ApiStatusResult<WorkflowAnalyticsDetailDto>>(
            $"{Detail}?definitionId={Guid.NewGuid()}");

        bound!.Result!.DefinitionId.Should().Be(task.DefinitionId);
        bound.Result.Summary.TotalCount.Should().Be(1);
        foreign!.Result!.Summary.TotalCount.Should().Be(0);
        foreign.Result.Nodes.Should().BeEmpty();
    }

    // Testzweck: Die Zeitreihe deckt jeden Kalendertag des Zeitraums ab und zaehlt den Start am
    // richtigen Tag; eine Luecke wuerde in der Darstellung wie ein fehlender Tag aussehen.
    [Test]
    public async Task WorkflowAnalyticsDetail_ShouldReturnAGaplessDailyTimeline()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient(isOperator: true);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);
        var from = to.AddDays(-2);

        var payload = await client.GetFromJsonAsync<ApiStatusResult<WorkflowAnalyticsDetailDto>>(
            $"{Detail}?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");

        var timeline = payload!.Result!.Timeline;
        timeline.Should().HaveCount(3);
        timeline.Select(point => point.Day).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        timeline.Sum(point => point.StartedCount).Should().Be(1);
    }

    // Testzweck: Ein Zeitraum ueber der dokumentierten Obergrenze wird als unbrauchbare Anfrage
    // abgelehnt — mit 422 und Problem Details, die den kuerzeren Zeitraum nennen.
    [Test]
    public async Task WorkflowAnalytics_ShouldRejectAPeriodBeyondTheDocumentedLimit()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateClient(isOperator: true);
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-(WorkflowAnalyticsRange.MaxDays + 5));

        using var response = await client.GetAsync(
            $"{Overview}?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("shorter period");
    }

    // Testzweck: Ein unbekannter Katalogeintrag ist nicht auswertbar und bleibt 404 statt einer
    // erfundenen leeren Auswertung.
    [Test]
    public async Task WorkflowAnalyticsDetail_ShouldReportAnUnknownWorkflowAsNotFound()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateClient(isOperator: true);

        using var response = await client.GetAsync($"{Overview}/Definitions_Unknown");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    // Testzweck: Die Auswertung bleibt datensparsam. Weder Akteure noch Variablen, Token- oder
    // Korrelationskennungen duerfen ueber die Kennzahlen nach aussen gelangen.
    [Test]
    public async Task WorkflowAnalytics_ShouldNotExposeActorsTokensOrVariables()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.StartAsync("assignee=\"TOP-SECRET-ASSIGNMENT\"");
        using var client = context.CreateClient(isOperator: true);

        using var response = await client.GetAsync(Detail);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("TOP-SECRET-ASSIGNMENT");
        json.ToLowerInvariant().Should().NotContain("tokenid")
            .And.NotContain("correlationid")
            .And.NotContain("instanceid")
            .And.NotContain("variables")
            .And.NotContain("assignee");
    }

    // Testzweck: Oberhalb der dokumentierten Ereignisgrenze antwortet die API mit 422 und dem
    // Hinweis auf den kuerzeren Zeitraum, statt beliebig viel in den Speicher zu laden.
    [Test]
    public async Task WorkflowAnalyticsDetail_ShouldRejectMoreEventsThanTheDocumentedLimit()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        var provider = new FloodedStorageProvider(
            context.Services.GetRequiredService<ITransactionalStorageProvider>(),
            WorkflowAnalyticsService.MaxRuntimeNodeEvents + 1,
            task.ProcessInstanceId!.Value,
            task.DefinitionId);
        var analytics = new WorkflowAnalyticsService(provider, TimeProvider.System);

        var read = () => analytics.GetDetailAsync(WorkflowId, null, null, null);

        (await read.Should().ThrowAsync<WorkflowAnalyticsRequestException>())
            .WithMessage("*shorter period*");
    }

    /// <summary>Ein Durchlauf mit bekannter Wartezeit: ein Active und sein Abschluss.</summary>
    private static async Task AppendWaitAsync(
        AuthenticatedWorkflowTestContext context,
        Guid instanceId,
        Guid definitionId,
        string flowNodeId,
        DateTimeOffset origin,
        int seconds)
    {
        var tokenId = Guid.NewGuid();
        foreach (var (state, occurredAtUtc) in new[]
        {
            (FlowNodeState.Active, origin),
            (FlowNodeState.Completed, origin.AddSeconds(seconds))
        })
        {
            await context.Storage.RuntimeNodeEventStorage.AppendIfAbsent(new RuntimeNodeEvent
            {
                Id = Guid.NewGuid(),
                ProcessInstanceId = instanceId,
                DefinitionId = definitionId,
                TokenId = tokenId,
                FlowNodeId = flowNodeId,
                State = state,
                CorrelationId = Guid.NewGuid(),
                OccurredAtUtc = occurredAtUtc.ToUniversalTime()
            });
        }
    }

    /// <summary>
    /// Legt eine kuenstlich grosse Ereignisspur ueber die echte Ablage. Sie 50 000-fach wirklich
    /// zu schreiben wuerde denselben Fall belegen und den Testlauf unbrauchbar lang machen.
    /// </summary>
    private sealed class FloodedStorageProvider(
        ITransactionalStorageProvider inner,
        int count,
        Guid instanceId,
        Guid definitionId) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() =>
            new FloodedStorage(inner.GetTransactionalStorage(), count, instanceId, definitionId);
    }

    private sealed class FloodedStorage(
        ITransactionalStorage inner,
        int count,
        Guid instanceId,
        Guid definitionId) : ITransactionalStorage
    {
        public IRuntimeNodeEventStorage RuntimeNodeEventStorage { get; } =
            new FloodedRuntimeNodeEventStorage(count, instanceId, definitionId);

        public IDefinitionStorage DefinitionStorage => inner.DefinitionStorage;
        public IFolderStorage FolderStorage => inner.FolderStorage;
        public IMessageSubscriptionStorage SubscriptionStorage => inner.SubscriptionStorage;
        public IInstanceStorage InstanceStorage => inner.InstanceStorage;
        public IFormStorage FormStorage => inner.FormStorage;
        public IFormAuthoringStorage FormAuthoringStorage => inner.FormAuthoringStorage;
        public IFormSectionStorage FormSectionStorage => inner.FormSectionStorage;
        public IServiceTaskStorage ServiceTaskStorage => inner.ServiceTaskStorage;
        public IIdempotencyStorage IdempotencyStorage => inner.IdempotencyStorage;
        public IIdentityDirectoryStorage IdentityDirectoryStorage => inner.IdentityDirectoryStorage;
        public IUserTaskDraftStorage UserTaskDraftStorage => inner.UserTaskDraftStorage;
        public IUserTaskLifecycleStorage UserTaskLifecycleStorage => inner.UserTaskLifecycleStorage;
        public IUserTaskDeadlineStorage UserTaskDeadlineStorage => inner.UserTaskDeadlineStorage;
        public IUserTaskNotificationStorage UserTaskNotificationStorage => inner.UserTaskNotificationStorage;
        public IAiConnectionStorage AiConnectionStorage => inner.AiConnectionStorage;
        public IAiRunStorage AiRunStorage => inner.AiRunStorage;
        public void CommitChanges() => inner.CommitChanges();
        public void RollbackTransaction() => inner.RollbackTransaction();
        public void Dispose() => inner.Dispose();
    }

    private sealed class FloodedRuntimeNodeEventStorage(int count, Guid instanceId, Guid definitionId)
        : IRuntimeNodeEventStorage
    {
        public Task<bool> AppendIfAbsent(RuntimeNodeEvent runtimeEvent) => Task.FromResult(true);

        public Task<IReadOnlyList<RuntimeNodeEvent>> GetByProcessInstance(Guid processInstanceId) =>
            Task.FromResult<IReadOnlyList<RuntimeNodeEvent>>([]);

        public Task<IReadOnlyList<RuntimeNodeEvent>> GetByDefinitionIds(
            IReadOnlyCollection<Guid> definitionIds,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc) =>
            Task.FromResult<IReadOnlyList<RuntimeNodeEvent>>(
            [
                .. Enumerable.Range(0, count).Select(index => new RuntimeNodeEvent
                {
                    Id = Guid.NewGuid(),
                    ProcessInstanceId = instanceId,
                    DefinitionId = definitionId,
                    TokenId = Guid.NewGuid(),
                    FlowNodeId = "Review",
                    State = FlowNodeState.Active,
                    CorrelationId = Guid.NewGuid(),
                    OccurredAtUtc = fromUtc.AddSeconds(index % 1000)
                })
            ]);
    }
}
