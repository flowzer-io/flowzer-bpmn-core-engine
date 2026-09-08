using BPMN.HumanInteraction;
using WebApiEngine.Auth;

namespace WebApiEngine.BusinessLogic;

public partial class BpmnBusinessLogic
{
    /// <summary>
    /// Aktualisiert wartende Aufgaben nach Tokenidentität statt alle IDs neu zu erzeugen.
    /// Unter der Engine-Sperre und innerhalb der bestehenden Storage-Transaktion aufrufen.
    /// </summary>
    private static async Task SaveUserTasks(IStorageSystem storage, ICatchHandler catchHandler,
        string metaDefinitionId, Guid definitionId, string processId, Guid? processInstanceId)
    {
        var active = catchHandler.ActiveUserTasks().ToArray();
        var existing = processInstanceId is { } instanceId
            ? (await storage.SubscriptionStorage.GetAllUserTasks(instanceId)).ToArray()
            : [];

        // Erst den gesamten Bestand prüfen, bevor eine Aufgabe gelöscht/überschrieben wird.
        // Mehrdeutige Altbestände könnten verschiedene Claims/Entwürfe besitzen; kein Raten.
        ValidateTaskIdentities(active, existing, metaDefinitionId, definitionId, processId, processInstanceId);
        var byToken = existing.ToDictionary(task => task.Token.Id);
        var activeIds = active.Select(token => token.Id).ToHashSet();
        foreach (var obsolete in existing.Where(task => !activeIds.Contains(task.Token.Id)))
            await storage.SubscriptionStorage.RemoveUserTaskSubscription(obsolete.Id);

        foreach (var token in active)
        {
            var model = (UserTask)token.CurrentFlowNode!;
            if (byToken.TryGetValue(token.Id, out var task))
            {
                // Tokenkontext aktualisieren, aber die Subscription samt ID und sämtlichen
                // persistierten Zuweisungsmetadaten behalten. Keine Modell-Neuzuweisung.
                task.Token = token;
                task.Name = model.Name;
            }
            else
            {
                task = new UserTaskSubscription
                {
                    Id = Guid.NewGuid(), Token = token, Name = model.Name,
                    AssignmentMode = model.FlowzerAssignmentMode,
                    Assignee = model.FlowzerAssignmentMode == UserTaskAssignmentMode.Text
                        && !string.IsNullOrWhiteSpace(model.FlowzerAssignee)
                            ? model.FlowzerAssignee.Trim()
                            : null,
                    CandidateUsers = model.FlowzerAssignmentMode == UserTaskAssignmentMode.Text
                        ? UserTaskAssignment.SplitList(model.FlowzerCandidateUsers)
                        : [],
                    CandidateGroups = model.FlowzerAssignmentMode == UserTaskAssignmentMode.Text
                        ? UserTaskAssignment.SplitList(model.FlowzerCandidateGroups)
                        : [],
                    DirectoryAssigneeUserId = model.FlowzerDirectoryAssigneeUserId,
                    DirectoryCandidateUserIds = [.. model.FlowzerDirectoryCandidateUserIds],
                    DirectoryCandidateGroupIds = [.. model.FlowzerDirectoryCandidateGroupIds],
                    ProcessInstanceId = processInstanceId, DefinitionId = definitionId,
                    MetaDefinitionId = metaDefinitionId, ProcessId = processId
                };
            }
            // Beide produktiven Implementierungen besitzen hier Upsert-Semantik nach ID.
            await storage.SubscriptionStorage.AddUserTaskSubscription(task);
        }
    }

    private static void ValidateTaskIdentities(Token[] active, UserTaskSubscription[] existing,
        string metaDefinitionId, Guid definitionId, string processId, Guid? instanceId)
    {
        if (active.Any(token => token.CurrentFlowNode is not UserTask)
            || active.Select(token => token.Id).Distinct().Count() != active.Length
            || existing.Any(task => task.Token is null || task.Id == Guid.Empty
                || task.Token.CurrentFlowNode is not UserTask
                || task.ProcessInstanceId != instanceId || task.DefinitionId != definitionId
                || task.MetaDefinitionId != metaDefinitionId || task.ProcessId != processId)
            || existing.Select(task => task.Token.Id).Distinct().Count() != existing.Length
            || existing.Select(task => task.Id).Distinct().Count() != existing.Length)
            throw TaskIdentityConflict();

        var byToken = existing.ToDictionary(task => task.Token.Id);
        foreach (var token in active)
            if (byToken.TryGetValue(token.Id, out var previous)
                && (previous.Token.CurrentFlowNode!.Id != token.CurrentFlowNode!.Id
                    || previous.Token.ProcessInstanceId != token.ProcessInstanceId))
                throw TaskIdentityConflict();
    }

    private static InvalidOperationException TaskIdentityConflict() =>
        new("User task identity is inconsistent. Stored assignments require explicit reconciliation.");
}
