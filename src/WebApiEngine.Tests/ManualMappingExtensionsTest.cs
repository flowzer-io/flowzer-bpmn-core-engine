using System.Dynamic;
using BPMN.Activities;
using BPMN.HumanInteraction;
using BPMN.Process;
using FluentAssertions;
using Flowzer.Shared;
using Model;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

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
}
