using BPMN.HumanInteraction;
using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>
/// Prueft die Engine-Geschaeftslogik gegen die echte dateibasierte Ablage unter Nebenlaeufigkeit.
/// Die Ablage kennt weder Transaktionen noch Sperren; der Web-API-Host ruft die Logik aber aus
/// parallelen HTTP-Requests und dem Timer-Scheduler gleichzeitig auf.
/// </summary>
[NonParallelizable]
public class EngineConcurrencyIntegrationTest
{
    private const int InstanceCount = 16;
    private static readonly Guid UserId = Guid.Parse("2D3B8F8E-1F84-4B7B-9E4E-6B1B1C2D3E4F");

    // Testzweck: Parallele Starts und parallele User-Task-Abschluesse auf verschiedenen Instanzen
    // duerfen sich in der Dateiablage nicht gegenseitig stoeren (Lesen waehrend Loeschen, halb
    // geschriebene JSON-Dateien). Danach sind alle Instanzen abgeschlossen und es bleiben keine
    // offenen User-Task-Subscriptions zurueck. Ohne Serialisierung der Engine-Mutationen scheitert
    // der Lauf sporadisch mit FileNotFound-/JsonReader-Ausnahmen.
    [Test]
    public async Task StartAndCompleteUserTasks_ShouldLeaveConsistentStorage_WhenCalledConcurrently()
    {
        using var context = new FileStorageContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var businessLogic = new BpmnBusinessLogic(provider);
        var definition = await context.StoreDefinitionAsync(provider, CreateUserTaskXml());
        await businessLogic.DeployDefinition(definition);

        // Parallele Leser wie GET /instance, GET /usertask und GET /instance/{id} laufen ohne
        // Sperre und duerfen an halb geschriebenen oder gerade geloeschten Dateien nicht scheitern.
        using var readerCancellation = new CancellationTokenSource();
        var readerFailures = new List<Exception>();
        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            using var readerStorage = provider.GetTransactionalStorage();
            while (!readerCancellation.IsCancellationRequested)
            {
                try
                {
                    var instances = (await readerStorage.InstanceStorage.GetAllInstances()).ToArray();
                    await readerStorage.SubscriptionStorage.GetAllUserTasksExtended(UserId);
                    foreach (var instance in instances)
                    {
                        await readerStorage.InstanceStorage.GetProcessInstance(instance.InstanceId);
                    }
                }
                catch (Exception exception)
                {
                    lock (readerFailures)
                    {
                        readerFailures.Add(exception);
                    }
                }
            }
        })).ToArray();

        var startedInstances = await Task.WhenAll(Enumerable.Range(0, InstanceCount)
            .Select(_ => Task.Run(() => businessLogic.StartProcessInstance(definition.DefinitionId))));

        await Task.WhenAll(startedInstances.Select(instance => Task.Run(async () =>
        {
            var userTaskToken = instance.Tokens.Single(token =>
                token.CurrentFlowNode is UserTask && token.State == FlowNodeState.Active);
            await businessLogic.HandleUserTask(new UserTaskResult
            {
                ProcessInstanceId = instance.InstanceId,
                TokenId = userTaskToken.Id,
                FlowNodeId = "UserTask_Review"
            }, UserId);
        })));

        readerCancellation.Cancel();
        await Task.WhenAll(readers);
        readerFailures.Should().BeEmpty("parallel readers must tolerate concurrent engine mutations");

        using var storage = provider.GetTransactionalStorage();
        var instances = (await storage.InstanceStorage.GetAllInstances()).ToArray();
        instances.Should().HaveCount(InstanceCount);
        instances.Should().OnlyContain(instance => instance.State == ProcessInstanceState.Completed);
        foreach (var instance in instances)
        {
            (await storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().BeEmpty();
        }
    }

    private static string CreateUserTaskXml() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_Review" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_Review" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_ToReview</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:sequenceFlow id="Flow_ToReview" sourceRef="StartEvent_1" targetRef="UserTask_Review" />
            <bpmn:userTask id="UserTask_Review" name="Review">
              <bpmn:extensionElements>
                <zeebe:formDefinition formKey="Approval" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_ToReview</bpmn:incoming>
              <bpmn:outgoing>Flow_ToEnd</bpmn:outgoing>
            </bpmn:userTask>
            <bpmn:sequenceFlow id="Flow_ToEnd" sourceRef="UserTask_Review" targetRef="EndEvent_1" />
            <bpmn:endEvent id="EndEvent_1">
              <bpmn:incoming>Flow_ToEnd</bpmn:incoming>
            </bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// Lenkt die dateibasierte Ablage fuer die Dauer des Tests in ein leeres Temp-Verzeichnis.
    /// </summary>
    private sealed class FileStorageContext : IDisposable
    {
        private readonly string? _originalRoot;
        private readonly string _tempRoot;

        public FileStorageContext()
        {
            _originalRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-concurrency-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _tempRoot);
        }

        public async Task<BpmnDefinition> StoreDefinitionAsync(ITransactionalStorageProvider provider, string xml)
        {
            var definition = new BpmnDefinition
            {
                Id = Guid.NewGuid(),
                DefinitionId = "Definitions_Review",
                Hash = "hash",
                SavedByUser = UserId,
                SavedOn = DateTime.UtcNow,
                Version = new Model.Version(1, 0),
                IsActive = false
            };

            using var storage = provider.GetTransactionalStorage();
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = definition.DefinitionId,
                Name = "Review"
            });
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, xml);
            return definition;
        }

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
