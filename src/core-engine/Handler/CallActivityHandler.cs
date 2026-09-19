namespace core_engine.Handler;

/// <summary>
/// Eine lokale Call Activity wartet wie ein Service-Task: Der Token bleibt aktiv, und die Engine
/// stellt den Aufruf des Zielprozesses bereit. Starten muss ihn der Aufrufer; weiter geht es
/// erst, wenn das Ende der Kindinstanz zurückgemeldet wird.
/// </summary>
internal class CallActivityHandler : DefaultFlowNodeHandler
{
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        // Ein wartender Token wird in jedem weiteren Engine-Schritt erneut ausgeführt — etwa
        // wenn ein paralleler Zweig läuft oder die Instanz später aus der Ablage kommt. Die
        // bereits vergebene Kindinstanz ist deshalb die Bedingung: Ohne sie ist der Aufruf noch
        // nicht gestartet, mit ihr darf kein zweiter entstehen.
        if (token.CalledInstanceId is not null || token.CurrentFlowNode is not CallActivity callActivity)
        {
            return;
        }

        // Innerhalb eines Laufs merkt sich die Engine den bereits bereitgestellten Aufruf selbst.
        processInstance.RequestCallActivity(token, callActivity);
    }
}
