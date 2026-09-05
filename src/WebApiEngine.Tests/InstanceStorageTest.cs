using BPMN.Process;
using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

[NonParallelizable]
public class InstanceStorageTest
{
    // Testzweck: Eine unbekannte Instanz-Id ist ein erwartbarer Nicht-Treffer (404 am API-Rand),
    // kein InvalidOperationException-500 aus einem harten Single().
    [Test]
    public async Task GetProcessInstance_ShouldThrowFileNotFound_WhenInstanceDoesNotExist()
    {
        using var context = new StorageContext();

        var action = async () => await context.Storage.InstanceStorage.GetProcessInstance(Guid.NewGuid());

        await action.Should().ThrowAsync<FileNotFoundException>();
    }

    // Testzweck: Wiederholtes Speichern derselben Instanz ersetzt die Datei atomar: genau eine
    // Datei, keine liegen gebliebene Temporaerdatei, der zuletzt geschriebene Zustand ist lesbar.
    [Test]
    public async Task AddOrUpdateInstance_ShouldReplaceFileAtomically_WithoutLeavingTemporaryFiles()
    {
        using var context = new StorageContext();
        var instance = CreateInstance();
        await context.Storage.InstanceStorage.AddOrUpdateInstance(instance);
        instance.IsFinished = true;
        instance.State = ProcessInstanceState.Completed;

        await context.Storage.InstanceStorage.AddOrUpdateInstance(instance);

        var instancesPath = context.Storage.GetBasePath("FileStorage/Instances");
        Directory.GetFiles(instancesPath).Should().ContainSingle().Which.Should().EndWith(".json");
        Directory.GetFiles(instancesPath, "*.tmp-*").Should().BeEmpty();
        var stored = await context.Storage.InstanceStorage.GetProcessInstance(instance.InstanceId);
        stored.State.Should().Be(ProcessInstanceState.Completed);
        stored.IsFinished.Should().BeTrue();
    }

    private static ProcessInstanceInfo CreateInstance()
    {
        var instanceId = Guid.NewGuid();
        var process = new Process { Id = "Process_1", Name = "P", DefinitionsId = "Definitions_1", IsExecutable = true, FlowElements = [] };
        return new ProcessInstanceInfo
        {
            InstanceId = instanceId,
            metaDefinitionId = "meta-1",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_1",
            Tokens = [new Token { ProcessInstanceId = instanceId, CurrentBaseElement = process, ActiveBoundaryEvents = [], State = FlowNodeState.Active }],
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
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-instance-storage-test", Guid.NewGuid().ToString("N"));
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
