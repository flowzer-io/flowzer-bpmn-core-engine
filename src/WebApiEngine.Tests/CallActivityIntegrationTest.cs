using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using core_engine;
using FilesystemStorageSystem;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Die lokale Call Activity über die ganze Kette: Die Engine stellt den Aufruf bereit, die
/// Geschäftslogik startet die Kindinstanz in derselben Transaktion, und das Ende des Kindes
/// führt den wartenden Schritt des Aufrufers weiter — als Ergebnis oder als BPMN-Fehler.
/// </summary>
[NonParallelizable]
public sealed class CallActivityIntegrationTest
{
    private const string CallerDefinitionId = "Definitions_Caller";
    private const string CalledDefinitionId = "Definitions_Called";
    private const string RecursiveDefinitionId = "Definitions_Recursive";

    // Testzweck: Ein Token an der Call Activity wartet, und die Geschäftslogik startet die
    // Kindinstanz der deployten Version mit genau den zugeordneten Eingabevariablen.
    [Test]
    public async Task Start_ShouldStartTheCalledProcessWithTheMappedInput()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, CalledDefinitionId, CalledXml());
        await DeployAsync(provider, engine, CallerDefinitionId, CallerXml());

        var caller = await engine.StartProcessInstance(CallerDefinitionId,
            Variables(("antragsnummer", "4711"), ("intern", "bleibt hier")));

        using (new AssertionScope())
        {
            var stored = await InstanceAsync(provider, caller.InstanceId);
            ActiveNodeIds(stored).Should().BeEquivalentTo(["Aufruf"]);

            var child = (await ChildrenAsync(provider, caller.InstanceId)).Should().ContainSingle().Subject;
            child.metaDefinitionId.Should().Be(CalledDefinitionId);
            child.ParentInstanceId.Should().Be(caller.InstanceId);
            child.ParentTokenId.Should().Be(WaitingTokenId(stored, "Aufruf"));
            ProcessVariables(child).Should().Contain("ticketNummer", "4711");
            ProcessVariables(child).Should().NotContainKey("intern");
            ProcessVariables(child).Should().NotContainKey("antragsnummer");
        }
    }

    // Testzweck: Endet die Kindinstanz, läuft der wartende Schritt des Aufrufers mit genau den
    // zugeordneten Ausgabevariablen weiter — der übrige Stand des Kindes bleibt dort.
    [Test]
    public async Task CompletingTheCalledProcess_ShouldContinueTheCallerWithTheMappedOutput()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, CalledDefinitionId, CalledXml());
        await DeployAsync(provider, engine, CallerDefinitionId, CallerXml());
        var caller = await engine.StartProcessInstance(CallerDefinitionId, Variables(("antragsnummer", "4711")));
        var child = (await ChildrenAsync(provider, caller.InstanceId)).Single();

        await CompleteJobAsync(provider, engine, child.InstanceId,
            Variables(("loesung", "Kabel getauscht"), ("internesProtokoll", "bleibt im Kind")));

        using (new AssertionScope())
        {
            (await InstanceAsync(provider, child.InstanceId)).State.Should().Be(ProcessInstanceState.Completed);

            var continued = await InstanceAsync(provider, caller.InstanceId);
            ActiveNodeIds(continued).Should().BeEquivalentTo(["NachDemAufruf"]);
            ProcessVariables(continued).Should().Contain("loesungExtern", "Kabel getauscht");
            ProcessVariables(continued).Should().NotContainKey("internesProtokoll");
            ProcessVariables(continued).Should().NotContainKey("loesung");
        }
    }

    // Testzweck: Ein aufgerufener Prozess ohne Wartezustand ist schon fertig, wenn er gestartet
    // wird. Der Aufrufer läuft dann im selben Schreibvorgang weiter, statt ewig zu warten.
    [Test]
    public async Task CalledProcessWithoutWaitState_ShouldContinueTheCallerInTheSameMutation()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, CalledDefinitionId, ImmediateCalledXml());
        await DeployAsync(provider, engine, CallerDefinitionId, CallerXml());

        var caller = await engine.StartProcessInstance(CallerDefinitionId, Variables(("antragsnummer", "4711")));

        using (new AssertionScope())
        {
            var child = (await ChildrenAsync(provider, caller.InstanceId)).Should().ContainSingle().Subject;
            child.State.Should().Be(ProcessInstanceState.Completed);
            ActiveNodeIds(await InstanceAsync(provider, caller.InstanceId)).Should().BeEquivalentTo(["NachDemAufruf"]);
        }
    }

    // Testzweck: Scheitert die Kindinstanz mit einem ungefangenen BPMN-Fehler, wirft die Call
    // Activity denselben Code — ein Error-Boundary an ihr fängt ihn wie jeden anderen Fehler.
    [Test]
    public async Task FailingCalledProcess_ShouldRaiseTheSameErrorAtTheCallActivity()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, CalledDefinitionId, CalledXml());
        await DeployAsync(provider, engine, CallerDefinitionId, CallerWithBoundaryXml("BONITAET"));
        var caller = await engine.StartProcessInstance(CallerDefinitionId, Variables(("antragsnummer", "4711")));
        var child = (await ChildrenAsync(provider, caller.InstanceId)).Single();

        await ThrowJobErrorAsync(provider, engine, child.InstanceId, "BONITAET");

        using (new AssertionScope())
        {
            (await InstanceAsync(provider, child.InstanceId)).State.Should().Be(ProcessInstanceState.Failed);

            var continued = await InstanceAsync(provider, caller.InstanceId);
            continued.State.Should().Be(ProcessInstanceState.Waiting);
            ActiveNodeIds(continued).Should().BeEquivalentTo(["Nacharbeit"]);
        }
    }

    // Testzweck: Ein abgebrochener Kindvorgang ist kein Ergebnis. Die Call Activity wirft
    // CALLED_PROCESS_CANCELLED, damit der Aufrufer den Abbruch fachlich behandeln kann.
    [Test]
    public async Task CancellingTheCalledProcess_ShouldRaiseCalledProcessCancelled()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, CalledDefinitionId, CalledXml());
        await DeployAsync(provider, engine, CallerDefinitionId,
            CallerWithBoundaryXml(BpmnBusinessLogic.CallActivityErrors.ProcessCancelled));
        var caller = await engine.StartProcessInstance(CallerDefinitionId, Variables(("antragsnummer", "4711")));
        var child = (await ChildrenAsync(provider, caller.InstanceId)).Single();

        await engine.CancelInstance(child.InstanceId);

        using (new AssertionScope())
        {
            (await InstanceAsync(provider, child.InstanceId)).State.Should().Be(ProcessInstanceState.Terminated);
            ActiveNodeIds(await InstanceAsync(provider, caller.InstanceId)).Should().BeEquivalentTo(["Nacharbeit"]);
        }
    }

    // Testzweck: Wird der aufrufende Vorgang abgebrochen, wird der aufgerufene mit abgebrochen —
    // er arbeitete sonst für einen Vorgang, den es nicht mehr gibt.
    [Test]
    public async Task CancellingTheCaller_ShouldCancelTheCalledProcess()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, CalledDefinitionId, CalledXml());
        await DeployAsync(provider, engine, CallerDefinitionId, CallerXml());
        var caller = await engine.StartProcessInstance(CallerDefinitionId, Variables(("antragsnummer", "4711")));
        var child = (await ChildrenAsync(provider, caller.InstanceId)).Single();

        await engine.CancelInstance(caller.InstanceId);

        using (new AssertionScope())
        {
            (await InstanceAsync(provider, caller.InstanceId)).State.Should().Be(ProcessInstanceState.Terminated);
            (await InstanceAsync(provider, child.InstanceId)).State.Should().Be(ProcessInstanceState.Terminated);
        }
    }

    // Testzweck: Gibt es keinen deployten Prozess mit der aufgerufenen Kennung, wirft die Call
    // Activity CALLED_PROCESS_NOT_FOUND; ohne Boundary scheitert der aufrufende Vorgang daran.
    [Test]
    public async Task UnknownProcessId_ShouldRaiseCalledProcessNotFound()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        // Der aufgerufene Workflow wird bewusst nicht deployt.
        await DeployAsync(provider, engine, CallerDefinitionId, CallerXml());

        var caller = await engine.StartProcessInstance(CallerDefinitionId, Variables(("antragsnummer", "4711")));

        using (new AssertionScope())
        {
            var stored = await InstanceAsync(provider, caller.InstanceId);
            stored.State.Should().Be(ProcessInstanceState.Failed);
            stored.FailureReason.Should()
                .Contain(BpmnBusinessLogic.CallActivityErrors.ProcessNotFound).And.Contain("Aufruf");
            (await ChildrenAsync(provider, caller.InstanceId)).Should().BeEmpty();
        }
    }

    // Testzweck: Ein Prozess, der sich selbst aufruft, wird ab der zehnten Ebene mit
    // CALLED_PROCESS_DEPTH_EXCEEDED abgebrochen, statt die Transaktion endlos weiterlaufen zu lassen.
    [Test]
    public async Task Recursion_ShouldStopAtTheDepthLimit()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, RecursiveDefinitionId, RecursiveXml());

        var caller = await engine.StartProcessInstance(RecursiveDefinitionId, Variables(("tiefe", 1)));

        var all = await InstancesAsync(provider);
        using (new AssertionScope())
        {
            all.Should().HaveCount(10);
            all.Should().OnlyContain(instance => instance.State == ProcessInstanceState.Failed);

            var deepest = all.Single(instance => !all.Any(other => other.ParentInstanceId == instance.InstanceId));
            deepest.FailureReason.Should().Contain(BpmnBusinessLogic.CallActivityErrors.DepthExceeded);
            // Der Fehler wandert unverändert nach oben: Jede Ebene wirft ihn an ihrer Call Activity erneut.
            all.Single(instance => instance.InstanceId == caller.InstanceId).FailureReason.Should()
                .Contain(BpmnBusinessLogic.CallActivityErrors.DepthExceeded);
        }
    }

    // Testzweck: Solange ein Schritt auf einen aufgerufenen Vorgang wartet, ist der aufrufende
    // Vorgang nicht migrierbar; der aufgerufene selbst bleibt es.
    [Test]
    public async Task Migration_ShouldRejectACallerWaitingForACalledProcess()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, CalledDefinitionId, CalledXml());
        await DeployAsync(provider, engine, CallerDefinitionId, CallerXml());
        var caller = await engine.StartProcessInstance(CallerDefinitionId, Variables(("antragsnummer", "4711")));
        var child = (await ChildrenAsync(provider, caller.InstanceId)).Single();
        await DeployAsync(provider, engine, CallerDefinitionId, CallerXml(), new Model.Version(1, 1));
        await DeployAsync(provider, engine, CalledDefinitionId, CalledXml(), new Model.Version(1, 1));

        var callerPreview = await engine.PreviewInstanceMigration([caller.InstanceId]);
        var childPreview = await engine.PreviewInstanceMigration([child.InstanceId]);

        using (new AssertionScope())
        {
            var blocked = callerPreview.Instances.Should().ContainSingle().Subject;
            blocked.Migratable.Should().BeFalse();
            blocked.Problems.Should().ContainSingle(problem =>
                problem.Code == nameof(InstanceMigrationProblemCode.CallActivityWaiting)
                && problem.FlowNodeId == "Aufruf");

            childPreview.Instances.Should().ContainSingle().Which.Migratable.Should().BeTrue();
        }
    }

    // Testzweck: Die Instanzansicht nennt den aufrufenden Vorgang, und der neue Leseweg liefert
    // die aufgerufenen Vorgänge samt Zustand und der Aufruf-Aktivität, an der sie hängen.
    [Test]
    public async Task HttpChildren_ShouldListTheCalledInstancesAndNameTheCaller()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await DeployAsync(context.Storage, engine, CalledDefinitionId, CalledXml());
        await DeployAsync(context.Storage, engine, CallerDefinitionId, CallerXml());
        using var client = context.CreateClient(isOperator: true);
        var caller = await engine.StartProcessInstance(CallerDefinitionId, Variables(("antragsnummer", "4711")));

        var children = await client.GetFromJsonAsync<ApiStatusResult<List<CalledInstanceDto>>>(
            $"/instance/{caller.InstanceId}/children");

        using (new AssertionScope())
        {
            var child = children!.Result.Should().ContainSingle().Subject;
            child.RelatedDefinitionId.Should().Be(CalledDefinitionId);
            child.State.Should().Be(ProcessInstanceStateDto.Waiting);
            child.CallActivityFlowNodeId.Should().Be("Aufruf");
            child.DefinitionVersion.Should().NotBeNull();

            var childDto = await client.GetFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>(
                $"/instance/{child.InstanceId}");
            childDto!.Result!.ParentInstanceId.Should().Be(caller.InstanceId);
            childDto.Result.ParentTokenId.Should().NotBeNull();

            // Der aufrufende Vorgang selbst hat keinen Aufrufer.
            var callerDto = await client.GetFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>(
                $"/instance/{caller.InstanceId}");
            callerDto!.Result!.ParentInstanceId.Should().BeNull();
        }
    }

    // Testzweck: Wer den aufrufenden Vorgang angestoßen hat, sieht auch den aufgerufenen — sonst
    // bräche die Sicht auf den eigenen Vorgang genau an der Call Activity ab.
    [Test]
    public async Task Initiator_ShouldSeeTheCalledInstance()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await DeployAsync(context.Storage, engine, CalledDefinitionId, CalledXml());
        await DeployAsync(context.Storage, engine, CallerDefinitionId, CallerXml());
        using var initiator = context.CreateClient();

        using var started = await initiator.PostAsJsonAsync(
            $"/definition/meta/{CallerDefinitionId}/instance", new { variables = new { antragsnummer = "4711" } });
        started.EnsureSuccessStatusCode();
        var callerId = (await started.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>())!
            .Result!.InstanceId;

        var children = await initiator.GetFromJsonAsync<ApiStatusResult<List<CalledInstanceDto>>>(
            $"/instance/{callerId}/children");

        using (new AssertionScope())
        {
            var child = children!.Result.Should().ContainSingle().Subject;
            using var childResponse = await initiator.GetAsync($"/instance/{child.InstanceId}");
            childResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // Eine fremde Person sieht denselben Kindvorgang nicht.
            using var stranger = context.CreateClient(userId: Guid.NewGuid(), username: "fremd");
            using var forbidden = await stranger.GetAsync($"/instance/{child.InstanceId}");
            forbidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    private static ExpandoObject Variables(params (string Key, object? Value)[] values)
    {
        var variables = new ExpandoObject();
        var entries = (IDictionary<string, object?>)variables;
        foreach (var (key, value) in values) entries[key] = value;

        return variables;
    }

    private static string[] ActiveNodeIds(ProcessInstanceInfo instance) => instance.Tokens
        .Where(token => token.State == FlowNodeState.Active && token.CurrentFlowNode is not null
            && token.ParentTokenId is not null)
        .Select(token => token.CurrentFlowNode!.Id)
        .ToArray();

    private static Guid WaitingTokenId(ProcessInstanceInfo instance, string flowNodeId) => instance.Tokens
        .Single(token => token.State == FlowNodeState.Active && token.CurrentFlowNode?.Id == flowNodeId)
        .Id;

    private static IDictionary<string, object?> ProcessVariables(ProcessInstanceInfo instance) =>
        (IDictionary<string, object?>?)instance.Tokens.Single(token => token.ParentTokenId is null).Variables
        ?? new Dictionary<string, object?>();

    private static async Task<ProcessInstanceInfo> InstanceAsync(ITransactionalStorageProvider provider, Guid instanceId)
    {
        using var storage = provider.GetTransactionalStorage();
        return await storage.InstanceStorage.GetProcessInstance(instanceId);
    }

    private static async Task<ProcessInstanceInfo[]> InstancesAsync(ITransactionalStorageProvider provider)
    {
        using var storage = provider.GetTransactionalStorage();
        return (await storage.InstanceStorage.GetAllInstances()).ToArray();
    }

    private static async Task<ProcessInstanceInfo[]> ChildrenAsync(
        ITransactionalStorageProvider provider, Guid instanceId) =>
        (await InstancesAsync(provider))
        .Where(instance => instance.ParentInstanceId == instanceId)
        .ToArray();

    /// <summary>Erledigt den einen wartenden Auftrag einer Instanz.</summary>
    private static async Task CompleteJobAsync(
        ITransactionalStorageProvider provider, BpmnBusinessLogic engine, Guid instanceId, ExpandoObject? result)
    {
        await engine.CompleteServiceTaskJob(await SingleJobAsync(provider, instanceId), result, Guid.NewGuid());
    }

    /// <summary>Meldet an dem einen wartenden Auftrag einer Instanz einen fachlichen Fehler.</summary>
    private static async Task ThrowJobErrorAsync(
        ITransactionalStorageProvider provider, BpmnBusinessLogic engine, Guid instanceId, string errorCode)
    {
        await engine.ThrowServiceTaskJobError(await SingleJobAsync(provider, instanceId), errorCode,
            "Der aufgerufene Prozess ist fachlich gescheitert.", variables: null);
    }

    private static async Task<ServiceTaskJob> SingleJobAsync(ITransactionalStorageProvider provider, Guid instanceId)
    {
        using var storage = provider.GetTransactionalStorage();
        return (await storage.ServiceTaskStorage.GetJobs())
            .Single(job => job.ProcessInstanceId == instanceId);
    }

    private static async Task DeployAsync(
        ITransactionalStorageProvider provider,
        BpmnBusinessLogic engine,
        string metaDefinitionId,
        string xml,
        Model.Version? version = null)
    {
        using (var storage = provider.GetTransactionalStorage())
        {
            var definition = await StoreAsync(storage, metaDefinitionId, xml, version);
            storage.CommitChanges();
            await engine.DeployDefinition(definition);
        }
    }

    private static async Task DeployAsync(
        IStorageSystem storage,
        BpmnBusinessLogic engine,
        string metaDefinitionId,
        string xml)
    {
        await engine.DeployDefinition(await StoreAsync(storage, metaDefinitionId, xml, null));
    }

    private static async Task<BpmnDefinition> StoreAsync(
        IStorageSystem storage, string metaDefinitionId, string xml, Model.Version? version)
    {
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(),
            DefinitionId = metaDefinitionId,
            Version = version ?? new Model.Version(1, 0),
            Hash = "test",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            IsActive = false
        };

        // Eine zweite Version desselben Workflows braucht keinen zweiten Katalogeintrag.
        var catalog = await storage.DefinitionStorage.GetAllMetaDefinitions();
        if (catalog.All(entry => entry.DefinitionId != metaDefinitionId))
        {
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = metaDefinitionId, Name = metaDefinitionId
            });
        }
        await storage.DefinitionStorage.StoreDefinition(definition);
        await storage.DefinitionStorage.StoreBinary(definition.Id, xml);

        return definition;
    }

    /// <summary>Ruft einen anderen Prozess auf und wartet danach an einem eigenen Schritt.</summary>
    private static string CallerXml() => """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Caller" targetNamespace="test">
          <bpmn:process id="Process_Caller" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:callActivity id="Aufruf">
              <bpmn:extensionElements>
                <zeebe:calledElement processId="Process_Called"
                    propagateAllParentVariables="false" propagateAllChildVariables="false" />
                <zeebe:ioMapping>
                  <zeebe:input source="=antragsnummer" target="ticketNummer" />
                  <zeebe:output source="=loesung" target="loesungExtern" />
                </zeebe:ioMapping>
              </bpmn:extensionElements>
            </bpmn:callActivity>
            <bpmn:serviceTask id="NachDemAufruf"><bpmn:extensionElements>
              <zeebe:taskDefinition type="nacharbeit" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="Ende" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Aufruf" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Aufruf" targetRef="NachDemAufruf" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="NachDemAufruf" targetRef="Ende" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Wie <see cref="CallerXml"/>, aber mit Error-Boundary an der Aufruf-Aktivität.</summary>
    private static string CallerWithBoundaryXml(string errorCode) => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Caller" targetNamespace="test">
          <bpmn:error id="Error_Aufruf" name="Aufruf gescheitert" errorCode="{{errorCode}}" />
          <bpmn:process id="Process_Caller" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:callActivity id="Aufruf">
              <bpmn:extensionElements>
                <zeebe:calledElement processId="Process_Called"
                    propagateAllParentVariables="true" propagateAllChildVariables="true" />
              </bpmn:extensionElements>
            </bpmn:callActivity>
            <bpmn:boundaryEvent id="BoundaryFehler" attachedToRef="Aufruf">
              <bpmn:errorEventDefinition id="Definition_Fehler" errorRef="Error_Aufruf" />
            </bpmn:boundaryEvent>
            <bpmn:serviceTask id="Nacharbeit"><bpmn:extensionElements>
              <zeebe:taskDefinition type="nacharbeit" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:serviceTask id="NachDemAufruf"><bpmn:extensionElements>
              <zeebe:taskDefinition type="weiter" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="Ende" />
            <bpmn:endEvent id="EndeNacharbeit" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Aufruf" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Aufruf" targetRef="NachDemAufruf" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="NachDemAufruf" targetRef="Ende" />
            <bpmn:sequenceFlow id="Flow_4" sourceRef="BoundaryFehler" targetRef="Nacharbeit" />
            <bpmn:sequenceFlow id="Flow_5" sourceRef="Nacharbeit" targetRef="EndeNacharbeit" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Der aufgerufene Prozess: wartet auf einen Auftrag und endet danach.</summary>
    private static string CalledXml() => """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Called" targetNamespace="test">
          <bpmn:process id="Process_Called" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:serviceTask id="Bearbeiten"><bpmn:extensionElements>
              <zeebe:taskDefinition type="bearbeiten" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="Ende" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Bearbeiten" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Bearbeiten" targetRef="Ende" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Der aufgerufene Prozess ohne Wartezustand: Er endet sofort nach dem Start.</summary>
    private static string ImmediateCalledXml() => """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Called" targetNamespace="test">
          <bpmn:process id="Process_Called" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:endEvent id="Ende" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Ende" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Ein Prozess, der sich selbst aufruft — die Rekursionsgrenze muss greifen.</summary>
    private static string RecursiveXml() => """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Recursive" targetNamespace="test">
          <bpmn:process id="Process_Recursive" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:callActivity id="SichSelbst">
              <bpmn:extensionElements>
                <zeebe:calledElement processId="Process_Recursive"
                    propagateAllParentVariables="true" propagateAllChildVariables="true" />
              </bpmn:extensionElements>
            </bpmn:callActivity>
            <bpmn:endEvent id="Ende" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="SichSelbst" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="SichSelbst" targetRef="Ende" />
          </bpmn:process>
        </bpmn:definitions>
        """;
}
