using System.Dynamic;
using BPMN.Activities;
using BPMN.HumanInteraction;
using BPMN.Process;
using FluentAssertions;
using Flowzer.Shared;
using Model;
using StorageSystem;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using Task = System.Threading.Tasks.Task;

namespace WebApiEngine.Tests;

public class ManualMappingExtensionsTest
{
    // Testzweck: Deckt den Fall „Message DTO To Model Should Serialize Variables As JSON“ ab.
    [Test]
    public void MessageDto_ToModel_ShouldSerializeVariablesAsJson()
    {
        dynamic variables = new ExpandoObject();
        variables.Customer = "Ada";
        variables.Amount = 12;

        var dto = new MessageDto
        {
            Name = "InvoiceReceived",
            CorrelationKey = "INV-42",
            Variables = variables,
            TimeToLive = 180,
            InstanceId = Guid.NewGuid()
        };

        var result = dto.ToModel();

        result.Name.Should().Be("InvoiceReceived");
        result.CorrelationKey.Should().Be("INV-42");
        result.TimeToLive.Should().Be(180);
        result.Variables.Should().Contain("\"Customer\":\"Ada\"");
        result.Variables.Should().Contain("\"Amount\":12");
    }

    // Testzweck: Deckt den Fall „Message To DTO Should Deserialize Variables From JSON“ ab.
    [Test]
    public void Message_ToDto_ShouldDeserializeVariablesFromJson()
    {
        var message = new Message
        {
            Name = "InvoiceReceived",
            CorrelationKey = "INV-42",
            Variables = "{\"customer\":\"Ada\",\"approved\":true}",
            TimeToLive = 180,
            InstanceId = Guid.NewGuid()
        };

        var result = message.ToDto();

        result.Name.Should().Be("InvoiceReceived");
        ((IDictionary<string, object?>)result.Variables!).Should().ContainKey("customer");
        result.Variables!.GetValue("customer").Should().Be("Ada");
        result.Variables.GetValue("approved").Should().Be(true);
    }

    // Testzweck: Deckt den Fall „Token To DTO Should Map Runtime Fields Without Auto Mapper“ ab.
    [Test]
    public void Token_ToDto_ShouldMapRuntimeFieldsWithoutAutoMapper()
    {
        var processInstanceId = Guid.NewGuid();
        var previousToken = new Token
        {
            ProcessInstanceId = processInstanceId,
            CurrentBaseElement = new Process
            {
                Id = "Process_Previous",
                DefinitionsId = "Definitions_1",
                IsExecutable = true,
                FlowElements = []
            },
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Completed
        };

        var flowNode = new ServiceTask
        {
            Id = "Activity_ServiceTask",
            Name = "Calculate",
            Implementation = "calculate"
        };

        dynamic variables = new ExpandoObject();
        variables.Customer = "Ada";

        dynamic outputData = new ExpandoObject();
        outputData.Result = 42;

        var token = new Token
        {
            ProcessInstanceId = processInstanceId,
            CurrentBaseElement = flowNode,
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Active,
            PreviousToken = previousToken,
            Variables = variables,
            OutputData = outputData,
            ParentTokenId = Guid.NewGuid()
        };

        var originalConsoleOut = Console.Out;
        using var consoleWriter = new StringWriter();
        Console.SetOut(consoleWriter);

        try
        {
            var result = token.ToDto();

            result.Id.Should().Be(token.Id);
            result.State.Should().Be(FlowNodeStateDto.Active);
            result.CurrentFlowNodeId.Should().Be("Activity_ServiceTask");
            result.ParentTokenId.Should().Be(token.ParentTokenId);
            result.PreviousTokenId.Should().Be(previousToken.Id);
            result.Variables!.GetValue("Customer").Should().Be("Ada");
            result.OutputData!.GetValue("Result").Should().Be(42);
            result.CurrentFlowElement!.GetValue("Id").Should().Be("Activity_ServiceTask");
            result.CurrentFlowElement.GetValue("Implementation").Should().Be("calculate");
            consoleWriter.ToString().Should().BeEmpty("das manuelle Mapping keine Reflexionsfehler auf die Konsole schreiben soll");
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }

    // Testzweck: Deckt den Fall „Extended User Task Subscription To DTO Should Include Definition And Token Data“ ab.
    [Test]
    public void ExtendedUserTaskSubscription_ToDto_ShouldIncludeDefinitionAndTokenData()
    {
        var processInstanceId = Guid.NewGuid();
        var subscription = new ExtendedUserTaskSubscription
        {
            Id = Guid.NewGuid(),
            Name = "Approve Invoice",
            Token = new Token
            {
                ProcessInstanceId = processInstanceId,
                CurrentBaseElement = new UserTask
                {
                    Id = "Activity_UserTask",
                    Name = "Approve",
                    Implementation = "approve-form"
                },
                ActiveBoundaryEvents = [],
                State = FlowNodeState.Active
            },
            UserCandidates = [Guid.NewGuid()],
            UserGroups = [Guid.NewGuid()],
            CurrenAssignedUser = Guid.NewGuid(),
            ProcessInstanceId = processInstanceId,
            MetaDefinitionId = "invoice-process",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_Invoice",
            DefinitionMetaName = "Invoice Process",
            DefinitionVersion = new Model.Version(2, 3)
        };

        var result = subscription.ToDto();

        result.Name.Should().Be("Approve Invoice");
        result.DefinitionMetaName.Should().Be("Invoice Process");
        result.DefinitionVersion.Should().BeEquivalentTo(new VersionDto { Major = 2, Minor = 3 });
        result.Token.CurrentFlowNodeId.Should().Be("Activity_UserTask");
        result.UserCandidates.Should().BeEquivalentTo(subscription.UserCandidates);
        result.UserGroups.Should().BeEquivalentTo(subscription.UserGroups);
    }

    // Testzweck: Deckt den Fall „BPMN Meta Definition DTO To Model And Back Should Preserve Values“ ab.
    [Test]
    public void BpmnMetaDefinitionDto_ToModel_AndBack_ShouldPreserveValues()
    {
        var dto = new BpmnMetaDefinitionDto
        {
            DefinitionId = "invoice-process",
            Name = "Invoice Process",
            Description = "Prüft eingehende Rechnungen."
        };

        var model = dto.ToModel();
        var roundtrip = model.ToDto();

        roundtrip.Should().BeEquivalentTo(dto);
    }

    // Testzweck: Deckt den Fall „Form DTO To Model And Back Should Preserve Relevant Values“ ab.
    [Test]
    public void FormDto_ToModel_AndBack_ShouldPreserveRelevantValues()
    {
        var dto = new FormDto
        {
            Id = Guid.NewGuid(),
            FormId = Guid.NewGuid(),
            Version = new VersionDto { Major = 1, Minor = 4 },
            FormData = "{\"components\":[]}"
        };

        var model = dto.ToModel();
        var roundtrip = model.ToDto();

        roundtrip.Should().BeEquivalentTo(dto);
    }

    // Testzweck: Deckt den Fall „Form DTO To Model Should Throw When Form Data Is Missing“ ab.
    [Test]
    public void FormDto_ToModel_ShouldThrow_WhenFormDataIsMissing()
    {
        var dto = new FormDto
        {
            Id = Guid.NewGuid(),
            FormId = Guid.NewGuid(),
            Version = new VersionDto { Major = 1, Minor = 4 },
            FormData = null
        };

        var action = () => dto.ToModel();

        action.Should()
            .Throw<ArgumentException>()
            .WithMessage("*FormData is required*");
    }

    // Testzweck: Deckt den Fall „Token To DTO Should Use Empty String For Non Flow Node Tokens“ ab.
    [Test]
    public void Token_ToDto_ShouldUseEmptyString_ForNonFlowNodeTokens()
    {
        var processToken = new Token
        {
            ProcessInstanceId = Guid.NewGuid(),
            CurrentBaseElement = new Process
            {
                Id = "Process_1",
                DefinitionsId = "Definitions_1",
                IsExecutable = true,
                FlowElements = []
            },
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Active
        };

        var result = processToken.ToDto();

        result.CurrentFlowNodeId.Should().BeEmpty();
        result.CurrentFlowElement.Should().BeNull();
    }
    // Testzweck: Token-Zeitstempel (Erzeugung, letzter Statuswechsel) muessen im DTO ankommen,
    // damit Clients einen Verlauf darstellen koennen, ohne das Flow-Element zu parsen.
    [Test]
    public void Token_ToDto_ShouldMapStartAndLastStateChangeTime()
    {
        var token = new Token
        {
            ProcessInstanceId = Guid.NewGuid(),
            CurrentBaseElement = new UserTask { Id = "UserTask_1", Name = "Review", Implementation = "Approval" },
            ActiveBoundaryEvents = [],
            StartTime = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            LastStateChangeTime = new DateTime(2026, 9, 1, 9, 30, 0, DateTimeKind.Utc)
        };

        var dto = token.ToDto();

        dto.StartTime.Should().Be(new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        dto.LastStateChangeTime.Should().Be(new DateTime(2026, 9, 1, 9, 30, 0, DateTimeKind.Utc));
    }

    // Testzweck: Start- und Endzeitpunkt einer Instanz werden aus den Tokens abgeleitet: das aelteste
    // Token markiert den Start, der letzte Statuswechsel einer beendeten Instanz das Ende. Eine
    // laufende Instanz hat kein Ende, eine Instanz ohne Tokens keinen Start.
    [Test]
    public async Task ProcessInstanceInfo_ToDtoAsync_ShouldDeriveStartedAtAndFinishedAtFromTokens()
    {
        var definitionStorage = new EmptyDefinitionStorage();
        var process = new Process { Id = "Process_1", Name = "P", DefinitionsId = "Definitions_1", IsExecutable = true, FlowElements = [] };
        var start = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var finishedInstance = new ProcessInstanceInfo
        {
            InstanceId = Guid.NewGuid(),
            metaDefinitionId = "meta",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_1",
            IsFinished = true,
            State = ProcessInstanceState.Completed,
            MessageSubscriptionCount = 0,
            SignalSubscriptionCount = 0,
            UserTaskSubscriptionCount = 0,
            ServiceSubscriptionCount = 0,
            Tokens =
            [
                new Token { ProcessInstanceId = Guid.NewGuid(), CurrentBaseElement = process, ActiveBoundaryEvents = [], StartTime = start, LastStateChangeTime = end },
                new Token { ProcessInstanceId = Guid.NewGuid(), CurrentBaseElement = process, ActiveBoundaryEvents = [], StartTime = start.AddMinutes(5), LastStateChangeTime = end.AddMinutes(-10) }
            ]
        };
        var runningInstance = new ProcessInstanceInfo
        {
            InstanceId = Guid.NewGuid(),
            metaDefinitionId = "meta",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_1",
            IsFinished = false,
            State = ProcessInstanceState.Waiting,
            MessageSubscriptionCount = 0,
            SignalSubscriptionCount = 0,
            UserTaskSubscriptionCount = 0,
            ServiceSubscriptionCount = 0,
            Tokens = [new Token { ProcessInstanceId = Guid.NewGuid(), CurrentBaseElement = process, ActiveBoundaryEvents = [], StartTime = start, LastStateChangeTime = end }]
        };
        var emptyInstance = new ProcessInstanceInfo
        {
            InstanceId = Guid.NewGuid(),
            metaDefinitionId = "meta",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_1",
            IsFinished = false,
            State = ProcessInstanceState.Waiting,
            MessageSubscriptionCount = 0,
            SignalSubscriptionCount = 0,
            UserTaskSubscriptionCount = 0,
            ServiceSubscriptionCount = 0,
            Tokens = []
        };

        var finishedDto = await finishedInstance.ToDtoAsync(definitionStorage);
        var runningDto = await runningInstance.ToDtoAsync(definitionStorage);
        var emptyDto = await emptyInstance.ToDtoAsync(definitionStorage);

        finishedDto.StartedAt.Should().Be(start);
        finishedDto.FinishedAt.Should().Be(end);
        runningDto.StartedAt.Should().Be(start);
        runningDto.FinishedAt.Should().BeNull();
        emptyDto.StartedAt.Should().BeNull();
        emptyDto.FinishedAt.Should().BeNull();
    }

    // Testzweck: Der Startzeitpunkt eines Tokens muss den Weg durch die JSON-Ablage ueberleben.
    // Ein schreibgeschuetztes Auto-Property wird von Newtonsoft uebersprungen und wuerde bei jedem
    // Laden auf "jetzt" zurueckgesetzt.
    [Test]
    public void Token_StartTime_ShouldSurviveJsonRoundTrip()
    {
        var start = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var token = new Token
        {
            ProcessInstanceId = Guid.NewGuid(),
            CurrentBaseElement = new UserTask { Id = "UserTask_1", Name = "Review", Implementation = "Approval" },
            ActiveBoundaryEvents = [],
            StartTime = start
        };
        var settings = new Newtonsoft.Json.JsonSerializerSettings
        {
            TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Auto,
            TypeNameAssemblyFormatHandling = Newtonsoft.Json.TypeNameAssemblyFormatHandling.Simple
        };

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(token, settings);
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Token>(json, settings)!;

        restored.StartTime.Should().Be(start);
    }

    // Testzweck: Der Zeitpunkt des letzten Statuswechsels muss den Weg durch die JSON-Ablage
    // ueberleben, auch wenn `State` in der Datei nach `LastStateChangeTime` steht. Der State-Setter
    // wuerde den Zeitstempel sonst beim Laden auf "jetzt" setzen und FinishedAt verfaelschen.
    [Test]
    public void Token_LastStateChangeTime_ShouldSurviveJsonRoundTrip_RegardlessOfPropertyOrder()
    {
        var changedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var token = new Token
        {
            ProcessInstanceId = Guid.NewGuid(),
            CurrentBaseElement = new UserTask { Id = "UserTask_1", Name = "Review", Implementation = "Approval" },
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Completed
        };
        token.LastStateChangeTime = changedAt;
        var settings = new Newtonsoft.Json.JsonSerializerSettings
        {
            TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Auto,
            TypeNameAssemblyFormatHandling = Newtonsoft.Json.TypeNameAssemblyFormatHandling.Simple
        };

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(token, settings);
        var stateIndex = json.IndexOf("\"State\"", StringComparison.Ordinal);
        var changeIndex = json.IndexOf("\"LastStateChangeTime\"", StringComparison.Ordinal);
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Token>(json, settings)!;

        changeIndex.Should().BeGreaterThan(stateIndex, "LastStateChangeTime must be written after State");
        restored.State.Should().Be(FlowNodeState.Completed);
        restored.LastStateChangeTime.Should().Be(changedAt);
    }

    // Testzweck: Form-Key, Faelligkeit, Wiedervorlage und Prioritaet stehen nur am BPMN-Element des
    // User-Tasks und muessen flach in das erweiterte Subscription-DTO gehoben werden.
    [Test]
    public void ExtendedUserTaskSubscription_ToDto_ShouldExposeFormKeyAndSchedule()
    {
        var subscription = new ExtendedUserTaskSubscription
        {
            Id = Guid.NewGuid(),
            Name = "Review",
            Token = new Token
            {
                ProcessInstanceId = Guid.NewGuid(),
                CurrentBaseElement = new UserTask
                {
                    Id = "UserTask_1",
                    Name = "Review",
                    Implementation = "Approval:1.0",
                    FlowzerDueDate = "2026-10-01T10:00:00Z",
                    FlowzerFollowUpDate = "2026-09-28T10:00:00Z",
                    FlowzerPriority = "high"
                },
                ActiveBoundaryEvents = []
            },
            MetaDefinitionId = "meta",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_1"
        };

        var dto = subscription.ToDto();

        dto.FormKey.Should().Be("Approval:1.0");
        dto.DueDate.Should().Be("2026-10-01T10:00:00Z");
        dto.FollowUpDate.Should().Be("2026-09-28T10:00:00Z");
        dto.Priority.Should().Be("high");
    }

    private sealed class EmptyDefinitionStorage : StorageSystem.IDefinitionStorage
    {
        public Task StoreBinary(Guid guid, string data) => throw new NotSupportedException();
        public Task<string> GetBinary(Guid guid) => throw new NotSupportedException();
        public Task<Guid[]> GetAllBinaryDefinitions() => throw new NotSupportedException();
        public Task<BpmnDefinition[]> GetAllDefinitions() => throw new NotSupportedException();
        public Task StoreDefinition(BpmnDefinition definition) => throw new NotSupportedException();
        public Task<Model.Version?> GetMaxVersionId(string modelId) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetDefinitionById(Guid id) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetLatestDefinition(string definitionId) => throw new NotSupportedException();
        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId) => throw new NotSupportedException();
        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => Task.FromResult(Array.Empty<ExtendedBpmnMetaDefinition>());
        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => throw new NotSupportedException();
        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => throw new NotSupportedException();
        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => throw new NotSupportedException();
    }
}
