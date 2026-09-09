using BPMN.Activities;
using BPMN.Flowzer;
using core_engine.Exceptions;
using FluentAssertions;
using Model;
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
