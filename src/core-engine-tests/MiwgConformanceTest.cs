using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BPMN.Common;
using BPMN.Events;
using BPMN.Flowzer;
using BPMN.Flowzer.Events;
using BPMN.HumanInteraction;
using BPMN.Infrastructure;
using BPMN.Process;
using core_engine.Exceptions;
using FluentAssertions;
using Model;
using Variables = System.Dynamic.ExpandoObject;

namespace core_engine_tests;

/// <summary>
/// Prüft Flowzer gegen die Referenzmodelle der BPMN Model Interchange Working Group
/// (<c>embeddings/miwg</c>, Herkunft siehe <c>MANIFEST.md</c>).
///
/// Je Modell werden drei Stufen beobachtet:
/// <list type="number">
///   <item>Liest der <see cref="ModelParser"/> das Dokument ohne Ausnahme?</item>
///   <item>Würde <see cref="BpmnCapabilityMatrix.ValidateForDeployment"/> es veröffentlichen?</item>
///   <item>Läuft eine Instanz mit generischer Bedienung bis zum Ende?</item>
/// </list>
///
/// <b>Kein Modell muss unterstützt sein.</b> Der Test vergleicht das beobachtete Verhalten mit der
/// eingecheckten Tabelle <c>miwg-expectations.json</c> und schlägt fehl, sobald es sich ändert —
/// in beide Richtungen. Eine unerwartete Verbesserung soll genauso auffallen wie eine Regression,
/// damit <c>docs/BPMN-MIWG-COVERAGE.md</c> nachgezogen wird.
///
/// Tabelle und Bericht werden vom ausdrücklich angeforderten Test
/// <see cref="RegenerateExpectationsAndReport"/> erzeugt (<c>dotnet test --filter
/// FullyQualifiedName~MiwgConformanceTest.RegenerateExpectationsAndReport</c>).
/// </summary>
public class MiwgConformanceTest
{
    private const string FixtureDirectory = "embeddings/miwg";
    private const string ExpectationsFileName = "miwg-expectations.json";
    private const string ReportPath = "docs/BPMN-MIWG-COVERAGE.md";
    private const string ExpectationsRepositoryPath = "src/core-engine-tests/" + ExpectationsFileName;

    /// <summary>
    /// Obergrenze für den generischen Durchlauf. Dieselbe Zahl wie die Schleifenerkennung der
    /// Engine: Ein Modell, das damit nicht endet, ist für diesen Nachweis nicht ausführbar.
    /// </summary>
    private const int MaximumExecutionSteps = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly Regex UnsupportedElementPattern =
        new(@"^(?:\{[^}]*\})?(?<element>[^\s{}]+) is not supported at moment\.$", RegexOptions.Compiled);

    public static IEnumerable<string> ModelNames => EnumerateModelFiles().Select(Path.GetFileName)!;

    // Testzweck: Prüft, dass jedes MIWG-Referenzmodell sich genau so verhält wie in der
    // eingecheckten Erwartungstabelle festgehalten — auch eine Verbesserung gilt als Abweichung.
    [TestCaseSource(nameof(ModelNames))]
    public void Model_behaves_as_recorded(string modelName)
    {
        var expectations = LoadExpectations();
        expectations.Should().ContainKey(modelName,
            "jedes Referenzmodell braucht einen Eintrag in {0}; bei neuen Modellen den Generatorlauf ausführen",
            ExpectationsRepositoryPath);

        var observed = Observe(Path.Combine(FixtureDirectory, modelName));

        observed.Should().BeEquivalentTo(expectations[modelName],
            "das beobachtete Verhalten muss der Tabelle {0} entsprechen; bei einer bewussten Änderung "
            + "Tabelle und {1} neu erzeugen", ExpectationsRepositoryPath, ReportPath);
    }

    // Testzweck: Prüft, dass die Erwartungstabelle keine Einträge für entfernte Modelle behält.
    [Test]
    public void Expectations_describe_only_present_models()
    {
        var presentModels = ModelNames.ToHashSet(StringComparer.Ordinal);

        LoadExpectations().Keys.Should().OnlyContain(model => presentModels.Contains(model),
            "die Tabelle darf keine Modelle beschreiben, die nicht mehr unter {0} liegen", FixtureDirectory);
    }

    // Testzweck: Prüft, dass der generische Durchlauf ein bekannt ausführbares Flowzer-Modell
    // wirklich bis zum Ende bedient. Solange kein MIWG-Modell die Veröffentlichungsprüfung
    // besteht, bliebe die dritte Stufe sonst vollständig unbelegt — ein „0 ausgeführt" im
    // Bericht wäre dann nicht vom stillen Defekt im Durchlauf zu unterscheiden.
    [TestCase("ExklusiveGateway.bpmn")]
    [TestCase("ParallelFlowTest.bpmn")]
    [TestCase("Messages.bpmn")]
    [TestCase("MigrationReview_v2.bpmn")]
    [TestCase("MiwgTimerProbe.bpmn")]
    public void Execution_stage_completes_a_supported_model(string fixtureName)
    {
        var observation = Observe(Path.Combine("embeddings", fixtureName));

        observation.Parse.Should().Be("ok");
        observation.Deploy.Should().Be("ok", "Grund: {0}", observation.DeployCode);
        observation.Execute.Should().Be("completed", "Grund: {0}", observation.ExecuteDetail);
    }

    // Testzweck: Prüft, dass der eingecheckte Modellsatz vom Herkunftsnachweis begleitet wird.
    [Test]
    public void Fixture_directory_carries_a_manifest()
    {
        var manifest = Path.Combine(FixtureDirectory, "MANIFEST.md");

        File.Exists(manifest).Should().BeTrue("der Herkunfts- und Lizenznachweis gehört zu den Modellen");
        var content = File.ReadAllText(manifest);
        content.Should().Contain("bpmn-miwg/bpmn-miwg-test-suite");
        content.Should().Contain("Creative Commons Attribution 3.0");
    }

    // Testzweck: Erzeugt Erwartungstabelle und Bericht aus dem tatsächlichen Verhalten. Läuft nur
    // auf ausdrückliche Anforderung und ist deshalb kein Teil des normalen Testlaufs.
    [Test]
    [Explicit("Generatorlauf: schreibt miwg-expectations.json und docs/BPMN-MIWG-COVERAGE.md neu.")]
    public void RegenerateExpectationsAndReport()
    {
        var repositoryRoot = FindRepositoryRoot();
        var observations = EnumerateModelFiles().Select(Observe).ToArray();

        File.WriteAllText(
            Path.Combine(repositoryRoot, ExpectationsRepositoryPath),
            JsonSerializer.Serialize(observations, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(repositoryRoot, ReportPath),
            MiwgCoverageReport.Build(observations),
            new UTF8Encoding(false));

        TestContext.Out.WriteLine($"{observations.Length} Modelle nach {ExpectationsRepositoryPath} und {ReportPath} geschrieben.");
    }

    // ---------------------------------------------------------------------------------------
    // Beobachtung
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Führt die drei Stufen für ein Modell aus. Jede Stufe fängt ihre Ausnahmen selbst; der
    /// Nachweis will Verhalten festhalten, nicht an der ersten Lücke abbrechen.
    /// </summary>
    private static MiwgObservation Observe(string modelPath)
    {
        var modelName = Path.GetFileName(modelPath);
        var xml = File.ReadAllText(modelPath);

        Definitions? definitions = null;
        var parse = "ok";
        string? parseCode = null;
        string? parseDetail = null;
        var executableProcesses = 0;

        try
        {
            definitions = ModelParser.ParseModel(xml);
            executableProcesses = definitions.GetProcesses().Count();
            if (executableProcesses == 0)
            {
                // Der Parser liest ausschliesslich Prozesse mit isExecutable="true". Ein Modell
                // ohne solchen Prozess laeuft fehlerfrei durch, ohne dass etwas gelesen wurde —
                // das darf im Bericht nicht wie eine gelungene Abdeckung aussehen.
                parse = "empty";
            }
        }
        catch (Exception exception)
        {
            parse = "rejected";
            (parseCode, parseDetail) = DescribeParseFailure(exception);
        }

        var deploy = "ok";
        string? deployCode = null;
        string? deployElement = null;
        string? deployElementId = null;

        try
        {
            BpmnCapabilityMatrix.ValidateForDeployment(xml);
        }
        catch (BpmnCapabilityValidationException failure)
        {
            deploy = "rejected";
            deployCode = failure.Code;
            deployElementId = failure.ElementId;
            deployElement = DescribeElementKind(xml, failure.ElementId);
        }
        catch (Exception exception)
        {
            deploy = "rejected";
            deployCode = $"unexpected.{exception.GetType().Name}";
            deployElement = null;
        }

        var (execute, executeDetail) = deploy != "ok"
            ? ("skipped", "deployment rejected")
            : definitions is null
                ? ("skipped", "parser rejected")
                : Execute(definitions);

        return new MiwgObservation
        {
            Model = modelName,
            Parse = parse,
            ParseCode = parseCode,
            ParseDetail = parseDetail,
            ExecutableProcesses = executableProcesses,
            Deploy = deploy,
            DeployCode = deployCode,
            DeployElement = deployElement,
            DeployElementId = deployElementId,
            Execute = execute,
            ExecuteDetail = executeDetail,
        };
    }

    /// <summary>
    /// Startet den ersten ausführbaren Prozess mit reinem Startereignis und bedient wartende
    /// Elemente generisch, bis die Instanz endet, stehen bleibt oder die Schrittgrenze erreicht.
    /// </summary>
    private static (string Result, string? Detail) Execute(Definitions definitions)
    {
        var process = definitions.GetProcesses().FirstOrDefault(HasPlainStartEvent);
        if (process is null)
        {
            return ("skipped", "no executable process with a plain start event");
        }

        InstanceEngine instance;
        try
        {
            instance = new ProcessEngine(process, Helper.TestFlowzerConfig).StartProcess();
        }
        catch (Exception exception)
        {
            return ("exception", Describe(exception));
        }

        // Die Engine rechnet Fristen gegen den realen Zeitstempel des Tokens. Eine erfundene
        // Startzeit in der Vergangenheit wuerde deshalb nie eine Frist faellig machen; die Uhr
        // beginnt hier bei jetzt und springt je Schritt weit genug nach vorne.
        var clock = DateTime.UtcNow;
        var previousSignature = Signature(instance);

        for (var step = 0; step < MaximumExecutionSteps; step++)
        {
            if (instance.IsFinished)
            {
                return (instance.ProcessInstanceState.ToString().ToLowerInvariant(), null);
            }

            string? servedNodeId;
            try
            {
                clock = clock.AddDays(400);
                servedNodeId = ServeOneWaitingElement(instance, clock);
            }
            catch (Exception exception)
            {
                return ("exception", Describe(exception));
            }

            if (servedNodeId is null)
            {
                return ("stuck", $"no generic action for {DescribeWaitingNodes(instance)}");
            }

            var signature = Signature(instance);
            if (signature == previousSignature)
            {
                return ("stuck", $"serving '{servedNodeId}' did not advance the instance");
            }

            previousSignature = signature;
        }

        return ("step_limit", $"still waiting at {DescribeWaitingNodes(instance)} after {MaximumExecutionSteps} steps");
    }

    /// <summary>
    /// Bedient genau ein wartendes Element und gibt dessen Knoten-ID zurück. <c>null</c> heisst:
    /// Für keinen aktiven Token gibt es eine generische Handlung.
    /// </summary>
    private static string? ServeOneWaitingElement(InstanceEngine instance, DateTime clock)
    {
        foreach (var token in instance.ActiveTokens.ToArray())
        {
            switch (token.CurrentFlowNode)
            {
                case UserTask userTask:
                    instance.HandleTaskResult(token.Id, new Variables());
                    return userTask.Id;

                case IFlowzerWorkerTask { Implementation.Length: > 0 } workerTask:
                    instance.HandleTaskResult(token.Id, new Variables());
                    return ((FlowNode)workerTask).Id;

                case FlowzerIntermediateMessageCatchEvent catchEvent:
                    instance.HandleMessage(new Message
                    {
                        Name = catchEvent.MessageDefinition.Name,
                        CorrelationKey = catchEvent.MessageDefinition.FlowzerCorrelationKey,
                    });
                    return catchEvent.Id;

                case ReceiveTask { MessageRef: not null } receiveTask:
                    instance.HandleMessage(new Message
                    {
                        Name = receiveTask.MessageRef.Name,
                        CorrelationKey = receiveTask.MessageRef.FlowzerCorrelationKey,
                    });
                    return receiveTask.Id;

                case FlowzerIntermediateSignalCatchEvent signalCatchEvent:
                    instance.HandleSignal(signalCatchEvent.Signal.Name);
                    return signalCatchEvent.Id;

                case FlowzerIntermediateTimerCatchEvent timerCatchEvent:
                    instance.HandleTime(clock);
                    return timerCatchEvent.Id;
            }
        }

        // Ein angeheftetes Zeitereignis kann eine wartende Activity verlassen, ohne dass die
        // Activity selbst generisch bedienbar waere.
        var tokenWithBoundaryTimer = instance.ActiveTokens
            .FirstOrDefault(token => token.ActiveBoundaryEvents.OfType<FlowzerBoundaryTimerEvent>().Any());
        if (tokenWithBoundaryTimer is not null)
        {
            instance.HandleTime(clock);
            return tokenWithBoundaryTimer.CurrentFlowNode?.Id ?? tokenWithBoundaryTimer.Id.ToString();
        }

        return null;
    }

    private static bool HasPlainStartEvent(Process process) => process
        .GetStartFlowNodes()
        .Any(flowNode => flowNode.GetType() == typeof(StartEvent));

    private static string Signature(InstanceEngine instance) => string.Join(
        '|',
        instance.Tokens.Select(token => $"{token.Id}:{token.State}"));

    private static string DescribeWaitingNodes(InstanceEngine instance)
    {
        var nodes = instance.ActiveTokens
            .Select(token => token.CurrentFlowNode)
            .Where(flowNode => flowNode is not null)
            .Select(flowNode => $"{flowNode!.GetType().Name} '{flowNode.Id}'")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return nodes.Length == 0 ? "no active node" : string.Join(", ", nodes);
    }

    private static string Describe(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";

    /// <summary>
    /// Normalisiert die Parserablehnung auf einen stabilen Code. Das „ist nicht unterstützt"-
    /// Wurfmuster des Parsers trägt den Elementnamen im Text; ohne diese Normalisierung stünde
    /// der XML-Namensraum in der eingecheckten Tabelle.
    /// </summary>
    private static (string Code, string Detail) DescribeParseFailure(Exception exception)
    {
        if (exception is NotSupportedException)
        {
            var match = UnsupportedElementPattern.Match(exception.Message);
            if (match.Success)
            {
                return ("parser.element.unsupported", match.Groups["element"].Value);
            }
        }

        return ($"parser.{exception.GetType().Name}", exception.Message);
    }

    /// <summary>
    /// Die Elementart hinter einer Element-ID, angereichert um die Ereignisdefinition. Erst damit
    /// lässt sich im Bericht „Escalation-Boundary" von „Timer-Boundary" unterscheiden.
    /// </summary>
    private static string? DescribeElementKind(string xml, string? elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return null;
        }

        var element = XDocument.Parse(xml).Descendants()
            .FirstOrDefault(candidate => candidate.Attribute("id")?.Value == elementId);
        if (element is null)
        {
            return null;
        }

        var eventDefinition = element.Elements()
            .FirstOrDefault(child => child.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal));

        return eventDefinition is null
            ? element.Name.LocalName
            : $"{element.Name.LocalName}.{eventDefinition.Name.LocalName}";
    }

    // ---------------------------------------------------------------------------------------
    // Tabelle und Bericht
    // ---------------------------------------------------------------------------------------

    private static IReadOnlyDictionary<string, MiwgObservation> LoadExpectations()
    {
        var path = ExpectationsFileName;
        File.Exists(path).Should().BeTrue(
            "die Erwartungstabelle {0} gehört eingecheckt und in die Testausgabe kopiert", ExpectationsRepositoryPath);

        var entries = JsonSerializer.Deserialize<MiwgObservation[]>(File.ReadAllText(path), JsonOptions)
                      ?? throw new InvalidOperationException($"{ExpectationsRepositoryPath} ist leer oder ungültig.");

        return entries.ToDictionary(entry => entry.Model, StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateModelFiles() => Directory
        .Exists(FixtureDirectory)
        ? Directory.GetFiles(FixtureDirectory, "*.bpmn").OrderBy(SortKey, StringComparer.Ordinal)
        : [];

    /// <summary>
    /// Sortiert <c>C.2.0</c> vor <c>C.10.0</c>. Eine reine Textsortierung würde die Reihenfolge in
    /// Tabelle und Bericht bei jedem neuen zweistelligen Fall durcheinanderbringen.
    /// </summary>
    private static string SortKey(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        return string.Concat(Regex.Split(name, @"(\d+)")
            .Select(part => int.TryParse(part, out var number) ? number.ToString("D4") : part));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "core-engine.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Das Repository-Wurzelverzeichnis (core-engine.sln) wurde nicht gefunden.");
    }

}

/// <summary>
/// Das beobachtete Verhalten eines Referenzmodells über die drei Stufen. Dieselbe Form wird
/// eingecheckt (<c>miwg-expectations.json</c>) und im Testlauf verglichen.
/// </summary>
public sealed record MiwgObservation
{
    public required string Model { get; init; }

    /// <summary><c>ok</c>, <c>empty</c> (kein ausführbarer Prozess im Dokument) oder <c>rejected</c>.</summary>
    public required string Parse { get; init; }

    public string? ParseCode { get; init; }
    public string? ParseDetail { get; init; }
    public int ExecutableProcesses { get; init; }

    /// <summary><c>ok</c> oder <c>rejected</c>.</summary>
    public required string Deploy { get; init; }

    public string? DeployCode { get; init; }
    public string? DeployElement { get; init; }
    public string? DeployElementId { get; init; }

    /// <summary><c>completed</c>, <c>stuck</c>, <c>exception</c>, <c>step_limit</c> oder <c>skipped</c>.</summary>
    public required string Execute { get; init; }

    public string? ExecuteDetail { get; init; }
}
