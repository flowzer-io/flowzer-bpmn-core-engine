using core_engine.Exceptions;
using FlowzerDmn.Evaluation;
using FlowzerDmn.Exceptions;
using FlowzerDmn.Parsing;
using Model;
using BusinessRuleTask = BPMN.Activities.BusinessRuleTask;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.BusinessLogic;

public partial class BpmnBusinessLogic
{
    /// <summary>
    /// BPMN-Fehler, die ein Business-Rule-Task an seinem eigenen Knoten wirft. Sie sind Teil
    /// des Modellvertrags: Ein Error-Boundary am Task kann sie fangen.
    /// </summary>
    public static class BusinessRuleTaskErrors
    {
        /// <summary>Keine Entscheidungsdatei enthaelt eine Entscheidung mit dieser Kennung.</summary>
        public const string DecisionNotFound = "DECISION_NOT_FOUND";

        /// <summary>Mehrere Entscheidungsdateien enthalten dieselbe Kennung; welche gilt, ist offen.</summary>
        public const string DecisionAmbiguous = "DECISION_AMBIGUOUS";

        /// <summary>
        /// Die Tabelle liess sich nicht rechnen: verletzte Trefferregel, ein Eingabewert
        /// ausserhalb seiner <c>inputValues</c> oder ein Fehler in der Auswertung selbst.
        /// </summary>
        public const string DecisionEvaluationFailed = "DECISION_EVALUATION_FAILED";
    }

    /// <summary>
    /// Rechnet alle Entscheidungen, die diese Instanz bereitgestellt hat — lokal und in
    /// derselben Transaktion wie das Speichern der Instanz.
    /// </summary>
    /// <remarks>
    /// Die Schleife laeuft, bis nichts mehr aussteht: Ein abgeschlossener Business-Rule-Task
    /// laesst die Instanz weiterlaufen und kann dabei am naechsten Knoten schon die naechste
    /// Entscheidung anfordern. Jeder Schritt zaehlt gegen dasselbe Budget wie die Aufrufe und
    /// die Nachrichten; eine Schleife im Modell sprengt damit nicht die Transaktion.
    /// </remarks>
    private static async Task EvaluatePendingDecisions(
        ITransactionalStorage storageSystem,
        InstanceContext current,
        Action countStep)
    {
        for (var decisions = current.Instance.TakePendingDecisions();
             decisions.Count > 0;
             decisions = current.Instance.TakePendingDecisions())
        {
            foreach (var decision in decisions)
            {
                countStep();
                await EvaluateDecision(storageSystem, current.Instance, decision);
            }
        }
    }

    /// <summary>
    /// Sucht die Entscheidung in den deployten Dateien und rechnet sie. Jeder fachliche
    /// Fehlausgang wird zu einem BPMN-Fehler am Task; die Instanz laeuft danach weiter,
    /// statt die ganze Mutation scheitern zu lassen.
    /// </summary>
    private static async Task EvaluateDecision(
        ITransactionalStorage storageSystem,
        InstanceEngine instance,
        PendingDecision pending)
    {
        var matches = await FindDeployedDecisions(storageSystem, pending.DecisionId);
        if (matches.Count == 0)
        {
            instance.ThrowBpmnError(pending.TokenId, BusinessRuleTaskErrors.DecisionNotFound,
                $"No deployed decision definition contains a decision with the id \"{pending.DecisionId}\".");
            return;
        }

        if (matches.Count > 1)
        {
            // Welche Datei gaelte? Ein stiller Zufallstreffer waere hier der schlimmste
            // Ausgang: Die Tabelle rechnete, nur eben moeglicherweise die falsche.
            instance.ThrowBpmnError(pending.TokenId, BusinessRuleTaskErrors.DecisionAmbiguous,
                $"The decision id \"{pending.DecisionId}\" exists in more than one decision definition: "
                + $"{string.Join(", ", matches.Select(match => match.DecisionDefinitionId))}.");
            return;
        }

        // Der Name der Ergebnisvariablen steht am Modell, nicht in der Ablage: Er gehoert zum
        // aufrufenden Task und nicht zur Entscheidung, die mehrere Tasks benutzen koennen.
        var resultVariable = RequireResultVariable(instance, pending);

        DmnDecisionResult result;
        try
        {
            var definitions = DmnModelParser.Parse(matches[0].Xml);
            result = new DmnDecisionEvaluator(FlowzerConfig.Default.FeelEngine)
                .Evaluate(definitions, pending.DecisionId, SnapshotOf(pending.Variables));
        }
        // Absichtlich die Basis: Neben der verletzten Trefferregel, dem Eingabewert ausserhalb
        // seines Bereichs und dem Auswertungsfehler faellt damit auch eine gespeicherte Datei
        // darunter, die sich nicht mehr lesen laesst. Auch das ist ein Fehler dieses Schritts
        // und darf die uebrige Instanz nicht mitreissen.
        catch (DmnException exception)
        {
            instance.ThrowBpmnError(pending.TokenId, BusinessRuleTaskErrors.DecisionEvaluationFailed,
                exception.Message);
            return;
        }

        instance.CompleteDecision(pending.TokenId, resultVariable, result.Value);
    }

    /// <summary>
    /// Die juengste Fassung jeder Entscheidungsdatei, die diese Entscheidung enthaelt.
    /// </summary>
    /// <remarks>
    /// Gesucht wird wie beim aufgerufenen Prozess erst im Moment des Aufrufs und immer in der
    /// juengsten Fassung: Eine Entscheidung darf spaeter entstehen, und welche Fassung gilt,
    /// entscheidet der Zeitpunkt. Die enthaltenen Kennungen stehen am gespeicherten Stand —
    /// die Suche muss dafuer keine einzige Datei parsen.
    /// </remarks>
    private static async Task<IReadOnlyList<DeployedDecision>> FindDeployedDecisions(
        ITransactionalStorage storageSystem, string decisionId)
    {
        IReadOnlyList<DecisionDefinition> definitions;
        try
        {
            definitions = await storageSystem.DecisionStorage.GetDefinitions();
        }
        catch (NotSupportedException)
        {
            // Kompatibilitaetsgrenze fuer aeltere Host-Adapter ohne Entscheidungsablage: Dort
            // gibt es keine Entscheidung, und der Task meldet genau das.
            return [];
        }

        var matches = new List<DeployedDecision>();
        foreach (var definition in definitions.OrderBy(entry => entry.DecisionDefinitionId, StringComparer.Ordinal))
        {
            var latest = await storageSystem.DecisionStorage.GetLatestVersion(definition.DecisionDefinitionId);
            if (latest is null)
            {
                continue;
            }

            if (latest.Decisions.Any(entry => string.Equals(entry.DecisionId, decisionId, StringComparison.Ordinal)))
            {
                matches.Add(new DeployedDecision(definition.DecisionDefinitionId, latest.Xml));
            }
        }

        return matches;
    }

    /// <summary>
    /// Der Name, unter dem das Ergebnis in den Prozess geschrieben wird
    /// (<c>zeebe:calledDecision/@resultVariable</c>).
    /// </summary>
    /// <remarks>
    /// Die Veroeffentlichungspruefung verlangt ihn bereits; ein aelteres Modell aus der Ablage
    /// koennte ihn trotzdem nicht tragen. Dann ist klar zu sagen, was fehlt — ein erfundener
    /// Name legte das Ergebnis an eine Stelle, an der es niemand sucht.
    /// </remarks>
    private static string RequireResultVariable(InstanceEngine instance, PendingDecision pending)
    {
        var task = instance.GetWaitingBusinessRuleTasks()
            .FirstOrDefault(token => token.Id == pending.TokenId)?
            .CurrentFlowNode as BusinessRuleTask;

        if (string.IsNullOrWhiteSpace(task?.FlowzerResultVariable))
        {
            throw new FlowzerRuntimeException(
                $"Der Business-Rule-Task {task?.Id ?? pending.DecisionId} nennt keine Ergebnisvariable "
                + "(zeebe:calledDecision/@resultVariable).");
        }

        return task.FlowzerResultVariable;
    }

    /// <summary>
    /// Die Variablen der Entscheidung als Momentaufnahme. Der Auswerter arbeitet auf einer
    /// Lesesicht; eine <c>Variables</c>-Instanz ist keine.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> SnapshotOf(Variables variables) =>
        new Dictionary<string, object?>((IDictionary<string, object?>)variables, StringComparer.Ordinal);

    /// <summary>Eine deployte Entscheidungsdatei, die die gesuchte Entscheidung enthaelt.</summary>
    private sealed record DeployedDecision(string DecisionDefinitionId, string Xml);

    /// <summary>
    /// Speichert einen neuen Stand einer Entscheidungsdatei und legt den Katalogeintrag an,
    /// falls es ihn noch nicht gibt.
    /// </summary>
    /// <remarks>
    /// Laeuft unter derselben Sperre wie das Deployen eines Workflows: Nur so ist die naechste
    /// Versionsnummer wirklich die naechste. Zwei gleichzeitige Uploads bekaemen sonst beide
    /// dieselbe Nummer — die Ablage lehnte den zweiten zwar ab, aber erst nach dem Schreiben
    /// des Katalogeintrags.
    /// </remarks>
    public async Task<DecisionDefinitionVersion> SaveDecisionVersion(
        string decisionDefinitionId,
        string name,
        string xml,
        IReadOnlyList<DecisionSummary> decisions,
        Guid? deployedBy)
    {
        DefinitionIdRules.EnsureValid(decisionDefinitionId);

        await _engineMutationLock.WaitAsync();
        try
        {
            using var storageSystem = storageProvider.GetTransactionalStorage();

            var latest = await storageSystem.DecisionStorage.GetLatestVersion(decisionDefinitionId);
            var version = new DecisionDefinitionVersion(
                decisionDefinitionId,
                (latest?.Version ?? 0) + 1,
                xml,
                DateTimeOffset.UtcNow,
                deployedBy,
                decisions);

            await storageSystem.DecisionStorage.SaveDefinition(new DecisionDefinition(decisionDefinitionId, name));
            await storageSystem.DecisionStorage.SaveVersion(version);
            storageSystem.CommitChanges();

            return version;
        }
        finally
        {
            _engineMutationLock.Release();
        }
    }

    /// <summary>Ergebnis eines Loeschversuchs an einer Entscheidungsdatei.</summary>
    /// <param name="Definition">Die geloeschte Datei; <c>null</c>, wenn es sie nicht gab.</param>
    /// <param name="UsedBy">Workflows, die sie benutzen. Nicht leer heisst: nichts geloescht.</param>
    public sealed record DecisionDeletion(DecisionDefinition? Definition, IReadOnlyList<string> UsedBy);

    /// <summary>
    /// Loescht eine Entscheidungsdatei samt allen Staenden — aber nur, wenn kein Workflow eine
    /// ihrer Entscheidungen aufruft.
    /// </summary>
    /// <remarks>
    /// Wie beim Formular unter derselben Sperre wie das Deployen und vollstaendig darin: Sonst
    /// liesse sich zwischen Pruefung und Loeschen ein Workflow deployen, der genau diese
    /// Entscheidung aufruft — und dessen Business-Rule-Task liefe danach in
    /// <c>DECISION_NOT_FOUND</c>.
    /// </remarks>
    public async Task<DecisionDeletion> DeleteDecisionIfUnused(string decisionDefinitionId)
    {
        DefinitionIdRules.EnsureValid(decisionDefinitionId);

        await _engineMutationLock.WaitAsync();
        try
        {
            using var storageSystem = storageProvider.GetTransactionalStorage();

            var definition = await storageSystem.DecisionStorage.GetDefinition(decisionDefinitionId);
            if (definition is null)
            {
                return new DecisionDeletion(null, []);
            }

            var decisionIds = (await storageSystem.DecisionStorage.GetVersions(decisionDefinitionId))
                .SelectMany(version => version.Decisions)
                .Select(entry => entry.DecisionId)
                .ToHashSet(StringComparer.Ordinal);

            var usedBy = await FindWorkflowsUsingDecisions(storageSystem, decisionIds);
            if (usedBy.Count > 0)
            {
                return new DecisionDeletion(definition, usedBy);
            }

            await storageSystem.DecisionStorage.DeleteDefinition(decisionDefinitionId);
            storageSystem.CommitChanges();

            return new DecisionDeletion(definition, []);
        }
        finally
        {
            _engineMutationLock.Release();
        }
    }

    /// <summary>
    /// Nennt die Workflows, deren Business-Rule-Tasks eine dieser Entscheidungen aufrufen.
    /// </summary>
    /// <remarks>
    /// Geprueft wird zweierlei — dieselbe Ueberlegung wie beim Formular: die jeweils
    /// <em>deployte</em> Fassung jedes Katalogeintrags, denn daraus entstehen kuenftige
    /// Instanzen, und die Fassungen, auf denen noch Instanzen <em>laufen</em>. Eine laufende
    /// Instanz, die spaeter an einem Business-Rule-Task ankommt, braucht die Entscheidung
    /// genauso; ohne sie liefe sie in <c>DECISION_NOT_FOUND</c>.
    /// </remarks>
    private static async Task<List<string>> FindWorkflowsUsingDecisions(
        ITransactionalStorage storageSystem,
        IReadOnlySet<string> decisionIds)
    {
        if (decisionIds.Count == 0)
        {
            return [];
        }

        // Fassung -> Anzeigename. Mehrere Instanzen teilen sich eine Fassung; jede nur einmal
        // lesen und parsen.
        var zuPruefen = new Dictionary<Guid, string>();

        foreach (var metaDefinition in await storageSystem.DefinitionStorage.GetAllMetaDefinitions())
        {
            var deployed = await storageSystem.DefinitionStorage.GetDeployedDefinition(metaDefinition.DefinitionId);
            if (deployed is not null)
            {
                zuPruefen[deployed.Id] = metaDefinition.Name ?? metaDefinition.DefinitionId;
            }
        }

        foreach (var instanz in await storageSystem.InstanceStorage.GetAllActiveInstances())
        {
            zuPruefen.TryAdd(instanz.DefinitionId, instanz.metaDefinitionId);
        }

        var treffer = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (definitionId, anzeigename) in zuPruefen)
        {
            BPMN.Infrastructure.Definitions modell;
            try
            {
                modell = ModelParser.ParseModel(await storageSystem.DefinitionStorage.GetBinary(definitionId));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Fail-closed wie beim Formular: Ein Modell, das sich nicht lesen laesst,
                // koennte die Entscheidung aufrufen. Es zu ueberspringen hiesse, im Zweifel
                // zu loeschen.
                treffer.Add(anzeigename);
                continue;
            }

            var benutzt = modell.GetProcesses()
                .SelectMany(AlleFlowElemente)
                .OfType<BusinessRuleTask>()
                .Any(task => task.FlowzerCalledDecisionId is { } called && decisionIds.Contains(called));

            if (benutzt)
            {
                treffer.Add(anzeigename);
            }
        }

        return [.. treffer];
    }
}
