using System.Dynamic;
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
        instanceEngine.Instance.State.Should().Be(ProcessInstanceState.Waiting);

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

        //should be completed
        instanceEngine.Instance.State.Should().Be(ProcessInstanceState.Completed);

        //check if global variable is set
        instanceEngine.Instance.ProcessVariables.GetValue<string>("GlobalResult").Should().Be("World123");
        using (new AssertionScope())
        {
            instanceEngine.Instance.State.Should().Be(ProcessInstanceState.Completed);
            instanceEngine.Instance.Tokens.Should().HaveCount(3);
        }
    }

    [Test]
    public async Task VariableMappingTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("VariablesTest.bpmn");

        //should have one active service task
        var serviceTaskToken = instanceEngine.GetActiveServiceTasks().ToArray().First();


        serviceTaskToken.InputData!.GetValue<string>("Firstname").Should().Be("Lukas");

        var variables = (ExpandoObject)new
        {
            Out = new
            {
                Firstname = "LukasNeu"
            }
        }.ToDynamic()!;
        instanceEngine.HandleTaskResult(serviceTaskToken.Id, variables);

        //check if global variable is set
        instanceEngine.Instance.ProcessVariables.GetValue<string>("Order.Address.NewFirstname").Should().Be("LukasNeu");
    }


    [Test]
    public async Task ConditionalSequenceFlowTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ConditionalSequenceFlow.bpmn");
        using (new AssertionScope())
        {
            instanceEngine.Instance.State.Should().Be(ProcessInstanceState.Completed);
            instanceEngine.Instance.Tokens.Should().HaveCount(3);
            instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode.Name == "ShouldReached").Should().Be(1);
            instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode.Name == "ShouldNotReached").Should().Be(0);
        }
    }

    [Test]
    public async Task ParallelGatewayTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("GatewayJoin.bpmn");
        Helper.DoTestServiceThings(instanceEngine);

        instanceEngine.Instance.State.Should().Be(ProcessInstanceState.Completed);

        var endEventTokens = instanceEngine.Instance.Tokens.Where(t => t.CurrentFlowNode is EndEvent).ToList();

        endEventTokens.Should().ContainSingle();
    }


    [Test]
    public async Task ParallelGatewayComplexTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("GatewayJoinComplex.bpmn");
        instanceEngine.Instance.State.Should().Be(ProcessInstanceState.Waiting);

        var tokensAtParallelGateway =
            instanceEngine.ActiveTokens.Where(x => x.CurrentFlowNode.Id == "ParallelGateway").ToArray();
        using (new AssertionScope())
        {
            tokensAtParallelGateway.Should().ContainSingle();
            instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode is EndEvent).Should().Be(0);
        }


        //do step1
        instanceEngine.HandleServiceTaskResult("step1");
        tokensAtParallelGateway =
            instanceEngine.ActiveTokens.Where(x => x.CurrentFlowNode.Id == "ParallelGateway").ToArray();
        using (new AssertionScope())
        {
           tokensAtParallelGateway.Should().HaveCount(2);
           tokensAtParallelGateway.All(x =>
                x.LastSequenceFlow?.Id == tokensAtParallelGateway.First().LastSequenceFlow?.Id).Should().BeTrue();
            instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode is EndEvent).Should().Be(0);
        }


        //do step2
        instanceEngine.HandleServiceTaskResult("step2");
        tokensAtParallelGateway =
            instanceEngine.ActiveTokens.Where(x => x.CurrentFlowNode.Id == "ParallelGateway").ToArray();
        using (new AssertionScope())
        {
            tokensAtParallelGateway.Should().ContainSingle();
            instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode is EndEvent).Should().Be(1);
        }


        //do step3
        instanceEngine.HandleServiceTaskResult("step3");
        using (new AssertionScope())
        {
            instanceEngine.ActiveTokens.Should().BeEmpty();
            instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode is EndEvent).Should().Be(2);

            instanceEngine.Instance.State.Should().Be(ProcessInstanceState.Completed);
        }
    }

    [Test]
    public async Task ExklusiveGatewayTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ExklusiveGateway.bpmn");
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
            Assert.That(instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode.Name == "ShouldReached"),
                Is.EqualTo(1));
            Assert.That(instanceEngine.Instance.Tokens.Count(t => t.CurrentFlowNode.Name == "ShouldNotReached"),
                Is.EqualTo(0));
        });
    }

    [Test]
    public async Task TerminateEndEventTest()
    {
        const string filename = "TerminateEndEvent.bpmn";
        var instanceEngine = await Helper.StartFirstProcessOfFile(filename);
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
        instanceEngine.HandleServiceTaskResult("stepEnd");
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(1));
        });

        instanceEngine = await Helper.StartFirstProcessOfFile(filename);
        Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
        instanceEngine.HandleServiceTaskResult("stepTerminate");
        Assert.Multiple(() =>
        {
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Terminated));
            Assert.That(instanceEngine.ActiveTokens.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ParallelTaskTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ParallelFlowTest.bpmn");
        var activeTokens = instanceEngine.GetActiveServiceTasks().Where(x => x.CurrentFlowNode.Name == "Test")
            .ToArray();
        Assert.That(activeTokens, Has.Length.EqualTo(3)); //3 Mitarbeiter
        Assert.Multiple(() =>
        {
            Assert.That(
                activeTokens.SingleOrDefault(x => x.InputData?.GetValue("Person.Vorname")?.ToString() == "Lukas"),
                Is.Not.Null);
            Assert.That(
                activeTokens.SingleOrDefault(x => x.InputData?.GetValue("Person.Vorname")?.ToString() == "Christian"),
                Is.Not.Null);
            Assert.That(activeTokens.SingleOrDefault(x => x.InputData?.GetValue("Person.Vorname")?.ToString() == "Max"),
                Is.Not.Null);
            Assert.That(activeTokens, Has.Length.EqualTo(3));

            Assert.That(
                activeTokens.Single(x => x.InputData?.GetValue("Person.Vorname")?.ToString() == "Lukas").InputData
                    .GetValue("loopCounter"), Is.EqualTo(1));
            Assert.That(
                activeTokens.Single(x => x.InputData?.GetValue("Person.Vorname")?.ToString() == "Christian").InputData
                    .GetValue("loopCounter"), Is.EqualTo(2));
            Assert.That(
                activeTokens.Single(x => x.InputData?.GetValue("Person.Vorname")?.ToString() == "Max").InputData
                    .GetValue("loopCounter"), Is.EqualTo(3));
        });

        var activeTokenList = activeTokens.OrderBy(_ => new Random().Next(0, 1000)).ToArray();
        foreach (var activeToken in activeTokenList)
        {
            var isLast = activeToken == activeTokenList.Last();
            var data = new ExpandoObject();
            data.SetValue("OutProperty", activeToken.InputData.GetValue("Person"));
            instanceEngine.HandleTaskResult(activeToken.Id, data);
            Assert.That(instanceEngine.Instance.State,
                isLast ? Is.EqualTo(ProcessInstanceState.Completed) : Is.EqualTo(ProcessInstanceState.Waiting));
        }

        var outList = (List<object>?)instanceEngine.Instance.ProcessVariables.GetValue("MitarbeiterOut");
        Assert.That(outList, Is.Not.Null);
        Assert.That(outList, Has.Count.EqualTo(3));

        Assert.Multiple(() =>
        {
            Assert.That(outList.SingleOrDefault(x => x.GetValue("Vorname")?.ToString() == "Lukas"), Is.Not.Null);
            Assert.That(outList.SingleOrDefault(x => x.GetValue("Vorname")?.ToString() == "Christian"), Is.Not.Null);
            Assert.That(outList.SingleOrDefault(x => x.GetValue("Vorname")?.ToString() == "Max"), Is.Not.Null);

            //check if the order is correct
            Assert.That(outList.IndexOf(outList.Single(x => x.GetValue("Vorname")?.ToString() == "Lukas")),
                Is.EqualTo(0));
            Assert.That(outList.IndexOf(outList.Single(x => x.GetValue("Vorname")?.ToString() == "Christian")),
                Is.EqualTo(1));
            Assert.That(outList.IndexOf(outList.Single(x => x.GetValue("Vorname")?.ToString() == "Max")),
                Is.EqualTo(2));

            Assert.That(instanceEngine.Instance.Tokens.All(x => x.State == FlowNodeState.Completed));
        });
    }

    [Test]
    public async Task SequentialTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("SequentialTest.bpmn");

        var index = 0;
        while (true)
        {
            var activeTokens = instanceEngine.GetActiveServiceTasks().Where(x => x.CurrentFlowNode.Name == "Test")
                .ToArray();
            if (activeTokens.Length == 0)
                break;

            Assert.Multiple(() =>
            {
                Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
                Assert.That(activeTokens, Has.Length.EqualTo(1));
            });

            var token = activeTokens.Single();

            var data = new ExpandoObject();
            data.SetValue("OutProperty", token.InputData.GetValue("Person"));
            instanceEngine.HandleTaskResult(token.Id, data);
            index++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(index, Is.EqualTo(3));
            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Completed));
        });

        var outList = (List<object>)instanceEngine.Instance.ProcessVariables.GetValue("MitarbeiterOut")!;
        Assert.That(outList, Is.Not.Null);
        Assert.That(outList, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            //check if the order is correct
            Assert.That(outList.IndexOf(outList.Single(x => x.GetValue("Vorname")?.ToString() == "Lukas")),
                Is.EqualTo(0));
            Assert.That(outList.IndexOf(outList.Single(x => x.GetValue("Vorname")?.ToString() == "Christian")),
                Is.EqualTo(1));
            Assert.That(outList.IndexOf(outList.Single(x => x.GetValue("Vorname")?.ToString() == "Max")),
                Is.EqualTo(2));

            Assert.That(instanceEngine.Instance.Tokens.All(x => x.State == FlowNodeState.Completed));
        });
    }

    [Test]
    public async Task ParallelTaskWithCompletionConditionTest()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ParallelFlowWithCompletingConditionTest.bpmn");
        var activeTokens = instanceEngine.GetActiveServiceTasks()
            .Where(x => x.CurrentFlowNode.Name == "Test")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(activeTokens.Single(x => x.InputData?.GetValue("Person.Vorname")?.ToString() == "Lukas"),
                Is.Not.Null);
            Assert.That(activeTokens.Single(x => x.InputData?.GetValue("Person.Vorname")?.ToString() == "Christian"),
                Is.Not.Null);
            Assert.That(activeTokens.All(x => x.InputData?.GetValue("Person.Vorname")?.ToString() != "Max"));

            Assert.That(activeTokens, Has.Length.EqualTo(2));

            Assert.That(instanceEngine.Instance.State, Is.EqualTo(ProcessInstanceState.Waiting));
        });
    }
    
    
      
    [Test]
    public async Task SimpleTimerTest()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/SimpleTimerEvent.bpmn", FileMode.Open));
        var process = model.GetProcesses();
        var processEngine = new ProcessEngine(process.First());
        var activeTimers = processEngine.GetActiveTimers().ToArray();
        activeTimers.Should().HaveCount(1);
        activeTimers.Single().Should().BeCloseTo(DateTime.Now.AddSeconds(2), new TimeSpan(0, 0, 0, 0, 100));

        // var instanceEngine = await processEngine.HandleTime(DateTime.Now);
        // activeTimers = instanceEngine.ActiveTimers.ToArray();
        // activeTimers.Should().HaveCount(0);
        //


    }
    
    
}