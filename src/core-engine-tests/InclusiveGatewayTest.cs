using Flowzer.Shared;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>
/// Das inklusive Gateway nimmt beim Split jeden Ausgang mit wahrer Bedingung und wartet beim
/// Join auf genau die Zweige, die der zugehoerige Split aktiviert hat.
/// </summary>
public class InclusiveGatewayTest
{
    // Testzweck: Treffen zwei Bedingungen zu, laufen beide Zweige — der Standardfluss bleibt aus.
    [Test]
    public async Task TwoTrueConditions_ShouldActivateBothBranchesAndSkipTheDefaultFlow()
    {
        var instanceEngine = await StartWith(wegA: true, wegB: true);

        using (new AssertionScope())
        {
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_A", "ServiceTask_B"]);
            instanceEngine.Tokens.Should().NotContain(token => token.CurrentBaseElement.Id == "ServiceTask_Standard");
        }
    }

    // Testzweck: Der Join wartet auf genau die aktivierten Zweige — nach dem ersten laeuft die
    // Instanz weiter, erst der zweite loest ihn aus.
    [Test]
    public async Task Join_ShouldWaitForEveryActivatedBranch()
    {
        var instanceEngine = await StartWith(wegA: true, wegB: true);

        CompleteServiceTask(instanceEngine, "ServiceTask_A");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);

        CompleteServiceTask(instanceEngine, "ServiceTask_B");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Trifft nur eine Bedingung zu, wartet der Join auch nur auf diesen einen Zweig.
    [Test]
    public async Task OneTrueCondition_ShouldLetTheJoinPassWithASingleBranch()
    {
        var instanceEngine = await StartWith(wegA: true, wegB: false);

        instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
            .Should().BeEquivalentTo(["ServiceTask_A"]);

        CompleteServiceTask(instanceEngine, "ServiceTask_A");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Trifft keine Bedingung zu, greift der Standardfluss — genau wie am exklusiven Gateway.
    [Test]
    public async Task NoTrueCondition_ShouldTakeTheDefaultFlow()
    {
        var instanceEngine = await StartWith(wegA: false, wegB: false);

        instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
            .Should().BeEquivalentTo(["ServiceTask_Standard"]);

        CompleteServiceTask(instanceEngine, "ServiceTask_Standard");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Ein inklusiver Join ohne zugehoerigen Split verhaelt sich wie ein paralleler
    // Join und wartet auf alle seine Eingaenge.
    [Test]
    public async Task JoinWithoutSplit_ShouldBehaveLikeAParallelJoin()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("InclusiveJoinWithoutSplit.bpmn");

        CompleteServiceTask(instanceEngine, "ServiceTask_A");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);

        CompleteServiceTask(instanceEngine, "ServiceTask_B");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Ein paralleles Gateway innerhalb eines eingebetteten Subprozesses findet seine
    // Sequenzfluesse im eigenen Container — nicht nur auf der obersten Prozessebene.
    [Test]
    public async Task ParallelGatewayInsideASubProcess_ShouldForkAndJoin()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ParallelGatewayInSubProcess.bpmn");

        instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
            .Should().BeEquivalentTo(["SubServiceTask_A", "SubServiceTask_B"]);

        CompleteServiceTask(instanceEngine, "SubServiceTask_A");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);

        CompleteServiceTask(instanceEngine, "SubServiceTask_B");
        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    private static async Task<InstanceEngine> StartWith(bool wegA, bool wegB)
    {
        var data = (System.Dynamic.ExpandoObject?)new { WegA = wegA, WegB = wegB }.ToDynamic();

        return await Helper.StartFirstProcessOfFile("InclusiveGateway.bpmn", data);
    }

    private static void CompleteServiceTask(InstanceEngine instanceEngine, string flowNodeId)
    {
        var token = instanceEngine.GetActiveServiceTasks()
            .Single(candidate => candidate.CurrentBaseElement.Id == flowNodeId);
        instanceEngine.HandleTaskResult(token.Id, null);
    }
}
