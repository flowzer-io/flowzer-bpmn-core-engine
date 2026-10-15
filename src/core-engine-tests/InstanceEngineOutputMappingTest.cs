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
    // Testzweck: Opt-in bleibt an explizite Subprozess-Eingänge gebunden. Ein historischer
    // Subprozess mit lokaler Zielvariable, aber ohne Eingänge behält seine lokale Ausgabe.
    [Test]
    public void UnmappedSubProcessOutput_ShouldRetainItsHistoricalLocalTarget()
    {
        var id = Guid.NewGuid();
        var master = new Token
        {
            ProcessInstanceId = id,
            CurrentBaseElement = new Process { Id = "Root", DefinitionsId = "Legacy", IsExecutable = true, FlowElements = [] },
            ActiveBoundaryEvents = [], Variables = new Variables(), State = FlowNodeState.Active
        };
        master.Variables.SetValue("legacy", "root-unchanged");
        var scope = new Token
        {
            ProcessInstanceId = id, ParentTokenId = master.Id,
            CurrentBaseElement = new SubProcess
            {
                Id = "LegacyScope", Name = "Legacy scope", FlowElements = [],
                OutputMappings = [new("=answer", "legacy")]
            },
            ActiveBoundaryEvents = [], Variables = new Variables(), OutputData = new Variables(), State = FlowNodeState.Completed
        };
        scope.Variables.SetValue("legacy", "old-local");
        scope.OutputData.SetValue("answer", "updated-local");
        InvokePrepareOutputData(new InstanceEngine([master, scope], Helper.TestFlowzerConfig), scope);
        scope.Variables.GetValue("legacy").Should().Be("updated-local");
        master.Variables.GetValue("legacy").Should().Be("root-unchanged");
    }

    // Testzweck: Deckt den Fall „Prepare Output Data Should Initialize Sub Process Variables For Multi Instance Propagation“ ab.
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

    // Testzweck: Deckt den Fall „Prepare Output Data Should Apply Output Mappings For Multi Instance Tokens“ ab.
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

    // Testzweck: In einem ausdrücklich gemappten Subprozess bleiben sowohl MI-
    // Aggregation als auch die deklarierte Ausgabe lokal; historische Root-Ausgaben
    // der beiden unveränderten Tests darüber bleiben weiterhin maßgeblich.
    [Test]
    public void MappedSubProcess_ShouldKeepMultiInstanceAggregateAndMappedOutputLocal()
    {
        var values = new List<object?> { "Synthetic first", "Synthetic second" };
        var instance = CreateInstanceEngineWithMultiInstanceToken(values,
            new FlowzerList<FlowzerIoMapping> { new("=MitarbeiterOut", "GemappteMitarbeiter") }, mappedScope: true);
        var scope = instance.Tokens[1];
        InvokePrepareOutputData(instance, instance.Tokens.Single(token => token.ParentTokenId == scope.Id));
        ((List<object?>?)scope.Variables!.GetValue("MitarbeiterOut")).Should().BeEquivalentTo(values);
        ((List<object?>?)scope.Variables!.GetValue("GemappteMitarbeiter")).Should().BeEquivalentTo(values);
        ((IDictionary<string, object?>)instance.MasterToken.Variables!).Should().NotContainKey("MitarbeiterOut")
            .And.NotContainKey("GemappteMitarbeiter");
    }

    // Testzweck: Deckt den Fall „Get Correct Variables Token Should Throw Helpful Exception When Parent Scope Is Requested Without Parent“ ab.
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
        FlowzerList<FlowzerIoMapping>? outputMappings, bool mappedScope = false)
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
            FlowElements = [],
            InputMappings = mappedScope ? new FlowzerList<FlowzerIoMapping> { new("=marker", "marker") } : null
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
