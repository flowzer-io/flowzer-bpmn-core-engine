using BPMN.Activities;
using BPMN.Flowzer;
using core_engine.Exceptions;
using FluentAssertions;
using Model;
using System.Text.Json;
using StorageSystem;
using WebApiEngine.Ai;
using Task = System.Threading.Tasks.Task;

namespace WebApiEngine.Tests;

public sealed class AiTaskDeploymentValidatorTest
{
    private static readonly Guid ConnectionId = Guid.Parse("118adeb6-65a4-4e57-a03b-d3b0a3300ac9");

    // Testzweck: Ein Workflow darf keine unbekannte KI-Verbindung binden; der Fehler markiert
    // den konkreten BPMN-Schritt und nicht erst eine später hängenbleibende Instanz.
    [Test]
    public async Task ValidateAsync_ShouldRejectUnknownConnection()
    {
        var action = () => AiTaskDeploymentValidator.ValidateAsync(
            [BuildTask()],
            new ConnectionStorage(),
            new SecretStore(exists: true));

        var exception = (await action.Should().ThrowAsync<BpmnCapabilityValidationException>()).Which;
        exception.Code.Should().Be("bpmn.ai_task.connection_not_found");
        exception.ElementId.Should().Be("Ai_1");
        exception.PropertyPath.Should().Be("extensionElements.aiTask.connectionId");
    }

    // Testzweck: Deaktivierte Verbindungen bleiben historisch auflösbar, sind für eine neue
    // Workflow-Version aber nicht verwendbar.
    [Test]
    public async Task ValidateAsync_ShouldRejectDisabledConnection()
    {
        var action = () => AiTaskDeploymentValidator.ValidateAsync(
            [BuildTask()],
            new ConnectionStorage(Connection(enabled: false)),
            new SecretStore(exists: true));

        var exception = (await action.Should().ThrowAsync<BpmnCapabilityValidationException>()).Which;
        exception.Code.Should().Be("bpmn.ai_task.connection_disabled");
    }

    // Testzweck: Eine reine Metadatenreferenz ohne verfügbares Secret gilt nicht als
    // einsatzbereite Verbindung und wird vor dem Speichern abgelehnt.
    [Test]
    public async Task ValidateAsync_ShouldRejectConnectionWithoutSecret()
    {
        var action = () => AiTaskDeploymentValidator.ValidateAsync(
            [BuildTask()],
            new ConnectionStorage(Connection(enabled: true)),
            new SecretStore(exists: false));

        var exception = (await action.Should().ThrowAsync<BpmnCapabilityValidationException>()).Which;
        exception.Code.Should().Be("bpmn.ai_task.connection_not_ready");
    }

    // Testzweck: Eine aktive Verbindung mit serverseitig verfügbarem Secret erfüllt den
    // Bindungsvertrag, ohne dass der geheime Wert gelesen oder in das BPMN kopiert wird.
    [Test]
    public async Task ValidateAsync_ShouldAcceptReadyConnectionWithoutResolvingSecret()
    {
        var secrets = new SecretStore(exists: true);

        await AiTaskDeploymentValidator.ValidateAsync(
            [BuildTask()],
            new ConnectionStorage(Connection(enabled: true)),
            secrets);

        secrets.ExistsCalls.Should().Be(1);
        secrets.ResolveCalls.Should().Be(0);
    }

    // Testzweck: Das Deployment bindet die exakte Verbindungsrevision und das effektive
    // Standardmodell. Eine spaetere Aenderung der Verbindung darf den Workflow nicht
    // unbemerkt auf ein anderes Modell umstellen.
    [Test]
    public async Task BindAsync_ShouldCaptureConnectionRevisionAndDefaultModel()
    {
        var bindings = await AiTaskDeploymentValidator.BindAsync(
            [BuildTask()],
            new ConnectionStorage(Connection(enabled: true) with
            {
                Revision = 7,
                DefaultModel = "model-seven"
            }),
            new SecretStore(exists: true));

        bindings.Should().ContainSingle();
        bindings["Ai_1"].Should().Be(new BoundAiTask(ConnectionId, 7, "model-seven"));
    }

    // Testzweck: Ein explizites Modell im KI-Task hat Vorrang vor dem Default der gebundenen
    // Verbindung und wird ebenfalls unveraenderlich in der Workflow-Version gespeichert.
    [Test]
    public async Task BindAsync_ShouldCaptureExplicitTaskModel()
    {
        var task = BuildTask() with
        {
            FlowzerAiTask = BuildTask().FlowzerAiTask! with { Model = "model-task" }
        };

        var bindings = await AiTaskDeploymentValidator.BindAsync(
            [task],
            new ConnectionStorage(Connection(enabled: true)),
            new SecretStore(exists: true));

        bindings["Ai_1"].Model.Should().Be("model-task");
    }

    // Testzweck: Die effektiven Werkzeugrechte sind die Schnittmenge aus Registry,
    // Verbindungs-Allowlist und Taskvertrag; eine manipulierte Taskreferenz kann sie nicht erweitern.
    [TestCase(false, true, "bpmn.ai_task.tool_not_allowed")]
    [TestCase(true, false, "bpmn.ai_task.tool_not_registered")]
    public async Task BindAsync_ShouldRejectToolOutsideRegistryOrConnection(
        bool connectionAllows,
        bool registerTool,
        string expectedCode)
    {
        var task = ToolTask(AiToolApprovalMode.Automatic);
        var connection = Connection(enabled: true) with
        {
            AllowedTools = connectionAllows
                ? [new AiToolPermission("flowzer.directory.lookup", 1, false)]
                : []
        };
        var registry = new AiToolRegistry(registerTool
            ? [Tool("flowzer.directory.lookup", AiToolSideEffect.ReadOnly, false)]
            : []);

        var action = () => AiTaskDeploymentValidator.BindAsync(
            [task], new ConnectionStorage(connection), new SecretStore(exists: true), registry);

        var exception = (await action.Should().ThrowAsync<BpmnCapabilityValidationException>()).Which;
        exception.Code.Should().Be(expectedCode);
        exception.PropertyPath.Should().StartWith("extensionElements.aiTask.tool");
    }

    // Testzweck: Schreibende oder versendende Werkzeuge duerfen nie durch den automatischen
    // Modus laufen; eine Vorabfreigabe braucht zusaetzlich Registry- und Verbindungsfreigabe.
    [TestCase(AiToolApprovalMode.Automatic, AiToolSideEffect.Write, true, true, "bpmn.ai_task.tool_approval_required")]
    [TestCase(AiToolApprovalMode.PreApproved, AiToolSideEffect.Send, false, true, "bpmn.ai_task.tool_preapproval_not_allowed")]
    [TestCase(AiToolApprovalMode.PreApproved, AiToolSideEffect.Send, true, false, "bpmn.ai_task.tool_preapproval_not_allowed")]
    public async Task BindAsync_ShouldRejectUnsafeToolApproval(
        AiToolApprovalMode approval,
        AiToolSideEffect sideEffect,
        bool registryAllows,
        bool connectionAllows,
        string expectedCode)
    {
        var connection = Connection(enabled: true) with
        {
            AllowedTools = [new AiToolPermission("flowzer.directory.lookup", 1, connectionAllows)]
        };
        var registry = new AiToolRegistry([
            Tool("flowzer.directory.lookup", sideEffect, registryAllows)
        ]);

        var action = () => AiTaskDeploymentValidator.BindAsync(
            [ToolTask(approval)], new ConnectionStorage(connection), new SecretStore(exists: true), registry);

        (await action.Should().ThrowAsync<BpmnCapabilityValidationException>())
            .Which.Code.Should().Be(expectedCode);
    }

    // Testzweck: Ein erlaubtes Werkzeug bindet ID, Version, Vertragshash, Seiteneffekt und
    // Freigabemodus unveraenderlich an genau diese Workflow-Version.
    [Test]
    public async Task BindAsync_ShouldCaptureEffectiveToolContract()
    {
        var connection = Connection(enabled: true) with
        {
            AllowedTools = [new AiToolPermission("flowzer.directory.lookup", 1, false)]
        };
        var registry = new AiToolRegistry([
            Tool("flowzer.directory.lookup", AiToolSideEffect.ReadOnly, false)
        ]);

        var bindings = await AiTaskDeploymentValidator.BindAsync(
            [ToolTask(AiToolApprovalMode.Automatic)],
            new ConnectionStorage(connection),
            new SecretStore(exists: true),
            registry);

        var tool = bindings["Ai_1"].Tools.Should().ContainSingle().Subject;
        tool.ToolId.Should().Be("flowzer.directory.lookup");
        tool.ToolVersion.Should().Be(1);
        tool.SideEffect.Should().Be(AiToolSideEffect.ReadOnly);
        tool.ApprovalMode.Should().Be(AiToolApprovalMode.Automatic);
        tool.ContractHash.Should().MatchRegex("^[A-F0-9]{64}$");
        var validation = () => AiTaskDeploymentValidator.ValidateBindings(
            [ToolTask(AiToolApprovalMode.Automatic)], bindings, registry);
        validation.Should().NotThrow();
    }

    private static ServiceTask BuildTask() => new()
    {
        Id = "Ai_1",
        Name = "Classify",
        Implementation = "flowzer.ai.v1",
        FlowzerAiTask = new AiTaskDefinition(
            1,
            ConnectionId,
            null,
            1,
            "Classify the request.",
            "{\"type\":\"object\"}",
            4096,
            1024,
            60)
    };

    private static ServiceTask ToolTask(AiToolApprovalMode approval) => BuildTask() with
    {
        FlowzerAiTask = BuildTask().FlowzerAiTask! with
        {
            Tools = [new AiTaskToolReference("flowzer.directory.lookup", 1, approval)]
        }
    };

    private static IAiTool Tool(string id, AiToolSideEffect sideEffect, bool allowsPreApproval) =>
        new FakeTool(new AiToolDefinition(
            id,
            1,
            "Directory lookup",
            "Reads a bounded directory entry.",
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            sideEffect,
            allowsPreApproval));

    private sealed class FakeTool(AiToolDefinition definition) : IAiTool
    {
        public AiToolDefinition Definition { get; } = definition;

        public ValueTask<AiToolExecutionResult> ExecuteAsync(
            AiToolExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AiToolExecutionResult(JsonDocument.Parse("{}").RootElement.Clone()));
    }

    private static AiConnection Connection(bool enabled) => new(
        ConnectionId,
        "AI",
        AiProviderKind.OpenAi,
        AiProcessingLocation.Cloud,
        null,
        "model-a",
        "env:FLOWZER_AI_KEY",
        enabled,
        1,
        DateTimeOffset.UtcNow,
        Guid.NewGuid());

    private sealed class ConnectionStorage(params AiConnection[] connections) : IAiConnectionStorage
    {
        public Task<IReadOnlyList<AiConnection>> List() => Task.FromResult<IReadOnlyList<AiConnection>>(connections);

        public Task<AiConnection?> Get(Guid id) =>
            Task.FromResult(connections.SingleOrDefault(connection => connection.Id == id));

        public Task<AiConnectionWriteResult> TryCreate(AiConnection connection) => throw new NotSupportedException();
        public Task<AiConnectionWriteResult> TryUpdate(AiConnection connection, long expectedRevision) => throw new NotSupportedException();
    }

    private sealed class SecretStore(bool exists) : IAiSecretStore
    {
        public int ExistsCalls { get; private set; }
        public int ResolveCalls { get; private set; }

        public ValueTask<bool> ExistsAsync(string reference, CancellationToken cancellationToken = default)
        {
            ExistsCalls++;
            return ValueTask.FromResult(exists);
        }

        public ValueTask<AiSecretValue?> ResolveAsync(string reference, CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            return ValueTask.FromResult<AiSecretValue?>(null);
        }
    }
}
