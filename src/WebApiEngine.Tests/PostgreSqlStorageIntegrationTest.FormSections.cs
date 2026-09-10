using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem;
using StorageSystem.Exceptions;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Die PostgreSQL-Ablage hält Metadaten eindeutig, liefert sie geordnet und
    // benennt nur den bestehenden Abschnitt um.
    [Test]
    public async Task FormSectionStorage_ShouldCreateListAndRenameMetadata()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var sectionId = Guid.NewGuid();
        await storage.FormSectionStorage.CreateMetadata(new FormSectionMetadata(sectionId, "Approval"));
        await storage.FormSectionStorage.CreateMetadata(new FormSectionMetadata(Guid.NewGuid(), "Address"));

        (await storage.FormSectionStorage.ListMetadata()).Select(metadata => metadata.Name)
            .Should().Equal("Address", "Approval");
        (await storage.FormSectionStorage.RenameMetadata(sectionId, "Approval fields")).Name
            .Should().Be("Approval fields");
        (await storage.FormSectionStorage.GetMetadata(sectionId)).Name.Should().Be("Approval fields");
        await storage.FormSectionStorage.Invoking(candidate => candidate.CreateMetadata(
            new FormSectionMetadata(sectionId, "Duplicate"))).Should().ThrowAsync<DefinitionStorageConflictException>();
    }

    // Testzweck: Zwei Datenbanksitzungen können dieselbe Entwurfsrevision nicht beide
    // veröffentlichen; genau eine neue, unveränderliche Version bleibt zurück.
    [Test]
    public async Task FormSectionStorage_ShouldCompareAndSwapAndPublishOnceAcrossSessions()
    {
        var firstStorage = new PostgreSqlStorage(_dataSource!, Schema);
        var secondStorage = new PostgreSqlStorage(_dataSource!, Schema);
        var sectionId = Guid.NewGuid();
        await firstStorage.FormSectionStorage.CreateMetadata(new FormSectionMetadata(sectionId, "Approval"));

        var writes = await Task.WhenAll(
            firstStorage.FormSectionStorage.TrySave(SectionDraft(sectionId, 1, "{\"v\":1}"), 0),
            secondStorage.FormSectionStorage.TrySave(SectionDraft(sectionId, 1, "{\"v\":2}"), 0));
        writes.Should().ContainSingle(result => result.Status == FormSectionAuthoringWriteStatus.Written);
        writes.Should().ContainSingle(result => result.Status == FormSectionAuthoringWriteStatus.RevisionConflict);

        var publications = await Task.WhenAll(
            firstStorage.FormSectionStorage.TryPublish(sectionId, 1, Guid.NewGuid()),
            secondStorage.FormSectionStorage.TryPublish(sectionId, 1, Guid.NewGuid()));
        publications.Should().ContainSingle(result => result.Status == FormSectionAuthoringPublishStatus.Published);
        publications.Should().ContainSingle(result => result.Status == FormSectionAuthoringPublishStatus.RevisionConflict);
        (await firstStorage.FormSectionStorage.ListVersions(sectionId)).Should().ContainSingle()
            .Which.Version.Should().Be(new Model.Version(0, 1));
        (await secondStorage.FormSectionStorage.GetDraft(sectionId)).Should().BeNull();
    }

    // Testzweck: Jede weitere Veröffentlichung erhält die nächste Version und die öffentliche
    // Speicheroberfläche bietet keinen Löschpfad für bereits publizierte Fassungen.
    [Test]
    public async Task FormSectionStorage_ShouldAppendPublishVersions()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var sectionId = Guid.NewGuid();
        await storage.FormSectionStorage.CreateMetadata(new FormSectionMetadata(sectionId, "Approval"));
        (await storage.FormSectionStorage.TrySave(SectionDraft(sectionId, 1, "{\"v\":1}"), 0)).Status
            .Should().Be(FormSectionAuthoringWriteStatus.Written);
        var first = await storage.FormSectionStorage.TryPublish(sectionId, 1, Guid.NewGuid());

        (await storage.FormSectionStorage.TrySave(
            SectionDraft(sectionId, 1, "{\"v\":2}", first.PublishedSection!.Id, first.PublishedSection.Version), 0)).Status
            .Should().Be(FormSectionAuthoringWriteStatus.Written);
        await storage.FormSectionStorage.TryPublish(sectionId, 1, Guid.NewGuid());

        (await storage.FormSectionStorage.ListVersions(sectionId)).Select(version => version.Version)
            .Should().Equal(new Model.Version(0, 1), new Model.Version(0, 2));
    }

    // Testzweck: Nicht bestätigte Abschnittsänderungen bleiben innerhalb der PostgreSQL-
    // Transaktion und sind für einen separaten Leser nach Dispose nicht sichtbar.
    [Test]
    public async Task TransactionalStorage_ShouldRollBackFormSectionsWithoutCommit()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var reader = new PostgreSqlStorage(_dataSource!, Schema);
        var sectionId = Guid.NewGuid();

        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.FormSectionStorage.CreateMetadata(new FormSectionMetadata(sectionId, "Approval"));
            (await storage.FormSectionStorage.TrySave(SectionDraft(sectionId, 1, "{}"), 0)).Status
                .Should().Be(FormSectionAuthoringWriteStatus.Written);
            (await storage.FormSectionStorage.TryPublish(sectionId, 1, Guid.NewGuid())).Status
                .Should().Be(FormSectionAuthoringPublishStatus.Published);
            (await storage.FormSectionStorage.ListVersions(sectionId)).Should().ContainSingle();
        }

        await reader.FormSectionStorage.Invoking(candidate => candidate.GetMetadata(sectionId))
            .Should().ThrowAsync<FileNotFoundException>();
        (await reader.FormSectionStorage.ListVersions(sectionId)).Should().BeEmpty();
    }

    private static FormSectionAuthoringDraft SectionDraft(
        Guid sectionId,
        long revision,
        string sectionData,
        Guid? basedOnPublishedSectionId = null,
        Model.Version? basedOnVersion = null) => new(
        sectionId,
        revision,
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        basedOnPublishedSectionId,
        basedOnVersion,
        sectionData);
}
