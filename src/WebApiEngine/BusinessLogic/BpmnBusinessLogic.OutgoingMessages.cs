using BPMN.Process;
using Model;

namespace WebApiEngine.BusinessLogic;

public partial class BpmnBusinessLogic
{
    /// <summary>
    /// Wie viele Nachrichten eine einzelne Mutation zustellen darf. Zwei Prozesse, die sich
    /// gegenseitig beantworten, drehten sonst endlos in derselben Transaktion. Die Grenze
    /// bricht den Vorgang ab, statt den Aufrufer hängen zu lassen.
    /// </summary>
    private const int MaximumMessageDeliveriesPerMutation = 100;

    /// <summary>
    /// Stellt die Nachrichten zu, die eine Instanz beim Speichern ausgesendet hat.
    ///
    /// Zugestellt wird über denselben Weg wie bei <c>POST /message</c>: an ein wartendes
    /// Catch-Event, einen Receive-Task oder ein Message-Boundary — derselben oder einer
    /// anderen Instanz —, sonst an ein Message-Start-Event, das eine neue Instanz beginnt.
    /// Findet sich niemand, verfällt die Nachricht; das sendende Element bleibt trotzdem
    /// abgeschlossen. Alles läuft in der Transaktion und unter der Sperre des Aufrufers:
    /// Scheitert eine Zustellung, scheitert die ganze Mutation, und ein transaktionaler
    /// Adapter verwirft auch den Fortschritt des Senders.
    /// </summary>
    private async Task DeliverOutgoingMessages(ITransactionalStorage storageSystem, InstanceContext source)
    {
        if (source.Instance.OutgoingMessages.Count == 0)
        {
            return;
        }

        var deliveries = 0;
        var pending = new Queue<InstanceContext>([source]);
        while (pending.TryDequeue(out var current))
        {
            foreach (var message in current.Instance.TakeOutgoingMessages())
            {
                if (++deliveries > MaximumMessageDeliveriesPerMutation)
                    throw new InvalidOperationException(
                        $"More than {MaximumMessageDeliveriesPerMutation} messages were delivered in one mutation. "
                        + "The processes involved most likely answer each other without end.");

                var target = await DeliverMessage(storageSystem, current, message);
                if (target is null)
                {
                    continue;
                }

                await PersistInstance(storageSystem, target.Instance, target.RelatedDefinitionId,
                    target.DefinitionId, target.ProcessId);
                pending.Enqueue(target);
            }
        }
    }

    /// <summary>
    /// Sucht den Empfänger einer ausgehenden Nachricht und führt ihn weiter.
    /// </summary>
    /// <returns>Die weitergelaufene Instanz oder <c>null</c>, wenn niemand gewartet hat.</returns>
    private async Task<InstanceContext?> DeliverMessage(
        ITransactionalStorage storageSystem,
        InstanceContext source,
        Message message)
    {
        // Der Absender kennt die Instanz des Empfängers nicht — er kennt nur Name und
        // Korrelationsschlüssel. Anders als beim Aufruf von aussen, der eine Instanz benennen
        // darf, wird hier deshalb über alle Anmeldungen korreliert.
        var subscriptions = (await storageSystem.SubscriptionStorage.GetAllMessageSubscriptions())
            .Where(subscription => subscription.Message.Name == message.Name)
            .ToArray();

        // Eine wartende Instanz hat Vorrang vor einem Start: Sonst begänne jede Antwort einen
        // zweiten Vorgang, statt den laufenden fortzusetzen.
        var waiting = subscriptions.FirstOrDefault(subscription => subscription.ProcessInstanceId is not null
            && subscription.Message.FlowzerCorrelationKey == message.CorrelationKey);
        if (waiting is not null)
        {
            return await ContinueWaitingInstance(storageSystem, source, message, waiting);
        }

        // Ein Message-Start-Event trägt keinen Korrelationsschlüssel — es beginnt immer einen
        // neuen Vorgang. Der Schlüssel der Nachricht wird hier deshalb nicht verglichen; er
        // gehört zur Frage, welche laufende Instanz gemeint ist, und die ist bereits beantwortet.
        var start = subscriptions.FirstOrDefault(subscription => subscription.ProcessInstanceId is null);
        if (start is null)
        {
            // Kein Empfänger: Die Nachricht verfällt. BPMN puffert nicht, und Flowzer hebt
            // sie bewusst nicht auf — ein spät gestarteter Empfänger bekäme sonst eine
            // Nachricht aus einem längst abgeschlossenen Vorgang.
            return null;
        }

        var xmlData = await storageSystem.DefinitionStorage.GetBinary(start.DefinitionId);
        var model = ModelParser.ParseModel(xmlData);
        var process = model.GetProcesses().FirstOrDefault(candidate => candidate.Id == start.ProcessId)
            ?? throw new FileNotFoundException(
                $"No process with the id \"{start.ProcessId}\" was found in the definition with the id \"{start.DefinitionId}\".");

        var started = StartProcessByMessage(start.DefinitionId, start.RelatedDefinitionId, process, message);

        return new InstanceContext(started, start.RelatedDefinitionId, start.DefinitionId, start.ProcessId);
    }

    private async Task<InstanceContext> ContinueWaitingInstance(
        ITransactionalStorage storageSystem,
        InstanceContext source,
        Message message,
        MessageSubscription subscription)
    {
        var instanceId = subscription.ProcessInstanceId!.Value;

        // Korreliert die Nachricht auf die sendende Instanz selbst — ein paralleler Zweig, der
        // auf sie wartet —, muss der Stand im Speicher weiterlaufen. Die Instanz noch einmal
        // aus der Ablage zu laden, verwürfe den gerade erreichten Fortschritt des Senders.
        if (instanceId == source.Instance.InstanceId)
        {
            source.Instance.HandleMessage(message);
            return source;
        }

        await storageSystem.InstanceStorage.LockForMutation(instanceId);
        var processInstance = await storageSystem.InstanceStorage.GetProcessInstance(instanceId);
        var instance = new InstanceEngine(processInstance.Tokens) { InstanceId = instanceId };
        instance.HandleMessage(message);

        return new InstanceContext(instance, processInstance.metaDefinitionId, processInstance.DefinitionId,
            processInstance.ProcessId);
    }

    /// <summary>Eine Instanz samt der Kennungen, unter denen sie gespeichert wird.</summary>
    private sealed record InstanceContext(
        InstanceEngine Instance,
        string RelatedDefinitionId,
        Guid DefinitionId,
        string ProcessId);
}
