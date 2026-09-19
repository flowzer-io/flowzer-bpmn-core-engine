using System.Net;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Timer, Fristen, Abbruch, Deployment und Instanzmigration unter zwei API-Hosts.
/// Der Aufbau steht in <see cref="MultiProcessConcurrencyTest"/>.
/// </summary>
public sealed partial class MultiProcessConcurrencyTest
{
    // Testzweck: Ein faelliger Start-Timer darf auch dann nur eine Instanz erzeugen, wenn beide
    // Hosts denselben Scheduler-Durchgang gleichzeitig ausfuehren.
    [Test]
    public async Task StartTimer_ShouldCreateExactlyOneInstance_WhenBothHostsProcessTheSameDueTimer()
    {
        var definition = await DeployAsync(First, MultiProcessWorkflows.TimerStartDefinitionId,
            MultiProcessWorkflows.TimerStart(), formName: null);
        var template = (await First.Storage.SubscriptionStorage.GetAllTimerSubscriptions())
            .Single(subscription => subscription.ProcessInstanceId is null);
        // Die beim Deployment angelegte Anmeldung wird entfernt; jede Runde setzt ihre eigene.
        await First.Storage.SubscriptionStorage.RemoveTimerSubscription(template.Id);

        for (var round = 0; round < Rounds; round++)
        {
            await SeedDueStartTimerAsync(template);
            var dueUpTo = DateTime.UtcNow.AddMinutes(1);

            await RaceCatchingAsync(
                () => First.Engine.HandleTime(dueUpTo),
                () => Second.Engine.HandleTime(dueUpTo));

            var instances = await Cluster.CountAsync("instances",
                $"meta_definition_id = '{definition.DefinitionId}'");
            instances.Should().Be(round + 1,
                "ein faelliger Start-Timer darf genau eine Instanz erzeugen");
            (await Cluster.CountAsync("timer_subscriptions", "process_instance_id IS NULL"))
                .Should().Be(0, "der einmalige Start-Timer ist nach dem Feuern verbraucht");
        }
    }

    // Testzweck: Ein faelliger Intermediate-Timer fuehrt den Token genau einmal weiter, auch
    // wenn beide Hosts denselben Durchgang gleichzeitig ausfuehren.
    [Test]
    public async Task IntermediateTimer_ShouldAdvanceTokenOnce_WhenBothHostsProcessTheSameDueTimer()
    {
        var definition = await DeployAsync(First, MultiProcessWorkflows.TimerCatchDefinitionId,
            MultiProcessWorkflows.TimerCatch(), formName: null);

        for (var round = 0; round < Rounds; round++)
        {
            var instance = await First.Engine.StartProcessInstance(definition.DefinitionId);
            var dueUpTo = DateTime.UtcNow.AddMinutes(1);

            await RaceCatchingAsync(
                () => First.Engine.HandleTime(dueUpTo),
                () => Second.Engine.HandleTime(dueUpTo));

            var stored = await First.Storage.InstanceStorage.GetProcessInstance(instance.InstanceId);
            stored.IsFinished.Should().BeTrue();
            stored.Tokens.Count(token => token.CurrentFlowNode?.Id == "End").Should().Be(1,
                "ein faelliger Timer darf den Folgepfad nur einmal erzeugen");
            (await First.Storage.SubscriptionStorage.GetTimerSubscriptions(instance.InstanceId))
                .Should().BeEmpty();
        }
    }

    // Testzweck: Zwei Hosts mit echtem, kurz getaktetem Timer-Scheduler holen denselben
    // faelligen Start-Timer ab. Auch ohne kuenstliches Rennen entsteht genau eine Instanz.
    [Test]
    public async Task TimerScheduler_ShouldFireDueStartTimerOnce_WhenBothHostsPoll()
    {
        var firstScheduler = Cluster.CreateHost(schedulers: true);
        var secondScheduler = Cluster.CreateHost(schedulers: true);
        try
        {
            // Beide Hosts hochfahren, bevor der Timer existiert; sonst holte der Hochlauf ihn
            // ab und die zyklischen Durchgaenge kaemen gar nicht erst zum Zug.
            _ = firstScheduler.Services;
            _ = secondScheduler.Services;

            var definition = await DeployAsync(firstScheduler, MultiProcessWorkflows.TimerStartDefinitionId,
                MultiProcessWorkflows.TimerStart(), formName: null);

            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline
                   && await Cluster.CountAsync("timer_subscriptions", "process_instance_id IS NULL") > 0)
            {
                await Task.Delay(200);
            }

            (await Cluster.CountAsync("instances", $"meta_definition_id = '{definition.DefinitionId}'"))
                .Should().Be(1, "beide Scheduler sehen denselben Timer, nur einer darf ihn ausfuehren");

            // Kurz nachfassen: Ein zweiter Durchgang darf nichts nachschieben.
            await Task.Delay(2000);
            (await Cluster.CountAsync("instances", $"meta_definition_id = '{definition.DefinitionId}'"))
                .Should().Be(1);
        }
        finally
        {
            firstScheduler.Dispose();
            secondScheduler.Dispose();
            Cluster.Forget(firstScheduler);
            Cluster.Forget(secondScheduler);
        }
    }

    // Testzweck: Der Fristendienst beider Hosts verarbeitet dieselbe faellige Aufgabe. Die
    // Benachrichtigung entsteht genau einmal (Zeilensperre plus Dedup-Schluessel).
    [Test]
    public async Task Deadlines_ShouldCreateDueNotificationOnce_WhenBothHostsProcessDueTasks()
    {
        var definition = await DeployAsync(First, MultiProcessWorkflows.UserTaskDefinitionId,
            MultiProcessWorkflows.UserTask("<zeebe:taskSchedule dueDate=\"PT0S\" />"));

        for (var round = 0; round < Rounds; round++)
        {
            await First.Engine.StartProcessInstance(definition.DefinitionId);
            var nowUtc = DateTimeOffset.UtcNow.AddSeconds(1);

            await RaceCatchingAsync(
                () => First.Deadlines.ProcessDueAsync(nowUtc, 100, CancellationToken.None),
                () => Second.Deadlines.ProcessDueAsync(nowUtc, 100, CancellationToken.None));

            (await Cluster.CountAsync("user_task_notifications")).Should().Be(round + 1,
                "je faelliger Aufgabe entsteht genau eine Benachrichtigung");
        }
    }

    // Testzweck: Instanzabbruch auf dem einen und Aufgabenabschluss auf dem anderen Host
    // treffen dieselbe Instanz. Der Endzustand ist eindeutig: entweder abgebrochen ohne
    // Folge-Token oder abgeschlossen — niemals beides.
    [Test]
    public async Task CancelAgainstCompletion_ShouldLeaveOneConsistentEndState_WhenBothHostsAct()
    {
        var definition = await DeployAsync(First, MultiProcessWorkflows.UserTaskDefinitionId,
            MultiProcessWorkflows.UserTask());

        for (var round = 0; round < Rounds; round++)
        {
            var instance = await First.Engine.StartProcessInstance(definition.DefinitionId);
            var task = (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
            var result = new UserTaskResultDto
            {
                FlowNodeId = "Review",
                TokenId = task.Token.Id,
                ProcessInstanceId = instance.InstanceId
            };

            var responses = await RaceAsync(
                () => PostAsync(_firstClient, $"/instance/{instance.InstanceId}/cancel", new { }),
                () => PostAsync(_secondClient, "/usertask", result));

            var cancelled = responses[0] == HttpStatusCode.OK;
            var completed = responses[1] == HttpStatusCode.OK;
            (cancelled ^ completed).Should().BeTrue(
                $"genau ein Eingriff darf gewinnen, Antworten waren {responses[0]} und {responses[1]}");

            var stored = await First.Storage.InstanceStorage.GetProcessInstance(instance.InstanceId);
            stored.IsFinished.Should().BeTrue();
            stored.Tokens.Count(token => token.CurrentFlowNode?.Id == "End")
                .Should().Be(completed ? 1 : 0,
                    "ein Abbruch darf keinen Folge-Token hinterlassen");
            (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().BeEmpty();
        }
    }

    // Testzweck: Beide Hosts veroeffentlichen denselben Workflow gleichzeitig. Danach ist genau
    // eine Version deployt, jede gespeicherte Version hat ihr BPMN und die deployte Version
    // traegt ihre Formularbindungen.
    [Test]
    public async Task Deployment_ShouldLeaveExactlyOneActiveVersion_WhenBothHostsDeploy()
    {
        var storage = First.Storage;
        await FormTestSeed.StoreAsync(storage, "Approval");
        await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
        {
            DefinitionId = MultiProcessWorkflows.UserTaskDefinitionId,
            Name = MultiProcessWorkflows.UserTaskDefinitionId
        });
        var xml = MultiProcessWorkflows.UserTask();

        for (var round = 0; round < Rounds; round++)
        {
            var responses = await RaceAsync(
                () => PostXmlAsync(_firstClient, "/definition/deploy", xml),
                () => PostXmlAsync(_secondClient, "/definition/deploy", xml));

            responses.Should().Contain(HttpStatusCode.OK,
                $"mindestens ein Deployment muss gelingen, Antworten waren {responses[0]} und {responses[1]}");

            (await Cluster.CountAsync("definitions", "is_active = true")).Should().Be(1,
                "nach dem Rennen darf genau eine Version deployt sein");
            (await Cluster.CountAsync("definitions",
                    $"NOT EXISTS (SELECT 1 FROM {MultiProcessCluster.Schema}.definition_binaries b WHERE b.id = definitions.id)"))
                .Should().Be(0, "keine Version ohne ihr BPMN-Dokument");

            var deployed = await storage.DefinitionStorage
                .GetDeployedDefinition(MultiProcessWorkflows.UserTaskDefinitionId);
            deployed.Should().NotBeNull();
            deployed!.FormBindings.Should().NotBeNull("die deployte Version braucht ihre Formularbindungen");
        }
    }

    // Testzweck: Instanzmigration auf dem einen und Aufgabenabschluss auf dem anderen Host
    // treffen dieselbe Instanz. Kein Token geht verloren; die Aufgabe ist danach entweder auf
    // der Quell- oder auf der Zielversion abgeschlossen oder wartet genau einmal weiter.
    [Test]
    public async Task MigrationAgainstCompletion_ShouldKeepEveryToken_WhenBothHostsAct()
    {
        var source = await DeployAsync(First, MultiProcessWorkflows.UserTaskDefinitionId,
            MultiProcessWorkflows.UserTask());
        var instances = new List<ProcessInstanceInfo>();
        for (var round = 0; round < Rounds; round++)
        {
            instances.Add(await First.Engine.StartProcessInstance(source.DefinitionId));
        }

        var target = await DeployAsync(First, MultiProcessWorkflows.UserTaskDefinitionId,
            MultiProcessWorkflows.UserTask());

        foreach (var instance in instances)
        {
            var task = (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
            var result = new UserTaskResultDto
            {
                FlowNodeId = "Review",
                TokenId = task.Token.Id,
                ProcessInstanceId = instance.InstanceId
            };

            await RaceAsync(
                () => PostAsync(_firstClient, "/instance/migration", new InstanceMigrationRequestDto
                {
                    InstanceIds = [instance.InstanceId],
                    TargetDefinitionId = target.Id
                }),
                () => PostAsync(_secondClient, "/usertask", result));

            var stored = await First.Storage.InstanceStorage.GetProcessInstance(instance.InstanceId);
            var waiting = (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).ToArray();
            var finished = stored.Tokens.Count(token => token.CurrentFlowNode?.Id == "End");

            new[] { source.Id, target.Id }.Should().Contain(stored.DefinitionId,
                "die Instanz steht entweder auf der Quell- oder auf der Zielversion");
            (waiting.Length + finished).Should().Be(1,
                "die Aufgabe wartet entweder genau einmal weiter oder ist genau einmal abgeschlossen");
            if (finished == 1)
            {
                stored.IsFinished.Should().BeTrue();
            }
            else
            {
                waiting[0].ProcessInstanceId.Should().Be(instance.InstanceId);
                waiting[0].DefinitionId.Should().Be(stored.DefinitionId);
            }
        }
    }

    /// <summary>
    /// Legt eine sofort faellige Anmeldung fuer den einmaligen Start-Timer an. Der Timer selbst
    /// wird beim Feuern verbraucht; fuer die naechste Runde wird er deshalb neu gesetzt.
    /// </summary>
    private async Task SeedDueStartTimerAsync(TimerSubscription template)
    {
        await First.Storage.SubscriptionStorage.AddTimerSubscription(new TimerSubscription
        {
            Id = Guid.NewGuid(),
            DueAt = DateTime.UtcNow.AddSeconds(-5),
            FlowNodeId = template.FlowNodeId,
            Kind = template.Kind,
            ProcessId = template.ProcessId,
            RelatedDefinitionId = template.RelatedDefinitionId,
            DefinitionId = template.DefinitionId,
            ProcessInstanceId = null,
            TokenId = template.TokenId,
            RemainingOccurrences = template.RemainingOccurrences
        });
    }
}
