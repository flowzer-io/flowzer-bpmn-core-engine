using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Model;
using Newtonsoft.Json.Linq;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Reale Deployment-/Laufzeitpfade und isolierte Dateiablage, kein produktiver IdP.</summary>
[NonParallelizable]
public class FormDeploymentBindingTest
{
    private const string InitialSchema = """{"components":[{"type":"textfield","key":"vorgang","input":true}]}""";
    private const string ChangedSchema = """{"components":[{"type":"textfield","key":"privateSalary","input":true}]}""";

    // Testzweck: Eine neue Fassung und Umbenennung im Formularbestand verändern weder
    // bestehende Aufgaben noch erst später aktivierte Aufgaben derselben Definitionsversion.
    [Test]
    public async Task Tasks_ShouldKeepDeployedForm_AcrossVersionChangeRenameAndLaterActivation()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var form = await SaveForm(context);
        var definition = await Deploy(context);
        dynamic data = new ExpandoObject();
        data.vorgang = "Urlaub";
        data.privateSalary = "private";
        var instance = await context.Services.GetRequiredService<BpmnBusinessLogic>()
            .StartProcessInstance(definition.DefinitionId, (ExpandoObject)data);
        var first = (await context.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
        await SaveForm(context, form.FormId, major: 2, schema: ChangedSchema);
        await context.Storage.FormStorage.UpdateFormMetaData(new FormMetadata { FormId = form.FormId, Name = "Renamed" });
        // Selbst eine beschädigte/quellenextern überschriebene Versionszeile ersetzt den Snapshot nicht.
        form.FormData = ChangedSchema;
        await context.Storage.FormStorage.SaveForm(form);
        using var client = context.CreateClient();

        var response = await client.GetAsync($"/usertask/{first.Id}/form");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>())!.Result!.Id.Should().Be(form.Id);
        (await client.GetStringAsync("/usertask")).Should().NotContain("privateSalary").And.NotContain("private\"");
        (await client.PostAsJsonAsync("/usertask", Result(first))).StatusCode.Should().Be(HttpStatusCode.OK);
        var second = (await context.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
        second.Token.CurrentFlowNode!.Id.Should().Be("Second");
        var secondForm = await client.GetFromJsonAsync<ApiStatusResult<FormDto>>($"/usertask/{second.Id}/form");
        secondForm!.Result!.FormData.Should().Be(InitialSchema);

        // Neue Resolver-/Storage-Instanz erzwingt einen echten Persistenz-Roundtrip.
        var fresh = new FormKeyResolver(new FilesystemStorageSystem.Storage());
        (await fresh.ResolveAsync("Approval", definition.Id)).Form!.Id.Should().Be(form.Id);
    }

    // Testzweck: Das Startformular ist schon beim Deployment fixiert, nicht erst beim
    // ersten Instanzstart; eine neue Workflow-Version bindet dagegen die neue Fassung.
    [Test]
    public async Task StartForm_ShouldChangeOnlyWithNewWorkflowDeployment()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var initial = await SaveForm(context);
        var first = await Deploy(context, startForm: true);
        var changed = await SaveForm(context, initial.FormId, major: 2, schema: ChangedSchema);
        using var client = context.CreateClient();
        var before = await client.GetFromJsonAsync<ApiStatusResult<FormDto>>($"/definition/meta/{first.DefinitionId}/start-form");
        before!.Result!.Id.Should().Be(initial.Id);
        await Deploy(context, startForm: true, version: 2);
        var after = await client.GetFromJsonAsync<ApiStatusResult<FormDto>>($"/definition/meta/{first.DefinitionId}/start-form");
        after!.Result!.Id.Should().Be(changed.Id);
    }

    // Testzweck: Unauflösbare/mehrdeutige Referenzen dürfen die bisherige aktive Version
    // und deren Start-Subscriptions nicht ersetzen; die ganze Bindung wird vorher geprüft.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Deployment_ShouldRejectUnresolvableForms_WithoutDeactivatingPreviousVersion(bool ambiguous)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await SaveForm(context);
        var first = await Deploy(context, messageStart: true);
        var before = (await context.Storage.SubscriptionStorage.GetAllMessageSubscriptions()).Single();
        if (ambiguous) await SaveForm(context);
        Func<Task> deploy = () => Deploy(context, formKey: ambiguous ? "Approval" : "Missing", version: 2);
        await deploy.Should().ThrowAsync<InvalidOperationException>().WithMessage("*form*");
        (await context.Storage.DefinitionStorage.GetDeployedDefinition(first.DefinitionId))!.Id.Should().Be(first.Id);
        (await context.Storage.DefinitionStorage.GetDefinitionById(first.Id)).IsActive.Should().BeTrue();
        (await context.Storage.SubscriptionStorage.GetAllMessageSubscriptions()).Single().Should().BeEquivalentTo(before);
    }

    // Testzweck: Der öffentliche Deployment-Weg räumt eine abgewiesene neue Version auf,
    // ohne die bisher aktive Fassung oder deren Startanmeldung anzutasten.
    [Test]
    public async Task HttpDeployment_ShouldRejectMissingForm_WithoutOrphanedVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await SaveForm(context);
        var original = await Deploy(context, messageStart: true);
        var xml = (await context.Storage.DefinitionStorage.GetBinary(original.Id)).Replace("Approval", "Missing");
        using var client = context.CreateClient(isModeler: true);
        using var response = await client.PostAsync("/definition/deploy", new StringContent(xml, Encoding.UTF8, "application/xml"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await context.Storage.DefinitionStorage.GetAllDefinitions()).Should().ContainSingle().Which.Id.Should().Be(original.Id);
        (await context.Storage.SubscriptionStorage.GetAllMessageSubscriptions()).Should().ContainSingle();
    }

    // Testzweck: Der Deployment-Scan steigt auch in Subprozesse ab. Deren erst später
    // erreichbare menschliche Aufgabe erhält ebenfalls einen festen Formularstand.
    [Test]
    public async Task Deployment_ShouldBindFormsInsideSubprocesses()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await SaveForm(context);
        var nestedForm = await SaveForm(context, name: "Nested");
        var original = await Deploy(context);
        var nestedDefinition = new BpmnDefinition { Id = Guid.NewGuid(), DefinitionId = original.DefinitionId,
            Hash = "nested", SavedByUser = original.SavedByUser, Version = new Model.Version(2, 0), IsActive = false };
        var xml = XDocument.Parse(await context.Storage.DefinitionStorage.GetBinary(original.Id));
        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        var second = xml.Descendants(bpmn + "userTask").Single(element => (string?)element.Attribute("id") == "Second");
        var nested = XElement.Parse("""
            <bpmn:subProcess xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Nested">
              <bpmn:incoming>F2</bpmn:incoming><bpmn:outgoing>F3</bpmn:outgoing>
              <bpmn:startEvent id="NestedStart"><bpmn:outgoing>N1</bpmn:outgoing></bpmn:startEvent>
              <bpmn:sequenceFlow id="N1" sourceRef="NestedStart" targetRef="NestedTask" />
              <bpmn:userTask id="NestedTask" name="Nested task"><bpmn:extensionElements><zeebe:formDefinition formKey="Nested" /></bpmn:extensionElements>
                <bpmn:incoming>N1</bpmn:incoming><bpmn:outgoing>N2</bpmn:outgoing></bpmn:userTask>
              <bpmn:sequenceFlow id="N2" sourceRef="NestedTask" targetRef="NestedEnd" />
              <bpmn:endEvent id="NestedEnd"><bpmn:incoming>N2</bpmn:incoming></bpmn:endEvent>
            </bpmn:subProcess>
            """);
        second.ReplaceWith(nested);
        xml.Descendants(bpmn + "sequenceFlow").Single(element => (string?)element.Attribute("id") == "F2").SetAttributeValue("targetRef", "Nested");
        xml.Descendants(bpmn + "sequenceFlow").Single(element => (string?)element.Attribute("id") == "F3").SetAttributeValue("sourceRef", "Nested");
        await context.Storage.DefinitionStorage.StoreDefinition(nestedDefinition);
        await context.Storage.DefinitionStorage.StoreBinary(nestedDefinition.Id, xml.ToString());
        await context.Services.GetRequiredService<BpmnBusinessLogic>().DeployDefinition(nestedDefinition);
        await SaveForm(context, nestedForm.FormId, major: 2, schema: ChangedSchema);
        (await new FormKeyResolver(context.Storage).ResolveAsync("Nested", nestedDefinition.Id)).Form!.Id.Should().Be(nestedForm.Id);
    }

    // Testzweck: Wiederaktivierung derselben Version darf den Snapshot nicht austauschen.
    [Test]
    public async Task Redeployment_ShouldNotRebindAnExistingVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var initial = await SaveForm(context);
        var definition = await Deploy(context);
        await SaveForm(context, initial.FormId, major: 2, schema: ChangedSchema);
        // Absichtlich die zuvor geladene Definition, nicht der neue Snapshot aus der Ablage.
        await context.Services.GetRequiredService<BpmnBusinessLogic>().DeployDefinition(definition);
        var resolved = await new FormKeyResolver(context.Storage).ResolveAsync("Approval", definition.Id);
        resolved.Form!.Id.Should().Be(initial.Id);
    }

    // Testzweck: Historische externe Referenzen ohne belegten Stand dürfen nicht still
    // auf heutige Formulare migriert werden; gebundene BPMN-Formulare brauchen diesen Weg nicht.
    [Test]
    public async Task LegacyExternalForm_ShouldRequireExplicitMigration_InsteadOfGuessingLatest()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await SaveForm(context);
        var definition = await Deploy(context);
        var historical = JObject.FromObject(await context.Storage.DefinitionStorage.GetDefinitionById(definition.Id));
        historical.Remove("FormBindings");
        await context.Storage.DefinitionStorage.StoreDefinition(historical.ToObject<BpmnDefinition>()!);
        var resolved = await new FormKeyResolver(context.Storage).ResolveAsync("Approval", definition.Id);
        resolved.Form.Should().BeNull();
        resolved.ErrorMessage.Should().Contain("binding");
    }

    // Testzweck: Explizit benannte Versionen bleiben beim Deployment maßgeblich.
    [Test]
    public async Task Deployment_ShouldRespectExplicitVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var initial = await SaveForm(context);
        await SaveForm(context, initial.FormId, major: 2, schema: ChangedSchema);
        var definition = await Deploy(context, formKey: "Approval:1.0");
        (await new FormKeyResolver(context.Storage).ResolveAsync("Approval:1.0", definition.Id)).Form!.Id.Should().Be(initial.Id);
    }

    private static async Task<Form> SaveForm(AuthenticatedWorkflowTestContext context, Guid? formId = null,
        int major = 1, string schema = InitialSchema, string name = "Approval")
    {
        var form = new Form { Id = Guid.NewGuid(), FormId = formId ?? Guid.NewGuid(), Version = new Model.Version(major, 0), FormData = schema };
        if (formId is null) await context.Storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = form.FormId, Name = name });
        await context.Storage.FormStorage.SaveForm(form);
        return form;
    }

    private static async Task<BpmnDefinition> Deploy(AuthenticatedWorkflowTestContext context, string formKey = "Approval",
        bool startForm = false, int version = 1, bool messageStart = false)
    {
        const string id = "Definitions_Bindings";
        if (version == 1) await context.Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = id, Name = "Form bindings" });
        var definition = new BpmnDefinition { Id = Guid.NewGuid(), DefinitionId = id, Version = new Model.Version(version, 0),
            Hash = "test", IsActive = false, SavedByUser = AuthenticatedWorkflowTestContext.UserId, SavedOn = DateTime.UtcNow };
        var startExtension = startForm ? $"<bpmn:extensionElements><zeebe:formDefinition formKey=\"{formKey}\" /></bpmn:extensionElements>" : "";
        var messageEntry = messageStart ? """
            <bpmn:startEvent id="MessageStart"><bpmn:messageEventDefinition messageRef="StartMessage" /><bpmn:outgoing>MF</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="MF" sourceRef="MessageStart" targetRef="End" />
            """ : "";
        await context.Storage.DefinitionStorage.StoreDefinition(definition);
        await context.Storage.DefinitionStorage.StoreBinary(definition.Id, $$"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                id="{{id}}" targetNamespace="test">
              <bpmn:message id="StartMessage" name="StartMessage" />
              <bpmn:process id="Process_Bindings" isExecutable="true">
                {{messageEntry}}
                <bpmn:startEvent id="Start">{{startExtension}}<bpmn:outgoing>F1</bpmn:outgoing></bpmn:startEvent>
                <bpmn:sequenceFlow id="F1" sourceRef="Start" targetRef="First" />
                <bpmn:userTask id="First" name="First"><bpmn:extensionElements><zeebe:formDefinition formKey="{{formKey}}" /></bpmn:extensionElements>
                  <bpmn:incoming>F1</bpmn:incoming><bpmn:outgoing>F2</bpmn:outgoing></bpmn:userTask>
                <bpmn:sequenceFlow id="F2" sourceRef="First" targetRef="Second" />
                <bpmn:userTask id="Second" name="Second"><bpmn:extensionElements><zeebe:formDefinition formKey="{{formKey}}" /></bpmn:extensionElements>
                  <bpmn:incoming>F2</bpmn:incoming><bpmn:outgoing>F3</bpmn:outgoing></bpmn:userTask>
                <bpmn:sequenceFlow id="F3" sourceRef="Second" targetRef="End" />
                <bpmn:endEvent id="End"><bpmn:incoming>F3</bpmn:incoming></bpmn:endEvent>
              </bpmn:process>
            </bpmn:definitions>
            """);
        await context.Services.GetRequiredService<BpmnBusinessLogic>().DeployDefinition(definition);
        return definition;
    }

    private static UserTaskResultDto Result(UserTaskSubscription task) => new()
    {
        ProcessInstanceId = task.ProcessInstanceId, TokenId = task.Token.Id,
        FlowNodeId = task.Token.CurrentFlowNode!.Id, Data = new ExpandoObject()
    };
}
