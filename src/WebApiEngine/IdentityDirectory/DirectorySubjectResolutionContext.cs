using BPMN.Common;
using BPMN.HumanInteraction;
using BPMN.Infrastructure;
using core_engine.Exceptions;
using Model;
using System.Text.Json;
using WebApiEngine.Auth;
using WebApiEngine.Forms;

namespace WebApiEngine.IdentityDirectory;

/// <summary>
/// Ermittelt die bereits gespeicherten Referenzen, die eine historische Directory-Projektion
/// überhaupt lesen darf. Aktive Suche und fachliche Auswahlprüfung bleiben davon getrennt.
/// </summary>
public sealed class DirectorySubjectResolutionContext(IStorageSystem storage)
{
    /// <summary>Referenzen der zuletzt gespeicherten, auch noch nicht deployten BPMN-Version.</summary>
    public async Task<IReadOnlySet<SubjectRef>> LoadWorkflowSubjects(string definitionId)
    {
        try
        {
            var definition = await storage.DefinitionStorage.GetLatestDefinition(definitionId);
            var model = ModelParser.ParseModel(await storage.DefinitionStorage.GetBinary(definition.Id));
            return model.GetProcesses()
                .SelectMany(AllFlowElements)
                .OfType<UserTask>()
                .Where(task => task.FlowzerAssignmentMode == UserTaskAssignmentMode.Directory)
                .SelectMany(TaskSubjects)
                .ToHashSet();
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                                   or InvalidOperationException
                                                   or ModelValidationException
                                                   or FlowzerModelParseException
                                                   or System.Xml.XmlException)
        {
            // Ein fehlender oder beschädigter Modellstand öffnet keine Directory-Projektion.
            return new HashSet<SubjectRef>();
        }
    }

    /// <summary>Nur direkte typisierte Zuweisungen des gerade bearbeiteten Ordners.</summary>
    public static IReadOnlySet<SubjectRef> FolderSubjects(WorkflowFolder folder) => folder.Assignments
        .Where(assignment => assignment.AssignmentMode == FolderAssignmentMode.Directory
                             && assignment.DirectorySubject is not null)
        .Select(assignment => assignment.DirectorySubject!)
        .ToHashSet();

    /// <summary>
    /// Filterreferenzen wurden bei Veröffentlichung gegen einen aktiven Snapshot geprüft.
    /// Reine Mitgliedschaftsfilter sind dagegen kein Beleg für ein bestimmtes Mitglied.
    /// </summary>
    public static HashSet<SubjectRef> PolicySubjects(DirectorySubjectSelectionPolicy policy)
    {
        HashSet<SubjectRef> result = [];
        if (policy.AllowedUserIds is not null)
            result.UnionWith(policy.AllowedUserIds.Select(id => new SubjectRef(DirectorySubjectKind.User, id)));
        if (policy.AllowedGroupIds is not null)
            result.UnionWith(policy.AllowedGroupIds.Select(id => new SubjectRef(DirectorySubjectKind.Group, id)));
        return result;
    }

    /// <summary>Persistierte Werte des exakt gebundenen, weiterhin aktiven Task-Tokens.</summary>
    public async Task<IReadOnlySet<SubjectRef>> LoadTaskFormSubjects(
        ExtendedUserTaskSubscription task,
        string fieldKey)
    {
        HashSet<SubjectRef> result = [];
        if (task.ProcessInstanceId is not { } instanceId) return result;
        try
        {
            var instance = await storage.InstanceStorage.GetProcessInstance(instanceId);
            var tokens = instance.Tokens.Where(candidate => candidate.Id == task.Token.Id).Take(2).ToArray();
            if (tokens.Length != 1 || tokens[0] is not { State: FlowNodeState.Active } token
                || token.CurrentFlowNode?.Id != task.Token.CurrentFlowNode?.Id
                || instance.DefinitionId != task.DefinitionId
                || instance.metaDefinitionId != task.MetaDefinitionId
                || instance.ProcessId != task.ProcessId)
                return result;

            var values = (IDictionary<string, object?>)TaskFormContext.Read(instance.Tokens, token);
            if (values.TryGetValue(fieldKey, out var value)) AddSubjectValues(result, value);
        }
        catch (FileNotFoundException)
        {
            // Verwaiste Tasks geben weder Prozess- noch Directorydaten frei.
        }
        return result;
    }

    /// <summary>Modellierte, aktuelle und append-only protokollierte Task-Bearbeiter.</summary>
    public async Task<IReadOnlySet<SubjectRef>> LoadTaskAssigneeSubjects(
        ExtendedUserTaskSubscription task,
        UserTaskWorkState? state)
    {
        UserTaskAssignment.EnsureAssignmentFromModel(task);
        HashSet<SubjectRef> result = [];
        if (task.DirectoryAssigneeUserId is { } assignee)
            result.Add(new SubjectRef(DirectorySubjectKind.User, assignee));
        result.UnionWith(task.DirectoryCandidateUserIds.Select(id => new SubjectRef(DirectorySubjectKind.User, id)));
        result.UnionWith(task.DirectoryCandidateGroupIds.Select(id => new SubjectRef(DirectorySubjectKind.Group, id)));
        if (state?.DirectoryAssigneeUserId is { } current)
            result.Add(new SubjectRef(DirectorySubjectKind.User, current));
        try
        {
            foreach (var history in await storage.UserTaskLifecycleStorage.GetEvents(task.Id))
            {
                if (history.PreviousDirectoryAssigneeUserId is { } previous)
                    result.Add(new SubjectRef(DirectorySubjectKind.User, previous));
                if (history.NextDirectoryAssigneeUserId is { } next)
                    result.Add(new SubjectRef(DirectorySubjectKind.User, next));
            }
        }
        catch (NotSupportedException)
        {
            // Legacy-Ablagen besitzen höchstens die modellierte Zuweisung.
        }
        return result;
    }

    private static IEnumerable<SubjectRef> TaskSubjects(UserTask task)
    {
        if (task.FlowzerDirectoryAssigneeUserId is { } assignee)
            yield return new SubjectRef(DirectorySubjectKind.User, assignee);
        foreach (var userId in task.FlowzerDirectoryCandidateUserIds)
            yield return new SubjectRef(DirectorySubjectKind.User, userId);
        foreach (var groupId in task.FlowzerDirectoryCandidateGroupIds)
            yield return new SubjectRef(DirectorySubjectKind.Group, groupId);
    }

    private static IEnumerable<FlowElement> AllFlowElements(IFlowElementContainer container)
    {
        foreach (var element in container.FlowElements)
        {
            yield return element;
            if (element is not IFlowElementContainer nested) continue;
            foreach (var child in AllFlowElements(nested)) yield return child;
        }
    }

    private static void AddSubjectValues(HashSet<SubjectRef> result, object? value)
    {
        if (DirectorySubjectValue.TryParse(value, out var subject))
        {
            result.Add(subject);
            return;
        }
        IEnumerable<object?>? values = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } json =>
                json.EnumerateArray().Select(entry => (object?)entry),
            System.Collections.IEnumerable sequence when value is not string => sequence.Cast<object?>(),
            _ => null
        };
        if (values is null) return;
        foreach (var entry in values.Take(500))
            if (DirectorySubjectValue.TryParse(entry, out subject)) result.Add(subject);
    }
}
