namespace Model;

/// <summary>
/// Beschreibt einen aktiven Timer aus Sicht der Runtime, bevor daraus ein persistierter Subscription-Eintrag wird.
/// </summary>
public record TimerSubscriptionDescriptor(
    DateTime DueAt,
    string FlowNodeId,
    TimerSubscriptionKind Kind,
    Guid? TokenId = null);
