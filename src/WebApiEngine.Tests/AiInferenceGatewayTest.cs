using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Model;
using StorageSystem;
using WebApiEngine.Ai;

namespace WebApiEngine.Tests;

/// <summary>Orchestrierungsvertrag zwischen persistierter Verbindung, Secret und Provider.</summary>
public sealed class AiInferenceGatewayTest
{
    private const string Schema = """
        {"type":"object","properties":{"category":{"type":"string"}},"required":["category"],"additionalProperties":false}
        """;

    // Testzweck: Nur der zur persistierten Providerfamilie registrierte Adapter erhaelt den
    // gebundenen Auftrag; Modelloverride, Limits und strukturierte Antwort bleiben erhalten.
    [Test]
    public async Task Execute_ShouldUseExactConfiguredProviderAndValidateResult()
    {
        var openAi = new FakeAdapter(AiProviderKind.OpenAi);
        var anthropic = new FakeAdapter(AiProviderKind.Anthropic)
        {
            Result = new AiProviderResult(
                "{\"category\":\"support\"}",
                "claude-result-model",
                new AiTokenUsage(40, 8, 48))
        };
        var secretStore = new TrackingSecretStore("resolved-secret");
        var gateway = Gateway(Connection(AiProviderKind.Anthropic), secretStore, openAi, anthropic);

        var result = await gateway.ExecuteAsync(Command(model: "task-model"), default);

        openAi.Calls.Should().Be(0);
        anthropic.Calls.Should().Be(1);
        anthropic.LastRequest!.Model.Should().Be("task-model");
        anthropic.LastRequest.MaxInputTokens.Should().Be(4_096);
        anthropic.LastSecret.Should().Be("resolved-secret");
        result.Output.GetProperty("category").GetString().Should().Be("support");
        result.Model.Should().Be("claude-result-model");
        result.Usage.Should().Be(new AiTokenUsage(40, 8, 48));
        var secretAccess = () => secretStore.Resolved!.Value;
        secretAccess.Should().Throw<ObjectDisposedException>();
    }

    // Testzweck: Fehlt der exakte Adapter, gibt es keinen stillen Wechsel auf einen anderen
    // Provider oder eine Cloudverbindung.
    [Test]
    public async Task Execute_ShouldRejectMissingProviderWithoutFallback()
    {
        var openAi = new FakeAdapter(AiProviderKind.OpenAi);
        var gateway = Gateway(Connection(AiProviderKind.Anthropic), new TrackingSecretStore("secret"), openAi);

        var action = () => gateway.ExecuteAsync(Command(), default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.provider.unsupported");
        exception.Retryable.Should().BeFalse();
        openAi.Calls.Should().Be(0);
    }

    // Testzweck: Eine registrierte Providerfamilie reicht nicht aus; der konkrete Adapter
    // muss den vom Task benoetigten strukturierten Ausgabevertrag ausdruecklich zusagen.
    [Test]
    public async Task Execute_ShouldRejectAdapterWithoutStructuredOutputCapability()
    {
        var adapter = new FakeAdapter(AiProviderKind.OpenAi)
        {
            Capabilities = AiProviderCapability.None
        };
        var gateway = Gateway(Connection(AiProviderKind.OpenAi), new TrackingSecretStore("secret"), adapter);

        var action = () => gateway.ExecuteAsync(Command(), default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.provider.capability_unsupported");
        adapter.Calls.Should().Be(0);
    }

    // Testzweck: Ein inzwischen fehlendes Secret stoppt vor jedem externen Aufruf und sein
    // interner Referenzname wird nicht in die Fehlermeldung uebernommen.
    [Test]
    public async Task Execute_ShouldRejectMissingSecretBeforeProviderCall()
    {
        var adapter = new FakeAdapter(AiProviderKind.OpenAi);
        var gateway = Gateway(Connection(AiProviderKind.OpenAi), new TrackingSecretStore(null), adapter);

        var action = () => gateway.ExecuteAsync(Command(), default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.connection.secret_missing");
        exception.Message.Should().NotContain("FLOWZER_AI_TEST");
        adapter.Calls.Should().Be(0);
    }

    // Testzweck: Providerantworten werden trotz behauptetem strukturiertem Modus erneut lokal
    // geprueft; ein Pflichtfeldfehler gelangt nicht als Prozessausgabe weiter.
    [Test]
    public async Task Execute_ShouldRejectProviderOutputOutsideSchema()
    {
        var adapter = new FakeAdapter(AiProviderKind.OpenAi)
        {
            Result = new AiProviderResult("{}", "result-model", new AiTokenUsage(4, 2, 6))
        };
        var gateway = Gateway(Connection(AiProviderKind.OpenAi), new TrackingSecretStore("secret"), adapter);

        var action = () => gateway.ExecuteAsync(Command(), default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.result.required");
        exception.Retryable.Should().BeFalse();
        exception.Message.Should().NotContain("category");
    }

    // Testzweck: Tatsaechlich gemeldete Token duerfen die gespeicherten Aufgabenlimits nicht
    // unbemerkt ueberschreiten, auch wenn der Provider die Vorgabe ignoriert hat.
    [TestCase(4_097, 10, "ai.provider.input_budget_exceeded")]
    [TestCase(100, 513, "ai.provider.output_budget_exceeded")]
    public async Task Execute_ShouldRejectReportedUsageAboveTaskLimit(
        int inputTokens,
        int outputTokens,
        string expectedCode)
    {
        var adapter = new FakeAdapter(AiProviderKind.OpenAi)
        {
            Result = new AiProviderResult(
                "{\"category\":\"support\"}",
                "result-model",
                new AiTokenUsage(inputTokens, outputTokens, inputTokens + outputTokens))
        };
        var gateway = Gateway(Connection(AiProviderKind.OpenAi), new TrackingSecretStore("secret"), adapter);

        var action = () => gateway.ExecuteAsync(Command(), default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be(expectedCode);
        exception.Retryable.Should().BeFalse();
    }

    // Testzweck: Ungueltige Auftragsgrenzen und nicht objektfoermige Eingaben werden geprueft,
    // bevor Verbindung, Secret oder Netzwerk beruehrt werden.
    [Test]
    public async Task Execute_ShouldRejectInvalidCommandBeforeResolvingSecret()
    {
        var secrets = new TrackingSecretStore("secret");
        var adapter = new FakeAdapter(AiProviderKind.OpenAi);
        var gateway = Gateway(Connection(AiProviderKind.OpenAi), secrets, adapter);
        var invalid = Command() with { Inputs = Json("[1,2,3]") };

        var action = () => gateway.ExecuteAsync(invalid, default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.request.invalid");
        secrets.ResolveCalls.Should().Be(0);
        adapter.Calls.Should().Be(0);
    }

    // Testzweck: Persistierte Legacy- oder manipulierte Ziele werden bei jeder Ausfuehrung
    // erneut gegen die Installationsgrenze geprueft, bevor Secret oder Netzwerk beruehrt wird.
    [Test]
    public async Task Execute_ShouldRejectPersistedUnsafeTarget()
    {
        var connection = Connection(AiProviderKind.OpenAiCompatible) with
        {
            Location = AiProcessingLocation.Cloud,
            BaseAddress = "http://127.0.0.1:11434/v1"
        };
        var secrets = new TrackingSecretStore("secret");
        var adapter = new FakeAdapter(AiProviderKind.OpenAiCompatible);
        var gateway = Gateway(connection, secrets, adapter);

        var action = () => gateway.ExecuteAsync(Command(), default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.connection.invalid");
        secrets.ResolveCalls.Should().Be(0);
        adapter.Calls.Should().Be(0);
    }

    // Testzweck: Ein Lauf bleibt an die beim Erstellen gespeicherte Verbindungsrevision
    // gebunden; wenn die Ablage diese historische Revision nicht kennt, stoppt er vor
    // Secret-Aufloesung und Providerzugriff.
    [Test]
    public async Task Execute_ShouldRejectChangedConnectionRevisionBeforeSecretOrProvider()
    {
        var secrets = new TrackingSecretStore("secret");
        var adapter = new FakeAdapter(AiProviderKind.OpenAi);
        var gateway = Gateway(Connection(AiProviderKind.OpenAi) with { Revision = 2 }, secrets, adapter);

        var action = () => gateway.ExecuteAsync(Command() with { ConnectionRevision = 1 }, default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.connection.revision_changed");
        exception.Retryable.Should().BeFalse();
        secrets.ResolveCalls.Should().Be(0);
        adapter.Calls.Should().Be(0);
    }

    // Testzweck: Bewahrt die Ablage eine gebundene historische Revision auf, verwendet der
    // Lauf deren Modell- und Secret-Konfiguration statt der inzwischen aktuellen Fassung.
    [Test]
    public async Task Execute_ShouldUseBoundHistoricalConnectionRevision()
    {
        var initial = Connection(AiProviderKind.OpenAi);
        var current = initial with
        {
            Revision = 2,
            DefaultModel = "new-model",
            SecretReference = "env:FLOWZER_AI_NEW"
        };
        var secrets = new TrackingSecretStore("historic-secret");
        var adapter = new FakeAdapter(AiProviderKind.OpenAi);
        var gateway = new AiInferenceGateway(
            new VersionedConnectionStorage(current, initial),
            secrets,
            new AiProviderRegistry([adapter]),
            Options.Create(new FlowzerAiOptions { AllowCloudProviders = true }));

        await gateway.ExecuteAsync(Command(), default);

        adapter.LastRequest!.Connection.Revision.Should().Be(1);
        adapter.LastRequest.Model.Should().Be("connection-model");
        adapter.LastSecret.Should().Be("historic-secret");
    }

    // Testzweck: Der unveränderliche historische Snapshot bewahrt die Ausführungskonfiguration,
    // umgeht aber nicht den aktuellen administrativen Deaktivierungsschalter der Verbindung.
    [Test]
    public async Task Execute_ShouldRejectDisabledCurrentConnectionBeforeUsingHistoricalRevision()
    {
        var initial = Connection(AiProviderKind.OpenAi);
        var current = initial with { Revision = 2, Enabled = false };
        var secrets = new TrackingSecretStore("historic-secret");
        var adapter = new FakeAdapter(AiProviderKind.OpenAi);
        var gateway = new AiInferenceGateway(
            new VersionedConnectionStorage(current, initial),
            secrets,
            new AiProviderRegistry([adapter]),
            Options.Create(new FlowzerAiOptions { AllowCloudProviders = true }));

        var action = () => gateway.ExecuteAsync(Command(), default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.connection.disabled");
        secrets.ResolveCalls.Should().Be(0);
        adapter.Calls.Should().Be(0);
    }

    private static AiInferenceGateway Gateway(
        AiConnection connection,
        IAiSecretStore secretStore,
        params IAiProviderAdapter[] adapters) => new(
        new SingleConnectionStorage(connection),
        secretStore,
        new AiProviderRegistry(adapters),
        Options.Create(new FlowzerAiOptions
        {
            AllowCloudProviders = true,
            AllowLocalEndpoints = true
        }));

    private static AiInferenceCommand Command(string? model = null) => new(
        Guid.Parse("A1111111-1111-4111-8111-111111111111"),
        1,
        model,
        3,
        "Classify the workflow input.",
        Json("{\"request\":\"Please help\"}"),
        Schema,
        4_096,
        512,
        TimeSpan.FromSeconds(30));

    private static AiConnection Connection(AiProviderKind provider) => new(
        Guid.Parse("A1111111-1111-4111-8111-111111111111"),
        "Provider",
        provider,
        AiProcessingLocation.Cloud,
        provider == AiProviderKind.OpenAiCompatible ? "https://models.example.test/v1" : null,
        "connection-model",
        "env:FLOWZER_AI_TEST",
        true,
        1,
        DateTimeOffset.Parse("2026-09-09T12:00:00Z"),
        Guid.Parse("A2222222-2222-4222-8222-222222222222"));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FakeAdapter(AiProviderKind provider) : IAiProviderAdapter
    {
        public AiProviderKind Provider { get; } = provider;
        public AiProviderCapability Capabilities { get; set; } = AiProviderCapability.StructuredOutput;
        public int Calls { get; private set; }
        public AiProviderRequest? LastRequest { get; private set; }
        public string? LastSecret { get; private set; }
        public AiProviderResult Result { get; set; } = new(
            "{\"category\":\"support\"}",
            "result-model",
            new AiTokenUsage(20, 5, 25));

        public Task<AiProviderResult> ExecuteAsync(
            AiProviderRequest request,
            ReadOnlyMemory<char> secret,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequest = request;
            LastSecret = new string(secret.Span);
            return Task.FromResult(Result);
        }
    }

    private sealed class TrackingSecretStore(string? value) : IAiSecretStore
    {
        public int ResolveCalls { get; private set; }
        public AiSecretValue? Resolved { get; private set; }

        public ValueTask<bool> ExistsAsync(string reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(value is not null);

        public ValueTask<AiSecretValue?> ResolveAsync(
            string reference,
            CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            Resolved = value is null ? null : new AiSecretValue(value);
            return ValueTask.FromResult(Resolved);
        }
    }

    private sealed class SingleConnectionStorage(AiConnection connection) : IAiConnectionStorage
    {
        public Task<IReadOnlyList<AiConnection>> List() =>
            Task.FromResult<IReadOnlyList<AiConnection>>([connection]);

        public Task<AiConnection?> Get(Guid id) =>
            Task.FromResult<AiConnection?>(id == connection.Id ? connection : null);

        public Task<AiConnectionWriteResult> TryCreate(AiConnection created) =>
            throw new NotSupportedException();

        public Task<AiConnectionWriteResult> TryUpdate(AiConnection updated, long expectedRevision) =>
            throw new NotSupportedException();
    }

    private sealed class VersionedConnectionStorage(
        AiConnection current,
        params AiConnection[] history) : IAiConnectionStorage
    {
        public Task<IReadOnlyList<AiConnection>> List() =>
            Task.FromResult<IReadOnlyList<AiConnection>>([current]);

        public Task<AiConnection?> Get(Guid id) =>
            Task.FromResult<AiConnection?>(id == current.Id ? current : null);

        public Task<AiConnection?> Get(Guid id, long revision) =>
            Task.FromResult(history.SingleOrDefault(item => item.Id == id && item.Revision == revision));

        public Task<AiConnectionWriteResult> TryCreate(AiConnection created) => throw new NotSupportedException();
        public Task<AiConnectionWriteResult> TryUpdate(AiConnection updated, long expectedRevision) => throw new NotSupportedException();
    }
}
