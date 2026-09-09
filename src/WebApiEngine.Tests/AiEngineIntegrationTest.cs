using System.Dynamic;
using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Model;
using StorageSystem;
using WebApiEngine.Ai;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>End-to-End-Vertrag zwischen BPMN-Token, persistentem KI-Lauf und Prozessausgabe.</summary>
[NonParallelizable]
public sealed class AiEngineIntegrationTest
{
    private static readonly Guid ConnectionId = Guid.Parse("118adeb6-65a4-4e57-a03b-d3b0a3300ac9");
    private static readonly DateTime Now = new(2026, 9, 9, 20, 0, 0, DateTimeKind.Utc);

    // Testzweck: Ein aktiver KI-Service-Task erzeugt genau einen internen, unveraenderlich
    // gebundenen Lauf und niemals einen ueber die generische Worker-API abrufbaren Auftrag.
    [Test]
    public async Task Start_ShouldCreateBoundAiRunInsteadOfExternalServiceJob()
    {
        using var context = new Context();
        var definition = await context.DeployAsync();

        // Die Administration veraendert nach dem Deployment den aktuellen Stand. Der neue
        // Lauf muss trotzdem Revision und Modell des Deployment-Snapshots behalten.
        using (var update = context.Provider.GetTransactionalStorage())
        {
            var current = (await update.AiConnectionStorage.Get(ConnectionId))!;
            await update.AiConnectionStorage.TryUpdate(current with
            {
                Revision = 2,
                DefaultModel = "model-new",
                SecretReference = "env:FLOWZER_AI_NEW"
            }, 1);
            update.CommitChanges();
        }

        ExpandoObject input = new();
        ((IDictionary<string, object?>)input)["request"] = "Please classify";
        var instance = await context.BusinessLogic.StartProcessInstance(definition.DefinitionId, input);

        using var storage = context.Provider.GetTransactionalStorage();
        var run = (await storage.AiRunStorage.List()).Should().ContainSingle().Subject;
        run.ProcessInstanceId.Should().Be(instance.InstanceId);
        run.FlowNodeId.Should().Be("Ai_1");
        run.ConnectionRevision.Should().Be(1);
        run.Model.Should().Be("model-deployed");
        using var inputs = JsonDocument.Parse(run.InputsJson);
        inputs.RootElement.GetProperty("request").GetString().Should().Be("Please classify");
        (await storage.ServiceTaskStorage.GetJobs()).Should().BeEmpty();

        var storedDefinition = await storage.DefinitionStorage.GetDefinitionById(definition.Id);
        storedDefinition.AiTaskBindings!["Ai_1"]
            .Should().Be(new BoundAiTask(ConnectionId, 1, "model-deployed"));
    }

    // Testzweck: Ein lokal bereits schema-validiertes Providerergebnis wird genau einmal
    // ueber das deklarierte Output-Mapping in die Instanz uebernommen; KI wird dabei nicht als
    // menschlicher Bearbeiter ausgegeben.
    [Test]
    public async Task ReadyResult_ShouldCompleteRunAndAdvanceInstanceExactlyOnce()
    {
        using var context = new Context();
        var definition = await context.DeployAsync();
        ExpandoObject input = new();
        ((IDictionary<string, object?>)input)["request"] = "Please classify";
        var started = await context.BusinessLogic.StartProcessInstance(definition.DefinitionId, input);
        await context.MakeResultReadyAsync("{\"category\":\"support\"}");
        var time = new FakeTimeProvider(new DateTimeOffset(Now.AddMinutes(1)));
        var policy = TestPolicy();

        var first = await context.BusinessLogic.CompleteAiRunBatchAsync(time, policy, default);
        var second = await context.BusinessLogic.CompleteAiRunBatchAsync(time, policy, default);

        first.Claimed.Should().Be(1);
        first.Completed.Should().Be(1);
        second.Claimed.Should().Be(0);
        using var storage = context.Provider.GetTransactionalStorage();
        (await storage.AiRunStorage.List()).Single().Status.Should().Be(AiRunStatus.Completed);
        var instance = await storage.InstanceStorage.GetProcessInstance(started.InstanceId);
        instance.State.Should().Be(ProcessInstanceState.Completed);
        var aiToken = instance.Tokens.Single(token => token.CurrentFlowNode?.Id == "Ai_1");
        aiToken.CompletedByUserId.Should().BeNull();
        var master = instance.Tokens.Single(token => token.ParentTokenId is null);
        ((IDictionary<string, object?>)master.Variables!)["classification"].Should().Be("support");
    }

    // Testzweck: Wird eine Instanz waehrend eines KI-Schritts abgebrochen, entzieht derselbe
    // Engine-Commit dem internen Lauf seine Lease-/Claimfaehigkeit; ein spaeter Hintergrundtick
    // darf keinen Provider mehr aufrufen.
    [Test]
    public async Task CancellingInstance_ShouldCancelPendingAiRun()
    {
        using var context = new Context();
        var definition = await context.DeployAsync();
        ExpandoObject input = new();
        ((IDictionary<string, object?>)input)["request"] = "Please classify";
        var started = await context.BusinessLogic.StartProcessInstance(definition.DefinitionId, input);

        await context.BusinessLogic.CancelInstance(started.InstanceId);

        using var storage = context.Provider.GetTransactionalStorage();
        var run = (await storage.AiRunStorage.List()).Single();
        run.Status.Should().Be(AiRunStatus.Cancelled);
        (await storage.AiRunStorage.ClaimProviderRuns(
            "provider",
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            1)).Should().BeEmpty();
    }

    // Testzweck: Derselbe unveränderliche Eingabesnapshot bleibt auch nach einer
    // JSON-Neusortierung identisch; reine Objektreihenfolge darf keinen Laufkonflikt auslösen.
    [Test]
    public void AiRunSnapshot_ShouldCompareInputsByJsonValue()
    {
        var current = AiRunStorageTest.Run() with { InputsJson = "{\"request\":\"x\",\"priority\":2}" };
        var expected = current with { InputsJson = "{\"priority\":2,\"request\":\"x\"}" };

        BpmnBusinessLogic.SameAiRunSnapshot(current, expected).Should().BeTrue();
    }

    private static AiRunExecutionPolicy TestPolicy() => new(
        Enabled: true,
        PollInterval: TimeSpan.FromSeconds(5),
        BatchSize: 10,
        LeaseDuration: TimeSpan.FromMinutes(5),
        HeartbeatInterval: TimeSpan.FromSeconds(30),
        RetryBaseDelay: TimeSpan.FromSeconds(30),
        MaximumRetryDelay: TimeSpan.FromMinutes(10));

    private sealed class Context : IDisposable
    {
        private readonly string? _previousRoot;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"flowzer-ai-engine-{Guid.NewGuid():N}");

        public Context()
        {
            _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
            Provider = new FileSystemTransactionalStorageProvider();
            BusinessLogic = new BpmnBusinessLogic(Provider, aiSecretStore: new ReadySecretStore());
        }

        public FileSystemTransactionalStorageProvider Provider { get; }
        public BpmnBusinessLogic BusinessLogic { get; }

        public async Task<BpmnDefinition> DeployAsync()
        {
            var definition = new BpmnDefinition
            {
                Id = Guid.NewGuid(),
                DefinitionId = "Definitions_Ai",
                Hash = "hash",
                SavedByUser = Guid.NewGuid(),
                SavedOn = Now,
                Version = new Model.Version(1, 0),
                IsActive = false
            };
            using (var storage = Provider.GetTransactionalStorage())
            {
                await storage.AiConnectionStorage.TryCreate(new AiConnection(
                    ConnectionId,
                    "AI",
                    AiProviderKind.OpenAi,
                    AiProcessingLocation.Cloud,
                    null,
                    "model-deployed",
                    "env:FLOWZER_AI_DEPLOYED",
                    true,
                    1,
                    new DateTimeOffset(Now),
                    Guid.NewGuid()));
                await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
                {
                    DefinitionId = definition.DefinitionId,
                    Name = "AI workflow"
                });
                await storage.DefinitionStorage.StoreDefinition(definition);
                await storage.DefinitionStorage.StoreBinary(definition.Id, ProcessXml);
                storage.CommitChanges();
            }

            await BusinessLogic.DeployDefinition(definition);
            return definition;
        }

        public async Task MakeResultReadyAsync(string outputJson)
        {
            using var storage = Provider.GetTransactionalStorage();
            var claimed = (await storage.AiRunStorage.ClaimProviderRuns(
                "provider",
                Now.AddSeconds(1),
                Now.AddMinutes(5),
                1)).Single();
            var started = claimed with
            {
                Attempt = 1,
                ProviderCallStartedAtUtc = Now.AddSeconds(2),
                Revision = claimed.Revision + 1,
                UpdatedAtUtc = Now.AddSeconds(2)
            };
            await storage.AiRunStorage.TryUpdate(started, claimed.Revision, "provider", Now.AddSeconds(2));
            var ready = started with
            {
                Status = AiRunStatus.ResultReady,
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                OutputJson = outputJson,
                ResultModel = "provider-model",
                InputTokens = 10,
                OutputTokens = 2,
                TotalTokens = 12,
                Revision = started.Revision + 1,
                UpdatedAtUtc = Now.AddSeconds(3)
            };
            (await storage.AiRunStorage.TryUpdate(ready, started.Revision, "provider", Now.AddSeconds(3)))
                .Status.Should().Be(AiRunWriteStatus.Written);
            storage.CommitChanges();
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousRoot);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ReadySecretStore : IAiSecretStore
    {
        public ValueTask<bool> ExistsAsync(string reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask<AiSecretValue?> ResolveAsync(string reference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Engine integration tests do not call an external provider.");
    }

    private const string ProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                          id="Definitions_Ai" targetNamespace="test">
          <bpmn:process id="Process_Ai" isExecutable="true">
            <bpmn:startEvent id="Start"><bpmn:outgoing>ToAi</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="ToAi" sourceRef="Start" targetRef="Ai_1" />
            <bpmn:serviceTask id="Ai_1" name="Classify">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="flowzer.ai.v1" retries="3" />
                <flowzer:aiTask contractVersion="1"
                    connectionId="118adeb6-65a4-4e57-a03b-d3b0a3300ac9"
                    instructionVersion="1" maxInputTokens="4096" maxOutputTokens="1024" timeoutSeconds="60">
                  <flowzer:instruction>Classify the request.</flowzer:instruction>
                  <flowzer:resultSchema>{"type":"object","properties":{"category":{"type":"string"}},"required":["category"],"additionalProperties":false}</flowzer:resultSchema>
                </flowzer:aiTask>
                <zeebe:ioMapping>
                  <zeebe:input source="=request" target="request" />
                  <zeebe:output source="=category" target="classification" />
                </zeebe:ioMapping>
              </bpmn:extensionElements>
              <bpmn:incoming>ToAi</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:sequenceFlow id="ToEnd" sourceRef="Ai_1" targetRef="End" />
            <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;
}
