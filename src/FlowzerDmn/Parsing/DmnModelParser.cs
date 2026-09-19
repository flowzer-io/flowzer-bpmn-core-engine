using System.Xml;
using System.Xml.Linq;
using FlowzerDmn.Exceptions;
using FlowzerDmn.Model;

namespace FlowzerDmn.Parsing;

/// <summary>
/// Liest eine DMN-Datei in das Modell der Bibliothek ein.
/// </summary>
/// <remarks>
/// <para>
/// Der Parser arbeitet ueber die lokalen Elementnamen und ist damit gegenueber den
/// Namensraeumen von DMN 1.1 bis 1.4 gleichgueltig. Die Fassung wird nur erkannt und in
/// <see cref="DmnDefinitions.Version"/> abgelegt.
/// </para>
/// <para>
/// Alles, was die Bibliothek nicht kennt, wird ignoriert — darunter der Diagrammteil
/// <c>DMNDI</c>, <c>inputData</c>, <c>businessKnowledgeModel</c> und
/// <c>extensionElements</c>. Nur eine Entscheidungslogik, die diese Ausbaustufe nicht
/// auswerten kann, fuehrt zu einer <see cref="DmnUnsupportedException"/>: sonst wuerde
/// eine Decision spaeter still nichts liefern.
/// </para>
/// </remarks>
public static class DmnModelParser
{
    private const string Dmn11Namespace = "http://www.omg.org/spec/DMN/20151101/dmn.xsd";
    private const string Dmn12Namespace = "https://www.omg.org/spec/DMN/20180521/MODEL/";
    private const string Dmn13Namespace = "https://www.omg.org/spec/DMN/20191111/MODEL/";
    private const string Dmn14Namespace = "https://www.omg.org/spec/DMN/20211108/MODEL/";

    /// <summary>
    /// Entscheidungslogiken, die es in DMN gibt, die diese Ausbaustufe aber nicht auswertet.
    /// </summary>
    private static readonly HashSet<string> UnsupportedLogicElements = new(StringComparer.Ordinal)
    {
        "context",
        "invocation",
        "relation",
        "list",
        "functionDefinition",
        "conditional",
        "filter",
        "for",
        "every",
        "some"
    };

    /// <summary>
    /// Liest eine DMN-Datei ein und liefert das geprueefte Modell.
    /// </summary>
    /// <param name="xml">Der vollstaendige Inhalt der DMN-Datei.</param>
    /// <returns>Das eingelesene Modell.</returns>
    /// <exception cref="DmnParseException">
    /// Das XML ist kaputt, eine Pflichtangabe fehlt, eine Tabelle ist in sich
    /// widerspruechlich oder die Decisions haengen im Kreis voneinander ab.
    /// </exception>
    /// <exception cref="DmnUnsupportedException">
    /// Eine Decision benutzt eine Entscheidungslogik, die diese Ausbaustufe nicht kennt.
    /// </exception>
    public static DmnDefinitions Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException exception)
        {
            throw new DmnParseException("Die DMN-Datei ist kein gueltiges XML.", exception);
        }

        var root = document.Root
                   ?? throw new DmnParseException("Die DMN-Datei hat kein Wurzelelement.");

        if (root.Name.LocalName != "definitions")
        {
            throw new DmnParseException(
                $"Erwartet wurde das Wurzelelement 'definitions', gefunden wurde '{root.Name.LocalName}'.");
        }

        var decisions = Children(root, "decision").Select(ParseDecision).ToList();
        ValidateDecisionNetwork(decisions);

        return new DmnDefinitions
        {
            Id = Attribute(root, "id") ?? string.Empty,
            Name = Attribute(root, "name"),
            Namespace = Attribute(root, "namespace") ?? string.Empty,
            ModelNamespace = root.Name.NamespaceName,
            Version = ResolveVersion(root.Name.NamespaceName),
            Decisions = decisions
        };
    }

    private static DmnVersion ResolveVersion(string modelNamespace) => modelNamespace switch
    {
        Dmn11Namespace => DmnVersion.Dmn11,
        Dmn12Namespace => DmnVersion.Dmn12,
        Dmn13Namespace => DmnVersion.Dmn13,
        Dmn14Namespace => DmnVersion.Dmn14,
        _ => DmnVersion.Unknown
    };

    private static DmnDecision ParseDecision(XElement element)
    {
        var id = Attribute(element, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            var name = Attribute(element, "name");
            throw new DmnParseException(
                "Eine 'decision' ohne id laesst sich nicht auswerten" +
                (name is null ? "." : $" (name: '{name}')."));
        }

        var variable = Children(element, "variable").FirstOrDefault();

        return new DmnDecision
        {
            Id = id,
            Name = Attribute(element, "name"),
            OutputVariableName = variable is null ? null : Attribute(variable, "name"),
            OutputTypeRef = variable is null ? null : Attribute(variable, "typeRef"),
            Requirements = Children(element, "informationRequirement").Select(ParseRequirement).ToList(),
            Logic = ParseLogic(element, id)
        };
    }

    private static DmnInformationRequirement ParseRequirement(XElement element) =>
        new()
        {
            Id = Attribute(element, "id"),
            RequiredDecisionId = ResolveHref(Children(element, "requiredDecision").FirstOrDefault()),
            RequiredInputId = ResolveHref(Children(element, "requiredInput").FirstOrDefault())
        };

    /// <summary>
    /// Loest eine Referenz wie <c>#decideDish</c> oder <c>someUri#decideDish</c> in die
    /// blosse Id auf.
    /// </summary>
    private static string? ResolveHref(XElement? element)
    {
        var href = element is null ? null : Attribute(element, "href");
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var separator = href.LastIndexOf('#');
        return separator < 0 ? href : href[(separator + 1)..];
    }

    private static DmnDecisionLogic ParseLogic(XElement decision, string decisionId)
    {
        foreach (var child in decision.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "decisionTable":
                    return ParseDecisionTable(child, decisionId);
                case "literalExpression":
                    return ParseLiteralExpression(child, decisionId);
                default:
                    if (UnsupportedLogicElements.Contains(child.Name.LocalName))
                    {
                        throw new DmnUnsupportedException(child.Name.LocalName, decisionId);
                    }

                    break;
            }
        }

        throw new DmnParseException($"Die Decision '{decisionId}' enthaelt keine Entscheidungslogik.");
    }

    private static DmnLiteralExpression ParseLiteralExpression(XElement element, string decisionId)
    {
        var text = Text(element)
                   ?? throw new DmnParseException(
                       $"Der 'literalExpression' der Decision '{decisionId}' fehlt der Ausdruckstext.");

        return new DmnLiteralExpression
        {
            Id = Attribute(element, "id"),
            Text = text,
            ExpressionLanguage = Attribute(element, "expressionLanguage")
        };
    }

    private static DmnDecisionTable ParseDecisionTable(XElement element, string decisionId)
    {
        var hitPolicy = ParseHitPolicy(Attribute(element, "hitPolicy"), decisionId);
        var aggregation = ParseAggregation(Attribute(element, "aggregation"), decisionId);

        if (aggregation != DmnAggregation.None && hitPolicy != DmnHitPolicy.Collect)
        {
            throw new DmnParseException(
                $"Die Decision '{decisionId}' nennt die Verdichtung '{aggregation}', " +
                $"benutzt aber die Trefferregel {hitPolicy}. Verdichtet wird nur bei COLLECT.");
        }

        var inputs = Children(element, "input").Select(ParseInput).ToList();
        var outputs = Children(element, "output").Select(ParseOutput).ToList();
        var rules = Children(element, "rule").Select((rule, index) => ParseRule(rule, decisionId, index)).ToList();

        ValidateTable(decisionId, hitPolicy, aggregation, inputs, outputs, rules);

        return new DmnDecisionTable
        {
            Id = Attribute(element, "id"),
            HitPolicy = hitPolicy,
            Aggregation = aggregation,
            Inputs = inputs,
            Outputs = outputs,
            Rules = rules
        };
    }

    private static void ValidateTable(
        string decisionId,
        DmnHitPolicy hitPolicy,
        DmnAggregation aggregation,
        IReadOnlyList<DmnDecisionTableInput> inputs,
        IReadOnlyList<DmnDecisionTableOutput> outputs,
        IReadOnlyList<DmnDecisionRule> rules)
    {
        if (outputs.Count == 0)
        {
            throw new DmnParseException($"Die Tabelle der Decision '{decisionId}' hat keine Ausgabespalte.");
        }

        if (outputs.Count > 1 && outputs.Any(output => string.IsNullOrWhiteSpace(output.Name)))
        {
            throw new DmnParseException(
                $"Die Tabelle der Decision '{decisionId}' hat mehrere Ausgabespalten; " +
                "dann braucht jede Spalte einen Namen, sonst laesst sich das Ergebnis nicht benennen.");
        }

        if (aggregation != DmnAggregation.None && outputs.Count != 1)
        {
            throw new DmnParseException(
                $"Die Decision '{decisionId}' verdichtet mit '{aggregation}', hat aber " +
                $"{outputs.Count} Ausgabespalten. Verdichtet wird nur ueber genau eine Spalte.");
        }

        if (hitPolicy is DmnHitPolicy.Priority or DmnHitPolicy.OutputOrder &&
            outputs.All(output => output.OutputValues is null))
        {
            throw new DmnParseException(
                $"Die Decision '{decisionId}' benutzt die Trefferregel {hitPolicy}, " +
                "nennt aber keine 'outputValues'. Ohne sie gibt es keine Rangfolge.");
        }

        foreach (var rule in rules)
        {
            if (rule.InputEntries.Count != inputs.Count)
            {
                throw new DmnParseException(
                    $"Die Regel '{rule.Id}' der Decision '{decisionId}' hat " +
                    $"{rule.InputEntries.Count} Bedingungen, die Tabelle aber {inputs.Count} Eingabespalten.");
            }

            if (rule.OutputEntries.Count != outputs.Count)
            {
                throw new DmnParseException(
                    $"Die Regel '{rule.Id}' der Decision '{decisionId}' hat " +
                    $"{rule.OutputEntries.Count} Ausgaben, die Tabelle aber {outputs.Count} Ausgabespalten.");
            }
        }
    }

    private static DmnDecisionTableInput ParseInput(XElement element)
    {
        var inputExpression = Children(element, "inputExpression").FirstOrDefault();

        return new DmnDecisionTableInput
        {
            Id = Attribute(element, "id"),
            Label = Attribute(element, "label"),
            Expression = (inputExpression is null ? null : Text(inputExpression)) ?? string.Empty,
            TypeRef = inputExpression is null ? null : Attribute(inputExpression, "typeRef"),
            InputValues = ParseUnaryTests(Children(element, "inputValues").FirstOrDefault())
        };
    }

    private static DmnDecisionTableOutput ParseOutput(XElement element) =>
        new()
        {
            Id = Attribute(element, "id"),
            Name = Attribute(element, "name"),
            Label = Attribute(element, "label"),
            TypeRef = Attribute(element, "typeRef"),
            OutputValues = ParseUnaryTests(Children(element, "outputValues").FirstOrDefault())
        };

    private static DmnUnaryTests? ParseUnaryTests(XElement? element)
    {
        var text = element is null ? null : Text(element);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new DmnUnaryTests
        {
            Text = text,
            Entries = DmnUnaryTestSplitter.Split(text)
        };
    }

    private static DmnDecisionRule ParseRule(XElement element, string decisionId, int index) =>
        new()
        {
            // Eine Regel ohne id bekommt eine aus ihrer Position. Regel-Ids tauchen in
            // Ergebnissen und Fehlern auf; ohne sie waere nicht zu sehen, welche Zeile
            // gegriffen hat.
            Id = Attribute(element, "id") ?? $"{decisionId}-rule-{index + 1}",
            Description = Children(element, "description").FirstOrDefault()?.Value.Trim(),
            InputEntries = Children(element, "inputEntry").Select(entry => Text(entry) ?? string.Empty).ToList(),
            OutputEntries = Children(element, "outputEntry").Select(entry => Text(entry) ?? string.Empty).ToList()
        };

    private static DmnHitPolicy ParseHitPolicy(string? value, string decisionId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DmnHitPolicy.Unique;
        }

        return Normalize(value) switch
        {
            "UNIQUE" => DmnHitPolicy.Unique,
            "FIRST" => DmnHitPolicy.First,
            "PRIORITY" => DmnHitPolicy.Priority,
            "ANY" => DmnHitPolicy.Any,
            "COLLECT" => DmnHitPolicy.Collect,
            "RULE ORDER" => DmnHitPolicy.RuleOrder,
            "OUTPUT ORDER" => DmnHitPolicy.OutputOrder,
            _ => throw new DmnParseException(
                $"Die Decision '{decisionId}' nennt die unbekannte Trefferregel '{value}'.")
        };
    }

    private static DmnAggregation ParseAggregation(string? value, string decisionId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DmnAggregation.None;
        }

        return Normalize(value) switch
        {
            "SUM" => DmnAggregation.Sum,
            "MIN" => DmnAggregation.Min,
            "MAX" => DmnAggregation.Max,
            "COUNT" => DmnAggregation.Count,
            _ => throw new DmnParseException(
                $"Die Decision '{decisionId}' nennt die unbekannte Verdichtung '{value}'.")
        };
    }

    /// <summary>
    /// Bringt ein Attribut wie <c>"rule order"</c> oder <c>"RULE  ORDER"</c> auf eine Form.
    /// </summary>
    private static string Normalize(string value) =>
        string.Join(' ', value.ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Prueft die Abhaengigkeiten zwischen den Decisions: eindeutige Ids, aufloesbare
    /// Referenzen und keine Zyklen. Der Zyklus muss beim Parsen auffallen — beim Auswerten
    /// waere er eine Endlosschleife.
    /// </summary>
    private static void ValidateDecisionNetwork(IReadOnlyList<DmnDecision> decisions)
    {
        var byId = new Dictionary<string, DmnDecision>(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            if (!byId.TryAdd(decision.Id, decision))
            {
                throw new DmnParseException($"Die Decision-Id '{decision.Id}' kommt mehrfach vor.");
            }
        }

        foreach (var decision in decisions)
        {
            foreach (var requiredId in decision.RequiredDecisionIds)
            {
                if (!byId.ContainsKey(requiredId))
                {
                    throw new DmnParseException(
                        $"Die Decision '{decision.Id}' braucht die Decision '{requiredId}', " +
                        "die es in dieser Datei nicht gibt.");
                }
            }
        }

        var state = new Dictionary<string, VisitState>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var decision in decisions)
        {
            Visit(decision, byId, state, path);
        }
    }

    private static void Visit(
        DmnDecision decision,
        IReadOnlyDictionary<string, DmnDecision> byId,
        IDictionary<string, VisitState> state,
        List<string> path)
    {
        if (state.TryGetValue(decision.Id, out var visitState))
        {
            if (visitState == VisitState.Done)
            {
                return;
            }

            var cycleStart = path.IndexOf(decision.Id);
            var cycle = cycleStart < 0
                ? new List<string> { decision.Id }
                : path.Skip(cycleStart).Append(decision.Id).ToList();
            throw new DmnParseException(
                "Die Decisions haengen im Kreis voneinander ab: " + string.Join(" -> ", cycle) + ".");
        }

        state[decision.Id] = VisitState.InProgress;
        path.Add(decision.Id);

        foreach (var requiredId in decision.RequiredDecisionIds)
        {
            Visit(byId[requiredId], byId, state, path);
        }

        path.RemoveAt(path.Count - 1);
        state[decision.Id] = VisitState.Done;
    }

    private static IEnumerable<XElement> Children(XElement element, string localName) =>
        element.Elements().Where(child => child.Name.LocalName == localName);

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;

    /// <summary>Liefert den getrimmten Inhalt des <c>text</c>-Kindelements.</summary>
    private static string? Text(XElement element) =>
        Children(element, "text").FirstOrDefault()?.Value.Trim();

    private enum VisitState
    {
        InProgress,
        Done
    }
}
