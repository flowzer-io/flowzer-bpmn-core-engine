using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;
using StorageSystem.Exceptions;

namespace WebApiEngine.Tests;

/// <summary>Dateisystemvertrag für die Einzelprozess-Entwicklung der Abschnittsbibliothek.</summary>
[NonParallelizable]
public sealed class FormSectionStorageTest
{
    // Testzweck: Katalogmetadaten werden neu angelegt, sortiert gelesen und gezielt umbenannt,
    // ohne aus einem zweiten Create einen stillen Upsert zu machen.
    [Test]
    public async Task FilesystemStorage_ShouldCreateListAndRenameSectionMetadata()
    {
        using var context = new Context();
        var approval = new FormSectionMetadata(context.SectionId, "Approval fields");
        var address = new FormSectionMetadata(Guid.NewGuid(), "Address");

        await context.Storage.FormSectionStorage.CreateMetadata(approval);
        await context.Storage.FormSectionStorage.CreateMetadata(address);

        (await context.Storage.FormSectionStorage.ListMetadata()).Select(metadata => metadata.Name)
            .Should().Equal("Address", "Approval fields");
        (await context.Storage.FormSectionStorage.RenameMetadata(context.SectionId, "Approval"))
            .Should().Be(new FormSectionMetadata(context.SectionId, "Approval"));
        (await context.Storage.FormSectionStorage.GetMetadata(context.SectionId)).Name.Should().Be("Approval");
        await context.Storage.FormSectionStorage.Invoking(storage => storage.CreateMetadata(approval))
            .Should().ThrowAsync<DefinitionStorageConflictException>();
    }

    // Testzweck: Der Entwicklungsadapter schützt Entwurfsrevisionen per CAS, so dass ein
    // veralteter Bearbeiter weder speichern noch löschen kann.
    [Test]
    public async Task FilesystemStorage_ShouldCompareAndSwapSectionDrafts()
    {
        using var context = new Context();
        await context.CreateMetadata();
        var initial = context.Draft(revision: 1, "{\"components\":[]}");

        (await context.Storage.FormSectionStorage.TrySave(initial, 0)).Status
            .Should().Be(FormSectionAuthoringWriteStatus.Written);
        var staleWrite = await context.Storage.FormSectionStorage.TrySave(
            context.Draft(revision: 1, "{\"components\":[{\"key\":\"x\"}]}"), 0);
        staleWrite.Status.Should().Be(FormSectionAuthoringWriteStatus.RevisionConflict);
        staleWrite.CurrentRevision.Should().Be(1);
        (await context.Storage.FormSectionStorage.TryDelete(context.SectionId, 0)).Status
            .Should().Be(FormSectionAuthoringDeleteStatus.RevisionConflict);

        var updated = context.Draft(revision: 2, "{\"components\":[{\"key\":\"x\"}]}");
        (await context.Storage.FormSectionStorage.TrySave(updated, 1)).Status
            .Should().Be(FormSectionAuthoringWriteStatus.Written);
        (await context.Storage.FormSectionStorage.GetDraft(context.SectionId))!.SectionData
            .Should().Be(updated.SectionData);
    }

    // Testzweck: Veröffentlichen ergänzt ausschließlich Folgeversionen; ein konkurrierendes
    // Publish derselben Revision kann weder eine zweite Fassung noch den Entwurf zurücklassen.
    [Test]
    public async Task FilesystemStorage_ShouldPublishAppendOnlyVersionsExactlyOnce()
    {
        using var context = new Context();
        await context.CreateMetadata();
        (await context.Storage.FormSectionStorage.TrySave(context.Draft(1, "{\"v\":1}"), 0)).Status
            .Should().Be(FormSectionAuthoringWriteStatus.Written);

        var publications = await Task.WhenAll(
            context.Storage.FormSectionStorage.TryPublish(context.SectionId, 1, Guid.NewGuid()),
            context.Storage.FormSectionStorage.TryPublish(context.SectionId, 1, Guid.NewGuid()));

        publications.Should().ContainSingle(result => result.Status == FormSectionAuthoringPublishStatus.Published);
        publications.Should().ContainSingle(result => result.Status == FormSectionAuthoringPublishStatus.RevisionConflict);
        var firstVersion = publications.Single(result => result.PublishedSection is not null).PublishedSection!;
        firstVersion.Version.Should().Be(new Model.Version(0, 1));
        (await context.Storage.FormSectionStorage.GetVersion(
            context.SectionId, new Model.Version(0, 1))).Id.Should().Be(firstVersion.Id);

        (await context.Storage.FormSectionStorage.TrySave(
            context.Draft(1, "{\"v\":2}", firstVersion.Id, firstVersion.Version), 0)).Status
            .Should().Be(FormSectionAuthoringWriteStatus.Written);
        var second = await context.Storage.FormSectionStorage.TryPublish(context.SectionId, 1, Guid.NewGuid());
        second.PublishedSection!.Version.Should().Be(new Model.Version(0, 2));
        (await context.Storage.FormSectionStorage.ListVersions(context.SectionId))
            .Select(section => section.SectionData).Should().Equal("{\"v\":1}", "{\"v\":2}");
        (await context.Storage.FormSectionStorage.GetDraft(context.SectionId)).Should().BeNull();
    }

    private sealed class Context : IDisposable
    {
        private readonly string? _previousRoot;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"flowzer-form-sections-{Guid.NewGuid():N}");

        public Context()
        {
            _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
            Storage = new Storage();
        }

        public Storage Storage { get; }
        public Guid SectionId { get; } = Guid.NewGuid();

        public Task CreateMetadata() => Storage.FormSectionStorage.CreateMetadata(
            new FormSectionMetadata(SectionId, "Approval"));

        public FormSectionAuthoringDraft Draft(
            long revision,
            string sectionData,
            Guid? basedOnPublishedSectionId = null,
            Model.Version? basedOnVersion = null) => new(
            SectionId,
            revision,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            basedOnPublishedSectionId,
            basedOnVersion,
            sectionData);

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousRoot);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
