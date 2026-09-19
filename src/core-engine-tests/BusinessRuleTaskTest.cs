using System.Dynamic;
using core_engine.Exceptions;
using core_engine.Expression.Feelin;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>
/// Der Business-Rule-Task aus Sicht der Engine: Ein Token wartet an ihm wie an einem
/// Service-Task, und die Engine stellt die zu rechnende Entscheidung samt Variablen bereit.
/// Gerechnet wird ausserhalb — die Engine kennt weder DMN-Modell noch Auswerter. Mit
/// <c>zeebe:taskDefinition</c> ist derselbe Task dagegen ein gewoehnlicher Auftrag an einen
/// Worker.
/// </summary>
public class BusinessRuleTaskTest
{
    // Testzweck: Mit zeebe:ioMapping-Eingang stellt die Engine genau eine Entscheidung mit der
    // Decision-Id und genau den zugeordneten Variablen bereit — nicht mit dem ganzen Kontext.
    [Test]
    public async Task BusinessRuleTask_ShouldOfferPendingDecisionWithMappedInput()
    {
        var instance = await StartWithVariables("BusinessRuleTaskWithMapping.bpmn", new
        {
            umsatz = 120000,
            intern = "bleibt hier"
        });

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);

            var waiting = instance.GetWaitingBusinessRuleTasks().Should().ContainSingle().Which;
            waiting.CurrentBaseElement.Id.Should().Be("Decision_1");
            waiting.State.Should().Be(FlowNodeState.Active);

            var decision = instance.PendingDecisions.Should().ContainSingle().Which;
            decision.TokenId.Should().Be(waiting.Id);
            decision.DecisionId.Should().Be("rabattstufe");

            var variables = (IDictionary<string, object?>)decision.Variables;
            variables.Should().HaveCount(1);
            variables["jahresumsatz"].Should().Be(120000);
        }
    }

    // Testzweck: Ohne Eingangszuordnung bekommt die Entscheidung bewusst den ganzen
    // Prozesskontext — sie rechnet lokal, es verlaesst nichts den Server.
    [Test]
    public async Task BusinessRuleTaskWithoutMapping_ShouldOfferTheWholeProcessContext()
    {
        var instance = await StartWithVariables("BusinessRuleTaskWithoutMapping.bpmn", new
        {
            umsatz = 120000,
            kunde = "4711"
        });

        var decision = instance.PendingDecisions.Should().ContainSingle().Which;
        var variables = (IDictionary<string, object?>)decision.Variables;

        using (new AssertionScope())
        {
            decision.DecisionId.Should().Be("rabattstufe");
            variables["umsatz"].Should().Be(120000);
            variables["kunde"].Should().Be("4711");
        }
    }

    // Testzweck: Eine ausstehende Entscheidung wird genau einmal herausgegeben; ein weiterer
    // Engine-Lauf darf dieselbe Entscheidung nicht erneut anbieten.
    [Test]
    public async Task TakePendingDecisions_ShouldHandOutEveryDecisionOnlyOnce()
    {
        var instance = await StartWithVariables("BusinessRuleTaskWithoutMapping.bpmn", new { umsatz = 120000 });

        var taken = instance.TakePendingDecisions();
        // Ein weiterer Lauf der Engine, wie ihn jede spaetere Mutation ausloest.
        instance.HandleTime(DateTime.UtcNow);

        using (new AssertionScope())
        {
            taken.Should().ContainSingle();
            instance.PendingDecisions.Should().BeEmpty();
        }
    }

    // Testzweck: Das Ergebnis steht unter resultVariable im Prozesskontext, und zwar als
    // Objekt: Ein folgendes Exclusive Gateway muss ueber "ergebnis.rabatt" darauf zugreifen
    // koennen. Genau daran entscheidet sich, ob die Umsetzung des DMN-Werts taugt.
    [Test]
    public async Task CompleteDecision_ShouldWriteResultAndLetTheGatewayReadIt()
    {
        var instance = await StartWithVariables("BusinessRuleTaskWithoutMapping.bpmn", new { umsatz = 120000 });
        var decision = instance.PendingDecisions.Single();

        instance.CompleteDecision(decision.TokenId, "ergebnis", DecisionResult());

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);

            var ergebnis = (IDictionary<string, object?>)
                ((IDictionary<string, object?>)instance.MasterToken.Variables!)["ergebnis"]!;
            ergebnis["rabatt"].Should().Be(10);
            ergebnis["stufe"].Should().Be("Gold");

            instance.Tokens.Should().Contain(token => token.CurrentBaseElement.Id == "EndEvent_Rabatt");
            instance.Tokens.Should().NotContain(token => token.CurrentBaseElement.Id == "EndEvent_Standard");
        }
    }

    // Testzweck: Derselbe Nachweis mit dem echten FEEL-Handler der Engine — der Ausdruck
    // "ergebnis.rabatt = 10" am Gateway muss auch ueber libfeelin/V8 greifen.
    [Test]
    public async Task CompleteDecision_ShouldBeReadableByFeel()
    {
        var config = CreateFeelConfig();
        if (config is null)
        {
            Assert.Ignore("Ohne native V8-Bibliothek nicht pruefbar.");
            return;
        }

        var instance = await StartWithVariables("BusinessRuleTaskWithoutMapping.bpmn",
            new { umsatz = 120000 }, config);
        var decision = instance.PendingDecisions.Single();

        instance.CompleteDecision(decision.TokenId, "ergebnis", DecisionResult());

        instance.Tokens.Should().Contain(token => token.CurrentBaseElement.Id == "EndEvent_Rabatt");
    }

    // Testzweck: Der zeebe:ioMapping-Ausgang greift zusaetzlich — wie bei der Call Activity
    // uebernimmt der Prozess dann genau die zugeordneten Werte.
    [Test]
    public async Task CompleteDecision_ShouldApplyOutputMapping()
    {
        var instance = await StartWithVariables("BusinessRuleTaskWithMapping.bpmn", new { umsatz = 120000 });
        var decision = instance.PendingDecisions.Single();

        instance.CompleteDecision(decision.TokenId, "ergebnis", DecisionResult());

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);

            var processVariables = (IDictionary<string, object?>)instance.MasterToken.Variables!;
            processVariables["rabatt"].Should().Be(10);
            processVariables.Should().NotContainKey("ergebnis");
        }
    }

    // Testzweck: Ein Business-Rule-Task mit Auftragstyp ist ein Auftrag an einen Worker: Er
    // steht bei den Service-Tasks und wartet gerade nicht auf eine Entscheidung.
    [Test]
    public async Task BusinessRuleTaskWithTaskDefinition_ShouldBeAWorkerJob()
    {
        var instance = await Helper.StartFirstProcessOfFile("BusinessRuleTaskAsJob.bpmn");

        using (new AssertionScope())
        {
            var job = instance.GetActiveServiceTasks().Should().ContainSingle().Which;
            job.CurrentBaseElement.Id.Should().Be("Decision_1");

            instance.GetWaitingBusinessRuleTasks().Should().BeEmpty();
            instance.PendingDecisions.Should().BeEmpty();
        }
    }

    // Testzweck: Ein Token, das gar nicht auf eine Entscheidung wartet, darf nicht ueber
    // CompleteDecision weitergeschoben werden.
    [Test]
    public async Task CompleteDecision_ShouldRejectATokenThatIsNotWaitingForADecision()
    {
        var instance = await Helper.StartFirstProcessOfFile("BusinessRuleTaskAsJob.bpmn");
        var job = instance.GetActiveServiceTasks().Single();

        var complete = () => instance.CompleteDecision(job.Id, "ergebnis", DecisionResult());

        complete.Should().Throw<FlowzerRuntimeException>();
    }

    /// <summary>
    /// Ein Ergebnis in der Gestalt, die <c>DmnDecisionResult.Value</c> bei den
    /// Einzeltreffer-Trefferregeln liefert: die Ausgabespalten als Woerterbuch.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> DecisionResult() => new Dictionary<string, object?>
    {
        ["rabatt"] = 10,
        ["stufe"] = "Gold"
    };

    /// <summary>
    /// Die Konfiguration mit dem echten FEEL-Handler — oder null, wenn V8 auf dieser
    /// Plattform fehlt.
    /// </summary>
    private static FlowzerConfig? CreateFeelConfig()
    {
        try
        {
            return new FlowzerConfig { ExpressionHandler = new FeelinExpressionHandler() };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<InstanceEngine> StartWithVariables(
        string fileName, object variables, FlowzerConfig? config = null)
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/" + fileName, FileMode.Open));
        var processEngine = new ProcessEngine(model.GetProcesses().First(), config ?? Helper.TestFlowzerConfig);

        return processEngine.StartProcess(AsVariables(variables));
    }

    private static ExpandoObject AsVariables(object source)
    {
        var variables = new ExpandoObject();
        var target = (IDictionary<string, object?>)variables;
        foreach (var property in source.GetType().GetProperties())
        {
            target[property.Name] = property.GetValue(source);
        }

        return variables;
    }
}
