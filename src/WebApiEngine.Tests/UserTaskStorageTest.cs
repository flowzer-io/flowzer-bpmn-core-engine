using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using BPMN.Common;
using BPMN.HumanInteraction;
using BPMN.Process;

namespace WebApiEngine.Tests;

/// <summary>
/// Einzelzugriff auf eine User-Task-Subscription. Der Formular-Endpunkt hat bisher alle
/// Aufgaben aller Personen geladen und deserialisiert, um genau eine davon zu behalten.
/// </summary>
[NonParallelizable]
public class UserTaskStorageTest
{
    // Testzweck: Der Einzelzugriff liefert dieselben angereicherten Werte wie die Liste.
    [Test]
    public async Task GetUserTaskExtended_ShouldReturnTheSameEnrichedValuesAsTheList()
    {
        using var context = new UserTaskStorageTestContext();
        var wanted = await context.AddUserTask("Freigabe");
        await context.AddUserTask("Andere Aufgabe");

        var single = await context.SubscriptionStorage.GetUserTaskExtended(wanted.Id);
        var fromList = (await context.SubscriptionStorage.GetAllUserTasksExtended(Guid.NewGuid()))
            .Single(candidate => candidate.Id == wanted.Id);

        single.Should().NotBeNull();
        single!.Id.Should().Be(wanted.Id);
        single.Name.Should().Be("Freigabe");
        single.DefinitionMetaName.Should().Be(fromList.DefinitionMetaName);
        single.DefinitionVersion.Should().Be(fromList.DefinitionVersion);
    }

    // Testzweck: Eine unbekannte Id ist kein Fehler, sondern schlicht kein Treffer.
    [Test]
    public async Task GetUserTaskExtended_ShouldReturnNull_WhenTheTaskDoesNotExist()
    {
        using var context = new UserTaskStorageTestContext();
        await context.AddUserTask("Freigabe");

        var missing = await context.SubscriptionStorage.GetUserTaskExtended(Guid.NewGuid());

        missing.Should().BeNull();
    }

    private sealed class UserTaskStorageTestContext : IDisposable
    {
        private readonly string? _previousStorageRoot;
        private readonly string _storageRoot;
        private readonly Guid _definitionId = Guid.NewGuid();
        private const string MetaDefinitionId = "catalog-usertask";

        public UserTaskStorageTestContext()
        {
            _previousStorageRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _storageRoot = Path.Combine(Path.GetTempPath(), "flowzer-usertask-storage-test", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _storageRoot);

            Storage = new Storage();
            SubscriptionStorage = Storage.SubscriptionStorage;
        }

        public Storage Storage { get; }
        public StorageSystem.IMessageSubscriptionStorage SubscriptionStorage { get; }

        public async Task<UserTaskSubscription> AddUserTask(string name)
        {
            await EnsureDefinition();

            var userTask = new UserTask { Id = "UserTask_1", Name = name, Implementation = "Formular" };
            var process = new Process { Id = "Process_1", Name = "P", DefinitionsId = "D", IsExecutable = true, FlowElements = [userTask] };
            var subscription = new UserTaskSubscription
            {
                Id = Guid.NewGuid(),
                Name = name,
                Token = new Token
                {
                    ProcessInstanceId = Guid.NewGuid(),
                    CurrentBaseElement = userTask,
                    ActiveBoundaryEvents = [],
                    State = FlowNodeState.Active
                },
                MetaDefinitionId = MetaDefinitionId,
                DefinitionId = _definitionId,
                ProcessId = process.Id
            };

            await SubscriptionStorage.AddUserTaskSubscription(subscription);
            return subscription;
        }

        private async Task EnsureDefinition()
        {
            if ((await Storage.DefinitionStorage.GetAllDefinitions()).Any(definition => definition.Id == _definitionId))
            {
                return;
            }

            await Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = MetaDefinitionId,
                Name = "Freigabeprozess"
            });
            await Storage.DefinitionStorage.StoreDefinition(new BpmnDefinition
            {
                Id = _definitionId,
                DefinitionId = MetaDefinitionId,
                Hash = "hash",
                SavedByUser = Guid.NewGuid(),
                SavedOn = DateTime.UtcNow,
                Version = new Model.Version(2, 1),
                IsActive = true
            });
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousStorageRoot);

            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
    }
}
