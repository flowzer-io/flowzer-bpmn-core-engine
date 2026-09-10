using Model;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Verarbeitet fällige Human-Task-Meilensteine unter derselben Mutationsgrenze wie
/// Abschluss und Zuweisung. PostgreSQL ergänzt den Prozess-Lock durch Task-Row-Locks.
/// </summary>
public sealed class UserTaskDeadlineService(BpmnBusinessLogic businessLogic)
{
    /// <summary>Bindet offene Altaufgaben ohne Terminzeile einmalig an ihren Tokenstart.</summary>
    public async Task<int> BackfillMissingAsync(
        UserTaskDeadlinePolicy policy, CancellationToken cancellationToken)
    {
        try
        {
            return await businessLogic.ExecuteUserTaskMutationAsync(async storage =>
            {
                // Fähigkeiten vor dem potenziell teuren Bestandsabruf prüfen. Dadurch
                // berührt ein kompatibler Legacy-Adapter seine Subscriptions nicht.
                _ = await storage.UserTaskDeadlineStorage.Get(Guid.Empty);
                _ = await storage.UserTaskLifecycleStorage.Get(Guid.Empty);
                var tasks = (await storage.SubscriptionStorage.GetAllUserTasksExtended(Guid.Empty))
                    .OrderBy(task => task.Id).ToArray();

                var created = 0;
                foreach (var task in tasks)
                {
                    if (await storage.UserTaskDeadlineStorage.Get(task.Id) is not null) continue;
                    if (!await storage.UserTaskLifecycleStorage.LockTask(task.Id)) continue;
                    var current = await storage.SubscriptionStorage.GetUserTaskExtended(task.Id);
                    if (current?.Token.CurrentFlowNode is not BPMN.HumanInteraction.UserTask model) continue;
                    if (await storage.UserTaskDeadlineStorage.Get(current.Id) is not null) continue;
                    try
                    {
                        await storage.UserTaskDeadlineStorage.AddIfAbsent(UserTaskScheduleResolver.Resolve(
                            current.Id, ToUtc(current.Token.StartTime), model.FlowzerDueDate,
                            model.FlowzerFollowUpDate, policy));
                        created++;
                    }
                    catch (FileNotFoundException)
                    {
                        // Die Aufgabe wurde unmittelbar vor ihrer Sperre abgeschlossen.
                    }
                }
                return created;
            }, cancellationToken);
        }
        catch (NotSupportedException)
        {
            // Externe Legacy-Adapter dürfen ohne Deadline-Vertrag weiterlaufen.
            return 0;
        }
    }

    public async Task<int> ProcessDueAsync(
        DateTimeOffset nowUtc, int batchSize, CancellationToken cancellationToken)
    {
        try
        {
            return await businessLogic.ExecuteUserTaskMutationAsync(async storage =>
            {
                var candidates = await storage.UserTaskDeadlineStorage.GetDueCandidates(nowUtc, batchSize);

                var created = 0;
                foreach (var candidate in candidates)
                {
                    if (!await storage.UserTaskLifecycleStorage.LockTask(candidate.UserTaskId)) continue;
                    var current = await storage.UserTaskDeadlineStorage.Get(candidate.UserTaskId);
                    if (current is null || current.NextCheckAtUtc is null || current.NextCheckAtUtc > nowUtc) continue;
                    var advanced = UserTaskDeadlineProcessor.Advance(current, nowUtc);
                    if (ReferenceEquals(advanced.Deadline, current)) continue;

                    foreach (var notification in advanced.Notifications)
                    {
                        var result = await storage.UserTaskNotificationStorage.TryAdd(notification);
                        if (result.Added) created++;
                    }
                    if (!await storage.UserTaskDeadlineStorage.TryAdvance(advanced.Deadline, current.Revision))
                        throw new InvalidOperationException("The user-task deadline changed during a locked scheduler run.");
                }
                return created;
            }, cancellationToken);
        }
        catch (NotSupportedException)
        {
            // Der Deadline-Scheduler ist für kompatible externe Legacy-Adapter opt-in.
            return 0;
        }
    }

    private static DateTimeOffset ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value),
        DateTimeKind.Local => new DateTimeOffset(value).ToUniversalTime(),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
    };
}
