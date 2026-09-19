using System.Globalization;
using FlowzerDmn.Exceptions;
using FlowzerDmn.Model;

namespace FlowzerDmn.Evaluation;

/// <summary>
/// Wertet eine Decision aus einem eingelesenen DMN-Modell aus.
/// </summary>
/// <remarks>
/// <para>
/// Die Auswertung ist deterministisch und ohne Ein- und Ausgabe: dieselben Variablen
/// liefern dasselbe Ergebnis. Damit taugt derselbe Code fuer die Engine und fuer einen
/// Trockenlauf-Endpunkt.
/// </para>
/// <para>
/// Benoetigte Decisions werden vorher ausgewertet und stehen der aufrufenden Decision
/// unter ihrem Variablennamen im Kontext zur Verfuegung (siehe
/// <see cref="DmnDecision.ResultVariableName"/>). Zyklen kann es nicht geben, die faengt
/// schon der Parser ab.
/// </para>
/// </remarks>
public sealed class DmnDecisionEvaluator
{
    private readonly IFeelEngine _feelEngine;

    /// <summary>Erzeugt einen Auswerter ueber der gegebenen FEEL-Engine.</summary>
    /// <param name="feelEngine">Die FEEL-Engine, die Ausdruecke und Unary-Tests rechnet.</param>
    public DmnDecisionEvaluator(IFeelEngine feelEngine)
    {
        ArgumentNullException.ThrowIfNull(feelEngine);
        _feelEngine = feelEngine;
    }

    /// <summary>
    /// Wertet die Decision mit der Id <paramref name="decisionId"/> aus.
    /// </summary>
    /// <param name="definitions">Das eingelesene DMN-Modell.</param>
    /// <param name="decisionId">Die Id der gesuchten Decision.</param>
    /// <param name="variables">Die Variablen, die der Entscheidung zur Verfuegung stehen.</param>
    /// <returns>Das Ergebnis samt getroffener Regeln und Zwischenergebnissen.</returns>
    /// <exception cref="DmnEvaluationException">Die Decision gibt es nicht, oder eine Verdichtung passt nicht zu den Werten.</exception>
    /// <exception cref="DmnHitPolicyViolationException">Die Treffer widersprechen der Trefferregel.</exception>
    /// <exception cref="DmnInputOutOfRangeException">Ein Eingabewert liegt ausserhalb seiner <c>inputValues</c>.</exception>
    public DmnDecisionResult Evaluate(
        DmnDefinitions definitions,
        string decisionId,
        IReadOnlyDictionary<string, object?> variables)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(decisionId);
        ArgumentNullException.ThrowIfNull(variables);

        var decision = definitions.FindDecision(decisionId)
                       ?? throw new DmnEvaluationException(
                           $"Das DMN-Modell enthaelt keine Decision mit der Id '{decisionId}'.");

        var cache = new Dictionary<string, DmnDecisionResult>(StringComparer.Ordinal);
        return EvaluateDecision(definitions, decision, variables, cache);
    }

    private DmnDecisionResult EvaluateDecision(
        DmnDefinitions definitions,
        DmnDecision decision,
        IReadOnlyDictionary<string, object?> variables,
        Dictionary<string, DmnDecisionResult> cache)
    {
        var context = new Dictionary<string, object?>(variables, StringComparer.Ordinal);
        var requiredResults = new Dictionary<string, DmnDecisionResult>(StringComparer.Ordinal);

        foreach (var requiredId in decision.RequiredDecisionIds)
        {
            if (!cache.TryGetValue(requiredId, out var requiredResult))
            {
                var requiredDecision = definitions.FindDecision(requiredId)
                                       ?? throw new DmnEvaluationException(
                                           $"Die Decision '{decision.Id}' braucht die Decision '{requiredId}', " +
                                           "die es in diesem Modell nicht gibt.");

                requiredResult = EvaluateDecision(definitions, requiredDecision, variables, cache);
                cache[requiredId] = requiredResult;
            }

            requiredResults[requiredId] = requiredResult;

            // Auch die Ergebnisse weiter unten in der Kette bleiben sichtbar.
            foreach (var (nestedId, nestedResult) in requiredResult.RequiredResults)
            {
                requiredResults[nestedId] = nestedResult;
            }

            context[definitions.FindDecision(requiredId)!.ResultVariableName] = requiredResult.Value;
        }

        var (value, matchedRules) = decision.Logic switch
        {
            DmnLiteralExpression literal => (
                DmnTypeConverter.Convert(decision.OutputTypeRef, _feelEngine.Evaluate(literal.Text, context)),
                (IReadOnlyList<string>)[]),
            DmnDecisionTable table => EvaluateTable(decision, table, context),
            _ => throw new DmnEvaluationException(
                $"Die Decision '{decision.Id}' hat eine Entscheidungslogik, die der Auswerter nicht kennt.")
        };

        return new DmnDecisionResult
        {
            DecisionId = decision.Id,
            MatchedRules = matchedRules,
            Value = value,
            RequiredResults = requiredResults
        };
    }

    private (object? Value, IReadOnlyList<string> MatchedRules) EvaluateTable(
        DmnDecision decision,
        DmnDecisionTable table,
        IReadOnlyDictionary<string, object?> context)
    {
        var inputValues = ResolveInputValues(decision, table, context);
        var matches = MatchRules(table, inputValues, context);

        return table.HitPolicy switch
        {
            DmnHitPolicy.Unique => SingleResult(decision, table, matches, EnsureUnique),
            // FIRST nennt nur die Regel, die tatsaechlich gewonnen hat. Dass weiter unten
            // noch etwas passt haette, ist bei dieser Trefferregel gewollt und kein Befund.
            DmnHitPolicy.First => SingleResult(decision, table, matches,
                static (_, _, matched) => matched.Take(1).ToList()),
            DmnHitPolicy.Any => SingleResult(decision, table, matches, EnsureAnyAgrees),
            DmnHitPolicy.Priority => SingleResult(decision, table, OrderByPriority(table, matches, context),
                static (_, _, matched) => matched),
            DmnHitPolicy.Collect when table.Aggregation != DmnAggregation.None =>
                (Aggregate(decision, table, matches), matches.Select(match => match.Rule.Id).ToList()),
            DmnHitPolicy.Collect or DmnHitPolicy.RuleOrder => ListResult(matches),
            DmnHitPolicy.OutputOrder => ListResult(OrderByPriority(table, matches, context)),
            _ => throw new DmnEvaluationException(
                $"Die Trefferregel {table.HitPolicy} der Decision '{decision.Id}' wird nicht ausgewertet.")
        };
    }

    /// <summary>
    /// Rechnet die Eingabeausdruecke aus und prueft sie gegen die <c>inputValues</c>.
    /// </summary>
    private object?[] ResolveInputValues(
        DmnDecision decision,
        DmnDecisionTable table,
        IReadOnlyDictionary<string, object?> context)
    {
        var values = new object?[table.Inputs.Count];

        for (var index = 0; index < table.Inputs.Count; index++)
        {
            var input = table.Inputs[index];
            var value = _feelEngine.Evaluate(input.Expression, context);

            if (input.InputValues is { } allowed && !_feelEngine.UnaryTest(allowed.Text, value, context))
            {
                throw new DmnInputOutOfRangeException(
                    decision.Id, input.Id, input.Expression, value, allowed.Text);
            }

            values[index] = value;
        }

        return values;
    }

    private List<MatchedRule> MatchRules(
        DmnDecisionTable table,
        object?[] inputValues,
        IReadOnlyDictionary<string, object?> context)
    {
        var matches = new List<MatchedRule>();

        foreach (var rule in table.Rules)
        {
            var isMatch = true;

            for (var index = 0; index < table.Inputs.Count && isMatch; index++)
            {
                var entry = rule.InputEntries[index];

                // Ein leerer Eintrag und der Strich sind der Platzhalter der DMN-Notation:
                // die Spalte interessiert diese Regel nicht.
                if (IsWildcard(entry))
                {
                    continue;
                }

                isMatch = _feelEngine.UnaryTest(entry, inputValues[index], context);
            }

            if (isMatch)
            {
                matches.Add(BuildMatch(table, rule, context));
            }
        }

        return matches;
    }

    private MatchedRule BuildMatch(
        DmnDecisionTable table,
        DmnDecisionRule rule,
        IReadOnlyDictionary<string, object?> context)
    {
        var values = new object?[table.Outputs.Count];
        var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var index = 0; index < table.Outputs.Count; index++)
        {
            var entry = rule.OutputEntries[index];

            // Eine leere Ausgabe ist kein Fehler, sondern ein bewusstes "nichts".
            var value = string.IsNullOrWhiteSpace(entry) ? null : _feelEngine.Evaluate(entry, context);
            value = DmnTypeConverter.Convert(table.Outputs[index].TypeRef, value);

            values[index] = value;
            outputs[OutputKey(table.Outputs[index], index)] = value;
        }

        return new MatchedRule(rule, outputs, values);
    }

    /// <summary>
    /// Der Schluessel einer Ausgabespalte im Ergebnisobjekt: ihr Name, und bei der einen
    /// namenlosen Spalte, die DMN erlaubt, <c>result</c>.
    /// </summary>
    /// <remarks>
    /// Die Id waere hier der falsche Rueckfall: Editoren vergeben sie automatisch
    /// (<c>OutputClause_0x1</c>), und ein Business-Rule-Task soll nicht danach greifen muessen.
    /// </remarks>
    private static string OutputKey(DmnDecisionTableOutput output, int index)
    {
        if (!string.IsNullOrWhiteSpace(output.Name))
        {
            return output.Name;
        }

        return index == 0 ? "result" : $"result{index + 1}";
    }

    private static bool IsWildcard(string entry) =>
        string.IsNullOrWhiteSpace(entry) || entry.Trim() == "-";

    private static (object? Value, IReadOnlyList<string> MatchedRules) SingleResult(
        DmnDecision decision,
        DmnDecisionTable table,
        List<MatchedRule> matches,
        Func<DmnDecision, DmnDecisionTable, List<MatchedRule>, List<MatchedRule>> guard)
    {
        var checkedMatches = guard(decision, table, matches);
        var ids = checkedMatches.Select(match => match.Rule.Id).ToList();

        return (checkedMatches.Count == 0 ? null : checkedMatches[0].Outputs, ids);
    }

    private static (object? Value, IReadOnlyList<string> MatchedRules) ListResult(List<MatchedRule> matches) =>
        (matches.Select(match => match.Outputs).ToList(),
            matches.Select(match => match.Rule.Id).ToList());

    private static List<MatchedRule> EnsureUnique(
        DmnDecision decision,
        DmnDecisionTable table,
        List<MatchedRule> matches)
    {
        if (matches.Count > 1)
        {
            throw new DmnHitPolicyViolationException(
                decision.Id,
                table.HitPolicy,
                matches.Select(match => match.Rule.Id).ToList(),
                "Es darf hoechstens eine Regel passen, es passen aber mehrere.");
        }

        return matches;
    }

    private static List<MatchedRule> EnsureAnyAgrees(
        DmnDecision decision,
        DmnDecisionTable table,
        List<MatchedRule> matches)
    {
        for (var index = 1; index < matches.Count; index++)
        {
            if (!OutputsEqual(matches[0].Values, matches[index].Values))
            {
                throw new DmnHitPolicyViolationException(
                    decision.Id,
                    table.HitPolicy,
                    matches.Select(match => match.Rule.Id).ToList(),
                    "Mehrere Regeln passen, liefern aber unterschiedliche Ausgaben.");
            }
        }

        return matches;
    }

    private static bool OutputsEqual(object?[] left, object?[] right) =>
        left.Length == right.Length && !left.Where((value, index) => !ValuesEqual(value, right[index])).Any();

    /// <summary>
    /// Sortiert die Treffer nach der Rangfolge aus <c>outputValues</c>. Spalten ohne
    /// <c>outputValues</c> bleiben ausser Betracht, Werte ausserhalb der Liste landen
    /// hinten. Gleichrangiges behaelt die Regelreihenfolge.
    /// </summary>
    private List<MatchedRule> OrderByPriority(
        DmnDecisionTable table,
        List<MatchedRule> matches,
        IReadOnlyDictionary<string, object?> context)
    {
        if (matches.Count < 2)
        {
            return matches;
        }

        var rankings = new List<object?>?[table.Outputs.Count];
        for (var index = 0; index < table.Outputs.Count; index++)
        {
            var output = table.Outputs[index];
            rankings[index] = output.OutputValues is null
                ? null
                : output.OutputValues.Entries
                    .Select(entry => DmnTypeConverter.Convert(output.TypeRef, _feelEngine.Evaluate(entry, context)))
                    .ToList();
        }

        IOrderedEnumerable<MatchedRule>? ordered = null;
        for (var index = 0; index < table.Outputs.Count; index++)
        {
            var ranking = rankings[index];
            if (ranking is null)
            {
                continue;
            }

            var column = index;
            ordered = ordered is null
                ? matches.OrderBy(match => Rank(ranking, match.Values[column]))
                : ordered.ThenBy(match => Rank(ranking, match.Values[column]));
        }

        return ordered?.ToList() ?? matches;
    }

    private static int Rank(List<object?> ranking, object? value)
    {
        for (var index = 0; index < ranking.Count; index++)
        {
            if (ValuesEqual(ranking[index], value))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static object? Aggregate(DmnDecision decision, DmnDecisionTable table, List<MatchedRule> matches)
    {
        if (table.Aggregation == DmnAggregation.Count)
        {
            return matches.Count;
        }

        if (matches.Count == 0)
        {
            // Ohne Treffer gibt es nichts zu summieren; nur COUNT hat dann eine sinnvolle
            // Antwort, naemlich null Treffer.
            return null;
        }

        var numbers = matches.Select(match => ToNumber(decision, table, match)).ToList();

        return table.Aggregation switch
        {
            DmnAggregation.Sum => numbers.Sum(),
            DmnAggregation.Min => matches[IndexOfExtreme(numbers, smallest: true)].Values[0],
            DmnAggregation.Max => matches[IndexOfExtreme(numbers, smallest: false)].Values[0],
            _ => throw new DmnEvaluationException(
                $"Die Verdichtung {table.Aggregation} der Decision '{decision.Id}' wird nicht ausgewertet.")
        };
    }

    private static int IndexOfExtreme(List<double> numbers, bool smallest)
    {
        var best = 0;
        for (var index = 1; index < numbers.Count; index++)
        {
            if (smallest ? numbers[index] < numbers[best] : numbers[index] > numbers[best])
            {
                best = index;
            }
        }

        return best;
    }

    private static double ToNumber(DmnDecision decision, DmnDecisionTable table, MatchedRule match)
    {
        var value = match.Values[0];
        if (TryToDouble(value, out var number))
        {
            return number;
        }

        throw new DmnEvaluationException(
            $"Die Decision '{decision.Id}' verdichtet mit '{table.Aggregation}', die Regel " +
            $"'{match.Rule.Id}' liefert aber '{value ?? "null"}' und damit keine Zahl.");
    }

    private static bool TryToDouble(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case bool:
                number = 0;
                return false;
            case IConvertible convertible and (sbyte or byte or short or ushort or int or uint
                or long or ulong or float or double or decimal):
                number = convertible.ToDouble(CultureInfo.InvariantCulture);
                return true;
            default:
                number = 0;
                return false;
        }
    }

    /// <summary>
    /// Vergleicht zwei FEEL-Werte. Zahlen werden ueber <see cref="double"/> verglichen,
    /// damit <c>3</c> aus <c>outputValues</c> und <c>3.0</c> aus einer Regel dasselbe sind.
    /// </summary>
    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (TryToDouble(left, out var leftNumber) && TryToDouble(right, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        return left.Equals(right);
    }

    /// <summary>Eine Regel, die getroffen hat, samt ihrer berechneten Ausgaben.</summary>
    private sealed record MatchedRule(
        DmnDecisionRule Rule,
        IReadOnlyDictionary<string, object?> Outputs,
        object?[] Values);
}
