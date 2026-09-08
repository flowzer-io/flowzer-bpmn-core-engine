using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

public class FormControllerIntegrationTest
{
    private const string ValidFormSchema = "{\"display\":\"form\",\"components\":[{\"type\":\"textfield\",\"key\":\"reason\",\"input\":true}]}";

    // Testzweck: Compare-and-swap verhindert, dass ein alter Browserstand einen inzwischen
    // gespeicherten Autorenentwurf still ueberschreibt; der Konflikt verrät keine Formulardaten.
    [Test]
    public async Task SaveAuthoringDraft_ShouldRejectAStaleRevision()
    {
        var storage = TestStorage.Create();
        var formId = SeedFormMetadata(storage);
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = CreateAuthorClient(factory);

        var first = await client.PutAsJsonAsync($"/form/{formId}/draft",
            new SaveFormAuthoringDraftRequestDto { ExpectedRevision = 0, FormData = ValidFormSchema });
        var stale = await client.PutAsJsonAsync($"/form/{formId}/draft",
            new SaveFormAuthoringDraftRequestDto { ExpectedRevision = 0, FormData = "{}" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("form_draft.revision_conflict");
        problem.RootElement.GetProperty("currentRevision").GetInt64().Should().Be(1);
        (await storage.FormAuthoringStorageSeed.Get(formId))!.FormData.Should().Be(ValidFormSchema);
    }

    // Testzweck: Entwuerfe duerfen waehrend der Modellierung unvollstaendig sein; Publish
    // lehnt nicht unterstuetztes JavaScript serverseitig ab und erhaelt den Arbeitsstand.
    [Test]
    public async Task PublishAuthoringDraft_ShouldKeepAnInvalidDraftUnpublished()
    {
        var storage = TestStorage.Create();
        var formId = SeedFormMetadata(storage);
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = CreateAuthorClient(factory);
        var unsupported = "{\"components\":[{\"type\":\"textfield\",\"key\":\"x\",\"calculateValue\":\"value=1\"}]}";
        await client.PutAsJsonAsync($"/form/{formId}/draft",
            new SaveFormAuthoringDraftRequestDto { ExpectedRevision = 0, FormData = unsupported });

        var response = await client.PostAsJsonAsync($"/form/{formId}/publish",
            new PublishFormAuthoringDraftRequestDto { ExpectedRevision = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        storage.FormStorageSeed.Forms.Should().BeEmpty();
        (await storage.FormAuthoringStorageSeed.Get(formId)).Should().NotBeNull();
    }

    // Testzweck: Publish erzeugt genau die naechste unveraenderliche Version aus dem
    // erwarteten Entwurf und entfernt diesen im selben atomaren Speicheraufruf.
    [Test]
    public async Task PublishAuthoringDraft_ShouldCreateNextVersionAndRemoveDraft()
    {
        var storage = TestStorage.Create();
        var formId = SeedFormMetadata(storage);
        storage.FormStorageSeed.Forms.Add(new Form
        {
            Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(0, 1), FormData = "{}"
        });
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = CreateAuthorClient(factory);
        await client.PutAsJsonAsync($"/form/{formId}/draft",
            new SaveFormAuthoringDraftRequestDto { ExpectedRevision = 0, FormData = ValidFormSchema });

        var response = await client.PostAsJsonAsync($"/form/{formId}/publish",
            new PublishFormAuthoringDraftRequestDto { ExpectedRevision = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Result!.Version!.ToString().Should().Be("0.2");
        storage.FormStorageSeed.Forms.Should().HaveCount(2);
        storage.FormStorageSeed.Forms.Single(form => form.Version.Equals(new Model.Version(0, 2)))
            .FormData.Should().Be(ValidFormSchema);
        (await storage.FormAuthoringStorageSeed.Get(formId)).Should().BeNull();
    }

    // Testzweck: Ohne gespeicherten Entwurf liefert die Autorenansicht eine Basis aus der
    // letzten Veroeffentlichung, markiert sie aber nicht faelschlich als Entwurf.
    [Test]
    public async Task GetAuthoringDraft_ShouldReturnTheLatestPublishedBaseline()
    {
        var storage = TestStorage.Create();
        var formId = SeedFormMetadata(storage);
        storage.FormStorageSeed.Forms.Add(new Form
        {
            Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(2, 4), FormData = ValidFormSchema
        });
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = CreateAuthorClient(factory);

        var result = (await client.GetFromJsonAsync<ApiStatusResult<FormAuthoringDraftDto>>(
            $"/form/{formId}/draft"))!.Result!;

        result.HasDraft.Should().BeFalse();
        result.Revision.Should().Be(0);
        result.BasedOnVersion!.ToString().Should().Be("2.4");
        result.FormData.Should().Be(ValidFormSchema);
    }

    // Testzweck: Deckt den Fall „Save Form Should Create Initial Version When No Version Exists“ ab.
    [Test]
    public async Task SaveForm_ShouldCreateInitialVersion_WhenNoVersionExists()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/form", new FormDto
        {
            FormId = formId,
            FormData = "{\"type\":\"form\"}",
            Version = new VersionDto()
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().NotBeNull();
        payload.Result!.Version.Major.Should().Be(0);
        payload.Result.Version.Minor.Should().Be(1);
        storage.FormStorageSeed.Forms.Should().ContainSingle(form =>
            form.FormId == formId &&
            form.Version.Major == 0 &&
            form.Version.Minor == 1);
    }

    // Testzweck: Deckt den Fall „Save Form Should Return Bad Request When Form ID Is Empty“ ab.
    [Test]
    public async Task SaveForm_ShouldReturnBadRequest_WhenFormIdIsEmpty()
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/form", new FormDto
        {
            FormId = Guid.Empty,
            FormData = "{\"type\":\"form\"}",
            Version = new VersionDto()
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Be("FormId is required");
    }

    // Testzweck: Deckt den Fall „Get Form Should Return Specific Version When Version Identifier Is Provided“ ab.
    [Test]
    public async Task GetForm_ShouldReturnSpecificVersion_WhenVersionIdentifierIsProvided()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        var oldForm = new Form
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = new Model.Version(1, 0),
            FormData = "{\"version\":\"1.0\"}"
        };
        var newForm = new Form
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = new Model.Version(1, 1),
            FormData = "{\"version\":\"1.1\"}"
        };

        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Invoice" });
        storage.FormStorageSeed.Forms.AddRange([oldForm, newForm]);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/form/{formId}/1.0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().NotBeNull();
        payload.Result!.Id.Should().Be(oldForm.Id);
        payload.Result.FormData.Should().Be(oldForm.FormData);
        payload.Result.Version.ToString().Should().Be("1.0");
    }

    // Testzweck: Deckt den Fall „Get Form Should Return Bad Request When Version Identifier Is Invalid“ ab.
    [Test]
    public async Task GetForm_ShouldReturnBadRequest_WhenVersionIdentifierIsInvalid()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.Forms.Add(new Form
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = new Model.Version(1, 0),
            FormData = "{\"version\":\"1.0\"}"
        });

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/form/{formId}/invalid-version");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("Version string must have two parts separated by a dot.");
    }

    // Testzweck: Deckt den Fall „Save Form Metadata Should Use Route Form ID When Body Form ID Is Empty“ ab.
    [Test]
    public async Task SaveFormMetadata_ShouldUseRouteFormId_WhenBodyFormIdIsEmpty()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/form/meta/{formId}", new FormMetaDataDto
        {
            FormId = Guid.Empty,
            Name = "Invoice"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.FormStorageSeed.FormMetadatas.Should().ContainSingle(metadata =>
            metadata.FormId == formId &&
            metadata.Name == "Invoice");
    }

    // Testzweck: Loeschen entfernt das Formular samt allen seinen Versionen. Blieben die
    // Versionen liegen, waeren sie ueber /form/{id}/{version} weiter abrufbar, ohne dass sie
    // noch irgendwo im Katalog auftauchen.
    [Test]
    public async Task DeleteFormMetadata_ShouldRemoveTheFormAndItsVersions()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Krankmeldung" });
        storage.FormStorageSeed.Forms.Add(new Form
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = new Model.Version(1, 0),
            FormData = "{}"
        });

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/form/meta/{formId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormMetaDataDto>>();
        payload!.Successful.Should().BeTrue();
        payload.Result!.Name.Should().Be("Krankmeldung");

        storage.FormStorageSeed.FormMetadatas.Should().BeEmpty();
        storage.FormStorageSeed.Forms.Should().BeEmpty();
    }

    // Testzweck: Ein Formular, das ein deployter Workflow benutzt, bleibt stehen. Der Form-Key
    // im BPMN ist der Name des Formulars; waere es weg, liefe jede Aufgabe dieses Schrittes in
    // „No form named …" — auch in bereits laufenden Instanzen.
    [Test]
    public async Task DeleteFormMetadata_ShouldRefuse_WhenADeployedWorkflowUsesTheForm()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Freigabe" });
        storage.FormStorageSeed.Forms.Add(new Form
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = new Model.Version(1, 0),
            FormData = "{}"
        });

        SeedDeployedWorkflowUsingForm(storage, "Freigabe");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/form/meta/{formId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormMetaDataDto>>();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("Urlaubsantrag");

        storage.FormStorageSeed.FormMetadatas.Should().ContainSingle();
        storage.FormStorageSeed.Forms.Should().ContainSingle("die Versionen bleiben mit stehen");
    }

    // Testzweck: Der Vergleich laeuft ueber den Namen und achtet nicht auf Gross- und
    // Kleinschreibung — genauso loest die Engine den Form-Key auf. Sonst liesse sich ein
    // benutztes Formular loeschen, indem man es „freigabe" statt „Freigabe" nennt.
    [Test]
    public async Task DeleteFormMetadata_ShouldRefuse_RegardlessOfLetterCase()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "freigabe" });

        SeedDeployedWorkflowUsingForm(storage, "Freigabe:1.0");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        (await client.DeleteAsync($"/form/meta/{formId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        storage.FormStorageSeed.FormMetadatas.Should().ContainSingle();
    }

    // Testzweck: Ein Modell darf statt des Namens auch die Kennung des Formulars nennen —
    // der Parser nimmt `formId` als Alternative zu `formKey`. Ohne diesen Fall liesse sich
    // ein benutztes Formular loeschen, indem das Modell es ueber die Kennung anspricht.
    [Test]
    public async Task DeleteFormMetadata_ShouldRefuse_WhenTheWorkflowReferencesTheFormById()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Freigabe" });

        SeedDeployedWorkflowUsingForm(storage, formId.ToString());

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        (await client.DeleteAsync($"/form/meta/{formId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        storage.FormStorageSeed.FormMetadatas.Should().ContainSingle();
    }

    // Testzweck: Eine Aufgabe in einem Subprozess zaehlt genauso. Sie steht nicht in den
    // Flow-Elementen des Prozesses; wer nur dort sucht, haelt das Formular fuer unbenutzt und
    // loescht es — die Aufgabe im Subprozess laeuft danach in „No form named …".
    [Test]
    public async Task DeleteFormMetadata_ShouldRefuse_WhenTheFormIsUsedInsideASubProcess()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Freigabe" });

        SeedDeployedWorkflowUsingForm(storage, "Freigabe", inSubProcess: true);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        (await client.DeleteAsync($"/form/meta/{formId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        storage.FormStorageSeed.FormMetadatas.Should().ContainSingle();
    }

    // Testzweck: Auch eine Fassung, die nicht mehr deployt ist, zaehlt — solange Instanzen
    // darauf laufen. Wird eine Version abgeloest, die das Formular benutzt, warten ihre
    // Instanzen weiter auf die Aufgabe; ohne das Formular kommen sie nicht mehr weiter.
    [Test]
    public async Task DeleteFormMetadata_ShouldRefuse_WhenARunningInstanceUsesAnOlderDefinition()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Freigabe" });

        // Die alte Fassung benutzt das Formular, ist aber nicht mehr deployt.
        var alteFassung = Guid.NewGuid();
        storage.DefinitionStorageSeed.Binaries[alteFassung] = BuildWorkflowXml("Freigabe", inSubProcess: false);
        storage.InstanceStorageSeed.Active.Add(new ProcessInstanceInfo
        {
            InstanceId = Guid.NewGuid(),
            metaDefinitionId = "Urlaubsantrag",
            DefinitionId = alteFassung,
            ProcessId = "Process_Urlaub",
            Tokens = [],
            IsFinished = false,
            State = ProcessInstanceState.Waiting,
            MessageSubscriptionCount = 0,
            SignalSubscriptionCount = 0,
            UserTaskSubscriptionCount = 1,
            ServiceSubscriptionCount = 0
        });

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/form/meta/{formId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ApiStatusResult<FormMetaDataDto>>())!
            .ErrorMessage.Should().Contain("Urlaubsantrag");
        storage.FormStorageSeed.FormMetadatas.Should().ContainSingle();
    }

    // Testzweck: Ein Doppelpunkt im Formularnamen ist keine Versionsangabe. Wer stur am
    // letzten Doppelpunkt abschneidet, vergleicht „Pruefung" mit „Pruefung: Detail", findet
    // keine Nutzung und loescht ein benutztes Formular.
    [Test]
    public async Task DeleteFormMetadata_ShouldRefuse_WhenTheFormNameItselfContainsAColon()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Prüfung: Detail" });

        SeedDeployedWorkflowUsingForm(storage, "Prüfung: Detail");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        (await client.DeleteAsync($"/form/meta/{formId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        storage.FormStorageSeed.FormMetadatas.Should().ContainSingle();
    }

    // Testzweck: Ein Modell, das sich nicht lesen laesst, gilt als moeglicher Benutzer. Es zu
    // ueberspringen hiesse, im Zweifel zu loeschen.
    [Test]
    public async Task DeleteFormMetadata_ShouldRefuse_WhenADeployedModelCannotBeRead()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Freigabe" });

        SeedDeployedWorkflowUsingForm(storage, "Freigabe");
        // Dasselbe Modell, aber kaputt.
        var kaputt = storage.DefinitionStorageSeed.Deployed["urlaub"].Id;
        storage.DefinitionStorageSeed.Binaries[kaputt] = "<kein gueltiges bpmn";

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        (await client.DeleteAsync($"/form/meta/{formId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        storage.FormStorageSeed.FormMetadatas.Should().ContainSingle();
    }

    // Testzweck: Ein Formular, das es nicht gibt, ist kein erfolgreiches Loeschen.
    [Test]
    public async Task DeleteFormMetadata_ShouldReturnNotFound_ForAnUnknownForm()
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/form/meta/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormMetaDataDto>>();
        payload!.Successful.Should().BeFalse();
    }

    // Testzweck: Auch ein Startformular haelt das Loeschen auf. Waere es weg, liesse sich der
    // Workflow nicht mehr starten — obwohl keine einzige Aufgabe auf das Formular zeigt.
    [Test]
    public async Task DeleteFormMetadata_ShouldRefuse_WhenOnlyAStartEventUsesTheForm()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Antrag" });
        storage.FormStorageSeed.Forms.Add(new Form
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = new Model.Version(1, 0),
            FormData = "{}"
        });

        SeedDeployedWorkflowWithStartForm(storage, "Antrag");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/form/meta/{formId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormMetaDataDto>>();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("Urlaubsantrag");
        storage.FormStorageSeed.FormMetadatas.Should().ContainSingle();
    }

    // Testzweck: Ein Startereignis im Subprozess ist kein Startformular des Workflows — die
    // Engine liest seinen Form-Key nie. Wuerde es mitzaehlen, sperrte ein wirkungsloser
    // Verweis das Formular dauerhaft gegen das Loeschen.
    [Test]
    public async Task DeleteFormMetadata_ShouldAllow_WhenOnlyAStartEventInsideASubProcessReferencesTheForm()
    {
        var storage = TestStorage.Create();
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Antrag" });

        SeedDeployedWorkflowWithSubProcessStartForm(storage, "Antrag");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/form/meta/{formId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.FormStorageSeed.FormMetadatas.Should().BeEmpty();
    }

    /// <summary>Ein deployter Workflow, dessen Subprozess-Startereignis das Formular nennt.</summary>
    private static void SeedDeployedWorkflowWithSubProcessStartForm(TestStorage storage, string formKey)
    {
        var definitionId = Guid.NewGuid();
        storage.DefinitionStorageSeed.Metas.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = "urlaub",
            Name = "Urlaubsantrag"
        });
        storage.DefinitionStorageSeed.Deployed["urlaub"] = new BpmnDefinition
        {
            Id = definitionId,
            DefinitionId = "urlaub",
            Hash = "egal",
            SavedByUser = Guid.NewGuid(),
            IsActive = true,
            Version = new Model.Version(1, 0)
        };
        storage.DefinitionStorageSeed.Binaries[definitionId] = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                              id="urlaub" targetNamespace="http://bpmn.io/schema/bpmn">
              <bpmn:process id="Process_Urlaub" isExecutable="true">
                <bpmn:startEvent id="Start"><bpmn:outgoing>F1</bpmn:outgoing></bpmn:startEvent>
                <bpmn:sequenceFlow id="F1" sourceRef="Start" targetRef="Sub" />
                <bpmn:subProcess id="Sub">
                  <bpmn:incoming>F1</bpmn:incoming>
                  <bpmn:startEvent id="SubStart">
                    <bpmn:extensionElements>
                      <zeebe:formDefinition formKey="{formKey}" />
                    </bpmn:extensionElements>
                  </bpmn:startEvent>
                </bpmn:subProcess>
              </bpmn:process>
            </bpmn:definitions>
            """;
    }

    /// <summary>Legt einen deployten Workflow ab, dessen Startereignis das Formular benutzt.</summary>
    private static void SeedDeployedWorkflowWithStartForm(TestStorage storage, string formKey)
    {
        var definitionId = Guid.NewGuid();
        storage.DefinitionStorageSeed.Metas.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = "urlaub",
            Name = "Urlaubsantrag"
        });
        storage.DefinitionStorageSeed.Deployed["urlaub"] = new BpmnDefinition
        {
            Id = definitionId,
            DefinitionId = "urlaub",
            Hash = "egal",
            SavedByUser = Guid.NewGuid(),
            IsActive = true,
            Version = new Model.Version(1, 0)
        };
        storage.DefinitionStorageSeed.Binaries[definitionId] = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                              id="urlaub" targetNamespace="http://bpmn.io/schema/bpmn">
              <bpmn:process id="Process_Urlaub" isExecutable="true">
                <bpmn:startEvent id="Start">
                  <bpmn:extensionElements>
                    <zeebe:formDefinition formKey="{formKey}" />
                  </bpmn:extensionElements>
                  <bpmn:outgoing>F1</bpmn:outgoing>
                </bpmn:startEvent>
                <bpmn:sequenceFlow id="F1" sourceRef="Start" targetRef="End" />
                <bpmn:endEvent id="End"><bpmn:incoming>F1</bpmn:incoming></bpmn:endEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;
    }

    /// <summary>Legt einen deployten Workflow ab, dessen einzige Aufgabe das Formular benutzt.</summary>
    private static void SeedDeployedWorkflowUsingForm(TestStorage storage, string formKey, bool inSubProcess = false)
    {
        var definitionId = Guid.NewGuid();
        storage.DefinitionStorageSeed.Metas.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = "urlaub",
            Name = "Urlaubsantrag"
        });
        storage.DefinitionStorageSeed.Deployed["urlaub"] = new BpmnDefinition
        {
            Id = definitionId,
            DefinitionId = "urlaub",
            Hash = "egal",
            SavedByUser = Guid.NewGuid(),
            IsActive = true,
            Version = new Model.Version(1, 0)
        };
        storage.DefinitionStorageSeed.Binaries[definitionId] = BuildWorkflowXml(formKey, inSubProcess);
    }

    /// <summary>
    /// Ein Modell mit einer Aufgabe, wahlweise direkt im Prozess oder in einem Subprozess.
    /// Der Unterschied ist der Punkt: Eine Aufgabe im Subprozess steht nicht in den
    /// Flow-Elementen des Prozesses.
    /// </summary>
    private static string BuildWorkflowXml(string formKey, bool inSubProcess)
    {
        var aufgabe = $"""
                <bpmn:userTask id="Task" name="Freigeben">
                  <bpmn:extensionElements>
                    <zeebe:formDefinition formKey="{formKey}" />
                  </bpmn:extensionElements>
                  <bpmn:incoming>F1</bpmn:incoming>
                  <bpmn:outgoing>F2</bpmn:outgoing>
                </bpmn:userTask>
                <bpmn:sequenceFlow id="F2" sourceRef="Task" targetRef="End" />
                <bpmn:endEvent id="End"><bpmn:incoming>F2</bpmn:incoming></bpmn:endEvent>
        """;

        var koerper = inSubProcess
            ? $"""
                <bpmn:subProcess id="Sub">
                  <bpmn:startEvent id="SubStart"><bpmn:outgoing>F1</bpmn:outgoing></bpmn:startEvent>
                  <bpmn:sequenceFlow id="F1" sourceRef="SubStart" targetRef="Task" />
            {aufgabe}
                </bpmn:subProcess>
        """
            : $"""
                <bpmn:startEvent id="Start"><bpmn:outgoing>F1</bpmn:outgoing></bpmn:startEvent>
                <bpmn:sequenceFlow id="F1" sourceRef="Start" targetRef="Task" />
            {aufgabe}
        """;

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                              id="urlaub" targetNamespace="http://bpmn.io/schema/bpmn">
              <bpmn:process id="Process_Urlaub" isExecutable="true">
            {koerper}
              </bpmn:process>
            </bpmn:definitions>
            """;
    }

    private sealed class TestWebApplicationFactory(TestStorage storage) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TimerScheduler:Enabled"] = "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStorageSystem>();
                services.RemoveAll<ITransactionalStorageProvider>();

                services.AddSingleton<IStorageSystem>(storage);
                services.AddSingleton<ITransactionalStorageProvider>(new TestTransactionalStorageProvider(storage));
            });
        }
    }

    private sealed class TestTransactionalStorageProvider(TestStorage storage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage()
        {
            return storage;
        }
    }

    private sealed class TestStorage(TestFormStorage formStorage) : ITransactionalStorage
    {
        public static TestStorage Create()
        {
            return new TestStorage(new TestFormStorage());
        }

        public TestFormStorage FormStorageSeed { get; } = formStorage;
        public NoOpDefinitionStorage DefinitionStorageSeed { get; } = new();
        public IDefinitionStorage DefinitionStorage => DefinitionStorageSeed;
        public IFolderStorage FolderStorage { get; } = new InMemoryFolderStorage();
        public IMessageSubscriptionStorage SubscriptionStorage { get; } = new NoOpMessageSubscriptionStorage();
        public NoOpInstanceStorage InstanceStorageSeed { get; } = new();
        public IInstanceStorage InstanceStorage => InstanceStorageSeed;
        public IFormStorage FormStorage { get; } = formStorage;
        public InMemoryFormAuthoringStorage FormAuthoringStorageSeed { get; } = new(formStorage);
        public IFormAuthoringStorage FormAuthoringStorage => FormAuthoringStorageSeed;
        public IServiceTaskStorage ServiceTaskStorage { get; } = new InMemoryServiceTaskStorage();

        public void CommitChanges()
        {
        }

        public void RollbackTransaction()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class InMemoryFormAuthoringStorage(TestFormStorage forms) : IFormAuthoringStorage
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<Guid, FormAuthoringDraft> _drafts = [];

        public async Task<FormAuthoringDraft?> Get(Guid formId)
        {
            await _gate.WaitAsync();
            try { return _drafts.GetValueOrDefault(formId); }
            finally { _gate.Release(); }
        }

        public async Task<FormAuthoringWriteResult> TrySave(FormAuthoringDraft draft, long expectedRevision)
        {
            await _gate.WaitAsync();
            try
            {
                if (forms.FormMetadatas.All(metadata => metadata.FormId != draft.FormId))
                    return new FormAuthoringWriteResult(FormAuthoringWriteStatus.FormNotFound, null, 0);
                var current = _drafts.GetValueOrDefault(draft.FormId);
                if ((current?.Revision ?? 0) != expectedRevision)
                    return new FormAuthoringWriteResult(
                        FormAuthoringWriteStatus.RevisionConflict, current, current?.Revision ?? 0);
                _drafts[draft.FormId] = draft;
                return new FormAuthoringWriteResult(FormAuthoringWriteStatus.Written, draft, draft.Revision);
            }
            finally { _gate.Release(); }
        }

        public async Task<FormAuthoringDeleteResult> TryDelete(Guid formId, long expectedRevision)
        {
            await _gate.WaitAsync();
            try
            {
                if (forms.FormMetadatas.All(metadata => metadata.FormId != formId))
                    return new FormAuthoringDeleteResult(FormAuthoringDeleteStatus.FormNotFound, 0);
                var current = _drafts.GetValueOrDefault(formId);
                if ((current?.Revision ?? 0) != expectedRevision)
                    return new FormAuthoringDeleteResult(
                        FormAuthoringDeleteStatus.RevisionConflict, current?.Revision ?? 0);
                _drafts.Remove(formId);
                return new FormAuthoringDeleteResult(FormAuthoringDeleteStatus.Deleted, 0);
            }
            finally { _gate.Release(); }
        }

        public async Task<FormAuthoringPublishResult> TryPublish(Guid formId, long expectedRevision, Guid publishedFormId)
        {
            await _gate.WaitAsync();
            try
            {
                if (forms.FormMetadatas.All(metadata => metadata.FormId != formId))
                    return new FormAuthoringPublishResult(FormAuthoringPublishStatus.FormNotFound, null, 0);
                var draft = _drafts.GetValueOrDefault(formId);
                if (draft?.Revision != expectedRevision)
                    return new FormAuthoringPublishResult(
                        FormAuthoringPublishStatus.RevisionConflict, null, draft?.Revision ?? 0);
                var current = forms.Forms.Where(form => form.FormId == formId)
                    .OrderByDescending(form => form.Version).FirstOrDefault();
                var published = new Form
                {
                    Id = publishedFormId,
                    FormId = formId,
                    Version = (current?.Version ?? new Model.Version()) + 1,
                    FormData = draft.FormData
                };
                forms.Forms.Add(published);
                _drafts.Remove(formId);
                return new FormAuthoringPublishResult(FormAuthoringPublishStatus.Published, published, 0);
            }
            finally { _gate.Release(); }
        }
    }

    private static Guid SeedFormMetadata(TestStorage storage)
    {
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Freigabe" });
        return formId;
    }

    private static HttpClient CreateAuthorClient(TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Flowzer-UserId", Guid.NewGuid().ToString());
        return client;
    }

    private sealed class TestFormStorage : IFormStorage
    {
        public List<FormMetadata> FormMetadatas { get; } = [];
        public List<Form> Forms { get; } = [];

        public Task SaveFormMetaData(FormMetadata formMetadata)
        {
            var existing = FormMetadatas.SingleOrDefault(metadata => metadata.FormId == formMetadata.FormId);
            if (existing == null)
            {
                FormMetadatas.Add(formMetadata);
            }
            else
            {
                existing.Name = formMetadata.Name;
            }

            return Task.CompletedTask;
        }

        public Task<FormMetadata> GetFormMetaData(Guid formId)
        {
            var metadata = FormMetadatas.Single(metadata => metadata.FormId == formId);
            return Task.FromResult(metadata);
        }

        public Task<IEnumerable<FormMetadata>> GetFormMetadatas()
        {
            return Task.FromResult(FormMetadatas.AsEnumerable());
        }

        public Task UpdateFormMetaData(FormMetadata formMetaData)
        {
            return SaveFormMetaData(formMetaData);
        }

        public Task DeleteFormMetaData(Guid formId)
        {
            FormMetadatas.RemoveAll(metadata => metadata.FormId == formId);
            Forms.RemoveAll(form => form.FormId == formId);
            return Task.CompletedTask;
        }

        public Task SaveForm(Form form)
        {
            Forms.Add(form);
            return Task.CompletedTask;
        }

        public Task<Form> GetForm(Guid id)
        {
            return Task.FromResult(Forms.Single(form => form.Id == id));
        }

        public Task<IEnumerable<Form>> GetForms(Guid formId)
        {
            return Task.FromResult(Forms.Where(form => form.FormId == formId).AsEnumerable());
        }

        public Task DeleteForm(Guid id)
        {
            Forms.RemoveAll(form => form.Id == id);
            return Task.CompletedTask;
        }

        public Task<Model.Version> GetMaxVersion(Guid formId)
        {
            var version = Forms
                .Where(form => form.FormId == formId)
                .OrderByDescending(form => form.Version)
                .Select(form => form.Version)
                .FirstOrDefault() ?? new Model.Version();

            return Task.FromResult(version);
        }
    }

    private sealed class NoOpDefinitionStorage : IDefinitionStorage
    {
        public List<ExtendedBpmnMetaDefinition> Metas { get; } = [];
        public Dictionary<string, BpmnDefinition> Deployed { get; } = [];
        public Dictionary<Guid, string> Binaries { get; } = [];

        public Task<string> GetBinary(Guid guid) => Task.FromResult(Binaries[guid]);
        public Task<Guid[]> GetAllBinaryDefinitions() => Task.FromResult(Array.Empty<Guid>());
        public Task<BpmnDefinition[]> GetAllDefinitions() => Task.FromResult(Array.Empty<BpmnDefinition>());
        public Task StoreDefinition(BpmnDefinition definition) => Task.CompletedTask;
        public Task StoreBinary(Guid guid, string data) => Task.CompletedTask;
        public Task<Model.Version?> GetMaxVersionId(string modelId) => Task.FromResult<Model.Version?>(null);
        public Task<BpmnDefinition> GetDefinitionById(Guid id) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetLatestDefinition(string definitionId) => throw new NotSupportedException();
        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId) =>
            Task.FromResult(Deployed.GetValueOrDefault(definitionDefinitionId));
        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => Task.FromResult(Metas.ToArray());
        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => throw new NotSupportedException();
    }

    private sealed class NoOpMessageSubscriptionStorage : IMessageSubscriptionStorage
    {
        public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? messageInstanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task AddMessageSubscription(MessageSubscription messageSubscription) => Task.CompletedTask;
        public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId) => Task.CompletedTask;
        public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public void AddSignalSubscription(SignalSubscription signalSubscription) { }
        public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<SignalSubscription>());
        public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId) { }
        public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) => Task.FromResult(Enumerable.Empty<UserTaskSubscription>());
        public Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId) => Task.FromResult(Enumerable.Empty<ExtendedUserTaskSubscription>());
        public Task AddUserTaskSubscription(UserTaskSubscription userTasks) => Task.CompletedTask;
        public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) => Task.CompletedTask;
        public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId) { }
        public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task AddTimerSubscription(TimerSubscription timerSubscription) => Task.CompletedTask;
        public Task RemoveTimerSubscription(Guid timerSubscriptionId) => Task.CompletedTask;
        public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
    }

    private sealed class NoOpInstanceStorage : IInstanceStorage
    {
        public List<ProcessInstanceInfo> Active { get; } = [];

        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId) => throw new NotSupportedException();
        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstance) => Task.CompletedTask;
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() => Task.FromResult(Active.AsEnumerable());
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() => Task.FromResult(Active.AsEnumerable());
    }
}
