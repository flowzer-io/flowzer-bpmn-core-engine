using System.Text;
using System.Text.RegularExpressions;

namespace core_engine_tests;

/// <summary>
/// Rendert den eingecheckten Bericht <c>docs/BPMN-MIWG-COVERAGE.md</c> aus den beobachteten
/// Ergebnissen. Bewusst von <see cref="MiwgConformanceTest"/> getrennt: Dort wird gemessen,
/// hier wird gedeutet und formuliert.
/// </summary>
internal static class MiwgCoverageReport
{
    private const string FixtureDirectory = "embeddings/miwg";

    public static string Build(IReadOnlyCollection<MiwgObservation> observations)
    {
        var manifest = File.Exists(Path.Combine(FixtureDirectory, "MANIFEST.md"))
            ? File.ReadAllText(Path.Combine(FixtureDirectory, "MANIFEST.md"))
            : string.Empty;
        var commit = Regex.Match(manifest, @"\| Commit \| `(?<sha>[0-9a-f]+)` \|").Groups["sha"].Value;

        var parsed = observations.Count(observation => observation.Parse == "ok");
        var parsedEmpty = observations.Count(observation => observation.Parse == "empty");
        var deployable = observations.Count(observation => observation.Deploy == "ok");
        var deployableWithWarnings = observations.Count(observation =>
            observation.Deploy == "ok" && observation.DeployWarnings.Count > 0);
        var executed = observations.Count(observation => observation.Execute == "completed");

        var report = new StringBuilder();
        report.AppendLine("# BPMN-MIWG-Konformitätsnachweis");
        report.AppendLine();
        report.AppendLine("Flowzer belegte seine BPMN-Abdeckung bisher nur über den eigenen");
        report.AppendLine("[Fähigkeitsvertrag](BPMN-CAPABILITIES.md). Dieser Bericht stellt daneben eine fremde,");
        report.AppendLine("nicht von Flowzer gewählte Messlatte: die Referenzmodelle der");
        report.AppendLine("[BPMN Model Interchange Working Group](https://github.com/bpmn-miwg/bpmn-miwg-test-suite).");
        report.AppendLine();
        report.AppendLine("**Der Bericht ist ein Nachweis, keine Zusage.** Er hält fest, was Flowzer heute mit");
        report.AppendLine("diesen Modellen tut — einschließlich aller Ablehnungen und ihrer Gründe.");
        report.AppendLine();
        report.AppendLine($"- **Stand:** {DateTime.UtcNow:yyyy-MM-dd}");
        report.AppendLine($"- **Modellsatz:** `Reference/*.bpmn` der MIWG-Suite, Commit `{commit}`");
        report.AppendLine("- **Lizenz der Suite:** Creative Commons Attribution 3.0 Unported (CC BY 3.0)");
        report.AppendLine("- **Herkunftsnachweis:** [`src/core-engine-tests/embeddings/miwg/MANIFEST.md`](../src/core-engine-tests/embeddings/miwg/MANIFEST.md)");
        report.AppendLine("- **Erzeugt aus:** [`src/core-engine-tests/miwg-expectations.json`](../src/core-engine-tests/miwg-expectations.json)");
        report.AppendLine("  über den Generatorlauf in `MiwgConformanceTest.RegenerateExpectationsAndReport`");
        report.AppendLine("- **Aktualisieren des Modellsatzes:** `scripts/ci/fetch-miwg-reference-models.sh`");
        report.AppendLine();

        report.AppendLine("## Zahlen");
        report.AppendLine();
        report.AppendLine("| Stufe | Ergebnis |");
        report.AppendLine("| --- | --- |");
        report.AppendLine($"| Modelle im Satz | {observations.Count} |");
        report.AppendLine($"| gelesen (Parser, mit ausführbarem Prozess) | {parsed} |");
        report.AppendLine($"| gelesen, aber leer (kein `isExecutable=\"true\"`) | {parsedEmpty} |");
        report.AppendLine($"| veröffentlichbar (Fähigkeitsvertrag) | {deployable} |");
        report.AppendLine($"| davon mit Warnung veröffentlichbar | {deployableWithWarnings} |");
        report.AppendLine($"| ausgeführt bis Ende | {executed} |");
        report.AppendLine();
        report.AppendLine("„gelesen, aber leer\" ist bewusst getrennt: Der Parser liest ausschließlich Prozesse mit");
        report.AppendLine("`isExecutable=\"true\"`. Ein Modell ohne solchen Prozess läuft ohne Ausnahme durch, ohne dass");
        report.AppendLine("ein einziges Element gelesen worden wäre. Das ist keine Abdeckung.");
        report.AppendLine();

        AppendStageLegend(report);
        AppendModelTable(report, observations);
        AppendRejectionSummary(report, observations);
        AppendPriorities(report, observations);

        return report.ToString();
    }

    private static void AppendStageLegend(StringBuilder report)
    {
        report.AppendLine("## Die drei Stufen");
        report.AppendLine();
        report.AppendLine("| Stufe | Was geprüft wird | Mögliche Werte |");
        report.AppendLine("| --- | --- | --- |");
        report.AppendLine("| Lesen | `ModelParser.ParseModel` ohne Ausnahme | `ok`, `empty`, `rejected` |");
        report.AppendLine("| Veröffentlichen | `BpmnCapabilityMatrix.ValidateForDeployment` | `ok` (ggf. mit Warnungen), `rejected` |");
        report.AppendLine("| Ausführen | Instanz starten und wartende Elemente generisch bedienen | `completed`, `stuck`, `exception`, `step_limit`, `skipped` |");
        report.AppendLine();
        report.AppendLine("Die Ausführungsstufe läuft nur, wenn die Veröffentlichung zusagt und das Modell einen");
        report.AppendLine("ausführbaren Prozess mit reinem Startereignis hat. Wartende Elemente werden generisch");
        report.AppendLine("bedient: User- und Worker-Tasks abgeschlossen, Nachrichten und Signale gesendet, die Zeit");
        report.AppendLine("für Timer vorgestellt — höchstens 200 Schritte.");
        report.AppendLine();
    }

    private static void AppendModelTable(StringBuilder report, IEnumerable<MiwgObservation> observations)
    {
        report.AppendLine("## Ergebnis je Modell");
        report.AppendLine();
        report.AppendLine("| Modell | Lesen | Grund | Veröffentlichen | Grund | Ausführen |");
        report.AppendLine("| --- | --- | --- | --- | --- | --- |");

        foreach (var observation in observations)
        {
            var parseReason = observation.Parse switch
            {
                "rejected" => $"`{observation.ParseCode}` — `{observation.ParseDetail}`",
                "empty" => "kein Prozess mit `isExecutable=\"true\"`",
                _ => $"{observation.ExecutableProcesses} ausführbare(r) Prozess(e)",
            };
            var deployReason = observation.Deploy == "rejected"
                ? $"`{observation.DeployCode}`" + (observation.DeployElement is null
                    ? string.Empty
                    : $" an `{observation.DeployElement}`")
                : observation.DeployWarnings.Count > 0
                    ? "Warnung: " + string.Join(", ", observation.DeployWarnings.Select(code => $"`{code}`"))
                    : "—";
            var execute = observation.Execute == "skipped"
                ? $"übersprungen ({observation.ExecuteDetail})"
                : observation.ExecuteDetail is null
                    ? $"`{observation.Execute}`"
                    : $"`{observation.Execute}` — {observation.ExecuteDetail}";

            report.AppendLine($"| `{observation.Model}` | `{observation.Parse}` | {parseReason} "
                + $"| `{observation.Deploy}` | {deployReason} | {execute} |");
        }

        report.AppendLine();
    }

    /// <summary>
    /// Die Deutung je Fehlercode. Sie ist Analyse, nicht Messung — und steht deshalb bewusst hier
    /// im Generator und nicht in der Beobachtungstabelle. Jede Zeile ist am Quelltext belegt.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Interpretations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["parser.element.unsupported"] =
                "Eine Elementart mit Ausführungssemantik, die `ModelParser.GetFlowElements` nicht kennt. "
                + "Reines Diagramm-Beiwerk zählt nicht mehr dazu — es wird überlesen. "
                + "**Echte Ausführungslücke.**",
            ["parser.ModelValidationException"] =
                "Ein benannter Modellfehler des Parsers: fehlende Kennung, Verweis ins Leere oder eine "
                + "unvollständige Pflichtangabe. **Modellfehler, keine Fähigkeitslücke.**",
            ["parser.FlowzerModelParseException"] =
                "Ein benannter Parserfehler der Flowzer-Erweiterungen. **Modellfehler, keine BPMN-Lücke.**",
            ["bpmn.process.executable_required"] =
                "Das Dokument enthält keinen Prozess mit `isExecutable=\"true\"`. Die MIWG-Reihen A und B sind "
                + "reine Modellierungs- und Layoutbeispiele. **Kein Befund über die Engine.**",

            ["bpmn.element.unsupported"] =
                "Elementart, die der Fähigkeitsvertrag v7 nicht führt. **Echte Ausführungslücke.**",
            ["bpmn.element.not_executable"] =
                "Elementart, die der Vertrag als lesbar, aber ausdrücklich nicht als ausführbar führt. "
                + "**Echte Ausführungslücke.**",
            ["bpmn.element.id_required"] =
                "Ein ausführbarer Prozessbestandteil ohne `id`. Diagramm-Beiwerk wie `bpmn:ioSpecification` "
                + "zählt nicht mehr dazu und wird überlesen. **Modellfehler.**",
            ["bpmn.message.name_required"] =
                "Das Element zeigt auf eine `bpmn:message` ohne `name`. Der Parser liest sie, aber "
                + "korrelieren könnte sie nie. **Modellfehler.**",
            ["bpmn.flow_node.unreachable"] =
                "Ein Knoten ohne Weg von einem Start- oder Boundary-Event. Hier ist es ein "
                + "`subProcess triggeredByEvent=\"true\"` — ein Event-Subprozess, den weder Vertrag noch "
                + "Engine kennen. **Echte Ausführungslücke.**",
        };

    private static string Interpret(string? code) =>
        code is not null && Interpretations.TryGetValue(code, out var text) ? text : "—";

    private static void AppendRejectionSummary(StringBuilder report, IReadOnlyCollection<MiwgObservation> observations)
    {
        report.AppendLine("## Häufigste Ablehnungsgründe");
        report.AppendLine();
        report.AppendLine("### Lesen (Parser)");
        report.AppendLine();
        report.AppendLine("| Grund | Anzahl | Modelle | Deutung |");
        report.AppendLine("| --- | --- | --- | --- |");

        var parseGroups = observations
            .Where(observation => observation.Parse == "rejected")
            .GroupBy(observation => observation.ParseCode ?? "(unbekannt)")
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in parseGroups)
        {
            var kinds = group.Key == "parser.element.unsupported"
                ? " an " + string.Join(", ", group.Select(observation => $"`{observation.ParseDetail}`").Distinct(StringComparer.Ordinal))
                : string.Empty;
            report.AppendLine($"| `{group.Key}`{kinds} | {group.Count()} | {Models(group)} | {Interpret(group.Key)} |");
        }

        report.AppendLine();
        report.AppendLine("> Reines Diagramm-Beiwerk überliest der Parser (siehe");
        report.AppendLine("> [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md), „Was überlesen wird\"). An einem Element");
        report.AppendLine("> mit Ausführungssemantik, das er nicht kennt, bricht er dagegen ab. Gemeldet wird");
        report.AppendLine("> deshalb immer nur die **erste** Hürde eines Modells — hinter ihr können weitere liegen.");
        report.AppendLine();

        report.AppendLine("### Veröffentlichen (Fähigkeitsvertrag)");
        report.AppendLine();
        report.AppendLine("| Fehlercode | Elementart | Anzahl | Modelle | Deutung |");
        report.AppendLine("| --- | --- | --- | --- | --- |");

        var deployGroups = observations
            .Where(observation => observation.Deploy == "rejected")
            .GroupBy(observation => (observation.DeployCode, observation.DeployElement))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.DeployCode, StringComparer.Ordinal);

        foreach (var group in deployGroups)
        {
            report.AppendLine($"| `{group.Key.DeployCode}` | `{group.Key.DeployElement ?? "—"}` | {group.Count()} "
                + $"| {Models(group)} | {Interpret(group.Key.DeployCode)} |");
        }

        report.AppendLine();
        report.AppendLine("> Auch die Veröffentlichungsprüfung meldet bewusst den **ersten** Fehler in");
        report.AppendLine("> Dokumentreihenfolge (siehe [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md)). Die Zahlen sind");
        report.AppendLine("> also eine Rangfolge der ersten Hürde, keine vollständige Lückenzählung.");
        report.AppendLine();
    }

    private static string Models(IEnumerable<MiwgObservation> group) => string.Join(
        ", ",
        group.Select(observation => $"`{observation.Model}`"));

    private static void AppendPriorities(StringBuilder report, IReadOnlyCollection<MiwgObservation> observations)
    {
        report.AppendLine("## Was das für die nächsten Pakete heißt");
        report.AppendLine();
        report.AppendLine("Die Befunde zerfallen in drei Gruppen, die nicht vermischt werden dürfen. Nur die dritte");
        report.AppendLine("ist eine Ausführungslücke.");
        report.AppendLine();

        var noExecutableProcess = observations
            .Where(observation => observation.DeployCode == "bpmn.process.executable_required")
            .ToArray();
        report.AppendLine($"### 1. Das Modell will gar nicht ausgeführt werden ({noExecutableProcess.Length} Modelle)");
        report.AppendLine();
        report.AppendLine("Die MIWG-Reihen A und B prüfen Layout und Elementabdeckung von Modellierungswerkzeugen.");
        report.AppendLine("Ihre Prozesse tragen kein `isExecutable=\"true\"`. Dass Flowzer sie nicht veröffentlicht,");
        report.AppendLine("sagt **nichts** über die Engine — eine Ausführungsengine darf solche Modelle ablehnen.");
        report.AppendLine("Diese Modelle gehören in einen Import- oder Anzeigenachweis, nicht in die Ausführungsbilanz.");
        report.AppendLine();
        var alsoRejectedWhileReading = noExecutableProcess.Count(observation => observation.Parse != "empty");
        report.AppendLine($"Bei {noExecutableProcess.Count(observation => observation.Parse == "empty")} davon läuft auch der Parser ohne Ausnahme durch — er liest dann schlicht nichts.");
        report.AppendLine(alsoRejectedWhileReading == 0
            ? "Keines scheitert zusätzlich schon beim Lesen."
            : $"Die übrigen {alsoRejectedWhileReading} scheitern zusätzlich schon beim Lesen (siehe Gruppe 2).");
        report.AppendLine();

        report.AppendLine("### 2. Lese- und Prüfhürden vor der eigentlichen Semantik");
        report.AppendLine();
        report.AppendLine("Diese Fälle scheitern, **bevor** irgendeine Ausführungsfrage gestellt wird:");
        report.AppendLine();
        report.AppendLine("- **User-Tasks brauchen ein `zeebe:formDefinition`.** Das ist eine bewusste");
        report.AppendLine("  Produktentscheidung von Flowzer und keine BPMN-Lücke; werkzeugneutrale Modelle tragen");
        report.AppendLine("  diese Erweiterung nie. Sie ist in diesem Satz die verbliebene Lesehürde und steht vor");
        report.AppendLine("  jeder Aussage darüber, welche BPMN-Semantik Flowzer wirklich fehlt.");
        report.AppendLine();
        report.AppendLine("Drei frühere Hürden sind gefallen und stehen deshalb nicht mehr in dieser Liste:");
        report.AppendLine();
        report.AppendLine("- **Diagramm-Beiwerk wird überlesen.** `laneSet`, `lane`, `textAnnotation`, `association`,");
        report.AppendLine("  Datenobjekte, `ioSpecification` und Verwandtes tragen keine Ausführungssemantik. Parser");
        report.AppendLine("  und Veröffentlichungsprüfung gehen seither über dieselbe Liste hinweg, statt zu werfen.");
        report.AppendLine("  Überlesen heißt ausdrücklich **nicht** ausgeführt: Lanes weisen keine Arbeit zu.");
        report.AppendLine("- **`bpmn:message` ohne `name` wird gelesen.** Der Name ist in BPMN optional; die frühere");
        report.AppendLine("  `NullReferenceException` war ein Robustheitsmangel. Ein Element, das auf eine namenlose");
        report.AppendLine("  Nachricht zeigt, beanstandet nun die Veröffentlichungsprüfung mit");
        report.AppendLine("  `bpmn.message.name_required` am Knoten — dort, wo es die Konsole markieren kann.");
        report.AppendLine("- **Unbekannte Elemente heißen beim Namen.** Statt einer `NotSupportedException` mit rohem");
        report.AppendLine("  XML-Namen wirft der Parser eine `ModelValidationException` mit Elementart und Kennung.");
        report.AppendLine("  Bei einem Deployment sieht man sie ohnehin nicht: Dort läuft die Veröffentlichungs-");
        report.AppendLine("  prüfung vor dem Parser und meldet `bpmn.element.unsupported` am betroffenen Knoten.");
        report.AppendLine();

        report.AppendLine("### 3. Echte Ausführungslücken");
        report.AppendLine();

        var missingKinds = observations
            .Where(observation => observation.Deploy == "rejected"
                && observation.DeployCode is "bpmn.element.unsupported" or "bpmn.event_definition.unsupported"
                    or "bpmn.element.not_executable" or "bpmn.flow_node.unreachable")
            .Where(observation => observation.DeployElement is not null)
            .GroupBy(observation => observation.DeployElement!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        if (missingKinds.Length == 0)
        {
            report.AppendLine("Im aktuellen Satz erreicht kein Modell diese Stufe: Die erste Hürde ist überall eine");
            report.AppendLine("andere. Die Liste füllt sich, sobald die Hürden aus Gruppe 2 fallen.");
        }
        else
        {
            report.AppendLine("Elementarten, an denen ein Modell als **erste** Hürde scheitert, weil Vertrag und Engine");
            report.AppendLine("sie nicht führen:");
            report.AppendLine();
            report.AppendLine("| Elementart | Anzahl | Modelle |");
            report.AppendLine("| --- | --- | --- |");
            foreach (var group in missingKinds)
            {
                report.AppendLine($"| `{group.Key}` | {group.Count()} | {Models(group)} |");
            }
        }

        report.AppendLine();
        report.AppendLine("Weil jede Stufe beim ersten Fehler abbricht, ist diese Tabelle **keine** vollständige");
        report.AppendLine("Lückenliste. Vom Vertrag v7 ausdrücklich nicht zugesagt sind darüber hinaus: Event-based");
        report.AppendLine("Gateway, Inclusive Gateway, Complex Gateway, Event-Subprozess, Escalation, Kompensation,");
        report.AppendLine("Signal-Throw und Signal-Ende, Script-Tasks, Transaktionen, Lanes und Pools sowie Datenobjekte");
        report.AppendLine("und Datenflüsse (siehe [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md)).");
        report.AppendLine();
        report.AppendLine("## Grenzen dieses Nachweises");
        report.AppendLine();
        report.AppendLine("- Geprüft wird **Import**, nicht der MIWG-Export- oder Roundtrip-Teil. Flowzer schreibt");
        report.AppendLine("  kein BPMN zurück und tritt deshalb nicht als MIWG-Werkzeug an.");
        report.AppendLine("- Die Ausführungsstufe bedient generisch und ohne Fachdaten. Ein Modell, das eine");
        report.AppendLine("  bestimmte Variable braucht, kann daher hängen bleiben, obwohl die Engine es grundsätzlich");
        report.AppendLine("  könnte. Solche Fälle stehen als `stuck` mit dem Knoten, an dem es endete.");
        report.AppendLine("- Jede Stufe meldet den ersten Fehler. Die Zahlen sind eine Rangfolge, keine Gesamtbilanz.");
        report.AppendLine("- Solange kein Referenzmodell die Veröffentlichungsprüfung besteht, wird die dritte Stufe");
        report.AppendLine("  von keinem MIWG-Modell ausgeführt. Damit ein `0` dort nicht mit einem stillen Defekt im");
        report.AppendLine("  Durchlauf verwechselt werden kann, belegt `Execution_stage_completes_a_supported_model`");
        report.AppendLine("  denselben Durchlauf an fünf bekannt ausführbaren Flowzer-Modellen (Gateways, paralleler");
        report.AppendLine("  Fluss, Nachrichten, User-Task, Zeitereignis).");
        report.AppendLine("- Die Modelle sind eine eingecheckte Kopie. Der Test läuft offline; nur");
        report.AppendLine("  `scripts/ci/fetch-miwg-reference-models.sh` braucht Netz.");
    }
}
