using System.Dynamic;
using BPMN.Common;
using BPMN.Events;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Task = System.Threading.Tasks.Task;
using FluentAssertions;
using FluentAssertions.Execution;

namespace core_engine_tests;

public class EngineTest
{
    [Test]
    public async Task StartStopWithVariables()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("StartStopWithVariables.bpmn");

        //should wait for service task
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);

        //should have one active service task
        var serviceTaskToken = instanceEngine.GetActiveServiceTasks().ToArray().First();
        var serviceTask = serviceTaskToken.CurrentFlowNode as ServiceTask;
        serviceTask?.Id.Should().Be("ServiceTask_1");

        //inject service result
        var variables = (ExpandoObject?)new
        {
            ServiceResult = "World123"
        }.ToDynamic();
        instanceEngine.HandleTaskResult(serviceTaskToken.Id, variables);
        
        using (new AssertionScope())
        {
            instanceEngine.Tokens.Should().HaveCount(4);
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            //check if global variable is set
            instanceEngine.MasterToken.Variables.GetValue<string>("GlobalResult").Should().Be("World123");
        }
    }

    [Test]
    public async Task VariableMappingTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("VariablesTest.bpmn");

        //should have one active service task
        var serviceTaskToken = instanceEngine.GetActiveServiceTasks().ToArray().First();


        serviceTaskToken.Variables!.GetValue<string>("Firstname").Should().Be("Lukas");

        var variables = (ExpandoObject)new
        {
            Out = new
            {
                Firstname = "LukasNeu"
            }
        }.ToDynamic()!;
        instanceEngine.HandleTaskResult(serviceTaskToken.Id, variables);

        //check if global variable is set
        instanceEngine.MasterToken.Variables.GetValue<string>("Order.Address.NewFirstname").Should().Be("LukasNeu");
    }


    [Test]
    public async Task ConditionalSequenceFlowTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ConditionalSequenceFlow.bpmn");
        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instanceEngine.Tokens.Should().HaveCount(4);
            instanceEngine.Tokens.Count(t => t.CurrentFlowNode?.Name == "ShouldReached").Should().Be(1);
            instanceEngine.Tokens.Count(t => t.CurrentFlowNode?.Name == "ShouldNotReached").Should().Be(0);
        }
    }

    [Test]
    public async Task ParallelGatewayTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("GatewayJoin.bpmn");
        Helper.DoTestServiceThings(instanceEngine);

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);

        var endEventTokens = instanceEngine.Tokens.Where(t => t.CurrentFlowNode is EndEvent).ToList();

        endEventTokens.Should().ContainSingle();
    }


    [Test]
    public async Task ParallelGatewayComplexTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("GatewayJoinComplex.bpmn");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);

        var tokensAtParallelGateway =
            instanceEngine.ActiveTokens.Where(x => x.CurrentBaseElement.Id == "ParallelGateway").ToArray();
        using (new AssertionScope())
        {
            tokensAtParallelGateway.Should().ContainSingle();
            instanceEngine.Tokens.Count(t => t.CurrentFlowNode is EndEvent).Should().Be(0);
        }


        //do step1
        instanceEngine.HandleServiceTaskResult("step1");
        tokensAtParallelGateway =
            instanceEngine.ActiveTokens.Where(x => x.CurrentBaseElement.Id == "ParallelGateway").ToArray();
        using (new AssertionScope())
        {
           tokensAtParallelGateway.Should().HaveCount(2);
           tokensAtParallelGateway.All(x =>
                x.LastSequenceFlow?.Id == tokensAtParallelGateway.First().LastSequenceFlow?.Id).Should().BeTrue();
            instanceEngine.Tokens.Count(t => t.CurrentFlowNode is EndEvent).Should().Be(0);
        }


        //do step2
        instanceEngine.HandleServiceTaskResult("step2");
        tokensAtParallelGateway =
            instanceEngine.ActiveTokens.Where(x => x.CurrentBaseElement.Id == "ParallelGateway").ToArray();
        using (new AssertionScope())
        {
            tokensAtParallelGateway.Should().ContainSingle();
            instanceEngine.Tokens.Count(t => t.CurrentFlowNode is EndEvent).Should().Be(1);
        }


        //do step3
        instanceEngine.HandleServiceTaskResult("step3");
        using (new AssertionScope())
        {
            instanceEngine.ActiveTokens.Should().BeEmpty();
            instanceEngine.Tokens.Count(t => t.CurrentFlowNode is EndEvent).Should().Be(2);

            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
        }
    }

    [Test]
    public async Task ExklusiveGatewayTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ExklusiveGateway.bpmn");
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.ProcessInstanceState, Is.EqualTo(ProcessInstanceState.Completed));
            Assert.That(instanceEngine.Tokens.Count(t => t.CurrentFlowNode?.Name == "ShouldReached"),
                Is.EqualTo(1));
            Assert.That(instanceEngine.Tokens.Count(t => t.CurrentFlowNode?.Name == "ShouldNotReached"),
                Is.EqualTo(0));
        });
    }

    [Test]
    public async Task TerminateEndEventTest()
    {
        const string filename = "TerminateEndEvent.bpmn";
        var instanceEngine = await Helper.StartFirstProcessOfFile(filename);
        Assert.That(instanceEngine.ProcessInstanceState, Is.EqualTo(ProcessInstanceState.Waiting));
        instanceEngine.HandleServiceTaskResult("stepEnd");
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.ProcessInstanceState, Is.EqualTo(ProcessInstanceState.Waiting));
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(2));
        });

        instanceEngine = await Helper.StartFirstProcessOfFile(filename);
        Assert.That(instanceEngine.ProcessInstanceState, Is.EqualTo(ProcessInstanceState.Waiting));
        instanceEngine.HandleServiceTaskResult("stepTerminate");
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.ProcessInstanceState, Is.EqualTo(ProcessInstanceState.Terminated));
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ParallelTaskTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ParallelFlowTest.bpmn");
        var activeTokens = instanceEngine.GetActiveServiceTasks().Where(x => x.CurrentFlowNode?.Name == "Test")
            .ToArray();
        activeTokens.Should().HaveCount(4); // 3 Mitarbeiter + MultiInstance Token
        using(new AssertionScope())
        {
            var vornamen = activeTokens
                .Where(x => x.CurrentFlowNode is Activity { LoopCharacteristics: null })
                .Select(t => (string?) t.Variables?.GetValue("Person")?.GetValue("Vorname"))
                .ToArray();
            vornamen.Should().Contain(["Lukas", "Christian", "Max"]);
        }

        var activeTokensWithoutLoop = activeTokens
            .Where(x => x.CurrentFlowNode is Activity { LoopCharacteristics: null }).ToArray();

        int[] order = [2, 0, 1];
        foreach (var i in order)
        {
            var activeToken = activeTokensWithoutLoop[i];
            var data = new ExpandoObject();
            data.SetValue("OutProperty", activeToken.Variables.GetValue("Person"));
            instanceEngine.HandleTaskResult(activeToken.Id, data);
            instanceEngine.ProcessInstanceState.Should().Be(
                i == 1 // Letzter Eintrag in der Liste
                    ? ProcessInstanceState.Completed
                    : ProcessInstanceState.Waiting);
        }
        
        var outList = (List<object>?)instanceEngine.MasterToken.Variables.GetValue("MitarbeiterOut");
        outList.Should().HaveCount(3);
        
        using(new AssertionScope())
        {
            //check if the order is correct
            outList.Select(x => x.GetValue("Vorname")?.ToString()).Should()
                .ContainInOrder(["Lukas", "Christian", "Max"]);
            instanceEngine.Tokens.Should().OnlyContain(x => x.State == FlowNodeState.Completed);
        }
        // Assert.Multiple(() =>
        // {
        //     Assert.That(outList.SingleOrDefault(x => x.GetValue("Vorname")?.ToString() == "Lukas"), Is.Not.Null);
        //     Assert.That(outList.SingleOrDefault(x => x.GetValue("Vorname")?.ToString() == "Christian"), Is.Not.Null);
        //     Assert.That(outList.SingleOrDefault(x => x.GetValue("Vorname")?.ToString() == "Max"), Is.Not.Null);
        //
        //     //check if the order is correct
        //     Assert.That(outList.IndexOf(outList.Single(x => x.GetValue("Vorname")?.ToString() == "Lukas")),
        //         Is.EqualTo(0));
        //     Assert.That(outList.IndexOf(outList.Single(x => x.GetValue("Vorname")?.ToString() == "Christian")),
        //         Is.EqualTo(1));
        //     Assert.That(outList.IndexOf(outList.Single(x => x.GetValue("Vorname")?.ToString() == "Max")),
        //         Is.EqualTo(2));
        //
        //     Assert.That(instanceEngine.Instance.Tokens.All(x => x.State == FlowNodeState.Completed));
        // });
    }

    [Test]
    public async Task SequentialTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("SequentialTest.bpmn");
        
        
        var index = 0;
        while (index < 100) // Endlosschleife verhindern
        {
            var sequenzialChildToken = instanceEngine.GetActiveServiceTasks()
                .Where(x => x.CurrentFlowNode?.Name == "Test" 
                            && x.ParentTokenId is not null 
                            && x.ParentTokenId != instanceEngine.MasterToken.Id 
                            && x.State == FlowNodeState.Active)
                .ToArray();
            if (sequenzialChildToken.Length == 0)
                break;

            using (new AssertionScope())
            {
                instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
                sequenzialChildToken.Should().ContainSingle();
            }
            
            var token = sequenzialChildToken[0];
            var data = new ExpandoObject();
            data.SetValue("OutProperty", token.Variables.GetValue("Person"));
            instanceEngine.HandleTaskResult(token.Id, data);
            index++;
        }

        using (new AssertionScope())
        {
            index.Should().Be(3);
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
        }

        var outList = (List<object>)instanceEngine.MasterToken.Variables.GetValue("MitarbeiterOut")!;
        outList.Should().HaveCount(3);
        using(new AssertionScope())
        {
            //check if the order is correct
            outList.Select(x => x.GetValue("Vorname")?.ToString()).Should()
                .ContainInOrder(["Lukas", "Christian", "Max"]);
            instanceEngine.Tokens.Should().OnlyContain(x => x.State == FlowNodeState.Completed);
        }
    }

    [Test]
    public async Task ParallelTaskWithCompletionConditionTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ParallelFlowWithCompletingConditionTest.bpmn");
        var activeTokens = instanceEngine.GetActiveUserTasks().ToArray();

        var multiInstanceToken = activeTokens.SingleOrDefault(x => x.CurrentFlowNode is Activity
        {
            LoopCharacteristics: MultiInstanceLoopCharacteristics
        });
        var lukasTask = activeTokens.SingleOrDefault(x => x.Variables?.GetValue("Person.Vorname")?.ToString() == "Lukas");
        var christianTask = activeTokens.SingleOrDefault(x => x.Variables?.GetValue("Person.Vorname")?.ToString() == "Christian");
        var maxTask = activeTokens.SingleOrDefault(x => x.Variables?.GetValue("Person.Vorname")?.ToString() == "Max");
        
        using (new AssertionScope())
        {
            activeTokens.Should().HaveCount(4);
            multiInstanceToken.Should().NotBeNull();
            lukasTask.Should().NotBeNull();
            christianTask.Should().NotBeNull();
            maxTask.Should().NotBeNull();
            
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
        }

        instanceEngine.HandleTaskResult(lukasTask!.Id, new {HatZeit = false}.ToExpando());
        using (new AssertionScope())
        {
            instanceEngine.GetActiveUserTasks().Should().HaveCount(3);
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            var zeitInfo = multiInstanceToken!.Variables.GetValue("MitarbeiterZeitInfo");
            zeitInfo.Should().BeNull();
        }
        
        instanceEngine.HandleTaskResult(christianTask!.Id, new {HatZeit = true}.ToExpando());
        using (new AssertionScope())
        {
            instanceEngine.GetActiveUserTasks().Should().HaveCount(2);
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            var zeitInfo = multiInstanceToken!.Variables.GetValue("MitarbeiterZeitInfo");
            zeitInfo.Should().NotBeNull();
        }
        
    }
    
    [Test]
    public async Task SimpleTimerTest()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/SimpleTimerEvent.bpmn", FileMode.Open));
        var process = model.GetProcesses();
        var processEngine = new ProcessEngine(process.First());
        var activeTimers = processEngine.ActiveTimers.ToArray();
        activeTimers.Should().HaveCount(1);
        activeTimers.Single().Should().BeCloseTo(DateTime.Now.AddSeconds(2), new TimeSpan(0, 0, 0, 0, 100));

        // var instanceEngine = await processEngine.HandleTime(DateTime.Now);
        // activeTimers = instanceEngine.ActiveTimers.ToArray();
        // activeTimers.Should().HaveCount(0);
        //
    }
    
    [Test]
    public async Task SubProcessTest()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/SubProcess.bpmn", FileMode.Open));
        var process = model.GetProcesses().Single();
        var instanceEngine = new ProcessEngine(process).StartProcess();
        
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
        instanceEngine.Tokens.Should().HaveCount(5);
        
        instanceEngine.HandleServiceTaskResult("sub1_step1");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
        instanceEngine.Tokens.Should().HaveCount(9);
        
        instanceEngine.HandleServiceTaskResult("sub2_step1");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
        instanceEngine.Tokens.Should().HaveCount(12);
    }
}