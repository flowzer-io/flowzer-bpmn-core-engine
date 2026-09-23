using System.Xml;
using BPMN.Common;
using BPMN.Foundation;
using BPMN.Infrastructure;
using core_engine.Exceptions;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Auswertungen über die bereits vorhandene Laufzeithistorie: Wie lange dauert ein Vorgang,
/// wo wartet er am längsten, wie oft endet er wie. Es wird nichts zusätzlich erfasst — die
/// Kennzahlen entstehen serverseitig im Speicher aus den gespeicherten
/// <see cref="RuntimeNodeEvent"/>s und den Prozessinstanzen.
///
/// Die Projektion bleibt datensparsam: keine Akteure, keine Variablen, keine einzelnen
/// Instanzen. Siehe <c>docs/ANALYTICS.md</c> für Kennzahlen, Datenquelle und Grenzen.
/// </summary>
public sealed class WorkflowAnalyticsService(
    ITransactionalStorageProvider storageProvider,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Obergrenze der Ereignisse einer einzelnen Auswertung. Die Aggregation läuft im Speicher;
    /// darüber hinaus ist der kürzere Zeitraum die ehrlichere Antwort als eine langsame.
    /// </summary>
    public const int MaxRuntimeNodeEvents = 50_000;

    /// <summary>Zustände, die einen Durchlauf eines Knotens beenden.</summary>
    private static readonly HashSet<FlowNodeState> TerminalStates =
    [
        FlowNodeState.Completed, FlowNodeState.Withdrawn, FlowNodeState.Failed,
        FlowNodeState.Terminated, FlowNodeState.Compensated, FlowNodeState.Merged
    ];

    /// <summary>Zustände, deren Abstand zum <c>Active</c> desselben Tokens die Wartezeit ist.</summary>
    private static readonly HashSet<FlowNodeState> WaitMeasuringStates =
    [
        FlowNodeState.Completed, FlowNodeState.Withdrawn
    ];

    /// <summary>Kennzahlen aller Katalogeinträge im Zeitraum.</summary>
    public async Task<WorkflowAnalyticsOverviewDto> GetOverviewAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        var range = WorkflowAnalyticsRange.Create(fromUtc, toUtc, timeProvider.GetUtcNow());
        using var storage = storageProvider.GetTransactionalStorage();
        var names = await MetaNamesAsync(storage);
        var instances = await InstancesInRangeAsync(storage, range);
        var byWorkflow = instances
            .GroupBy(instance => instance.MetaDefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<InstanceFacts>)[.. group], StringComparer.Ordinal);

        // Ein Katalogeintrag ohne Vorgang im Zeitraum bleibt als Zeile mit Nullen stehen: Dass
        // ein Workflow nicht benutzt wurde, ist selbst eine Auskunft. Umgekehrt darf eine Instanz
        // ohne Katalogeintrag (Direkt-Deploy) nicht verschwinden.
        var workflowIds = names.Keys.Concat(byWorkflow.Keys).Distinct(StringComparer.Ordinal);

        return new WorkflowAnalyticsOverviewDto
        {
            FromUtc = range.FromUtc,
            ToUtc = range.ToUtc,
            Workflows =
            [
                .. workflowIds
                    .Select(id => Summarize(id, names, byWorkflow.GetValueOrDefault(id, [])))
                    .OrderByDescending(summary => summary.TotalCount)
                    .ThenBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(summary => summary.MetaDefinitionId, StringComparer.Ordinal)
            ]
        };
    }

    /// <summary>
    /// Dieselben Kennzahlen für einen Katalogeintrag, dazu die Schritte nach Engpass sortiert
    /// und der Tagesverlauf. <c>null</c> heisst: Diesen Katalogeintrag gibt es nicht.
    /// </summary>
    public async Task<WorkflowAnalyticsDetailDto?> GetDetailAsync(
        string metaDefinitionId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metaDefinitionId);
        var range = WorkflowAnalyticsRange.Create(fromUtc, toUtc, timeProvider.GetUtcNow());
        using var storage = storageProvider.GetTransactionalStorage();
        var names = await MetaNamesAsync(storage);
        var all = await InstancesInRangeAsync(storage, range);
        var ofWorkflow = all
            .Where(instance => string.Equals(instance.MetaDefinitionId, metaDefinitionId, StringComparison.Ordinal))
            .ToArray();

        // Ein unbekannter Katalogeintrag ohne jede Instanz ist nicht auswertbar und bleibt 404.
        // Ein bekannter Eintrag ohne Vorgang im Zeitraum dagegen ist eine gueltige, leere Antwort.
        if (!names.ContainsKey(metaDefinitionId) && ofWorkflow.Length == 0) return null;

        var selected = definitionId is null
            ? ofWorkflow
            : ofWorkflow.Where(instance => instance.DefinitionId == definitionId.Value).ToArray();

        var candidate = definitionId ?? await DeployedDefinitionIdAsync(storage, metaDefinitionId);
        var nodeNames = candidate is null ? null : await FlowNodeNamesAsync(storage, candidate.Value);

        var events = await EventsOfAsync(storage, selected, definitionId, range);

        return new WorkflowAnalyticsDetailDto
        {
            FromUtc = range.FromUtc,
            ToUtc = range.ToUtc,
            DefinitionId = definitionId,
            NamingDefinitionId = nodeNames is null ? null : candidate,
            Summary = Summarize(metaDefinitionId, names, selected),
            Nodes = AggregateNodes(events, selected, nodeNames ?? []),
            Timeline = BuildTimeline(range, selected)
        };
    }

    /* ------------------------------------------------------------------ Instanzen und Ausgang */

    private static async Task<Dictionary<string, string>> MetaNamesAsync(IStorageSystem storage)
    {
        var metaDefinitions = await storage.DefinitionStorage.GetAllMetaDefinitions();
        return metaDefinitions
            .GroupBy(metaDefinition => metaDefinition.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
    }

    private static async Task<IReadOnlyList<InstanceFacts>> InstancesInRangeAsync(
        IStorageSystem storage,
        WorkflowAnalyticsRange range)
    {
        var instances = await storage.InstanceStorage.GetAllInstances();
        return
        [
            .. instances
                .Select(InstanceFacts.From)
                .Where(instance => instance.StartedAt is { } startedAt
                    && startedAt >= range.FromUtc && startedAt < range.ToUtc)
        ];
    }

    private static WorkflowAnalyticsSummaryDto Summarize(
        string metaDefinitionId,
        IReadOnlyDictionary<string, string> names,
        IReadOnlyList<InstanceFacts> instances)
    {
        // Die Durchlaufzeit misst den regulaeren Abschluss. Ein Abbruch und ein Fehler sagen
        // etwas ueber den Ausgang, aber nichts darueber, wie lange der Vorgang normalerweise
        // dauert — sie wuerden die Kennzahl verkuerzen, ohne dass das jemand sieht.
        var cycleTimes = instances
            .Where(instance => instance.Outcome == InstanceOutcome.Completed)
            .Select(instance => instance.DurationSeconds)
            .OfType<double>()
            .ToArray();

        return new WorkflowAnalyticsSummaryDto
        {
            MetaDefinitionId = metaDefinitionId,
            Name = names.GetValueOrDefault(metaDefinitionId, metaDefinitionId),
            TotalCount = instances.Count,
            RunningCount = instances.Count(instance => instance.Outcome == InstanceOutcome.Running),
            CompletedCount = instances.Count(instance => instance.Outcome == InstanceOutcome.Completed),
            CancelledCount = instances.Count(instance => instance.Outcome == InstanceOutcome.Cancelled),
            FailedCount = instances.Count(instance => instance.Outcome == InstanceOutcome.Failed),
            CycleTime = DurationStatistics.Summarize(cycleTimes)
        };
    }

    private static IReadOnlyList<AnalyticsDayPointDto> BuildTimeline(
        WorkflowAnalyticsRange range,
        IReadOnlyList<InstanceFacts> instances)
    {
        var started = instances
            .Select(instance => DateOnly.FromDateTime(instance.StartedAt!.Value.UtcDateTime))
            .GroupBy(day => day)
            .ToDictionary(group => group.Key, group => group.Count());
        var finished = instances
            .Where(instance => instance.FinishedAt is not null)
            .Select(instance => DateOnly.FromDateTime(instance.FinishedAt!.Value.UtcDateTime))
            .GroupBy(day => day)
            .ToDictionary(group => group.Key, group => group.Count());

        return
        [
            .. range.Days().Select(day => new AnalyticsDayPointDto
            {
                Day = day,
                StartedCount = started.GetValueOrDefault(day),
                FinishedCount = finished.GetValueOrDefault(day)
            })
        ];
    }

    /* ---------------------------------------------------------------------------- Knoten */

    private static async Task<IReadOnlyList<RuntimeNodeEvent>> EventsOfAsync(
        IStorageSystem storage,
        IReadOnlyList<InstanceFacts> instances,
        Guid? definitionId,
        WorkflowAnalyticsRange range)
    {
        if (instances.Count == 0) return [];

        // Eine ausdrueckliche Versionswahl fragt genau deren Ereignisse ab. Ohne sie zaehlen alle
        // Versionen, unter denen die ausgewaehlten Instanzen gelaufen sind — auch die, die eine
        // migrierte Instanz vor ihrem Umzug getragen hat.
        var definitionIds = definitionId is not null
            ? [definitionId.Value]
            : instances.SelectMany(instance => instance.DefinitionIds).Distinct().ToArray();

        IReadOnlyList<RuntimeNodeEvent> events;
        try
        {
            events = await storage.RuntimeNodeEventStorage.GetByDefinitionIds(
                definitionIds, range.FromUtc, range.ToUtc);
        }
        catch (NotSupportedException)
        {
            // Eine Ablage ohne Ereignisspur liefert keine Schrittkennzahlen; die Anzahlen nach
            // Ausgang bleiben davon unberuehrt.
            return [];
        }

        if (events.Count > MaxRuntimeNodeEvents)
        {
            throw new WorkflowAnalyticsRequestException(
                $"The period contains more than {MaxRuntimeNodeEvents} runtime events. "
                + "Please request a shorter period.");
        }

        var wanted = instances.Select(instance => instance.InstanceId).ToHashSet();
        return [.. events.Where(item => wanted.Contains(item.ProcessInstanceId))];
    }

    private static IReadOnlyList<FlowNodeAnalyticsDto> AggregateNodes(
        IReadOnlyList<RuntimeNodeEvent> events,
        IReadOnlyList<InstanceFacts> instances,
        IReadOnlyDictionary<string, string> nodeNames)
    {
        var runs = new Dictionary<string, NodeAccumulator>(StringComparer.Ordinal);

        // Je Token und Knoten die Ereignisse in ihrer stabilen Reihenfolge durchgehen. Derselbe
        // Token kann denselben Knoten ueber eine Schleife mehrfach erreichen; jeder dieser
        // Durchlaeufe zaehlt einzeln.
        foreach (var group in events.GroupBy(item => (item.TokenId, item.FlowNodeId)))
        {
            var accumulator = runs.TryGetValue(group.Key.FlowNodeId, out var existing)
                ? existing
                : runs[group.Key.FlowNodeId] = new NodeAccumulator();
            DateTimeOffset? activeSince = null;

            foreach (var item in group.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id))
            {
                if (item.State == FlowNodeState.Active)
                {
                    // Ein zweites Active ohne Abschluss dazwischen bedeutet, dass der vorige
                    // Durchlauf ohne festgehaltenes Ende blieb. Er zaehlt, aber ohne Wartezeit.
                    if (activeSince is not null) accumulator.ExecutionCount++;
                    activeSince = item.OccurredAtUtc;
                    continue;
                }

                if (!TerminalStates.Contains(item.State)) continue;

                accumulator.ExecutionCount++;
                if (activeSince is { } since && WaitMeasuringStates.Contains(item.State))
                {
                    accumulator.WaitSeconds.Add(Math.Max(0, (item.OccurredAtUtc - since).TotalSeconds));
                }

                activeSince = null;
            }

            // Ein Durchlauf, der am Ende des Zeitraums noch laeuft, zaehlt mit — nur seine
            // Wartezeit steht noch nicht fest.
            if (activeSince is not null) accumulator.ExecutionCount++;
        }

        foreach (var (flowNodeId, count) in WaitingTokensByNode(instances))
        {
            var accumulator = runs.TryGetValue(flowNodeId, out var existing)
                ? existing
                : runs[flowNodeId] = new NodeAccumulator();
            accumulator.WaitingTokenCount = count;
        }

        return
        [
            .. runs
                .Select(entry => new FlowNodeAnalyticsDto
                {
                    FlowNodeId = entry.Key,
                    Name = nodeNames.GetValueOrDefault(entry.Key),
                    ExecutionCount = entry.Value.ExecutionCount,
                    WaitingTokenCount = entry.Value.WaitingTokenCount,
                    WaitTime = DurationStatistics.Summarize(entry.Value.WaitSeconds)
                })
                // Engpaesse zuerst. Ein Knoten ohne gemessene Wartezeit hat keinen Platz weit
                // oben: Er waere dort eine Behauptung, die die Daten nicht tragen.
                .OrderByDescending(node => node.WaitTime?.MedianSeconds ?? -1)
                .ThenByDescending(node => node.ExecutionCount)
                .ThenBy(node => node.FlowNodeId, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Wo Tokens gerade stehen, sagt der persistierte Tokenstand — nicht die Ereignisspur:
    /// Er ist auch für Instanzen aus der Zeit vor der Spur verbindlich.
    /// </summary>
    private static Dictionary<string, int> WaitingTokensByNode(IReadOnlyList<InstanceFacts> instances) =>
        instances
            .SelectMany(instance => instance.Tokens)
            .Where(token => token.State == FlowNodeState.Active && token.CurrentFlowNode is not null)
            .GroupBy(token => token.CurrentFlowNode!.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private sealed class NodeAccumulator
    {
        public int ExecutionCount { get; set; }
        public int WaitingTokenCount { get; set; }
        public List<double> WaitSeconds { get; } = [];
    }

    /* ------------------------------------------------------------------------ Knotennamen */

    private static async Task<Guid?> DeployedDefinitionIdAsync(IStorageSystem storage, string metaDefinitionId)
    {
        try
        {
            return (await storage.DefinitionStorage.GetDeployedDefinition(metaDefinitionId))?.Id;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Die Namen der Schritte aus der benennenden Version. <c>null</c> heisst: Diese Version war
    /// nicht lesbar — geraten wird nichts, die Kennung des Knotens steht ohnehin daneben. Die
    /// uebrigen Kennzahlen haengen nicht daran und bleiben vollstaendig.
    /// </summary>
    private static async Task<Dictionary<string, string>?> FlowNodeNamesAsync(
        IStorageSystem storage,
        Guid definitionId)
    {
        Definitions model;
        try
        {
            model = ModelParser.ParseModel(await storage.DefinitionStorage.GetBinary(definitionId));
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or FlowzerModelParseException or XmlException)
        {
            return null;
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var flowNode in model.GetProcesses().SelectMany(FlowNodes))
        {
            if (string.IsNullOrWhiteSpace(flowNode.Name)) continue;
            names.TryAdd(flowNode.Id, flowNode.Name);
        }

        return names;
    }

    /// <summary>Alle Knoten der Version, auch die in Teilprozessen.</summary>
    private static IEnumerable<FlowNode> FlowNodes(IFlowElementContainer container)
    {
        foreach (var element in container.FlowElements)
        {
            if (element is FlowNode flowNode) yield return flowNode;
            if (element is IFlowElementContainer nested)
            {
                foreach (var inner in FlowNodes(nested)) yield return inner;
            }
        }
    }

    /* ------------------------------------------------------------------------- Instanzfakt */

    private enum InstanceOutcome { Running, Completed, Cancelled, Failed }

    /// <summary>
    /// Was eine Instanz zur Auswertung beiträgt. Start- und Endzeitpunkt werden genauso aus den
    /// Tokens abgeleitet wie in der Instanzprojektion: Die Ablage speichert keinen eigenen
    /// Instanz-Zeitstempel.
    /// </summary>
    private sealed record InstanceFacts(
        Guid InstanceId,
        string MetaDefinitionId,
        Guid DefinitionId,
        IReadOnlyList<Guid> DefinitionIds,
        InstanceOutcome Outcome,
        DateTimeOffset? StartedAt,
        DateTimeOffset? FinishedAt,
        IReadOnlyList<Token> Tokens)
    {
        public double? DurationSeconds => StartedAt is { } start && FinishedAt is { } finish && finish >= start
            ? (finish - start).TotalSeconds
            : null;

        public static InstanceFacts From(ProcessInstanceInfo instance) => new(
            instance.InstanceId,
            instance.metaDefinitionId,
            instance.DefinitionId,
            [.. instance.Migrations.Select(migration => migration.SourceDefinitionId).Append(instance.DefinitionId).Distinct()],
            Classify(instance.State),
            instance.Tokens.Count == 0 ? null : AsUtc(instance.Tokens.Min(token => token.StartTime)),
            instance.IsFinished && instance.Tokens.Count > 0
                ? AsUtc(instance.Tokens.Max(token => token.LastStateChangeTime))
                : null,
            instance.Tokens);

        // Ein Abbruch ist kein Fehler, sondern ein regulaerer Ausgang — dieselbe Einteilung wie
        // im Betriebsbild unter /operations/diagnostics.
        private static InstanceOutcome Classify(ProcessInstanceState state) => state switch
        {
            ProcessInstanceState.Completed or ProcessInstanceState.Compensated => InstanceOutcome.Completed,
            ProcessInstanceState.Failed => InstanceOutcome.Failed,
            ProcessInstanceState.Terminated => InstanceOutcome.Cancelled,
            _ => InstanceOutcome.Running
        };

        /// <summary>Die Ablage speichert Tokenzeiten ohne Zonenangabe; sie sind UTC.</summary>
        private static DateTimeOffset AsUtc(DateTime value) =>
            new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
