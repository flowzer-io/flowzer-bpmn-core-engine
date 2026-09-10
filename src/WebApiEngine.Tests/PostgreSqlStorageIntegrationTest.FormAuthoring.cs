using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Zwei API-Prozesse koennen weder denselben ersten Autorenentwurf noch
    // zwei Veroeffentlichungen aus derselben Revision gewinnen lassen.
    [Test]
    public async Task FormAuthoringStorage_ShouldCompareAndSwapAndPublishOnceAcrossSessions()
    {
        var firstStorage = new PostgreSqlStorage(_dataSource!, Schema);
        var secondStorage = new PostgreSqlStorage(_dataSource!, Schema);
        var formId = Guid.NewGuid();
        await firstStorage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = "Approval" });
        var first = Draft(formId, "{\"components\":[]}");
        var second = Draft(formId, "{\"components\":[{\"type\":\"textfield\",\"key\":\"x\"}]}");

        var writes = await Task.WhenAll(
            firstStorage.FormAuthoringStorage.TrySave(first, 0),
            secondStorage.FormAuthoringStorage.TrySave(second, 0));

        writes.Should().ContainSingle(result => result.Status == FormAuthoringWriteStatus.Written);
        writes.Should().ContainSingle(result => result.Status == FormAuthoringWriteStatus.RevisionConflict);
        var publications = await Task.WhenAll(
            firstStorage.FormAuthoringStorage.TryPublish(formId, 1, Guid.NewGuid()),
            secondStorage.FormAuthoringStorage.TryPublish(formId, 1, Guid.NewGuid()));
        publications.Should().ContainSingle(result => result.Status == FormAuthoringPublishStatus.Published);
        publications.Should().ContainSingle(result => result.Status == FormAuthoringPublishStatus.RevisionConflict);
        (await firstStorage.FormStorage.GetForms(formId)).Should().ContainSingle();
        (await secondStorage.FormAuthoringStorage.Get(formId)).Should().BeNull();
    }

    // Testzweck: Der Datenbank-Fremdschluessel entfernt den Autorenentwurf zusammen mit
    // dem Katalogformular, statt einen nicht mehr zuordenbaren Arbeitsstand zu hinterlassen.
    [Test]
    public async Task FormAuthoringStorage_ShouldCascadeWithFormMetadata()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var formId = Guid.NewGuid();
        await storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = "Approval" });
        (await storage.FormAuthoringStorage.TrySave(Draft(formId, "{}"), 0)).Status
            .Should().Be(FormAuthoringWriteStatus.Written);

        await storage.FormStorage.DeleteFormMetaData(formId);

        (await storage.FormAuthoringStorage.Get(formId)).Should().BeNull();
    }

    private static FormAuthoringDraft Draft(Guid formId, string formData) => new()
    {
        FormId = formId,
        Revision = 1,
        UpdatedByUserId = Guid.NewGuid(),
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        FormData = formData
    };
}
