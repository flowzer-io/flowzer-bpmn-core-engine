namespace BPMN.Gateways;

/// <summary>
/// Exklusives Gateway. Implementiert <see cref="IHasDefault"/>, weil BPMN 2.0 für
/// diesen Gateway-Typ einen Standardfluss (Attribut <c>default</c>) vorsieht.
///
/// Ohne das Interface hat der Parser die bereits gelesene <see cref="DefaultId"/>
/// beim Aufbau der Sequenzflüsse ignoriert — er prüft dort auf <see cref="IHasDefault"/>.
/// Der Standardfluss galt dadurch als bedingungslos und die Laufzeit brach mit
/// "There is a SequenceFlow without a Condition and not default for Exclusive Gateway" ab.
/// </summary>
public record ExclusiveGateway : Gateway, IHasDefault
{
    public SequenceFlow? Default { get; set; }
    public string? DefaultId { get; init; }
}
