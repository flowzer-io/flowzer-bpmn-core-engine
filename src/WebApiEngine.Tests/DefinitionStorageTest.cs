using FilesystemStorageSystem;
using FilesystemStorageSystem.Exceptions;
using FluentAssertions;
using Model;

namespace WebApiEngine.Tests;

[NonParallelizable]
public class DefinitionStorageTest
{
    // Testzweck: Prüft, dass doppelte Metadefinitionen als fachlicher Konfliktfehler gemeldet werden.
    [Test]
    public async Task StoreMetaDefinition_ShouldThrowConflictException_WhenMetaAlreadyExists()
    {
        using var context = new DefinitionStorageTestContext();
        var metaDefinition = context.CreateMetaDefinition("Invoice");
        await context.DefinitionStorage.StoreMetaDefinition(metaDefinition);

        var action = async () => await context.DefinitionStorage.StoreMetaDefinition(metaDefinition);

        await action.Should().ThrowAsync<DefinitionStorageConflictException>();
    }

    // Testzweck: Prüft, dass das Aktualisieren fehlender Metadefinitionen als NotFound-Fehler endet.
    [Test]
    public async Task UpdateMetaDefinition_ShouldThrowNotFoundException_WhenMetaDoesNotExist()
    {
        using var context = new DefinitionStorageTestContext();

        var action = async () => await context.DefinitionStorage.UpdateMetaDefinition(context.CreateMetaDefinition("Missing"));

        await action.Should().ThrowAsync<DefinitionStorageNotFoundException>();
    }

    // Testzweck: Prüft, dass das Laden fehlender Metadefinitionen als NotFound-Fehler endet.
    [Test]
    public async Task GetMetaDefinitionById_ShouldThrowNotFoundException_WhenMetaDoesNotExist()
    {
        using var context = new DefinitionStorageTestContext();

        var action = async () => await context.DefinitionStorage.GetMetaDefinitionById(context.DefinitionId);

        await action.Should().ThrowAsync<DefinitionStorageNotFoundException>();
    }

    // Testzweck: Prüft, dass das Laden der neuesten Definition ohne vorhandene Versionen als NotFound-Fehler endet.
    [Test]
    public async Task GetLatestDefinition_ShouldThrowNotFoundException_WhenDefinitionDoesNotExist()
    {
        using var context = new DefinitionStorageTestContext();

        var action = async () => await context.DefinitionStorage.GetLatestDefinition(context.DefinitionId);

        await action.Should().ThrowAsync<DefinitionStorageNotFoundException>();
    }

    private sealed class DefinitionStorageTestContext : IDisposable
    {
        private readonly string _definitionsPath;
        private readonly string _metaPath;

        public DefinitionStorageTestContext()
        {
            Storage = new Storage();
            DefinitionStorage = Storage.DefinitionStorage;
            DefinitionId = $"definition-{Guid.NewGuid():N}";
            _definitionsPath = Storage.GetBasePath("FileStorage/Definitions");
            _metaPath = Storage.GetBasePath("FileStorage/Definitions/Meta");
            Cleanup();
        }

        public Storage Storage { get; }
        public StorageSystem.IDefinitionStorage DefinitionStorage { get; }
        public string DefinitionId { get; }

        public BpmnMetaDefinition CreateMetaDefinition(string name)
        {
            return new BpmnMetaDefinition
            {
                DefinitionId = DefinitionId,
                Name = name
            };
        }

        public void Dispose()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            foreach (var file in Directory.GetFiles(_definitionsPath, "*.json"))
            {
                if (File.ReadAllText(file).Contains(DefinitionId, StringComparison.Ordinal))
                {
                    File.Delete(file);
                }
            }

            var metaPath = Path.Combine(_metaPath, $"{DefinitionId}.json");
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }
    }
}
