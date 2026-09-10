using FluentAssertions;
using Microsoft.Extensions.Options;
using Model;
using System.Text.Json;
using StorageSystem;
using WebApiEngine.Ai;
using WebApiEngine.Auth;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Sicherheits- und Revisionsvertrag fuer administrierte KI-Verbindungen.</summary>
public sealed class AiConnectionServiceTest
{
    private static readonly Guid ActorId = Guid.Parse("C1111111-1111-4111-8111-111111111111");

    // Testzweck: Der oeffentliche Vertrag enthaelt nur verwendbare Metadaten und Status,
    // niemals die gespeicherte Secret-Referenz oder gar deren geheimen Wert.
    [Test]
    public async Task Create_ShouldReturnSafeMetadataWithoutSecretReference()
    {
        var context = new TestContext(cloudEnabled: true);
        context.Secrets.Available.Add("env:FLOWZER_AI_OPENAI");

        var created = await context.Service.CreateAsync(new CreateAiConnectionRequestDto
        {
            Name = "  OpenAI Produktion  ",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example",
            SecretReference = "env:FLOWZER_AI_OPENAI"
        });

        created.Name.Should().Be("OpenAI Produktion");
        created.Revision.Should().Be(1);
        created.Enabled.Should().BeTrue();
        created.Ready.Should().BeTrue();
        typeof(AiConnectionDto).GetProperty("SecretReference").Should().BeNull();
        context.Storage.Items[created.Id].SecretReference.Should().Be("env:FLOWZER_AI_OPENAI");
        context.Storage.Items[created.Id].UpdatedByUserId.Should().Be(ActorId);
    }

    // Testzweck: Ein Administrator kann nur konkrete registrierte Werkzeugversionen fuer eine
    // Verbindung erlauben; dieselbe nicht geheime Allowlist wird sicher an Modellierung und API ausgegeben.
    [Test]
    public async Task Create_ShouldPersistValidatedToolPermissions()
    {
        var context = new TestContext(
            cloudEnabled: true,
            tools: [Tool("flowzer.directory.lookup", allowsPreApproval: true)]);

        var created = await context.Service.CreateAsync(new CreateAiConnectionRequestDto
        {
            Name = "OpenAI",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example",
            SecretReference = "env:FLOWZER_AI_OPENAI",
            AllowedTools =
            [
                new AiToolPermissionDto
                {
                    ToolId = "flowzer.directory.lookup",
                    ToolVersion = 1,
                    AllowPreApproval = true
                }
            ]
        });

        created.AllowedTools.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new AiToolPermissionDto
            {
                ToolId = "flowzer.directory.lookup",
                ToolVersion = 1,
                AllowPreApproval = true
            });
        context.Storage.Items[created.Id].AllowedTools.Should().ContainSingle()
            .Which.Should().Be(new AiToolPermission("flowzer.directory.lookup", 1, true));
    }

    // Testzweck: Ein manipulierter Browser kann weder unbekannte Werkzeuge erlauben noch die
    // serverseitige Vorabfreigabegrenze einer registrierten Implementierung erweitern.
    [TestCase("flowzer.unknown", false)]
    [TestCase("flowzer.directory.lookup", true)]
    public async Task Create_ShouldRejectUnknownOrOverprivilegedToolPermission(
        string toolId,
        bool allowPreApproval)
    {
        var context = new TestContext(
            cloudEnabled: true,
            tools: [Tool("flowzer.directory.lookup", allowsPreApproval: false)]);
        var request = new CreateAiConnectionRequestDto
        {
            Name = "OpenAI",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example",
            SecretReference = "env:FLOWZER_AI_OPENAI",
            AllowedTools =
            [
                new AiToolPermissionDto
                {
                    ToolId = toolId,
                    ToolVersion = 1,
                    AllowPreApproval = allowPreApproval
                }
            ]
        };

        var action = () => context.Service.CreateAsync(request);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*tool*");
        context.Storage.Items.Should().BeEmpty();
    }

    // Testzweck: Cloud-Verarbeitung ist installationsweit opt-in und darf nicht allein durch
    // eine vom Browser gewaehlte Providerangabe eingeschaltet werden.
    [Test]
    public async Task Create_ShouldRejectCloudConnectionWhenInstallationDidNotEnableCloud()
    {
        var context = new TestContext(cloudEnabled: false);

        var action = () => context.Service.CreateAsync(new CreateAiConnectionRequestDto
        {
            Name = "OpenAI",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example",
            SecretReference = "env:FLOWZER_AI_OPENAI"
        });

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*Cloud*");
        context.Storage.Items.Should().BeEmpty();
    }

    // Testzweck: Lokale Ziele sind ein getrenntes Installations-Opt-in; ohne diese Freigabe
    // kann ein Administrator keinen Zugriff auf interne Adressen konfigurieren.
    [Test]
    public async Task Create_ShouldRejectLocalEndpointWithoutInstallationApproval()
    {
        var context = new TestContext(localEnabled: false);

        var action = () => context.Service.CreateAsync(new CreateAiConnectionRequestDto
        {
            Name = "Lokales Modell",
            Provider = AiProviderKindDto.OpenAiCompatible,
            Location = AiProcessingLocationDto.Local,
            BaseAddress = "http://127.0.0.1:11434/v1",
            DefaultModel = "local-example",
            SecretReference = "env:FLOWZER_AI_LOCAL"
        });

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*Local*");
        context.Storage.Items.Should().BeEmpty();
    }

    // Testzweck: Cloud-Ziele muessen HTTPS verwenden und duerfen weder lokale Hosts noch
    // eingebettete Zugangsdaten, Queryparameter oder Fragmente enthalten.
    [TestCase("http://models.example.test/v1")]
    [TestCase("https://localhost:11434/v1")]
    [TestCase("https://10.1.2.3/v1")]
    [TestCase("https://[::ffff:127.0.0.1]/v1")]
    // Testzweck: Auch IPv6-lokale sowie Zugangsdaten/Query/Fragmente duerfen die
    // Cloud-Zielgrenze nicht umgehen; alle Faelle verwenden denselben Vertragstest.
    [TestCase("https://[fd00::1]/v1")]
    // Testzweck: Eingebettete Zugangsdaten duerfen nicht Teil eines Providerziels sein.
    [TestCase("https://user:password@models.example.test/v1")]
    // Testzweck: Queryparameter duerfen keine Zugangsdaten am Verbindungsvertrag vorbeischleusen.
    [TestCase("https://models.example.test/v1?token=secret")]
    // Testzweck: Fragmente sind kein Bestandteil der an den Provider gesendeten Zieladresse.
    [TestCase("https://models.example.test/v1#fragment")]
    public async Task Create_ShouldRejectUnsafeCloudEndpoint(string baseAddress)
    {
        var context = new TestContext(cloudEnabled: true);

        var action = () => context.Service.CreateAsync(new CreateAiConnectionRequestDto
        {
            Name = "Kompatibel",
            Provider = AiProviderKindDto.OpenAiCompatible,
            Location = AiProcessingLocationDto.Cloud,
            BaseAddress = baseAddress,
            DefaultModel = "model-example",
            SecretReference = "env:FLOWZER_AI_COMPAT"
        });

        await action.Should().ThrowAsync<ArgumentException>();
        context.Storage.Items.Should().BeEmpty();
    }

    // Testzweck: Secret-Referenzen koennen nicht zum Auslesen beliebiger Prozessvariablen
    // missbraucht werden und muessen im installationsweit begrenzten Namensraum liegen.
    [TestCase("env:PATH")]
    [TestCase("env:FLOWZER_AI_BAD-NAME")]
    [TestCase("file:/tmp/secret")]
    [TestCase("FLOWZER_AI_OPENAI")]
    public async Task Create_ShouldRejectSecretReferenceOutsideConfiguredNamespace(string reference)
    {
        var context = new TestContext(cloudEnabled: true);

        var action = () => context.Service.CreateAsync(new CreateAiConnectionRequestDto
        {
            Name = "OpenAI",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example",
            SecretReference = reference
        });

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*SecretReference*");
    }

    // Testzweck: Gleichzeitige Administration ist revisionsgeschuetzt; ein veralteter Browser
    // ueberschreibt weder Ziel noch Secret-Referenz einer neueren Aenderung.
    [Test]
    public async Task Update_ShouldRejectStaleRevisionWithoutChangingConnection()
    {
        var context = new TestContext(cloudEnabled: true);
        var created = await context.CreateOpenAi();
        var first = await context.Service.UpdateAsync(created.Id, new UpdateAiConnectionRequestDto
        {
            ExpectedRevision = 1,
            Name = "Neue Bezeichnung",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example-2"
        });

        var stale = () => context.Service.UpdateAsync(created.Id, new UpdateAiConnectionRequestDto
        {
            ExpectedRevision = 1,
            Name = "Veraltete Bezeichnung",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example-3",
            SecretReference = "env:FLOWZER_AI_REPLACEMENT"
        });

        await stale.Should().ThrowAsync<AiConnectionConflictException>()
            .Where(exception => exception.ExpectedRevision == 1 && exception.CurrentRevision == 2);
        context.Storage.Items[created.Id].Name.Should().Be(first.Name);
        context.Storage.Items[created.Id].SecretReference.Should().Be("env:FLOWZER_AI_OPENAI");
    }

    // Testzweck: Eine manipulierte maximale Revision wird als ungueltige Eingabe
    // abgewiesen und darf nicht durch einen Integer-Ueberlauf zum Serverfehler werden.
    [Test]
    public async Task Update_ShouldRejectRevisionThatCannotBeIncremented()
    {
        var context = new TestContext(cloudEnabled: true);
        var created = await context.CreateOpenAi();

        var action = () => context.Service.UpdateAsync(created.Id, new UpdateAiConnectionRequestDto
        {
            ExpectedRevision = long.MaxValue,
            Name = "OpenAI",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example"
        });

        await action.Should().ThrowAsync<ArgumentException>();
        context.Storage.Items[created.Id].Revision.Should().Be(1);
    }

    // Testzweck: Deaktivieren ist eine revisionierte Zustandsaenderung statt Loeschung, damit
    // historische Workflowversionen ihre stabile Verbindung weiterhin erklaeren koennen.
    [Test]
    public async Task Disable_ShouldKeepHistoricalMetadataButRemoveReadiness()
    {
        var context = new TestContext(cloudEnabled: true);
        context.Secrets.Available.Add("env:FLOWZER_AI_OPENAI");
        var created = await context.CreateOpenAi();

        var disabled = await context.Service.SetEnabledAsync(created.Id, new SetAiConnectionEnabledRequestDto
        {
            ExpectedRevision = 1,
            Enabled = false
        });

        disabled.Enabled.Should().BeFalse();
        disabled.Ready.Should().BeFalse();
        disabled.Revision.Should().Be(2);
        (await context.Service.GetAsync(created.Id, includeDisabled: true)).Id.Should().Be(created.Id);
        var hidden = () => context.Service.GetAsync(created.Id, includeDisabled: false);
        await hidden.Should().ThrowAsync<AiConnectionNotFoundException>();
    }

    // Testzweck: Ein sicher konfigurierter Eintrag ohne aktuell aufloesbares Secret bleibt
    // sichtbar, wird aber nicht als ausfuehrungsbereit angeboten.
    [Test]
    public async Task List_ShouldExposeMissingSecretAsNotReady()
    {
        var context = new TestContext(cloudEnabled: true);
        await context.CreateOpenAi();

        var item = (await context.Service.ListAsync(includeDisabled: false)).Single();

        item.Enabled.Should().BeTrue();
        item.Ready.Should().BeFalse();
    }

    // Testzweck: Die reine Verwendungsrolle kann deaktivierte Verbindungen nicht mehr
    // auflisten; historische Metadaten bleiben ausschließlich der Verwaltung zugänglich.
    [Test]
    public async Task List_ShouldHideDisabledConnectionsFromUseProjection()
    {
        var context = new TestContext(cloudEnabled: true);
        var created = await context.CreateOpenAi();
        await context.Service.SetEnabledAsync(created.Id, new SetAiConnectionEnabledRequestDto
        {
            ExpectedRevision = created.Revision,
            Enabled = false
        });

        (await context.Service.ListAsync(includeDisabled: false)).Should().BeEmpty();
        (await context.Service.ListAsync(includeDisabled: true)).Should().ContainSingle();
    }

    private sealed class TestContext
    {
        public TestContext(
            bool cloudEnabled = false,
            bool localEnabled = true,
            IReadOnlyList<IAiTool>? tools = null)
        {
            Storage = new InMemoryAiConnectionStorage();
            Secrets = new FakeSecretStore();
            var provider = new SingleStorageProvider(Storage);
            Service = new AiConnectionService(
                provider,
                new FixedCurrentUserContextAccessor(),
                TimeProvider,
                Options.Create(new FlowzerAiOptions
                {
                    AllowCloudProviders = cloudEnabled,
                    AllowLocalEndpoints = localEnabled,
                    SecretEnvironmentVariablePrefix = "FLOWZER_AI_"
                }),
                Secrets,
                new AiToolRegistry(tools ?? []));
        }

        public static TimeProvider TimeProvider { get; } = new FixedTimeProvider(
            new DateTimeOffset(2026, 9, 9, 15, 0, 0, TimeSpan.Zero));
        public InMemoryAiConnectionStorage Storage { get; }
        public FakeSecretStore Secrets { get; }
        public AiConnectionService Service { get; }

        public Task<AiConnectionDto> CreateOpenAi() => Service.CreateAsync(new CreateAiConnectionRequestDto
        {
            Name = "OpenAI",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example",
            SecretReference = "env:FLOWZER_AI_OPENAI"
        });
    }

    private sealed class InMemoryAiConnectionStorage : IAiConnectionStorage
    {
        public Dictionary<Guid, AiConnection> Items { get; } = [];

        public Task<IReadOnlyList<AiConnection>> List() =>
            Task.FromResult<IReadOnlyList<AiConnection>>(Items.Values.OrderBy(item => item.Name).ToArray());

        public Task<AiConnection?> Get(Guid id) => Task.FromResult(Items.GetValueOrDefault(id));

        public Task<AiConnectionWriteResult> TryCreate(AiConnection connection)
        {
            if (Items.ContainsKey(connection.Id)
                || Items.Values.Any(item => string.Equals(item.Name, connection.Name, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(new AiConnectionWriteResult(AiConnectionWriteStatus.Conflict, null, 0));
            Items.Add(connection.Id, connection);
            return Task.FromResult(new AiConnectionWriteResult(AiConnectionWriteStatus.Written, connection, connection.Revision));
        }

        public Task<AiConnectionWriteResult> TryUpdate(AiConnection connection, long expectedRevision)
        {
            if (!Items.TryGetValue(connection.Id, out var current))
                return Task.FromResult(new AiConnectionWriteResult(AiConnectionWriteStatus.NotFound, null, 0));
            if (current.Revision != expectedRevision)
                return Task.FromResult(new AiConnectionWriteResult(AiConnectionWriteStatus.Conflict, current, current.Revision));
            Items[connection.Id] = connection;
            return Task.FromResult(new AiConnectionWriteResult(AiConnectionWriteStatus.Written, connection, connection.Revision));
        }
    }

    private sealed class FakeSecretStore : IAiSecretStore
    {
        public HashSet<string> Available { get; } = new(StringComparer.Ordinal);
        public ValueTask<bool> ExistsAsync(string reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Available.Contains(reference));

        public ValueTask<AiSecretValue?> ResolveAsync(
            string reference,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AiSecretValue?>(null);
    }

    private sealed class FixedCurrentUserContextAccessor : ICurrentUserContextAccessor
    {
        public CurrentUserContext GetCurrentUser() => new(ActorId, "ai-test", false);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static IAiTool Tool(string id, bool allowsPreApproval) => new FakeTool(new AiToolDefinition(
        id,
        1,
        "Directory lookup",
        "Reads one bounded directory record.",
        "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
        "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
        AiToolSideEffect.ReadOnly,
        allowsPreApproval));

    private sealed class FakeTool(AiToolDefinition definition) : IAiTool
    {
        public AiToolDefinition Definition { get; } = definition;

        public ValueTask<AiToolExecutionResult> ExecuteAsync(
            AiToolExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AiToolExecutionResult(JsonDocument.Parse("{}").RootElement.Clone()));
    }

    private sealed class SingleStorageProvider(IAiConnectionStorage connections) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() => new Wrapper(connections);

        private sealed class Wrapper(IAiConnectionStorage connections) : ITransactionalStorage
        {
            public IAiConnectionStorage AiConnectionStorage { get; } = connections;
            public IDefinitionStorage DefinitionStorage => throw new NotSupportedException();
            public IFolderStorage FolderStorage => throw new NotSupportedException();
            public IMessageSubscriptionStorage SubscriptionStorage => throw new NotSupportedException();
            public IInstanceStorage InstanceStorage => throw new NotSupportedException();
            public IFormStorage FormStorage => throw new NotSupportedException();
            public IServiceTaskStorage ServiceTaskStorage => throw new NotSupportedException();
            public void CommitChanges() { }
            public void RollbackTransaction() { }
            public void Dispose() { }
        }
    }
}
