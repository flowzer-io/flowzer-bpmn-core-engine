namespace Model;

/// <summary>
/// Eine einzelne Entscheidung innerhalb einer Entscheidungsdatei.
/// </summary>
/// <param name="DecisionId">
/// Die Kennung aus <c>dmn:decision/@id</c> — genau die, die ein Business-Rule-Task ueber
/// <c>zeebe:calledDecision/@decisionId</c> aufruft.
/// </param>
/// <param name="Name">Der Anzeigename der Entscheidung; ohne Namen die Kennung.</param>
public sealed record DecisionSummary(string DecisionId, string Name);

/// <summary>
/// Der Katalogkopf einer Entscheidungsdatei. Die XML-Staende haengen als
/// <see cref="DecisionDefinitionVersion"/> daran; deployt ist immer der juengste.
/// </summary>
/// <param name="DecisionDefinitionId">
/// Die Katalogkennung. Sie kommt aus <c>dmn:definitions/@id</c> oder wird vom Server vergeben
/// und muss denselben Regeln genuegen wie die Kennung eines Workflows
/// (<c>StorageSystem.DefinitionIdRules</c>): In der Dateiablage wird sie zum Dateinamen, ein
/// Trennzeichen oder ein ".." darin zeigte aus dem Ablageordner heraus.
/// </param>
/// <param name="Name">Der Anzeigename der Datei.</param>
public sealed record DecisionDefinition(string DecisionDefinitionId, string Name);
