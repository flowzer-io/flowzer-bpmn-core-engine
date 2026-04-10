using System.Reflection;
using BPMN.Activities;
using BPMN.Flowzer;
using BPMN.Process;
using core_engine.Exceptions;
using FluentAssertions;
using Flowzer.Shared;
using Model;
using Variables = System.Dynamic.ExpandoObject;

namespace core_engine_tests;

public class InstanceEngineOutputMappingTest
{
    [Test]
    public void PrepareOutputData_ShouldInitializeSubProcessVariablesForMultiInstancePropagation()
    {
        var outputCollection = new List<object?> { "Lukas", "Christian" };
        var instanceEngine = CreateInstanceEngineWithMultiInstanceToken(outputCollection, outputMappings: null);
        var multiInstanceToken = instanceEngine.Tokens.Single(token => token.ParentTokenId == instanceEngine.Tokens[1].Id);

        InvokePrepareOutputData(instanceEngine, multiInstanceToken);

        var subProcessToken = instanceEngine.Tokens[1];
        subProcessToken.Variables.Should().NotBeNull();
        ((List<object?>?)subProcessToken.Variables!.GetValue("MitarbeiterOut")).Should().BeEquivalentTo(outputCollection);
    }

    [Test]
    public void PrepareOutputData_ShouldApplyOutputMappingsForMultiInstanceTokens()
    {
        var outputCollection = new List<object?> { "Lukas", "Christian" };
        var outputMappings = new FlowzerList<FlowzerIoMapping>
        {
            new("=MitarbeiterOut", "GemappteMitarbeiter")
        };

        var instanceEngine = CreateInstanceEngineWithMultiInstanceToken(outputCollection, outputMappings);
        var multiInstanceToken = instanceEngine.Tokens.Single(token => token.ParentTokenId == instanceEngine.Tokens[1].Id);

        InvokePrepareOutputData(instanceEngine, multiInstanceToken);

        ((List<object?>?)instanceEngine.MasterToken.Variables!.GetValue("GemappteMitarbeiter"))
            .Should()
            .BeEquivalentTo(outputCollection);
    }

    [Test]
    public void GetCorrectVariablesToken_ShouldThrowHelpfulException_WhenParentScopeIsRequestedWithoutParent()
    {
        var processInstanceId = Guid.NewGuid();
        var process = new Process
        {
            Id = "Process_1",
            DefinitionsId = "Definitions_1",
            Name = "Test Process",
            IsExecutable = true,
            FlowElements = []
        };

        var masterToken = new Token
        {
            ProcessInstanceId = processInstanceId,
            CurrentBaseElement = process,
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Active,
            Variables = new Variables()
        };

        var instanceEngine = new InstanceEngine([masterToken], Helper.TestFlowzerConfig);
        var action = () => instanceEngine.GetCorrectVariablesToken(masterToken, "MitarbeiterOut", includeCurrentToken: false);

        action.Should()
            .Throw<FlowzerRuntimeException>()
            .WithMessage("*kein ParentToken*");
    }

    private static InstanceEngine CreateInstanceEngineWithMultiInstanceToken(
        List<object?> outputCollection,
        FlowzerList<FlowzerIoMapping>? outputMappings)
    {
        var processInstanceId = Guid.NewGuid();
        var process = new Process
        {
            Id = "Process_1",
            DefinitionsId = "Definitions_1",
            Name = "Test Process",
            IsExecutable = true,
            FlowElements = []
        };

        var subProcess = new SubProcess
        {
            Id = "Activity_SubProcess",
            Name = "SubProcess",
            FlowElements = []
        };

        var multiInstanceServiceTask = new ServiceTask
        {
            Id = "Activity_MultiInstance",
            Name = "Multi Instance Task",
            Implementation = "InputAsOutput",
            OutputMappings = outputMappings,
            LoopCharacteristics = new MultiInstanceLoopCharacteristics
            {
                Behavior = MultiInstanceBehavior.All,
                FlowzerLoopCharacteristics = new FlowzwerLoopCharacteristics
                {
                    InputCollection = Array.Empty<object>(),
                    OutputCollection = "MitarbeiterOut",
                    OutputElement = "=OutProperty"
                }
            }
        };

        var masterToken = new Token
        {
            ProcessInstanceId = processInstanceId,
            CurrentBaseElement = process,
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Active,
            Variables = new Variables()
        };

        var subProcessToken = new Token
        {
            ProcessInstanceId = processInstanceId,
            ParentTokenId = masterToken.Id,
            CurrentBaseElement = subProcess,
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Active
        };

        var multiInstanceToken = new Token
        {
            ProcessInstanceId = processInstanceId,
            ParentTokenId = subProcessToken.Id,
            CurrentBaseElement = multiInstanceServiceTask,
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Completed,
            OutputData = new Variables()
        };

        multiInstanceToken.OutputData.SetValue("MitarbeiterOut", outputCollection);

        return new InstanceEngine([masterToken, subProcessToken, multiInstanceToken], Helper.TestFlowzerConfig);
    }

    private static void InvokePrepareOutputData(InstanceEngine instanceEngine, Token token)
    {
        var prepareOutputDataMethod = typeof(InstanceEngine).GetMethod(
            "PrepareOutputData",
            BindingFlags.Instance | BindingFlags.NonPublic);

        prepareOutputDataMethod.Should().NotBeNull("die private PrepareOutputData-Methode für den Regressionstest erreichbar sein muss");
        prepareOutputDataMethod!.Invoke(instanceEngine, [token]);
    }
}
