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

    // Testzweck: Auch der konkrete Einzelzugriff auf eine User-Task-Datei muss denselben
    // Binder verwenden; genau dieser Pfad verarbeitet einen lokal manipulierten Dokumentrumpf.
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

        var exception = (await read.Should().ThrowAsync<JsonSerializationException>()).Which;
        exception.InnerException.Should().BeOfType<JsonSerializationException>()
            .Which.Message.Should().Contain("not allowed");
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
