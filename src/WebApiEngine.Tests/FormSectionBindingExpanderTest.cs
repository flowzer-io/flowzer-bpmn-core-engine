using System.Text.Json.Nodes;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.Forms;

namespace WebApiEngine.Tests;

public sealed class FormSectionBindingExpanderTest
{
    // Testzweck: Eine konkrete Abschnittsversion wird serverseitig in einen
    // eigenstaendigen Formularsnapshot mit nachvollziehbarer Bindung expandiert.
    [Test]
    public async Task Expand_ShouldEmbedConcreteVersionAndTrustedBindingMetadata()
    {
        var sectionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var storage = new StubStorage(new FormSectionVersion(
            versionId,
            sectionId,
            new Model.Version(0, 1),
            """{"flowzer":{"contractVersion":3},"components":[{"type":"textfield","key":"street"}]}"""));

        var expanded = await FormSectionBindingExpander.ExpandAsync(storage, Form(sectionId, "0.1"));
        var root = JsonNode.Parse(expanded)!.AsObject();

        root["components"]!.AsArray().Select(component => component!["key"]!.GetValue<string>())
            .Should().Equal("street", "comment");
        var binding = root["flowzer"]!["boundSections"]!.AsArray().Single()!.AsObject();
        binding["referenceKey"]!.GetValue<string>().Should().Be("address");
        binding["sectionId"]!.GetValue<Guid>().Should().Be(sectionId);
        binding["sectionVersionId"]!.GetValue<Guid>().Should().Be(versionId);
        binding["version"]!.GetValue<string>().Should().Be("0.1");
        binding["contentSha256"]!.GetValue<string>().Should().MatchRegex("^[0-9a-f]{64}$");
        expanded.Should().NotContain("flowzerSection");
    }

    // Testzweck: Eine spaeter publizierte Abschnittsfassung darf eine bereits konkret
    // gebundene Referenz weder automatisch noch durch Listenreihenfolge aktualisieren.
    [Test]
    public async Task Expand_ShouldKeepConcreteVersionAfterNewerPublication()
    {
        var sectionId = Guid.NewGuid();
        var original = new FormSectionVersion(
            Guid.NewGuid(), sectionId, new Model.Version(0, 1),
            """{"components":[{"type":"textfield","key":"original"}]}""");
        var newer = new FormSectionVersion(
            Guid.NewGuid(), sectionId, new Model.Version(0, 2),
            """{"components":[{"type":"textfield","key":"newer"}]}""");
        var storage = new StubStorage(original, newer);

        var expanded = await FormSectionBindingExpander.ExpandAsync(storage, Form(sectionId, "0.1"));

        expanded.Should().Contain("original").And.NotContain("newer");
    }

    // Testzweck: Eine unversionierte Referenz darf niemals implizit auf die aktuellste
    // Fassung zeigen, weil laufende und erneut veroeffentlichte Formulare stabil bleiben muessen.
    [Test]
    public async Task Expand_ShouldRejectMissingVersionWithStableCode()
    {
        var sectionId = Guid.NewGuid();
        var storage = new StubStorage();

        Func<Task> expand = () => FormSectionBindingExpander.ExpandAsync(
            storage,
            $$"""{"components":[{"type":"flowzerSection","key":"address","sectionId":"{{sectionId}}"}]}""");

        (await expand.Should().ThrowAsync<FormContractException>()).Which.Code
            .Should().Be("section.reference_version");
    }

    // Testzweck: Der symbolische Wert latest bleibt als instabile Versionsauswahl gesperrt.
    [Test]
    public async Task Expand_ShouldRejectLatestVersionWithStableCode()
    {
        var sectionId = Guid.NewGuid();
        var storage = new StubStorage();

        Func<Task> expand = () => FormSectionBindingExpander.ExpandAsync(storage, Form(sectionId, "latest"));

        (await expand.Should().ThrowAsync<FormContractException>()).Which.Code
            .Should().Be("section.reference_version");
    }

    // Testzweck: Eine fehlende konkrete Fassung liefert nur einen stabilen Fehlercode
    // und spiegelt weder Abschnittskennung noch Versionswert in die Fehlermeldung.
    [Test]
    public async Task Expand_ShouldRejectMissingConcreteVersionWithoutEchoingValues()
    {
        var sectionId = Guid.NewGuid();
        var storage = new StubStorage();

        Func<Task> expand = () => FormSectionBindingExpander.ExpandAsync(storage, Form(sectionId, "0.1"));

        var exception = (await expand.Should().ThrowAsync<FormContractException>()).Which;
        exception.Code.Should().Be("section.version_not_found");
        exception.Message.Should().NotContain(sectionId.ToString()).And.NotContain("0.1");
    }

    // Testzweck: Felder aus Abschnitten duerfen vorhandene Formularkeys nicht verdecken;
    // der gemeinsame Formularcompiler bleibt die verbindliche Kollisionspruefung.
    [Test]
    public async Task Expand_ShouldRejectKeyCollisionsWithStableFormCode()
    {
        var sectionId = Guid.NewGuid();
        var storage = new StubStorage(new FormSectionVersion(
            Guid.NewGuid(), sectionId, new Model.Version(0, 1),
            """{"components":[{"type":"textfield","key":"comment"}]}"""));

        Func<Task> expand = () => FormSectionBindingExpander.ExpandAsync(storage, Form(sectionId, "0.1"));

        (await expand.Should().ThrowAsync<FormContractException>()).Which.Code
            .Should().Be("schema.duplicate_key");
    }

    // Testzweck: Vom Browser behauptete Bindungsmetadaten werden verworfen und koennen
    // keine andere Abschnittsfassung als die tatsaechlich aufgeloeste vortaeuschen.
    [Test]
    public async Task Expand_ShouldReplaceUntrustedBindingMetadata()
    {
        var sectionId = Guid.NewGuid();
        var version = new FormSectionVersion(
            Guid.NewGuid(), sectionId, new Model.Version(0, 1),
            """{"components":[{"type":"textfield","key":"street"}]}""");
        var storage = new StubStorage(version);
        var form = Form(sectionId, "0.1").Replace(
            "\"contractVersion\":3",
            "\"contractVersion\":3,\"boundSections\":[{\"referenceKey\":\"forged\"}]");

        var expanded = await FormSectionBindingExpander.ExpandAsync(storage, form);

        expanded.Should().NotContain("forged");
        JsonNode.Parse(expanded)!["flowzer"]!["boundSections"]!.AsArray().Should().ContainSingle();
    }

    // Testzweck: Referenzen sind reine Bindungsmarker und dürfen keine eigenen Scripts,
    // Bedingungen, Datenquellen oder verschachtelten Komponenten transportieren.
    [Test]
    public async Task Expand_ShouldRejectBehaviorOnSectionReference()
    {
        var sectionId = Guid.NewGuid();
        var storage = new StubStorage(new FormSectionVersion(
            Guid.NewGuid(), sectionId, new Model.Version(0, 1),
            """{"components":[{"type":"textfield","key":"street"}]}"""));
        var form = Form(sectionId, "0.1").Replace(
            "\"label\":\"Address\"",
            "\"label\":\"Address\",\"conditional\":{\"when\":\"secret\",\"show\":true,\"eq\":\"yes\"}");

        Func<Task> expand = () => FormSectionBindingExpander.ExpandAsync(storage, form);

        (await expand.Should().ThrowAsync<FormContractException>()).Which.Code
            .Should().Be("section.reference_property");
    }

    private static string Form(Guid sectionId, string version) => $$"""
        {"flowzer":{"contractVersion":3},"components":[
          {"type":"flowzerSection","key":"address","label":"Address","sectionId":"{{sectionId}}","version":"{{version}}"},
          {"type":"textarea","key":"comment"}
        ]}
        """;

    private sealed class StubStorage(params FormSectionVersion[] versions) : IFormSectionStorage
    {
        public Task<FormSectionVersion> GetVersion(Guid sectionId, Model.Version version) =>
            Task.FromResult(versions.SingleOrDefault(candidate =>
                    candidate.SectionId == sectionId && candidate.Version == version)
                ?? throw new FileNotFoundException());

        public Task<FormSectionVersion> GetVersion(Guid id) =>
            Task.FromResult(versions.Single(candidate => candidate.Id == id));
        public Task<IReadOnlyList<FormSectionVersion>> ListVersions(Guid sectionId) =>
            Task.FromResult<IReadOnlyList<FormSectionVersion>>(
                versions.Where(candidate => candidate.SectionId == sectionId).ToArray());
        public Task CreateMetadata(FormSectionMetadata metadata) => throw new NotSupportedException();
        public Task<FormSectionMetadata> GetMetadata(Guid sectionId) => throw new NotSupportedException();
        public Task<IReadOnlyList<FormSectionMetadata>> ListMetadata() => throw new NotSupportedException();
        public Task<FormSectionMetadata> RenameMetadata(Guid sectionId, string name) => throw new NotSupportedException();
        public Task<FormSectionAuthoringDraft?> GetDraft(Guid sectionId) => throw new NotSupportedException();
        public Task<FormSectionAuthoringWriteResult> TrySave(FormSectionAuthoringDraft draft, long expectedRevision) =>
            throw new NotSupportedException();
        public Task<FormSectionAuthoringDeleteResult> TryDelete(Guid sectionId, long expectedRevision) =>
            throw new NotSupportedException();
        public Task<FormSectionAuthoringPublishResult> TryPublish(
            Guid sectionId, long expectedRevision, Guid publishedSectionId) => throw new NotSupportedException();
    }
}
