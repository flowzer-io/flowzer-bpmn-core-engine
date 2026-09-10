using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>Dateisystemvertrag fuer Entwicklung; Mehrprozessgarantien prueft PostgreSQL.</summary>
[NonParallelizable]
public sealed class FormAuthoringStorageTest
{
    // Testzweck: Der Entwicklungsadapter entscheidet konkurrierende Erstanlagen per CAS und
    // veroeffentlicht nur den Gewinner als genau eine neue Version.
    [Test]
    public async Task FilesystemStorage_ShouldCompareAndSwapAndPublishExactlyOneDraft()
    {
        using var context = new Context();
        await context.Storage.FormStorage.SaveFormMetaData(
            new FormMetadata { FormId = context.FormId, Name = "Approval" });
        var first = context.Draft("{\"components\":[]}");
        var second = context.Draft("{\"components\":[{\"type\":\"textfield\",\"key\":\"x\"}]}");

        var writes = await Task.WhenAll(
            context.Storage.FormAuthoringStorage.TrySave(first, 0),
            context.Storage.FormAuthoringStorage.TrySave(second, 0));

        writes.Should().ContainSingle(result => result.Status == FormAuthoringWriteStatus.Written);
        writes.Should().ContainSingle(result => result.Status == FormAuthoringWriteStatus.RevisionConflict);
        var published = await context.Storage.FormAuthoringStorage.TryPublish(context.FormId, 1, Guid.NewGuid());
        published.Status.Should().Be(FormAuthoringPublishStatus.Published);
        published.PublishedForm!.Version.Should().Be(new Model.Version(0, 1));
        (await context.Storage.FormStorage.GetForms(context.FormId)).Should().ContainSingle();
        (await context.Storage.FormAuthoringStorage.Get(context.FormId)).Should().BeNull();
    }

    // Testzweck: Der Publish-Schritt darf einen ausschliesslich serverseitig erzeugten
    // Snapshot speichern, ohne zuvor den unveraenderten Autorenentwurf umzuschreiben.
    [Test]
    public async Task FilesystemStorage_ShouldPublishProvidedServerSnapshot()
    {
        using var context = new Context();
        await context.Storage.FormStorage.SaveFormMetaData(
            new FormMetadata { FormId = context.FormId, Name = "Approval" });
        const string authoringData = "{\"components\":[{\"type\":\"flowzerSection\"}]}";
        const string serverSnapshot = "{\"components\":[{\"type\":\"textfield\",\"key\":\"requester\"}]}";
        (await context.Storage.FormAuthoringStorage.TrySave(context.Draft(authoringData), 0)).Status
            .Should().Be(FormAuthoringWriteStatus.Written);

        var published = await context.Storage.FormAuthoringStorage.TryPublish(
            context.FormId,
            1,
            Guid.NewGuid(),
            serverSnapshot);

        published.Status.Should().Be(FormAuthoringPublishStatus.Published);
        published.PublishedForm!.FormData.Should().Be(serverSnapshot);
        (await context.Storage.FormStorage.GetForms(context.FormId)).Should()
            .ContainSingle(form => form.FormData == serverSnapshot);
        (await context.Storage.FormAuthoringStorage.Get(context.FormId)).Should().BeNull();
    }

    // Testzweck: Beim Loeschen des Katalogeintrags verschwindet auch der nicht
    // veroeffentlichte Autorenentwurf aus der Entwicklungsablage.
    [Test]
    public async Task FilesystemStorage_ShouldDeleteDraftWithFormMetadata()
    {
        using var context = new Context();
        await context.Storage.FormStorage.SaveFormMetaData(
            new FormMetadata { FormId = context.FormId, Name = "Approval" });
        (await context.Storage.FormAuthoringStorage.TrySave(context.Draft("{}"), 0)).Status
            .Should().Be(FormAuthoringWriteStatus.Written);

        await context.Storage.FormStorage.DeleteFormMetaData(context.FormId);

        (await context.Storage.FormAuthoringStorage.Get(context.FormId)).Should().BeNull();
    }

    private sealed class Context : IDisposable
    {
        private readonly string? _previousRoot;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"flowzer-form-authoring-{Guid.NewGuid():N}");

        public Context()
        {
            _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
            Storage = new Storage();
        }

        public Storage Storage { get; }
        public Guid FormId { get; } = Guid.NewGuid();

        public FormAuthoringDraft Draft(string data) => new()
        {
            FormId = FormId,
            Revision = 1,
            UpdatedByUserId = Guid.NewGuid(),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            FormData = data
        };

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousRoot);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
