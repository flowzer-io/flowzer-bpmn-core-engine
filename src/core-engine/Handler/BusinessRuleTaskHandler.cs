namespace core_engine.Handler;

/// <summary>
/// Ein Business-Rule-Task wartet wie ein Service-Task: Der Token bleibt aktiv, und die Engine
/// stellt die zu rechnende Entscheidung bereit. Rechnen muss sie die Geschaeftslogik; weiter
/// geht es erst mit dem gemeldeten Ergebnis.
/// </summary>
internal class BusinessRuleTaskHandler : DefaultFlowNodeHandler
{
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        if (token.CurrentFlowNode is not BusinessRuleTask businessRuleTask)
        {
            return;
        }

        // Mit Auftragstyp ist der Task ein gewoehnlicher Auftrag an einen Worker: Er steht
        // dann in GetActiveServiceTasks() und wartet wie ein Service-Task. Hier ist nichts
        // zu tun — die Entscheidung faellt ausserhalb.
        if (businessRuleTask.Implementation.Length > 0)
        {
            return;
        }

        // Innerhalb eines Laufs merkt sich die Engine die bereits bereitgestellte Entscheidung
        // selbst; ein wartender Token wird in jedem weiteren Schritt erneut ausgefuehrt.
        processInstance.RequestDecision(token, businessRuleTask);
    }
}
