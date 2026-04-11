namespace core_engine;

/// <summary>
/// Minimale öffentliche Kernschnittstelle für das Laden einer BPMN-Definition
/// und das Anstoßen/Weiterführen von Interaktionen ohne Web- oder Storage-Abhängigkeiten.
/// </summary>
public interface ICore
{
    event EventHandler<CoreInstance>? InteractionFinished;

    System.Threading.Tasks.Task LoadBpmnFile(Stream xmlDataStream, bool verify, CancellationToken cancellationToken = default);

    System.Threading.Tasks.Task<CoreSubscription[]> GetInitialSubscriptions(CancellationToken cancellationToken = default);

    System.Threading.Tasks.Task<CoreEventResult> HandleEvent(CoreEventData eventData, CancellationToken cancellationToken = default);
}
