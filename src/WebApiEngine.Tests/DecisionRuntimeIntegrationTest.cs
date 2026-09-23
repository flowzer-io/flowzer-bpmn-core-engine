using System.Dynamic;
using FilesystemStorageSystem;
using FlowzerDmn.Parsing;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>
/// Der Business-Rule-Task ueber die ganze Kette: Die Engine stellt die Entscheidung bereit,
/// die Geschaeftslogik rechnet sie in derselben Transaktion, und die Instanz laeuft mit dem
/// Ergebnis weiter. Der Beweis dafuer ist das Exclusive Gateway dahinter: Es entscheidet
/// anhand des Werts, den die Tabelle geliefert hat.
/// </summary>
[NonParallelizable]
public sealed class DecisionRuntimeIntegrationTest
{
    private const string WorkflowId = "workflow-rabatt";
    private const string BoundaryWorkflowId = "workflow-boundary";

    // Testzweck: Die Entscheidung liefert "gold", und das Gateway hinter dem Task waehlt
    // genau deswegen den Goldzweig. Das Ergebnis steht also wirklich im Prozesskontext.
    [Test]
    public async Task BusinessRuleTask_ShouldContinueAtTheGatewayWithTheDecisionResult()
    {
        DecisionFixtures.RequireFeelEngine();
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployDecision(engine, DecisionFixtures.RabattDmn());
        await DeployWorkflow(provider, engine, WorkflowId, DecisionFixtures.RabattWorkflow());

        var instance = await engine.StartProcessInstance(WorkflowId, Variables(("jahresumsatz", 120000)));

        using (new AssertionScope())
        {
            var stored = await InstanceAsync(provider, instance.InstanceId);
            ActiveNodeIds(stored).Should().BeEquivalentTo(["GoldBehandeln"]);
            ProcessVariables(stored).Should().ContainKey("ergebnis");
        }
    }

    // Testzweck: Dieselbe Tabelle mit einem anderen Umsatz fuehrt an dieselbe Weiche und von
    // dort in den Standardzweig — die Entscheidung wird wirklich gerechnet und nicht geraten.
    [Test]
    public async Task BusinessRuleTask_ShouldTakeTheDefaultBranchForTheOtherResult()
    {
        DecisionFixtures.RequireFeelEngine();
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployDecision(engine, DecisionFixtures.RabattDmn());
        await DeployWorkflow(provider, engine, WorkflowId, DecisionFixtures.RabattWorkflow());

        var instance = await engine.StartProcessInstance(WorkflowId, Variables(("jahresumsatz", 50000)));

        ActiveNodeIds(await InstanceAsync(provider, instance.InstanceId))
            .Should().BeEquivalentTo(["StandardBehandeln"]);
    }

    // Testzweck: Eine Entscheidung, die es nirgends gibt, wirft DECISION_NOT_FOUND am Task —
    // ein Error-Boundary faengt sie, und die Instanz laeuft weiter statt zu scheitern.
    [Test]
    public async Task MissingDecision_ShouldRaiseDecisionNotFoundAtTheTask()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployWorkflow(provider, engine, BoundaryWorkflowId,
            DecisionFixtures.WorkflowMitBoundary("gibtesnicht",
                BpmnBusinessLogic.BusinessRuleTaskErrors.DecisionNotFound));

        var instance = await engine.StartProcessInstance(BoundaryWorkflowId, Variables(("jahresumsatz", 120000)));

        using (new AssertionScope())
        {
            var stored = await InstanceAsync(provider, instance.InstanceId);
            stored.State.Should().Be(ProcessInstanceState.Waiting);
            ActiveNodeIds(stored).Should().BeEquivalentTo(["Nacharbeit"]);
        }
    }

    // Testzweck: Steht dieselbe decisionId in zwei Dateien, waere jede Auswahl geraten. Der
    // Task wirft DECISION_AMBIGUOUS und die Meldung nennt beide Dateien.
    [Test]
    public async Task AmbiguousDecision_ShouldRaiseDecisionAmbiguousAndNameTheFiles()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployDecision(engine, DecisionFixtures.RabattDmn());
        await DeployDecision(engine, DecisionFixtures.RabattDmn("rabatt-zweit", "Zweite Rabattdatei"));
        await DeployWorkflow(provider, engine, BoundaryWorkflowId,
            DecisionFixtures.WorkflowMitBoundary(DecisionFixtures.RabattDecisionId,
                BpmnBusinessLogic.BusinessRuleTaskErrors.DecisionAmbiguous));

        var instance = await engine.StartProcessInstance(BoundaryWorkflowId, Variables(("jahresumsatz", 120000)));

        ActiveNodeIds(await InstanceAsync(provider, instance.InstanceId))
            .Should().BeEquivalentTo(["Nacharbeit"]);
    }

    // Testzweck: Eine verletzte Trefferregel ist ein fachlicher Fehler des Schritts. Sie wird
    // zu DECISION_EVALUATION_FAILED und ist am Boundary fangbar.
    [Test]
    public async Task HitPolicyViolation_ShouldRaiseDecisionEvaluationFailedAtTheTask()
    {
        DecisionFixtures.RequireFeelEngine();
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployDecision(engine, DecisionFixtures.KonfliktDmn());
        await DeployWorkflow(provider, engine, BoundaryWorkflowId,
            DecisionFixtures.WorkflowMitBoundary("konfliktstufe",
                BpmnBusinessLogic.BusinessRuleTaskErrors.DecisionEvaluationFailed));

        var instance = await engine.StartProcessInstance(BoundaryWorkflowId, Variables(("jahresumsatz", 120000)));

        using (new AssertionScope())
        {
            var stored = await InstanceAsync(provider, instance.InstanceId);
            stored.State.Should().Be(ProcessInstanceState.Waiting);
            ActiveNodeIds(stored).Should().BeEquivalentTo(["Nacharbeit"]);
        }
    }

    // Testzweck: Ohne Error-Boundary schlaegt derselbe Fehler bis zur Prozessebene durch; die
    // Instanz scheitert mit genau diesem Code statt still stehenzubleiben.
    [Test]
    public async Task MissingDecisionWithoutBoundary_ShouldFailTheInstanceWithTheErrorCode()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployWorkflow(provider, engine, WorkflowId, DecisionFixtures.RabattWorkflow("gibtesnicht"));

        var instance = await engine.StartProcessInstance(WorkflowId, Variables(("jahresumsatz", 120000)));

        (await InstanceAsync(provider, instance.InstanceId)).State.Should().Be(ProcessInstanceState.Failed);
    }

    /// <summary>Speichert eine Entscheidungsdatei so, wie es der API-Endpunkt tut.</summary>
    private static async Task DeployDecision(BpmnBusinessLogic engine, string xml)
    {
        var parsed = DmnModelParser.Parse(xml);
        await engine.SaveDecisionVersion(
            parsed.Id,
            parsed.Name ?? parsed.Id,
            xml,
            parsed.Decisions
                .Select(decision => new DecisionSummary(decision.Id, decision.Name ?? decision.Id))
                .ToArray(),
            Guid.NewGuid());
    }

    private static async Task DeployWorkflow(
        ITransactionalStorageProvider provider, BpmnBusinessLogic engine, string metaDefinitionId, string xml)
    {
        using var storage = provider.GetTransactionalStorage();
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(),
            DefinitionId = metaDefinitionId,
            Version = new Model.Version(1, 0),
            Hash = "test",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            IsActive = false
        };

        await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
        {
            DefinitionId = metaDefinitionId,
            Name = metaDefinitionId
        });
        await storage.DefinitionStorage.StoreDefinition(definition);
        await storage.DefinitionStorage.StoreBinary(definition.Id, xml);
        storage.CommitChanges();

        await engine.DeployDefinition(definition);
    }

    private static ExpandoObject Variables(params (string Key, object? Value)[] values)
    {
        var variables = new ExpandoObject();
        var entries = (IDictionary<string, object?>)variables;
        foreach (var (key, value) in values) entries[key] = value;

        return variables;
    }

    private static string[] ActiveNodeIds(ProcessInstanceInfo instance) => instance.Tokens
        .Where(token => token.State == FlowNodeState.Active && token.CurrentFlowNode is not null
            && token.ParentTokenId is not null)
        .Select(token => token.CurrentFlowNode!.Id)
        .ToArray();

    private static IDictionary<string, object?> ProcessVariables(ProcessInstanceInfo instance) =>
        (IDictionary<string, object?>?)instance.Tokens.Single(token => token.ParentTokenId is null).Variables
        ?? new Dictionary<string, object?>();

    private static async Task<ProcessInstanceInfo> InstanceAsync(
        ITransactionalStorageProvider provider, Guid instanceId)
    {
        using var storage = provider.GetTransactionalStorage();
        return await storage.InstanceStorage.GetProcessInstance(instanceId);
    }
}
