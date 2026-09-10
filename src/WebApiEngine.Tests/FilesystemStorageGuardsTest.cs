using FluentAssertions;
using Newtonsoft.Json;

namespace WebApiEngine.Tests;

/// <summary>Sicherheitsgrenzen der entwicklungsbezogenen Dateiablage.</summary>
public sealed class FilesystemStorageGuardsTest
{
    // Testzweck: Ein manipulierter $type-Wert aus einer fremden Assembly darf bei keiner
    // polymorphen Datei-Deserialisierung instanziiert werden.
    [Test]
    public void PolymorphicSettings_ShouldRejectForeignTypes()
    {
        using var root = new StorageRootScope();
        var settings = new FilesystemStorageSystem.Storage().NewtonSoftDefaultSettings;
        const string hostile = "{\"$type\":\"System.Diagnostics.Process, System.Diagnostics.Process\",\"StartInfo\":{}}";

        Action deserialize = () => JsonConvert.DeserializeObject<object>(hostile, settings);

        var exception = deserialize.Should().Throw<JsonSerializationException>().Which;
        exception.InnerException.Should().BeOfType<JsonSerializationException>()
            .Which.Message.Should().Contain("not allowed");
    }

    // Testzweck: Auch der konkrete Einzelzugriff auf eine User-Task-Datei darf einen fremden
    // $type-Wert niemals instanziieren; der Datenvertrag weist die ungültige Bindung zurück.
    [Test]
    public async Task UserTaskRead_ShouldRejectForeignTypes()
    {
        using var root = new StorageRootScope();
        var storage = new FilesystemStorageSystem.Storage();
        var id = Guid.NewGuid();
        var path = Path.Combine(
            root.Path,
            "FileStorage",
            "MessageSubscriptions",
            $"usertask_{id}.json");
        await File.WriteAllTextAsync(
            path,
            "{\"$type\":\"System.Diagnostics.Process, System.Diagnostics.Process\",\"StartInfo\":{}}");

        Func<Task> read = async () => await storage.SubscriptionStorage.GetUserTaskExtended(id);

        await read.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*invalid identity binding*");
    }

    // Testzweck: Konkrete Formulardokumente dürfen ein eingeschleustes $type-Feld nur als
    // unbekannte Nutzlast ignorieren und niemals als Aufforderung zur Typinstanziierung lesen.
    [Test]
    public async Task FormMetadataRead_ShouldIgnoreClrTypeMetadata()
    {
        using var root = new StorageRootScope();
        var storage = new FilesystemStorageSystem.Storage();
        var id = Guid.NewGuid();
        var path = Path.Combine(root.Path, "FileStorage", "Forms", "Meta", $"{id}.json");
        await File.WriteAllTextAsync(path, $$"""
            {
              "$type": "System.Diagnostics.Process, System.Diagnostics.Process",
              "FormId": "{{id}}",
              "Name": "Sicheres Formular"
            }
            """);

        var metadata = await storage.FormStorage.GetFormMetaData(id);

        metadata.FormId.Should().Be(id);
        metadata.Name.Should().Be("Sicheres Formular");
    }

    // Testzweck: Service-Task-Aufträge werden ohne polymorphe Typmetadaten gespeichert und
    // behalten trotzdem Token-/Schrittreferenz sowie verschachtelte Eingabewerte.
    [Test]
    public async Task ServiceTaskJob_ShouldRoundTripWithoutPolymorphicTypeMetadata()
    {
        using var root = new StorageRootScope();
        var storage = new FilesystemStorageSystem.Storage();
        var instanceId = Guid.NewGuid();
        var task = new BPMN.Activities.ServiceTask
        {
            Id = "ServiceTask_Safe",
            Name = "Sicher ausführen",
            Implementation = "safe-worker"
        };
        var token = new Model.Token
        {
            ProcessInstanceId = instanceId,
            CurrentBaseElement = task,
            ActiveBoundaryEvents = [],
            State = Model.FlowNodeState.Active
        };
        var variables = new System.Dynamic.ExpandoObject();
        ((IDictionary<string, object?>)variables)["nested"] = new Dictionary<string, object?>
        {
            ["answer"] = 42L
        };
        var job = new Model.ServiceTaskJob
        {
            Id = Guid.NewGuid(),
            Type = task.Implementation,
            Name = task.Name,
            TokenId = token.Id,
            FlowNodeId = task.Id,
            ProcessInstanceId = instanceId,
            MetaDefinitionId = "safe-catalog",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_Safe",
            Retries = 1,
            Variables = variables
        };

        await storage.ServiceTaskStorage.SaveJob(job);
        var path = Path.Combine(root.Path, "FileStorage", "ServiceTasks", $"job_{job.Id}.json");
        var persisted = await File.ReadAllTextAsync(path);
        var restored = await storage.ServiceTaskStorage.GetJob(job.Id);

        persisted.Should().NotContain("$type");
        restored.Should().NotBeNull();
        restored!.TokenId.Should().Be(token.Id);
        restored.FlowNodeId.Should().Be(task.Id);
        ((IDictionary<string, object?>)restored.Variables!).Should().ContainKey("nested");
    }

    private sealed class StorageRootScope : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable(
            FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName);
        private readonly string _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "flowzer-storage-guard",
            Guid.NewGuid().ToString("N"));

        public StorageRootScope() => Environment.SetEnvironmentVariable(
            FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName,
            _path);

        public string Path => _path;

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(
                FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName,
                _previous);
            if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
        }
    }
}
