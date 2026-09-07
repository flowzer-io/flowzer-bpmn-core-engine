using BPMN.Common;
using BPMN.Process;
using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>
/// Der letzte Riegel vor dem Dateinamen. Die API prueft die Kennung bereits an jedem Eingang;
/// ein Katalog aus aelteren Zeiten kann aber eine ungeprueft uebernommene Kennung enthalten,
/// und die traegt der Instanz- und Subscription-Dateiname weiter.
/// </summary>
[NonParallelizable]
public class FilesystemStorageDefinitionIdGuardTest
{
    // Testzweck: Prüft, dass eine Instanz mit Pfadanteilen in der Katalog-Kennung nicht
    // geschrieben wird — der Dateiname zeigte sonst aus dem Instanzordner heraus.
    [Test]
    public async Task AddOrUpdateInstance_ShouldRejectMetaDefinitionIdWithPathCharacters()
    {
        using var context = new StorageContext();
        var instance = CreateInstance("../x");

        var act = async () => await context.Storage.InstanceStorage.AddOrUpdateInstance(instance);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*is not a valid definition id*");
        Directory.GetFiles(context.Storage.GetBasePath("FileStorage/Instances")).Should().BeEmpty();
    }

    // Testzweck: Prüft dasselbe für Nachrichten-Anmeldungen.
    [Test]
    public async Task AddMessageSubscription_ShouldRejectRelatedDefinitionIdWithPathCharacters()
    {
        using var context = new StorageContext();
        var subscription = new MessageSubscription(
            new MessageDefinition { Name = "Antrag" },
            "Process_1",
            "../x",
            Guid.NewGuid(),
            Guid.NewGuid());

        var act = async () => await context.Storage.SubscriptionStorage.AddMessageSubscription(subscription);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*is not a valid definition id*");
        Directory.GetFiles(context.Storage.GetBasePath("FileStorage/MessageSubscriptions")).Should().BeEmpty();
    }

    // Testzweck: Prüft dasselbe für Signal-Anmeldungen.
    [Test]
    public void AddSignalSubscription_ShouldRejectRelatedDefinitionIdWithPathCharacters()
    {
        using var context = new StorageContext();
        var subscription = new SignalSubscription(
            "Signal_1",
            "Process_1",
            "../x",
            Guid.NewGuid(),
            Guid.NewGuid());

        var act = () => context.Storage.SubscriptionStorage.AddSignalSubscription(subscription);

        act.Should().Throw<ArgumentException>().WithMessage("*is not a valid definition id*");
        Directory.GetFiles(context.Storage.GetBasePath("FileStorage/MessageSubscriptions")).Should().BeEmpty();
    }

    // Testzweck: Prüft, dass eine übliche Kennung den Riegel unverändert passiert.
    [Test]
    public async Task AddOrUpdateInstance_ShouldStoreInstance_ForEstablishedMetaDefinitionId()
    {
        using var context = new StorageContext();
        var instance = CreateInstance("flowzer-urlaubsantrag");

        await context.Storage.InstanceStorage.AddOrUpdateInstance(instance);

        Directory.GetFiles(context.Storage.GetBasePath("FileStorage/Instances")).Should().ContainSingle();
    }

    private static ProcessInstanceInfo CreateInstance(string metaDefinitionId)
    {
        var instanceId = Guid.NewGuid();
        var process = new Process
        {
            Id = "Process_1",
            Name = "P",
            DefinitionsId = "Definitions_1",
            IsExecutable = true,
            FlowElements = []
        };

        return new ProcessInstanceInfo
        {
            InstanceId = instanceId,
            metaDefinitionId = metaDefinitionId,
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_1",
            Tokens =
            [
                new Token
                {
                    ProcessInstanceId = instanceId,
                    CurrentBaseElement = process,
                    ActiveBoundaryEvents = [],
                    State = FlowNodeState.Active
                }
            ],
            IsFinished = false,
            State = ProcessInstanceState.Waiting,
            MessageSubscriptionCount = 0,
            SignalSubscriptionCount = 0,
            UserTaskSubscriptionCount = 0,
            ServiceSubscriptionCount = 0
        };
    }

    private sealed class StorageContext : IDisposable
    {
        private readonly string? _originalRoot;
        private readonly string _tempRoot;

        public StorageContext()
        {
            _originalRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-definition-id-guard-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _tempRoot);
            Storage = new Storage();
        }

        public Storage Storage { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _originalRoot);
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
    }
}
