using System.Dynamic;
using BPMN.Common;
using BPMN.HumanInteraction;
using BPMN.Process;
using Flowzer.Shared;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>
/// Der Betriebseingriff innerhalb derselben Version: Ein wartender Schritt wird
/// zurueckgezogen, an anderer Stelle beginnt ein neuer, und Variablen lassen sich korrigieren.
/// </summary>
public class InstanceModificationTest
{
    // Testzweck: Der Kernfall. Der verlassene Token ist zurueckgezogen, am Ziel wartet ein
    // neuer Token mit neuer Kennung, und die Prozessvariablen ueberleben den Eingriff.
    [Test]
    public async Task MovesAWaitingUserTaskToAnotherUserTask()
    {
        var instance = await StartOrderInstance();
        var source = instance.GetActiveUserTasks().Single();
        source.CurrentFlowNode!.Id.Should().Be("UserTask_Check");

        var plan = InstanceModification.Plan(instance, MoveRequest(source.Id, "UserTask_Approve"));

        using (new AssertionScope())
        {
            plan.Problems.Should().BeEmpty();
            plan.IsApplicable.Should().BeTrue();
            plan.Moves.Should().ContainSingle().Which.TargetFlowNode.Id.Should().Be("UserTask_Approve");
            // Was der Eingriff kostet, muss vorher feststehen: Die Aufgabe verschwindet, und am
            // Ziel beginnt ein Boundary-Timer von vorn.
            plan.Notices.Select(notice => notice.Code).Should().BeEquivalentTo(new[]
            {
                InstanceModificationNoticeCode.UserTaskCancelled,
                InstanceModificationNoticeCode.TimerRecalculated
            });
        }

        InstanceModification.Apply(plan);

        var target = instance.GetActiveUserTasks().Single();
        using (new AssertionScope())
        {
            source.State.Should().Be(FlowNodeState.Withdrawn);
            target.Id.Should().NotBe(source.Id, "the old task is gone and a new one begins");
            target.State.Should().Be(FlowNodeState.Active);
            target.CurrentFlowNode!.Id.Should().Be("UserTask_Approve");
            target.PreviousToken.Should().BeSameAs(source);
            instance.MasterToken.Variables.GetValue<string>("Antragsteller").Should().Be("Lukas");
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            // Der Boundary-Timer der Zielaufgabe steht scharf; ohne ihn liefe die Frist nie an.
            target.ActiveBoundaryEvents.Should().ContainSingle()
                .Which.Id.Should().Be("BoundaryTimer_Approve");
        }
    }

    // Testzweck: Ein Gateway ist kein Wartepunkt. Wird ein Schritt dorthin gesetzt, muss die
    // Engine sofort weiterlaufen und die Instanz auf dem passenden Zweig landen.
    [Test]
    public async Task MovingOntoAGatewayContinuesImmediately()
    {
        var instance = await StartOrderInstance();
        var source = instance.GetActiveUserTasks().Single();

        var plan = InstanceModification.Plan(instance, MoveRequest(source.Id, "Gateway_Route"));
        plan.IsApplicable.Should().BeTrue();
        InstanceModification.Apply(plan);

        using (new AssertionScope())
        {
            // "Entscheidung" steht beim Start auf "ja"; der bedingte Zweig gewinnt gegen den Standardfluss.
            instance.GetActiveUserTasks().Should().ContainSingle()
                .Which.CurrentFlowNode!.Id.Should().Be("UserTask_Approve");
            instance.Tokens.Should().NotContain(token =>
                token.CurrentBaseElement.Id == "Gateway_Route" && token.State == FlowNodeState.Active);
        }
    }

    // Testzweck: Die korrigierte Variable muss gelten, bevor der verschobene Token auf ein
    // Gateway trifft — sonst entschiede der Eingriff nach dem alten Stand.
    [Test]
    public async Task AppliesVariablesBeforeTheMovedTokenReachesAGateway()
    {
        var instance = await StartOrderInstance();
        var source = instance.GetActiveUserTasks().Single();

        var plan = InstanceModification.Plan(instance, new InstanceModificationRequest(
            [new InstanceModificationMove(source.Id, "Gateway_Route")],
            new Dictionary<string, object?> { ["Entscheidung"] = "nein" }));
        plan.IsApplicable.Should().BeTrue();
        InstanceModification.Apply(plan);

        using (new AssertionScope())
        {
            instance.MasterToken.Variables.GetValue<string>("Entscheidung").Should().Be("nein");
            // Der Standardfluss fuehrt zur Ablehnung; die Instanz ist damit beendet.
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
        }
    }

    // Testzweck: Ein End-Event als Ziel beendet die Instanz. Das ist der Weg, einen Vorgang
    // ordentlich auslaufen zu lassen, statt ihn abzubrechen.
    [Test]
    public async Task MovingOntoAnEndEventFinishesTheInstance()
    {
        var instance = await StartOrderInstance();
        var source = instance.GetActiveUserTasks().Single();

        InstanceModification.Apply(InstanceModification.Plan(
            instance, MoveRequest(source.Id, "EndEvent_Rejected")));

        using (new AssertionScope())
        {
            source.State.Should().Be(FlowNodeState.Withdrawn);
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instance.GetActiveUserTasks().Should().BeEmpty();
        }
    }

    // Testzweck: Ziel gleich aktueller Knoten heisst "Schritt neu starten". Die alte Aufgabe
    // muss dabei trotzdem verschwinden, sonst stuende die Instanz doppelt an derselben Stelle.
    [Test]
    public async Task MovingOntoTheSameNodeRestartsTheStep()
    {
        var instance = await StartOrderInstance();
        var source = instance.GetActiveUserTasks().Single();

        InstanceModification.Apply(InstanceModification.Plan(
            instance, MoveRequest(source.Id, "UserTask_Check")));

        var restarted = instance.GetActiveUserTasks().Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            source.State.Should().Be(FlowNodeState.Withdrawn);
            restarted.Id.Should().NotBe(source.Id);
            restarted.CurrentFlowNode!.Id.Should().Be("UserTask_Check");
        }
    }

    // Testzweck: Ein wartender Service-Task muss als solcher angekuendigt werden — sein Auftrag
    // verfaellt, und ein Worker, der ihn noch holen will, findet ihn nicht mehr.
    [Test]
    public async Task AnnouncesTheCancelledServiceTaskJob()
    {
        var instance = await StartOrderInstance();
        var check = instance.GetActiveUserTasks().Single();
        instance.HandleTaskResult(check.Id, new ExpandoObject());
        var approve = instance.GetActiveUserTasks().Single();
        instance.HandleTaskResult(approve.Id, new ExpandoObject());
        var notify = instance.GetActiveServiceTasks().Single();

        var plan = InstanceModification.Plan(instance, MoveRequest(notify.Id, "EndEvent_Done"));

        plan.Notices.Should().ContainSingle().Which.Should().Match<InstanceModificationNotice>(notice =>
            notice.Code == InstanceModificationNoticeCode.ServiceTaskJobCancelled
            && notice.FlowNodeId == "ServiceTask_Notify");
    }

    // Testzweck: Teilprozesse bleiben dieser Stufe verschlossen. Ein Token dort zu verschieben
    // liesse einen Scope zurueck, den niemand mehr abschliesst.
    [Test]
    public async Task ReportsATokenInsideASubProcessAsNotMovable()
    {
        var process = await LoadProcess("SubProcess.bpmn");
        var instance = Helper.CreateProcessEngine(process).StartProcess();
        var nested = instance.Tokens.First(token =>
            token.State == FlowNodeState.Active
            && token.ParentTokenId != null
            && token.ParentTokenId != instance.MasterToken.Id);

        var plan = InstanceModification.Plan(instance, MoveRequest(nested.Id, nested.CurrentBaseElement.Id));

        using (new AssertionScope())
        {
            plan.IsApplicable.Should().BeFalse();
            plan.Problems.Should().ContainSingle()
                .Which.Code.Should().Be(InstanceModificationProblemCode.TokenNotMovable);
        }
    }

    // Testzweck: Ein Start-Event wird nie von einem Sequenzfluss erreicht; als Ziel ergaebe es
    // einen Zustand, den das Modell nicht kennt.
    [Test]
    public async Task ReportsAStartEventAsNotAllowedTarget()
    {
        var instance = await StartOrderInstance();
        var source = instance.GetActiveUserTasks().Single();

        var plan = InstanceModification.Plan(instance, MoveRequest(source.Id, "StartEvent_1"));

        using (new AssertionScope())
        {
            plan.IsApplicable.Should().BeFalse();
            var problem = plan.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceModificationProblemCode.TargetNotAllowed);
            problem.FlowNodeId.Should().Be("StartEvent_1");
        }
    }

    // Testzweck: Ein Boundary-Event haengt an einer Aktivitaet und laesst sich nicht betreten;
    // sonst liefe der Eskalationspfad an, ohne dass etwas eskaliert waere.
    [Test]
    public async Task ReportsABoundaryEventAsNotAllowedTarget()
    {
        var instance = await StartOrderInstance();
        var source = instance.GetActiveUserTasks().Single();

        InstanceModification.Plan(instance, MoveRequest(source.Id, "BoundaryTimer_Approve"))
            .Problems.Should().ContainSingle()
            .Which.Code.Should().Be(InstanceModificationProblemCode.TargetNotAllowed);
    }

    // Testzweck: Ein Zielknoten, den das Modell nicht kennt, ist eine unbrauchbare Angabe und
    // kein stiller Fehlschlag. Beanstandet wird die Angabe der Bedienung, also muss der Befund
    // den Zielknoten nennen — die Oberflaeche setzt ihn genauso in ihren Satz ein wie bei
    // TargetNotAllowed.
    [Test]
    public async Task ReportsAnUnknownTarget()
    {
        var instance = await StartOrderInstance();
        var source = instance.GetActiveUserTasks().Single();

        var problem = InstanceModification.Plan(instance, MoveRequest(source.Id, "UserTask_Ghost"))
            .Problems.Should().ContainSingle().Subject;

        using (new AssertionScope())
        {
            problem.Code.Should().Be(InstanceModificationProblemCode.TargetMissing);
            problem.FlowNodeId.Should().Be("UserTask_Ghost");
            problem.TokenId.Should().Be(source.Id);
        }
    }

    // Testzweck: Ein Schritt kann nur an eine Stelle. Zwei Ziele fuer denselben Token sind ein
    // Widerspruch, kein "das letzte gewinnt".
    [Test]
    public async Task ReportsTheSameTokenNamedTwice()
    {
        var instance = await StartOrderInstance();
        var source = instance.GetActiveUserTasks().Single();

        var plan = InstanceModification.Plan(instance, new InstanceModificationRequest([
            new InstanceModificationMove(source.Id, "UserTask_Approve"),
            new InstanceModificationMove(source.Id, "EndEvent_Done")
        ]));

        using (new AssertionScope())
        {
            plan.IsApplicable.Should().BeFalse();
            var problem = plan.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceModificationProblemCode.DuplicateMove);
            // Die Oberflaeche nennt den Schritt, den die Bedienung vor sich hat — eine blosse
            // Token-Kennung waere dort keine Auskunft.
            problem.FlowNodeId.Should().Be("UserTask_Check");
        }
    }

    // Testzweck: Eine Token-Kennung, die nicht zu dieser Instanz gehoert, darf nicht als
    // "nicht verschiebbar" durchgehen — die Bedienung sucht sonst am falschen Ende.
    [Test]
    public async Task ReportsAnUnknownToken()
    {
        var instance = await StartOrderInstance();

        InstanceModification.Plan(instance, MoveRequest(Guid.NewGuid(), "UserTask_Approve"))
            .Problems.Should().ContainSingle()
            .Which.Code.Should().Be(InstanceModificationProblemCode.TokenMissing);
    }

    // Testzweck: Ein abgeschlossener Token ist Historie. Ihn zu verschieben hiesse, Vergangenes
    // wiederzubeleben.
    [Test]
    public async Task ReportsACompletedTokenAsNotMovable()
    {
        var instance = await StartOrderInstance();
        var finished = instance.Tokens.First(token =>
            token.ParentTokenId != null && token.State == FlowNodeState.Completed);

        InstanceModification.Plan(instance, MoveRequest(finished.Id, "UserTask_Approve"))
            .Problems.Should().ContainSingle()
            .Which.Code.Should().Be(InstanceModificationProblemCode.TokenNotMovable);
    }

    // Testzweck: Eine beendete Instanz laesst sich nicht anpassen; weitere Befunde dazu waeren
    // bedeutungslos.
    [Test]
    public async Task ReportsAFinishedInstanceAsNotRunning()
    {
        var instance = await StartOrderInstance();
        instance.Cancel();

        var plan = InstanceModification.Plan(instance, new InstanceModificationRequest());

        using (new AssertionScope())
        {
            plan.IsApplicable.Should().BeFalse();
            plan.Problems.Should().ContainSingle()
                .Which.Code.Should().Be(InstanceModificationProblemCode.InstanceNotRunning);
        }
    }

    // Testzweck: Eine leere Anfrage ist ein gueltiger Trockenlauf — die Oberflaeche fragt damit
    // ab, was ueberhaupt moeglich waere, bevor jemand etwas auswaehlt.
    [Test]
    public async Task AcceptsAnEmptyRequestAsADryRun()
    {
        var instance = await StartOrderInstance();

        var plan = InstanceModification.Plan(instance, new InstanceModificationRequest());

        using (new AssertionScope())
        {
            plan.IsApplicable.Should().BeTrue();
            plan.Moves.Should().BeEmpty();
            plan.Notices.Should().BeEmpty();
            plan.Request.IsEmpty.Should().BeTrue();
        }
    }

    // Testzweck: Variablen der Prozessebene werden geschrieben und entfernt — der Kern der
    // Datenkorrektur. Ein unbekannter Name beim Entfernen ist ein Hinweis, kein Hindernis.
    [Test]
    public async Task SetsAndRemovesProcessVariables()
    {
        var instance = await StartOrderInstance();

        var plan = InstanceModification.Plan(instance, new InstanceModificationRequest(
            VariablesToSet: new Dictionary<string, object?> { ["Antragsteller"] = "Mara", ["Betrag"] = 42 },
            VariablesToRemove: ["Entscheidung", "GibtEsNicht"]));

        plan.Notices.Should().ContainSingle()
            .Which.Code.Should().Be(InstanceModificationNoticeCode.VariableNotFound);

        InstanceModification.Apply(plan);

        var variables = instance.MasterToken.Variables;
        using (new AssertionScope())
        {
            variables.GetValue<string>("Antragsteller").Should().Be("Mara");
            variables.GetValue<int>("Betrag").Should().Be(42);
            variables.HasProperty("Entscheidung").Should().BeFalse();
        }
    }

    // Testzweck: Liegt die Variable an einem wartenden Schritt — etwa als abgebildete Eingabe
    // eines Service-Tasks —, muss die Korrektur genau dort ankommen. Sonst arbeitete der Worker
    // weiter mit dem alten Wert.
    [Test]
    public async Task SetsAVariableOnTheLevelWhereItLives()
    {
        var instance = await StartOrderInstance(empfaenger: "alt@example.org");
        var check = instance.GetActiveUserTasks().Single();
        instance.HandleTaskResult(check.Id, new ExpandoObject());
        var approve = instance.GetActiveUserTasks().Single();
        instance.HandleTaskResult(approve.Id, new ExpandoObject());

        var notify = instance.GetActiveServiceTasks().Single();
        notify.Variables.GetValue<string>("Empfaenger").Should().Be("alt@example.org");
        // Die Eingabeabbildung hat die Variable an den Schritt kopiert; auf der Prozessebene
        // steht sie ebenfalls. Die Prozessebene hat Vorrang, damit alle Schritte denselben
        // Wert lesen.
        instance.MasterToken.Variables.HasProperty("Empfaenger").Should().BeTrue();

        InstanceModification.Apply(InstanceModification.Plan(instance, new InstanceModificationRequest(
            VariablesToSet: new Dictionary<string, object?> { ["Empfaenger"] = "neu@example.org" })));

        instance.MasterToken.Variables.GetValue<string>("Empfaenger").Should().Be("neu@example.org");
    }

    // Testzweck: Entfernen raeumt die ganze Prozessebene ab. Bliebe eine Kopie an einem
    // wartenden Schritt stehen, waere die Variable danach immer noch lesbar.
    [Test]
    public async Task RemovesAVariableFromEveryProcessLevelToken()
    {
        var instance = await StartOrderInstance(empfaenger: "alt@example.org");
        var check = instance.GetActiveUserTasks().Single();
        instance.HandleTaskResult(check.Id, new ExpandoObject());
        var approve = instance.GetActiveUserTasks().Single();
        instance.HandleTaskResult(approve.Id, new ExpandoObject());
        var notify = instance.GetActiveServiceTasks().Single();

        InstanceModification.Apply(InstanceModification.Plan(instance, new InstanceModificationRequest(
            VariablesToRemove: ["Empfaenger"])));

        using (new AssertionScope())
        {
            instance.MasterToken.Variables.HasProperty("Empfaenger").Should().BeFalse();
            notify.Variables.HasProperty("Empfaenger").Should().BeFalse();
        }
    }

    // Testzweck: Ein Variablenname mit Pfad wird abgelehnt statt halb verstanden. Diese Stufe
    // schreibt und entfernt ausschliesslich ganze Variablen.
    [Test]
    public async Task ReportsAVariablePathAsInvalid()
    {
        var instance = await StartOrderInstance();

        var plan = InstanceModification.Plan(instance, new InstanceModificationRequest(
            VariablesToSet: new Dictionary<string, object?> { ["Adresse.Ort"] = "Kiel" }));

        using (new AssertionScope())
        {
            plan.IsApplicable.Should().BeFalse();
            plan.Problems.Should().ContainSingle()
                .Which.Code.Should().Be(InstanceModificationProblemCode.VariableNameInvalid);
        }
    }

    // Testzweck: Ein abgelehnter Plan darf nicht versehentlich angewendet werden — sonst
    // veraenderte ein Trockenlauf mit Befunden doch die Instanz.
    [Test]
    public async Task ApplyRejectsANonApplicablePlan()
    {
        var instance = await StartOrderInstance();
        var source = instance.GetActiveUserTasks().Single();

        var plan = InstanceModification.Plan(instance, MoveRequest(source.Id, "StartEvent_1"));

        var apply = () => InstanceModification.Apply(plan);
        apply.Should().Throw<InvalidOperationException>();
    }

    // Testzweck: Mehrere Verschiebungen einer Anfrage gehoeren zusammen; die Instanz darf nicht
    // zwischendurch mit einem halben Stand weiterlaufen.
    [Test]
    public async Task MovesSeveralWaitingStepsInOneRequest()
    {
        var process = await LoadProcess("ModificationParallel.bpmn");
        var instance = Helper.CreateProcessEngine(process).StartProcess();
        var waiting = instance.GetActiveUserTasks().ToArray();
        waiting.Select(token => token.CurrentFlowNode!.Id)
            .Should().BeEquivalentTo(["UserTask_Left", "UserTask_Right"]);

        // Beide Zweige auf denselben Knoten: Der linke Schritt wandert, der rechte startet neu.
        var plan = InstanceModification.Plan(instance, new InstanceModificationRequest([
            new InstanceModificationMove(
                waiting.Single(token => token.CurrentFlowNode!.Id == "UserTask_Left").Id, "UserTask_Right"),
            new InstanceModificationMove(
                waiting.Single(token => token.CurrentFlowNode!.Id == "UserTask_Right").Id, "UserTask_Right")
        ]));

        using (new AssertionScope())
        {
            plan.Problems.Should().BeEmpty();
            plan.Moves.Should().HaveCount(2);
        }

        InstanceModification.Apply(plan);

        using (new AssertionScope())
        {
            waiting.Should().OnlyContain(token => token.State == FlowNodeState.Withdrawn);
            instance.GetActiveUserTasks().Should().HaveCount(2)
                .And.OnlyContain(token => token.CurrentFlowNode!.Id == "UserTask_Right");
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
        }
    }

    private static InstanceModificationRequest MoveRequest(Guid tokenId, string targetFlowNodeId) =>
        new([new InstanceModificationMove(tokenId, targetFlowNodeId)]);

    private static async Task<InstanceEngine> StartOrderInstance(string empfaenger = "team@example.org")
    {
        var process = await LoadProcess("ModificationOrder.bpmn");
        var startData = (ExpandoObject)new
        {
            Antragsteller = "Lukas",
            Entscheidung = "ja",
            Empfaenger = empfaenger
        }.ToDynamic()!;
        return Helper.CreateProcessEngine(process).StartProcess(startData);
    }

    private static async Task<Process> LoadProcess(string fileName)
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/" + fileName, FileMode.Open));
        return model.GetProcesses().First();
    }
}
