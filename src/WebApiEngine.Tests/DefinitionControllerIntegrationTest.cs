using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BPMN.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using StorageSystem;
using StorageSystem.Exceptions;
using WebApiEngine.Auth;
using WebApiEngine.Ai;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

[NonParallelizable]
public class DefinitionControllerIntegrationTest
{
    // Testzweck: Ein vollständig gebundener KI-Vertrag ist in Autoren- und Deployment-Prüfung
    // identisch ausführbar und speichert beim Deploy den unveraenderlichen Verbindungssnapshot.
    [Test]
    public async Task AiTask_ShouldBeAuthorableAndDeployableWithRuntimeBinding()
    {
        var storage = TestStorage.Create();
        storage.AiConnections.Items.Add(ReadyAiConnection());
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = "workflow-ai-draft",
            Name = "AI workflow"
        });
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();
        var xml = CreateAiTaskXml("workflow-ai-draft");

        using var authoringValidation = await client.PostAsync(
            "/definition/validate",
            new StringContent(xml, Encoding.UTF8, "application/xml"));
        using var save = await client.PostAsync(
            "/definition",
            new StringContent(xml, Encoding.UTF8, "application/xml"));
        using var deploymentValidation = await client.PostAsync(
            "/definition/validate/deployment",
            new StringContent(xml, Encoding.UTF8, "application/xml"));
        using var deploy = await client.PostAsync(
            "/definition/deploy",
            new StringContent(xml, Encoding.UTF8, "application/xml"));

        authoringValidation.StatusCode.Should().Be(HttpStatusCode.OK);
        save.StatusCode.Should().Be(HttpStatusCode.OK);
        deploymentValidation.StatusCode.Should().Be(HttpStatusCode.OK);
        deploy.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.DefinitionStorageSeed.Definitions.Should().HaveCount(2);
        storage.DefinitionStorageSeed.Definitions.Single(definition => definition.IsActive)
            .AiTaskBindings!["Ai_1"]
            .Should().Be(new BoundAiTask(ReadyAiConnection().Id, 1, "model-a"));
    }

    // Testzweck: Ein Werkzeug darf nur aus der serverseitigen Registry und der Allowlist der
    // Verbindung modelliert werden. Solange die Werkzeug-Runtime fehlt, bleibt der Entwurf
    // speicherbar, der Deploy wird jedoch mit einem stabilen Fähigkeitsfehler verhindert.
    [Test]
    public async Task AiToolTask_ShouldBeAuthorableButNotDeployableBeforeRuntimeExists()
    {
        const string definitionId = "workflow-ai-tool-draft";
        var storage = TestStorage.Create();
        storage.AiConnections.Items.Add(ReadyAiConnection() with
        {
            AllowedTools = [new AiToolPermission("flowzer.directory.lookup", 1, false)]
        });
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "AI tool workflow"
        });
        await using var factory = new TestWebApplicationFactory(storage, new DirectoryLookupTool());
        using var client = factory.CreateClient();
        var xml = CreateAiTaskXml(definitionId).Replace(
            "</flowzer:aiTask>",
            "<flowzer:tool id=\"flowzer.directory.lookup\" version=\"1\" approval=\"automatic\" /></flowzer:aiTask>",
            StringComparison.Ordinal);

        using var authoringValidation = await client.PostAsync(
            "/definition/validate",
            new StringContent(xml, Encoding.UTF8, "application/xml"));
        using var save = await client.PostAsync(
            "/definition",
            new StringContent(xml, Encoding.UTF8, "application/xml"));
        using var deploymentValidation = await client.PostAsync(
            "/definition/validate/deployment",
            new StringContent(xml, Encoding.UTF8, "application/xml"));
        using var deploy = await client.PostAsync(
            "/definition/deploy",
            new StringContent(xml, Encoding.UTF8, "application/xml"));

        authoringValidation.StatusCode.Should().Be(HttpStatusCode.OK);
        save.StatusCode.Should().Be(HttpStatusCode.OK);
        deploymentValidation.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        deploy.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await deploy.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("issues")[0].GetProperty("code").GetString()
            .Should().Be("bpmn.ai_task.tools_runtime_unavailable");
        storage.DefinitionStorageSeed.Definitions.Should().ContainSingle(definition => !definition.IsActive);
    }

    // Testzweck: Auch die Autorenprüfung bindet die stabile Verbindungs-ID serverseitig;
    // manipuliertes XML mit einer unbekannten ID darf keine Definition anlegen.
    [Test]
    public async Task UploadAiTask_ShouldRejectUnknownConnection()
    {
        var storage = TestStorage.Create();
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/definition",
            new StringContent(CreateAiTaskXml("workflow-ai-unknown"), Encoding.UTF8, "application/xml"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("issues")[0].GetProperty("code").GetString()
            .Should().Be("bpmn.ai_task.connection_not_found");
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
    }

    // Testzweck: Prüft, dass für eine deployte Plain-Start-Definition direkt über die API eine Instanz gestartet und persistiert werden kann.
    [Test]
    public async Task StartInstance_ShouldCreatePersistedInstance_ForDeployedPlainStartDefinition()
    {
        const string definitionId = "workflow-ui-start";
        var deployedDefinitionGuid = Guid.NewGuid();
        var storage = TestStorage.Create();
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "Workflow UI Start"
        });
        storage.DefinitionStorageSeed.Definitions.Add(new BpmnDefinition
        {
            Id = deployedDefinitionGuid,
            DefinitionId = definitionId,
            Hash = "hash-start",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0),
            IsActive = true,
            DeployedOn = DateTime.UtcNow
        });
        storage.DefinitionStorageSeed.Binaries[deployedDefinitionGuid] = CreatePlainStartXml(definitionId);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/definition/meta/{definitionId}/instance", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().NotBeNull();
        payload.Result!.RelatedDefinitionId.Should().Be(definitionId);
        payload.Result.RelatedDefinitionName.Should().Be("Workflow UI Start");
        payload.Result.State.Should().Be(ProcessInstanceStateDto.Completed);
        storage.InstanceStorageSeed.Instances.Should().ContainKey(payload.Result.InstanceId);
    }

    // Testzweck: Prüft, dass UI-Direktstarts für rein nachrichtengetriebene Prozesse kontrolliert mit BadRequest abgelehnt werden.
    [Test]
    public async Task StartInstance_ShouldReturnBadRequest_WhenDefinitionHasNoPlainStartEvent()
    {
        const string definitionId = "workflow-message-start-only";
        var deployedDefinitionGuid = Guid.NewGuid();
        var storage = TestStorage.Create();
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "Message only"
        });
        storage.DefinitionStorageSeed.Definitions.Add(new BpmnDefinition
        {
            Id = deployedDefinitionGuid,
            DefinitionId = definitionId,
            Hash = "hash-message",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0),
            IsActive = true,
            DeployedOn = DateTime.UtcNow
        });
        storage.DefinitionStorageSeed.Binaries[deployedDefinitionGuid] = CreateMessageStartXml(definitionId);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/definition/meta/{definitionId}/instance", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("cannot be started directly from the UI");
    }

    // Testzweck: Prüft, dass ein Deploy ohne zugehörige Meta-Definition kontrolliert abgelehnt wird,
    // statt verwaiste Definitionen zu hinterlassen, die später die Instanzliste zerstören.
    [Test]
    public async Task DeployDefinition_ShouldReturnBadRequest_WhenMetaDefinitionIsMissing()
    {
        const string definitionId = "workflow-deploy-without-meta";
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/definition/deploy",
            new StringContent(CreatePlainStartXml(definitionId), Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("No meta definition found");
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
        storage.DefinitionStorageSeed.Binaries.Should().BeEmpty();
    }

    // Testzweck: Ein nicht ausführbarer, aber parsebarer BPMN-Task wird schon beim Speichern mit dem gemeinsamen Problem-Details-Vertrag abgelehnt.
    [Test]
    public async Task UploadDefinition_ShouldReturnCapabilityIssue_WhenModelContainsNonExecutableElement()
    {
        var storage = TestStorage.Create();
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/definition",
            new StringContent(CreateUnsupportedScriptTaskXml("workflow-invalid-upload"), Encoding.UTF8, "application/xml"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("successful").GetBoolean().Should().BeFalse();
        problem.RootElement.TryGetProperty("errorMessage", out _).Should().BeTrue();
        problem.RootElement.TryGetProperty("errors", out _).Should().BeTrue();
        problem.RootElement.GetProperty("code").GetString().Should().Be("bpmn.model.invalid");
        var issue = problem.RootElement.GetProperty("issues")[0];
        issue.GetProperty("code").GetString().Should().Be("bpmn.element.not_executable");
        issue.GetProperty("elementId").GetString().Should().Be("Script_1");
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
    }

    // Testzweck: Der HTTP-Fehler verweist bei fehlender Service-Implementierung direkt auf die betroffene Modelleigenschaft.
    [Test]
    public async Task UploadDefinition_ShouldExposePropertyPath_ForIncompleteFlowzerConfiguration()
    {
        var storage = TestStorage.Create();
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/definition",
            new StringContent(CreateServiceTaskWithoutTypeXml("workflow-invalid-service"), Encoding.UTF8, "application/xml"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var issue = problem.RootElement.GetProperty("issues")[0];
        issue.GetProperty("code").GetString().Should().Be("bpmn.service_task.implementation_required");
        issue.GetProperty("propertyPath").GetString().Should().Be("extensionElements.taskDefinition.type");
    }

    // Testzweck: Fehlerhaftes XML bleibt ein strukturierter, wertefreier Modellfehler
    // und darf weder als 500 noch mit Parserfragmenten an den Client gelangen.
    [Test]
    public async Task UploadDefinition_ShouldReturnStableCapabilityIssue_WhenXmlIsMalformed()
    {
        var storage = TestStorage.Create();
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/definition",
            new StringContent("<bpmn:definitions>", Encoding.UTF8, "application/xml"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("bpmn.model.invalid");
        var issue = problem.RootElement.GetProperty("issues")[0];
        issue.GetProperty("code").GetString().Should().Be("bpmn.xml.invalid");
        issue.GetProperty("message").GetString().Should().Be("The BPMN XML document is invalid.");
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
    }

    // Testzweck: Der Deploy degradiert den gemeinsamen BPMN-Validierungsfehler nicht zu einem unstrukturierten 400-String.
    [Test]
    public async Task DeployDefinition_ShouldReturnCapabilityIssue_WhenModelContainsNonExecutableElement()
    {
        const string definitionId = "workflow-invalid-deploy";
        var storage = TestStorage.Create();
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "Invalid deploy"
        });
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/definition/deploy",
            new StringContent(CreateUnsupportedScriptTaskXml(definitionId), Encoding.UTF8, "application/xml"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("bpmn.model.invalid");
        problem.RootElement.GetProperty("issues")[0].GetProperty("elementId").GetString().Should().Be("Script_1");
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
    }

    // Testzweck: Die Vorabprüfung meldet dieselbe Knoten-ID wie Save und Deploy, ohne eine Definitionsversion anzulegen.
    [Test]
    public async Task ValidateDefinition_ShouldReturnCapabilityIssueWithoutPersistingVersion()
    {
        var storage = TestStorage.Create();
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/definition/validate",
            new StringContent(CreateUnsupportedScriptTaskXml("workflow-invalid-validate"), Encoding.UTF8, "application/xml"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("issues")[0].GetProperty("elementId").GetString().Should().Be("Script_1");
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
    }

    // Testzweck: Der Katalog-Endpunkt liefert exakt die zentrale, versionierte Fähigkeitsmatrix für hostneutrale Modellieransichten.
    [Test]
    public async Task GetCapabilities_ShouldReturnVersionedCapabilityContract()
    {
        var storage = TestStorage.Create();
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/definition/capabilities");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("result").GetProperty("contractVersion").GetString().Should().Be("3");
        payload.RootElement.GetProperty("result").GetProperty("elements").EnumerateArray()
            .Should().Contain(element => element.GetProperty("elementType").GetString() == "manualTask"
                && !element.GetProperty("executable").GetBoolean());
    }

    // Testzweck: Prüft, dass ein fehlgeschlagener Deploy-Versuch keine halb persistierte Definitionsversion zurücklässt.
    [Test]
    public async Task DeployDefinition_ShouldCleanupStoredVersion_WhenSubscriptionSetupFails()
    {
        const string definitionId = "workflow-deploy-cleanup";
        var storage = TestStorage.Create(throwOnMessageSubscriptionAdd: true);
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "Deploy cleanup"
        });

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/definition/deploy",
            new StringContent(CreateMessageStartXml(definitionId), Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("message subscriptions");
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
        storage.DefinitionStorageSeed.Binaries.Should().BeEmpty();
    }

    // Testzweck: Prüft, dass ein neuer Workflow unter dem übergebenen Namen im Katalog landet —
    // vorher hieß jeder neue Eintrag „New Definition" und musste hinterher umbenannt werden.
    [Test]
    public async Task NewDefinition_ShouldStoreTheGivenName()
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/definition/new?name=Urlaubsantrag", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnMetaDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result!.Name.Should().Be("Urlaubsantrag");
        storage.DefinitionStorageSeed.MetaDefinitions.Should().ContainSingle()
            .Which.Name.Should().Be("Urlaubsantrag");
        // Zum Katalogeintrag gehört von Anfang an eine leere Version, sonst ließe sich der
        // Workflow nicht öffnen.
        storage.DefinitionStorageSeed.Definitions.Should().ContainSingle();
    }

    // Testzweck: Prüft, dass ein Name ohne Inhalt nicht zu einem namenlosen Katalogeintrag führt.
    [Test]
    public async Task NewDefinition_ShouldFallBackToADefaultName_WhenNameIsBlank()
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/definition/new?name=%20%20", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnMetaDefinitionDto>>();
        payload!.Result!.Name.Should().Be("Neuer Workflow");
    }

    // Testzweck: Prüft, dass das Löschen den Katalogeintrag, alle Versionen und deren XML entfernt.
    // Ein zurückbleibendes XML wäre weiterhin über /definition/xml abrufbar.
    [Test]
    public async Task MetaDelete_ShouldRemoveCatalogEntryVersionsAndBinaries()
    {
        const string definitionId = "workflow-to-delete";
        var versionGuid = Guid.NewGuid();
        var storage = TestStorage.Create();
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "Wegwerfprozess"
        });
        storage.DefinitionStorageSeed.Definitions.Add(new BpmnDefinition
        {
            Id = versionGuid,
            DefinitionId = definitionId,
            Hash = "hash-delete",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0),
            IsActive = true
        });
        storage.DefinitionStorageSeed.Binaries[versionGuid] = CreatePlainStartXml(definitionId);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/definition/meta/{definitionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnMetaDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result!.DefinitionId.Should().Be(definitionId);
        storage.DefinitionStorageSeed.MetaDefinitions.Should().BeEmpty();
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
        storage.DefinitionStorageSeed.Binaries.Should().BeEmpty();
    }

    // Testzweck: Prüft, dass beendete Instanzen mit dem Workflow verschwinden. Bleiben sie
    // liegen, stehen sie ohne Definition in der Instanzliste — ohne Namen und ohne Diagramm.
    [Test]
    public async Task MetaDelete_ShouldRemoveFinishedInstancesOfTheDefinition()
    {
        const string definitionId = "workflow-with-finished-instance";
        var storage = TestStorage.Create();
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "Fertig"
        });

        var finishedId = Guid.NewGuid();
        storage.InstanceStorageSeed.Instances[finishedId] = CreateInstanceInfo(finishedId, definitionId, finished: true);

        // Eine fremde Instanz darf unangetastet bleiben.
        var foreignId = Guid.NewGuid();
        storage.InstanceStorageSeed.Instances[foreignId] = CreateInstanceInfo(foreignId, "anderer-workflow", finished: true);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/definition/meta/{definitionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.InstanceStorageSeed.Instances.Should().NotContainKey(finishedId);
        storage.InstanceStorageSeed.Instances.Should().ContainKey(foreignId);
    }

    // Testzweck: Prüft, dass laufende Instanzen das Löschen aufhalten. Ohne ihre Definition
    // könnte die Engine sie nicht mehr fortsetzen.
    [Test]
    public async Task MetaDelete_ShouldReturnConflict_WhenInstancesAreStillRunning()
    {
        const string definitionId = "workflow-with-running-instance";
        var storage = TestStorage.Create();
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "Laeuft noch"
        });
        var instanceId = Guid.NewGuid();
        storage.InstanceStorageSeed.Instances[instanceId] = CreateInstanceInfo(instanceId, definitionId, finished: false);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/definition/meta/{definitionId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnMetaDefinitionDto>>();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("laufende Instanz");
        storage.DefinitionStorageSeed.MetaDefinitions.Should().ContainSingle();
    }

    // Testzweck: Prüft, dass ein unbekannter Workflow mit 404 beantwortet wird und nicht als
    // erfolgreiches Löschen durchgeht — die Oberfläche zeigte sonst einen Erfolg ohne Wirkung.
    [Test]
    public async Task MetaDelete_ShouldReturnNotFound_ForUnknownDefinition()
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/definition/meta/gibt-es-nicht");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnMetaDefinitionDto>>();
        payload!.Successful.Should().BeFalse();
    }

    // Testzweck: Prüft, dass ein Katalogeintrag mit Pfadanteilen in der Kennung abgelehnt wird und
    // nichts gespeichert bleibt — die Kennung wird im Dateisystem-Storage zum Dateinamen.
    [TestCase("../x")]
    [TestCase("a/b")]
    [TestCase("a\\b")]
    [TestCase("..")]
    [TestCase("   ")]
    public async Task MetaPost_ShouldRejectDefinitionIdWithPathCharacters(string definitionId)
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/definition/meta",
            new { definitionId, name = "Katalogeintrag" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnMetaDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("is not a valid definition id");
        storage.DefinitionStorageSeed.MetaDefinitions.Should().BeEmpty();
    }

    // Testzweck: Prüft, dass auch das Ändern eines Katalogeintrags eine Kennung mit Pfadanteilen
    // ablehnt — sonst ließe sich die Prüfung beim Anlegen mit einem PUT umgehen.
    [TestCase("../x")]
    [TestCase("a/b")]
    public async Task MetaPut_ShouldRejectDefinitionIdWithPathCharacters(string definitionId)
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/definition/meta",
            new { definitionId, name = "Katalogeintrag" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnMetaDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("is not a valid definition id");
        storage.DefinitionStorageSeed.MetaDefinitions.Should().BeEmpty();
    }

    // Testzweck: Prüft, dass die heute üblichen Kennungen weiterhin angenommen werden — die Prüfung
    // darf vorhandene Kataloge und die Beispiele nicht brechen.
    [TestCase("flowzer-urlaubsantrag")]
    [TestCase("Definitions_0abc")]
    [TestCase("Process_1")]
    [TestCase("definition_3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public async Task MetaPost_ShouldAcceptEstablishedDefinitionIds(string definitionId)
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/definition/meta",
            new { definitionId, name = "Katalogeintrag" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnMetaDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        storage.DefinitionStorageSeed.MetaDefinitions.Should().ContainSingle()
            .Which.DefinitionId.Should().Be(definitionId);
    }

    // Testzweck: Prüft, dass eine Kennung mit Pfadanteilen auch aus dem hochgeladenen BPMN-XML
    // abgelehnt wird und keine Version anlegt.
    [Test]
    public async Task UploadDefinition_ShouldRejectDefinitionIdWithPathCharactersFromXml()
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/definition",
            new StringContent(CreateXmlWithCatalogId("../x"), Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("is not a valid definition id");
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
        storage.DefinitionStorageSeed.Binaries.Should().BeEmpty();
    }

    // Testzweck: Prüft, dass auch der Deploy eine Kennung mit Pfadanteilen ablehnt, bevor eine
    // Version entsteht.
    [Test]
    public async Task DeployDefinition_ShouldRejectDefinitionIdWithPathCharactersFromXml()
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/definition/deploy",
            new StringContent(CreateXmlWithCatalogId("../x"), Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("is not a valid definition id");
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
        storage.DefinitionStorageSeed.Binaries.Should().BeEmpty();
    }

    // Testzweck: Prüft, dass ein Upload mit einer üblichen Kennung weiterhin durchgeht — die
    // Prüfung darf den Normalfall nicht treffen.
    [Test]
    public async Task UploadDefinition_ShouldAcceptEstablishedDefinitionIdFromXml()
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/definition",
            new StringContent(CreateXmlWithCatalogId("flowzer-urlaubsantrag"), Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        storage.DefinitionStorageSeed.Definitions.Should().ContainSingle()
            .Which.DefinitionId.Should().Be("flowzer-urlaubsantrag");
    }

    private static ProcessInstanceInfo CreateInstanceInfo(Guid instanceId, string metaDefinitionId, bool finished) =>
        new()
        {
            InstanceId = instanceId,
            metaDefinitionId = metaDefinitionId,
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_1",
            Tokens = [],
            IsFinished = finished,
            State = finished ? ProcessInstanceState.Completed : ProcessInstanceState.Running,
            MessageSubscriptionCount = 0,
            SignalSubscriptionCount = 0,
            UserTaskSubscriptionCount = 0,
            ServiceSubscriptionCount = 0
        };

    private sealed class TestWebApplicationFactory(TestStorage storage, IAiTool? aiTool = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
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
                services.RemoveAll<ICurrentUserContextAccessor>();
                services.RemoveAll<IAiSecretStore>();

                services.AddSingleton<IStorageSystem>(storage);
                services.AddSingleton<ITransactionalStorageProvider>(new TestTransactionalStorageProvider(storage));
                services.AddSingleton<ICurrentUserContextAccessor>(new StubCurrentUserContextAccessor());
                services.AddSingleton<IAiSecretStore>(new ReadySecretStore());
                if (aiTool is not null) services.AddSingleton(aiTool);
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

    private sealed class StubCurrentUserContextAccessor : ICurrentUserContextAccessor
    {
        public CurrentUserContext GetCurrentUser()
        {
            return new CurrentUserContext(Guid.Parse("9AB0E5C5-A5B4-4F87-A857-EB821D12AF6E"), "test", false);
        }
    }

    private sealed class TestStorage : ITransactionalStorage
    {
        private readonly TestDefinitionStorage _definitionStorage;
        private readonly TestSubscriptionStorage _subscriptionStorage;
        private readonly TestInstanceStorage _instanceStorage;

        private TestStorage(
            TestDefinitionStorage definitionStorage,
            TestSubscriptionStorage subscriptionStorage,
            TestInstanceStorage instanceStorage)
        {
            _definitionStorage = definitionStorage;
            _subscriptionStorage = subscriptionStorage;
            _instanceStorage = instanceStorage;
            DefinitionStorageSeed = definitionStorage;
            SubscriptionStorageSeed = subscriptionStorage;
            InstanceStorageSeed = instanceStorage;
        }

        public static TestStorage Create(bool throwOnMessageSubscriptionAdd = false)
        {
            var definitionStorage = new TestDefinitionStorage();
            var subscriptionStorage = new TestSubscriptionStorage(throwOnMessageSubscriptionAdd);
            var instanceStorage = new TestInstanceStorage();

            return new TestStorage(definitionStorage, subscriptionStorage, instanceStorage);
        }

        public TestDefinitionStorage DefinitionStorageSeed { get; }
        public TestSubscriptionStorage SubscriptionStorageSeed { get; }
        public TestInstanceStorage InstanceStorageSeed { get; }

        public IDefinitionStorage DefinitionStorage => _definitionStorage;

        public IFolderStorage FolderStorage { get; } = new InMemoryFolderStorage();
        public IMessageSubscriptionStorage SubscriptionStorage => _subscriptionStorage;
        public IInstanceStorage InstanceStorage => _instanceStorage;
        public IFormStorage FormStorage { get; } = new NoOpFormStorage();
        public IServiceTaskStorage ServiceTaskStorage { get; } = new InMemoryServiceTaskStorage();
        public TestAiConnectionStorage AiConnections { get; } = new();
        public IAiConnectionStorage AiConnectionStorage => AiConnections;

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

    private sealed class TestAiConnectionStorage : IAiConnectionStorage
    {
        public List<AiConnection> Items { get; } = [];
        public Task<IReadOnlyList<AiConnection>> List() => Task.FromResult<IReadOnlyList<AiConnection>>(Items);
        public Task<AiConnection?> Get(Guid id) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        public Task<AiConnectionWriteResult> TryCreate(AiConnection connection) => throw new NotSupportedException();
        public Task<AiConnectionWriteResult> TryUpdate(AiConnection connection, long expectedRevision) => throw new NotSupportedException();
    }

    private sealed class ReadySecretStore : IAiSecretStore
    {
        public ValueTask<bool> ExistsAsync(string reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask<AiSecretValue?> ResolveAsync(string reference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Die Autorenprüfung darf Secrets nicht auflösen.");
    }

    private sealed class DirectoryLookupTool : IAiTool
    {
        public AiToolDefinition Definition { get; } = new(
            "flowzer.directory.lookup",
            1,
            "Directory lookup",
            "Reads one bounded directory entry.",
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}",
            AiToolSideEffect.ReadOnly,
            AllowsPreApproval: false);

        public ValueTask<AiToolExecutionResult> ExecuteAsync(
            AiToolExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Die Autorenprüfung darf Werkzeuge nicht ausführen.");
    }

    private static AiConnection ReadyAiConnection() => new(
        Guid.Parse("118adeb6-65a4-4e57-a03b-d3b0a3300ac9"),
        "AI",
        AiProviderKind.OpenAi,
        AiProcessingLocation.Cloud,
        null,
        "model-a",
        "env:FLOWZER_AI_KEY",
        true,
        1,
        DateTimeOffset.UtcNow,
        Guid.NewGuid());

    private static string CreateAiTaskXml(string definitionId) => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                          id="{{definitionId}}">
          <bpmn:process id="Process_1" isExecutable="true">
            <bpmn:startEvent id="Start_1" />
            <bpmn:serviceTask id="Ai_1">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="flowzer.ai.v1" />
                <flowzer:aiTask contractVersion="1"
                    connectionId="118adeb6-65a4-4e57-a03b-d3b0a3300ac9"
                    instructionVersion="1" maxInputTokens="4096" maxOutputTokens="1024" timeoutSeconds="60">
                  <flowzer:instruction>Classify the request.</flowzer:instruction>
                  <flowzer:resultSchema>{"type":"object"}</flowzer:resultSchema>
                </flowzer:aiTask>
                <zeebe:ioMapping>
                  <zeebe:input source="=request" target="request" />
                  <zeebe:output source="=result" target="classification" />
                </zeebe:ioMapping>
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            <bpmn:endEvent id="End_1" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Ai_1" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Ai_1" targetRef="End_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private sealed class TestDefinitionStorage : IDefinitionStorage
    {
        public List<BpmnDefinition> Definitions { get; } = [];
        public Dictionary<Guid, string> Binaries { get; } = [];
        public List<ExtendedBpmnMetaDefinition> MetaDefinitions { get; } = [];

        public Task StoreBinary(Guid guid, string data)
        {
            Binaries[guid] = data;
            return Task.CompletedTask;
        }

        public Task<string> GetBinary(Guid guid)
        {
            return Task.FromResult(Binaries[guid]);
        }

        public Task DeleteBinary(Guid guid)
        {
            Binaries.Remove(guid);
            return Task.CompletedTask;
        }

        public Task<Guid[]> GetAllBinaryDefinitions()
        {
            return Task.FromResult(Binaries.Keys.ToArray());
        }

        public Task<BpmnDefinition[]> GetAllDefinitions()
        {
            return Task.FromResult(Definitions.ToArray());
        }

        public Task StoreDefinition(BpmnDefinition definition)
        {
            Definitions.RemoveAll(existing => existing.Id == definition.Id);
            Definitions.Add(definition);
            return Task.CompletedTask;
        }

        public Task DeleteDefinition(Guid id)
        {
            Definitions.RemoveAll(definition => definition.Id == id);
            return Task.CompletedTask;
        }

        public Task<Model.Version?> GetMaxVersionId(string modelId)
        {
            var versions = Definitions
                .Where(definition => definition.DefinitionId == modelId)
                .Select(definition => definition.Version)
                .ToArray();

            return Task.FromResult<Model.Version?>(versions.Length == 0 ? null : versions.Max());
        }

        public Task<BpmnDefinition> GetDefinitionById(Guid id)
        {
            return Task.FromResult(Definitions.Single(definition => definition.Id == id));
        }

        public Task<BpmnDefinition> GetLatestDefinition(string definitionId)
        {
            return Task.FromResult(Definitions
                .Where(definition => definition.DefinitionId == definitionId)
                .MaxBy(definition => definition.Version)!);
        }

        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId)
        {
            return Task.FromResult(Definitions.SingleOrDefault(definition =>
                definition.DefinitionId == definitionDefinitionId &&
                definition.IsActive));
        }

        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions()
        {
            return Task.FromResult(MetaDefinitions.ToArray());
        }

        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition)
        {
            MetaDefinitions.RemoveAll(existing => existing.DefinitionId == metaDefinition.DefinitionId);
            MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
            {
                DefinitionId = metaDefinition.DefinitionId,
                Name = metaDefinition.Name,
                Description = metaDefinition.Description
            });
            return Task.CompletedTask;
        }

        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition)
        {
            MetaDefinitions.RemoveAll(existing => existing.DefinitionId == metaDefinition.DefinitionId);
            MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
            {
                DefinitionId = metaDefinition.DefinitionId,
                Name = metaDefinition.Name,
                Description = metaDefinition.Description
            });
            return Task.CompletedTask;
        }

        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id)
        {
            var metadata = MetaDefinitions.SingleOrDefault(existing => existing.DefinitionId == id)
                ?? throw new DefinitionStorageNotFoundException($"No meta definition found for definitionId {id}");
            return Task.FromResult<BpmnMetaDefinition>(new BpmnMetaDefinition
            {
                DefinitionId = metadata.DefinitionId,
                Name = metadata.Name,
                Description = metadata.Description
            });
        }

        public Task DeleteMetaDefinition(string definitionId)
        {
            if (MetaDefinitions.RemoveAll(existing => existing.DefinitionId == definitionId) == 0)
            {
                throw new DefinitionStorageNotFoundException($"No meta definition found for definitionId {definitionId}");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestSubscriptionStorage(bool throwOnMessageSubscriptionAdd) : IMessageSubscriptionStorage
    {
        public List<MessageSubscription> MessageSubscriptions { get; } = [];

        public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() =>
            Task.FromResult(MessageSubscriptions.AsEnumerable());

        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? messageInstanceId) =>
            Task.FromResult(Enumerable.Empty<MessageSubscription>());

        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) =>
            Task.FromResult(MessageSubscriptions.Where(subscription => subscription.ProcessInstanceId == instanceId).AsEnumerable());

        public Task AddMessageSubscription(MessageSubscription messageSubscription)
        {
            if (throwOnMessageSubscriptionAdd)
            {
                throw new InvalidOperationException("Adding message subscriptions failed.");
            }

            MessageSubscriptions.Add(messageSubscription);
            return Task.CompletedTask;
        }

        public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId)
        {
            MessageSubscriptions.RemoveAll(subscription => subscription.ProcessInstanceId == instanceId);
            return Task.CompletedTask;
        }

        public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId)
        {
            MessageSubscriptions.RemoveAll(subscription =>
                subscription.ProcessInstanceId == null &&
                subscription.RelatedDefinitionId == metaDefinitionId);
            return Task.CompletedTask;
        }

        public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public void AddSignalSubscription(SignalSubscription signalSubscription) { }
        public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId) =>
            Task.FromResult(Enumerable.Empty<SignalSubscription>());
        public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId) { }
        public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) =>
            Task.FromResult(Enumerable.Empty<UserTaskSubscription>());
        public Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId) =>
            Task.FromResult(Enumerable.Empty<ExtendedUserTaskSubscription>());
        public Task AddUserTaskSubscription(UserTaskSubscription userTasks) => Task.CompletedTask;
        public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) => Task.CompletedTask;
        public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId) { }
        public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() =>
            Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) =>
            Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task AddTimerSubscription(TimerSubscription timerSubscription) => Task.CompletedTask;
        public Task RemoveTimerSubscription(Guid timerSubscriptionId) => Task.CompletedTask;
        public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
    }

    private sealed class TestInstanceStorage : IInstanceStorage
    {
        public Dictionary<Guid, ProcessInstanceInfo> Instances { get; } = [];

        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId)
        {
            return Task.FromResult(Instances[processInstanceId]);
        }

        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo)
        {
            Instances[processInstanceInfo.InstanceId] = processInstanceInfo;
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() =>
            Task.FromResult(Instances.Values.Where(instance => !instance.IsFinished).AsEnumerable());

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() =>
            Task.FromResult(Instances.Values.AsEnumerable());

        public Task DeleteInstance(Guid processInstanceId)
        {
            Instances.Remove(processInstanceId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpFormStorage : IFormStorage
    {
        public Task SaveFormMetaData(FormMetadata formMetadata) => Task.CompletedTask;
        public Task<FormMetadata> GetFormMetaData(Guid formId) => throw new NotSupportedException();
        public Task<IEnumerable<FormMetadata>> GetFormMetadatas() => Task.FromResult(Enumerable.Empty<FormMetadata>());
        public Task UpdateFormMetaData(FormMetadata formMetaData) => Task.CompletedTask;
        public Task DeleteFormMetaData(Guid formId) => Task.CompletedTask;
        public Task SaveForm(Form form) => Task.CompletedTask;
        public Task<Form> GetForm(Guid id) => throw new NotSupportedException();
        public Task<IEnumerable<Form>> GetForms(Guid formId) => Task.FromResult(Enumerable.Empty<Form>());
        public Task DeleteForm(Guid id) => Task.CompletedTask;
        public Task<Model.Version> GetMaxVersion(Guid formId) => Task.FromResult(new Model.Version());
    }

    private static string CreateServiceTaskWithoutTypeXml(string definitionId) => $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="{definitionId}">
                  <bpmn:process id="Process_{definitionId}" isExecutable="true">
                    <bpmn:serviceTask id="Service_1" />
                  </bpmn:process>
                </bpmn:definitions>
                """;

    private static string CreateUnsupportedScriptTaskXml(string definitionId) => $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="{definitionId}">
                  <bpmn:process id="Process_{definitionId}" isExecutable="true">
                    <bpmn:startEvent id="Start_1" />
                    <bpmn:scriptTask id="Script_1" />
                    <bpmn:endEvent id="End_1" />
                    <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Script_1" />
                    <bpmn:sequenceFlow id="Flow_2" sourceRef="Script_1" targetRef="End_1" />
                  </bpmn:process>
                </bpmn:definitions>
                """;

    private static string CreatePlainStartXml(string definitionId)
    {
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                  id="{definitionId}"
                                  targetNamespace="http://bpmn.io/schema/bpmn">
                  <bpmn:process id="Process_{definitionId}" isExecutable="true">
                    <bpmn:startEvent id="StartEvent_1">
                      <bpmn:outgoing>Flow_1</bpmn:outgoing>
                    </bpmn:startEvent>
                    <bpmn:endEvent id="EndEvent_1">
                      <bpmn:incoming>Flow_1</bpmn:incoming>
                    </bpmn:endEvent>
                    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="EndEvent_1" />
                  </bpmn:process>
                  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
                    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Process_{definitionId}">
                      <bpmndi:BPMNShape id="StartEvent_1_di" bpmnElement="StartEvent_1">
                        <dc:Bounds x="179" y="99" width="36" height="36" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="EndEvent_1_di" bpmnElement="EndEvent_1">
                        <dc:Bounds x="332" y="99" width="36" height="36" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1">
                        <di:waypoint x="215" y="117" />
                        <di:waypoint x="332" y="117" />
                      </bpmndi:BPMNEdge>
                    </bpmndi:BPMNPlane>
                  </bpmndi:BPMNDiagram>
                </bpmn:definitions>
                """;
    }

    /// <summary>
    /// Ein Diagramm, dessen Katalog-Kennung frei waehlbar ist, waehrend die Prozesskennung gueltig
    /// bleibt — sonst pruefte ein Test mit unbrauchbarer Kennung zwei Dinge auf einmal.
    /// </summary>
    private static string CreateXmlWithCatalogId(string definitionId)
    {
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                  id="{definitionId}"
                                  targetNamespace="http://bpmn.io/schema/bpmn">
                  <bpmn:process id="Process_1" isExecutable="true">
                    <bpmn:startEvent id="StartEvent_1">
                      <bpmn:outgoing>Flow_1</bpmn:outgoing>
                    </bpmn:startEvent>
                    <bpmn:endEvent id="EndEvent_1">
                      <bpmn:incoming>Flow_1</bpmn:incoming>
                    </bpmn:endEvent>
                    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="EndEvent_1" />
                  </bpmn:process>
                  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
                    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Process_1">
                      <bpmndi:BPMNShape id="StartEvent_1_di" bpmnElement="StartEvent_1">
                        <dc:Bounds x="179" y="99" width="36" height="36" />
                      </bpmndi:BPMNShape>
                    </bpmndi:BPMNPlane>
                  </bpmndi:BPMNDiagram>
                </bpmn:definitions>
                """;
    }

    private static string CreateMessageStartXml(string definitionId)
    {
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                  id="{definitionId}"
                                  targetNamespace="http://bpmn.io/schema/bpmn">
                  <bpmn:message id="Message_1" name="StartMessage" />
                  <bpmn:process id="Process_{definitionId}" isExecutable="true">
                    <bpmn:startEvent id="StartEvent_Message">
                      <bpmn:outgoing>Flow_1</bpmn:outgoing>
                      <bpmn:messageEventDefinition id="MessageEventDefinition_1" messageRef="Message_1" />
                    </bpmn:startEvent>
                    <bpmn:endEvent id="EndEvent_1">
                      <bpmn:incoming>Flow_1</bpmn:incoming>
                    </bpmn:endEvent>
                    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_Message" targetRef="EndEvent_1" />
                  </bpmn:process>
                  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
                    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Process_{definitionId}">
                      <bpmndi:BPMNShape id="StartEvent_Message_di" bpmnElement="StartEvent_Message">
                        <dc:Bounds x="179" y="99" width="36" height="36" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="EndEvent_1_di" bpmnElement="EndEvent_1">
                        <dc:Bounds x="332" y="99" width="36" height="36" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1">
                        <di:waypoint x="215" y="117" />
                        <di:waypoint x="332" y="117" />
                      </bpmndi:BPMNEdge>
                    </bpmndi:BPMNPlane>
                  </bpmndi:BPMNDiagram>
                </bpmn:definitions>
                """;
    }
}
