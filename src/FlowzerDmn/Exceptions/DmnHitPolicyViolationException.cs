using FlowzerDmn.Model;

namespace FlowzerDmn.Exceptions;

/// <summary>
/// Die Tabelle hat getroffen, aber nicht so, wie ihre Trefferregel es zulaesst: UNIQUE mit
/// mehreren Treffern oder ANY mit widerspruechlichen Ausgaben.
/// </summary>
/// <remarks>
/// Der Fehler nennt die beteiligten Regel-Ids, damit die widerspruechlichen Zeilen sofort
/// auffindbar sind.
/// </remarks>
public sealed class DmnHitPolicyViolationException : DmnException
{
    /// <summary>Erzeugt den Fehler fuer eine verletzte Trefferregel.</summary>
    /// <param name="decisionId">Die Id der betroffenen Decision.</param>
    /// <param name="hitPolicy">Die verletzte Trefferregel.</param>
    /// <param name="ruleIds">Die Ids der Regeln, die gemeinsam den Widerspruch erzeugen.</param>
    /// <param name="reason">Ein Satz, der den Widerspruch beschreibt.</param>
    public DmnHitPolicyViolationException(
        string decisionId,
        DmnHitPolicy hitPolicy,
        IReadOnlyList<string> ruleIds,
        string reason)
        : base($"Die Decision '{decisionId}' verletzt die Trefferregel {hitPolicy}: {reason} " +
               $"Beteiligte Regeln: {string.Join(", ", ruleIds)}.")
    {
        DecisionId = decisionId;
        HitPolicy = hitPolicy;
        RuleIds = ruleIds;
    }

    /// <summary>Die Id der betroffenen Decision.</summary>
    public string DecisionId { get; }

    /// <summary>Die verletzte Trefferregel.</summary>
    public DmnHitPolicy HitPolicy { get; }

    /// <summary>Die Ids der beteiligten Regeln.</summary>
    public IReadOnlyList<string> RuleIds { get; }
}
