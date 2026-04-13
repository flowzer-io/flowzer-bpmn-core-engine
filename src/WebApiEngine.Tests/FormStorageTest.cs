using FilesystemStorageSystem;
using FluentAssertions;
using Model;

namespace WebApiEngine.Tests;

[NonParallelizable]
public class FormStorageTest
{
    // Testzweck: Deckt den Fall „Get Max Version Should Return Zero Version When No Forms Exist“ ab.
    [Test]
    public async Task GetMaxVersion_ShouldReturnZeroVersion_WhenNoFormsExist()
    {
        using var context = new FormStorageTestContext();

        var version = await context.FormStorage.GetMaxVersion(context.FormId);

        version.Major.Should().Be(0);
        version.Minor.Should().Be(0);
    }

    // Testzweck: Deckt den Fall „Update Form Meta Data Should Overwrite Existing Metadata“ ab.
    [Test]
    public async Task UpdateFormMetaData_ShouldOverwriteExistingMetadata()
    {
        using var context = new FormStorageTestContext();
        await context.FormStorage.SaveFormMetaData(context.CreateMetadata("Alte Bezeichnung"));

        await context.FormStorage.UpdateFormMetaData(context.CreateMetadata("Neue Bezeichnung"));
        var metadata = await context.FormStorage.GetFormMetaData(context.FormId);

        metadata.Name.Should().Be("Neue Bezeichnung");
    }

    // Testzweck: Deckt den Fall „Delete Form Meta Data Should Delete Metadata And Associated Forms“ ab.
    [Test]
    public async Task DeleteFormMetaData_ShouldDeleteMetadataAndAssociatedForms()
    {
        using var context = new FormStorageTestContext();
        var firstForm = context.CreateForm(1, 0, "{\"version\":1}");
        var secondForm = context.CreateForm(1, 1, "{\"version\":2}");

        await context.FormStorage.SaveFormMetaData(context.CreateMetadata("Rechnung"));
        await context.FormStorage.SaveForm(firstForm);
        await context.FormStorage.SaveForm(secondForm);

        await context.FormStorage.DeleteFormMetaData(context.FormId);

        var metadatas = await context.FormStorage.GetFormMetadatas();
        var forms = await context.FormStorage.GetForms(context.FormId);

        metadatas.Should().NotContain(metadata => metadata.FormId == context.FormId);
        forms.Should().BeEmpty();
        await FluentActions.Awaiting(() => context.FormStorage.GetForm(firstForm.Id)).Should().ThrowAsync<FileNotFoundException>();
    }

    // Testzweck: Deckt den Fall „Delete Form Should Delete Only Requested Version“ ab.
    [Test]
    public async Task DeleteForm_ShouldDeleteOnlyRequestedVersion()
    {
        using var context = new FormStorageTestContext();
        var firstForm = context.CreateForm(1, 0, "{\"version\":1}");
        var secondForm = context.CreateForm(1, 1, "{\"version\":2}");

        await context.FormStorage.SaveForm(firstForm);
        await context.FormStorage.SaveForm(secondForm);

        await context.FormStorage.DeleteForm(firstForm.Id);
        var forms = (await context.FormStorage.GetForms(context.FormId)).ToList();

        forms.Should().ContainSingle();
        forms[0].Id.Should().Be(secondForm.Id);
        forms[0].Version.ToString().Should().Be("1.1");
    }

    private sealed class FormStorageTestContext : IDisposable
    {
        private readonly string _formsPath;
        private readonly string _metaPath;

        public FormStorageTestContext()
        {
            Storage = new Storage();
            FormStorage = Storage.FormStorage;
            FormId = Guid.NewGuid();
            _formsPath = Storage.GetBasePath("FileStorage/Forms");
            _metaPath = Storage.GetBasePath("FileStorage/Forms/Meta");
            Cleanup();
        }

        public Storage Storage { get; }
        public StorageSystem.IFormStorage FormStorage { get; }
        public Guid FormId { get; }

        public FormMetadata CreateMetadata(string name)
        {
            return new FormMetadata
            {
                FormId = FormId,
                Name = name
            };
        }

        public Form CreateForm(int major, int minor, string formData)
        {
            return new Form
            {
                Id = Guid.NewGuid(),
                FormId = FormId,
                Version = new Model.Version(major, minor),
                FormData = formData
            };
        }

        public void Dispose()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            var metadataPath = Path.Combine(_metaPath, $"{FormId}.json");
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);

            foreach (var file in Directory.GetFiles(_formsPath, $"{FormId}_*.json"))
            {
                File.Delete(file);
            }
        }
    }
}
