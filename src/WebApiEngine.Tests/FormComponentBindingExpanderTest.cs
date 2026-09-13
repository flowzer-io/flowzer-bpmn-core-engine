using System.Text.Json.Nodes;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.Forms;

namespace WebApiEngine.Tests;

public sealed class FormComponentBindingExpanderTest
{
    // Testzweck: Ein Formular ist als konkrete Version wiederverwendbar; veröffentlicht wird
    // trotzdem ein eigenständiger Snapshot ohne Laufzeitabhängigkeit von der Bibliothek.
    [Test]
    public async Task Expand_ShouldEmbedConcreteFormVersionAndBindingMetadata()
    {
        var childId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var forms = new StubFormStorage(new Form
        {
            Id = versionId,
            FormId = childId,
            Version = new Model.Version(1, 2),
            FormData = """{"flowzer":{"decisionActions":[{"key":"approve"}]},"components":[{"type":"textfield","key":"street"}]}"""
        });

        var expanded = await FormSectionBindingExpander.ExpandAsync(
            forms,
            new StubSectionStorage(),
            Guid.NewGuid(),
            Parent(childId, "1.2"));
        var root = JsonNode.Parse(expanded)!.AsObject();

        root["components"]!.AsArray().Select(component => component!["key"]!.GetValue<string>())
            .Should().Equal("street", "comment");
        var binding = root["flowzer"]!["boundForms"]!.AsArray().Single()!.AsObject();
        binding["referenceKey"]!.GetValue<string>().Should().Be("address");
        binding["formId"]!.GetValue<Guid>().Should().Be(childId);
        binding["formVersionId"]!.GetValue<Guid>().Should().Be(versionId);
        binding["version"]!.GetValue<string>().Should().Be("1.2");
        binding["contentSha256"]!.GetValue<string>().Should().MatchRegex("^[0-9a-f]{64}$");
        expanded.Should().NotContain("flowzerForm");
        expanded.Should().NotContain("decisionActions", "root actions of a component form are not inherited");
    }

    // Testzweck: Form.io ergänzt beim Speichern leere Standardobjekte. Diese Defaults dürfen
    // eine ausschließlich über den vertrauenswürdigen Picker erzeugte Referenz nicht sperren.
    [Test]
    public async Task Expand_ShouldAcceptInactiveFormIoReferenceDefaults()
    {
        var childId = Guid.NewGuid();
        var forms = new StubFormStorage(Version(
            childId,
            "0.1",
            """{"components":[{"type":"textfield","key":"street"}]}"""));
        var parent = $$"""
            {"components":[{
              "type":"flowzerForm","key":"address","formId":"{{childId}}","version":"0.1",
              "calculateValue":"","customDefaultValue":"","customConditional":"","logic":[],
              "conditional":{"show":null,"when":null,"eq":""},"data":{}
            }]}
            """;

        var expanded = await FormSectionBindingExpander.ExpandAsync(
            forms, new StubSectionStorage(), Guid.NewGuid(), parent);

        expanded.Should().Contain("street").And.NotContain("flowzerForm");
    }

    // Testzweck: "latest" ist keine reproduzierbare Komponentenbindung und darf deshalb
    // weder in der Vorschau noch beim Veröffentlichen akzeptiert werden.
    [Test]
    public async Task Expand_ShouldRejectLatestFormVersion()
    {
        var formId = Guid.NewGuid();
        Func<Task> expand = () => FormSectionBindingExpander.ExpandAsync(
            new StubFormStorage(), new StubSectionStorage(), Guid.NewGuid(), Parent(formId, "latest"));

        (await expand.Should().ThrowAsync<FormContractException>()).Which.Code
            .Should().Be("form.reference_version");
    }

    // Testzweck: Ein Formular darf sich auch nicht über eine ältere Fassung selbst einbetten;
    // sonst wird die Bedeutung des Entwurfs für Autorinnen und Autoren unverständlich.
    [Test]
    public async Task Expand_ShouldRejectDirectSelfReference()
    {
        var formId = Guid.NewGuid();
        Func<Task> expand = () => FormSectionBindingExpander.ExpandAsync(
            new StubFormStorage(), new StubSectionStorage(), formId, Parent(formId, "0.1"));

        (await expand.Should().ThrowAsync<FormContractException>()).Which.Code
            .Should().Be("form.reference_cycle");
    }

    // Testzweck: Auch importierte Altbestände mit noch nicht expandierten Referenzen dürfen
    // keine indirekten Zyklen A -> B -> A in einen Publish einschleusen.
    [Test]
    public async Task Expand_ShouldRejectIndirectReferenceCycle()
    {
        var owner = Guid.NewGuid();
        var child = Guid.NewGuid();
        var forms = new StubFormStorage(
            Version(child, "0.1", Parent(owner, "0.1")),
            Version(owner, "0.1", Parent(child, "0.1")));

        Func<Task> expand = () => FormSectionBindingExpander.ExpandAsync(
            forms, new StubSectionStorage(), owner, Parent(child, "0.1"));

        (await expand.Should().ThrowAsync<FormContractException>()).Which.Code
            .Should().Be("form.reference_cycle");
    }

    // Testzweck: Nach dem Expandieren bleibt die bestehende globale Schlüsselprüfung aktiv;
    // ein Komponentenformular darf kein Feld des aufnehmenden Formulars verdecken.
    [Test]
    public async Task Expand_ShouldRejectDuplicateKeysAcrossForms()
    {
        var child = Guid.NewGuid();
        var forms = new StubFormStorage(Version(
            child,
            "0.1",
            """{"components":[{"type":"textfield","key":"comment"}]}"""));

        Func<Task> expand = () => FormSectionBindingExpander.ExpandAsync(
            forms, new StubSectionStorage(), Guid.NewGuid(), Parent(child, "0.1"));

        (await expand.Should().ThrowAsync<FormContractException>()).Which.Code
            .Should().Be("schema.duplicate_key");
    }

    private static Form Version(Guid formId, string version, string data)
    {
        var parsed = Model.Version.FromString(version);
        return new Form { Id = Guid.NewGuid(), FormId = formId, Version = parsed, FormData = data };
    }

    private static string Parent(Guid formId, string version) => $$"""
        {"flowzer":{"contractVersion":3},"components":[
          {"type":"flowzerForm","key":"address","label":"Address","formId":"{{formId}}","version":"{{version}}"},
          {"type":"textarea","key":"comment"}
        ]}
        """;

    private sealed class StubFormStorage(params Form[] versions) : IFormStorage
    {
        public Task<Form> GetForm(Guid id) => Task.FromResult(versions.Single(item => item.Id == id));
        public Task<IEnumerable<Form>> GetForms(Guid formId) => Task.FromResult(versions.Where(item => item.FormId == formId));
        public Task<Model.Version> GetMaxVersion(Guid formId) => throw new NotSupportedException();
        public Task SaveFormMetaData(FormMetadata formMetadata) => throw new NotSupportedException();
        public Task<FormMetadata> GetFormMetaData(Guid formId) => throw new NotSupportedException();
        public Task<IEnumerable<FormMetadata>> GetFormMetadatas() => throw new NotSupportedException();
        public Task UpdateFormMetaData(FormMetadata formMetaData) => throw new NotSupportedException();
        public Task DeleteFormMetaData(Guid formId) => throw new NotSupportedException();
        public Task SaveForm(Form form) => throw new NotSupportedException();
        public Task DeleteForm(Guid id) => throw new NotSupportedException();
    }

    private sealed class StubSectionStorage : IFormSectionStorage
    {
        public Task CreateMetadata(FormSectionMetadata metadata) => throw new NotSupportedException();
        public Task<FormSectionMetadata> GetMetadata(Guid sectionId) => throw new NotSupportedException();
        public Task<IReadOnlyList<FormSectionMetadata>> ListMetadata() => throw new NotSupportedException();
        public Task<FormSectionMetadata> RenameMetadata(Guid sectionId, string name) => throw new NotSupportedException();
        public Task<FormSectionVersion> GetVersion(Guid id) => throw new FileNotFoundException();
        public Task<FormSectionVersion> GetVersion(Guid sectionId, Model.Version version) => throw new FileNotFoundException();
        public Task<IReadOnlyList<FormSectionVersion>> ListVersions(Guid sectionId) => throw new NotSupportedException();
        public Task<FormSectionAuthoringDraft?> GetDraft(Guid sectionId) => throw new NotSupportedException();
        public Task<FormSectionAuthoringWriteResult> TrySave(FormSectionAuthoringDraft draft, long expectedRevision) => throw new NotSupportedException();
        public Task<FormSectionAuthoringDeleteResult> TryDelete(Guid sectionId, long expectedRevision) => throw new NotSupportedException();
        public Task<FormSectionAuthoringPublishResult> TryPublish(Guid sectionId, long expectedRevision, Guid publishedSectionId) => throw new NotSupportedException();
    }
}
