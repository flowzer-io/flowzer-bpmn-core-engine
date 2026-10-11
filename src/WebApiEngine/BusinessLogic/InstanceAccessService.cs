using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// HTTP-Anwendungsfall für Ressourcenrechte und öffentliche Projektionen. Jeder
/// Leseweg und die Startantwort beziehen ihre Rechte aus demselben Request-Kontext.
/// </summary>
public sealed class InstanceAccessService(
    IStorageSystem storage,
    ICurrentUserContextAccessor currentUserAccessor,
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorization,
    WorkflowOutcomeProjector outcomes)
{
    public async Task<(CurrentUserContext User, bool CanInspect)> GetPermissionsAsync()
    {
        var user = currentUserAccessor.GetCurrentUser();
        user.RequireResolvedUserId("accessing instances");
        if (user.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("A non-empty user identity is required for accessing instances.");
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new UnauthorizedAccessException("A request context is required for accessing instances.");
        return (user, (await authorization.AuthorizeAsync(principal, FlowzerPolicies.Operator)).Succeeded);
    }

    public async Task<List<ProcessInstanceInfoDto>> GetAllAsync()
    {
        var (user, canInspect) = await GetPermissionsAsync();
        var instances = await storage.InstanceStorage.GetAllInstances();
        if (canInspect) return await ProjectAsync(instances, canInspect: true);

        // Einmal laden, nicht eine vollständige Aufgabenliste je Instanz. Die Zuordnung
        // prüft zusätzlich die tatsächlichen aktiven Tokens der geladenen Instanz.
        var taskArray = (await storage.SubscriptionStorage.GetAllUserTasksExtended(user.UserId))
            .Where(task => task.ProcessInstanceId.HasValue)
            .ToArray();
        var workStates = await LoadWorkStates(taskArray);
        var directorySnapshot = await UserTaskAssignment.LoadDirectorySnapshotIfRequiredAsync(
            storage.IdentityDirectoryStorage, taskArray,
            workStates.Values.Any(state => state.DirectoryAssigneeUserId.HasValue));
        var tasks = taskArray.ToLookup(task => task.ProcessInstanceId!.Value);
        var visible = instances.Where(instance =>
            InstanceAccessPolicy.CanReadOverview(
                instance, user, tasks[instance.InstanceId], directorySnapshot, canInspect: false, workStates));
        return await ProjectAsync(visible, canInspect: false);
    }

    public async Task<ProcessInstanceInfoDto?> GetAsync(Guid instanceId)
    {
        var (user, canInspect) = await GetPermissionsAsync();
        var instance = await FindAsync(instanceId);
        if (instance is null) return null;
        var tasks = canInspect
            ? []
            : (await storage.SubscriptionStorage.GetAllUserTasks(instanceId)).ToArray();
        var workStates = canInspect ? null : await LoadWorkStates(tasks);
        var directorySnapshot = canInspect
            ? null
            : await UserTaskAssignment.LoadDirectorySnapshotIfRequiredAsync(
                storage.IdentityDirectoryStorage, tasks,
                workStates!.Values.Any(state => state.DirectoryAssigneeUserId.HasValue));
        if (!InstanceAccessPolicy.CanReadOverview(instance, user, tasks, directorySnapshot, canInspect, workStates))
            return null;
        var dto = await instance.ToDtoAsync(storage.DefinitionStorage, canInspect);
        dto.Outcome = await outcomes.ProjectAsync(instance);
        return dto;
    }

    /// <summary>
    /// Die direkten Kindinstanzen eines Vorgangs, älteste zuerst. <c>null</c> heisst: Der
    /// Vorgang ist für diese Person nicht sichtbar — dieselbe Grenze wie die Instanzansicht.
    /// </summary>
    public async Task<List<CalledInstanceDto>?> GetCalledAsync(Guid instanceId)
    {
        if (await GetAsync(instanceId) is null) return null;
        var parent = await FindAsync(instanceId);
        if (parent is null) return null;

        // Die Instanz liegt als JSON-Dokument; eine eigene Spalte lohnt erst, wenn diese Sicht
        // nicht mehr über den ohnehin vollständig geladenen Bestand laufen kann.
        var children = (await storage.InstanceStorage.GetAllInstances())
            .Where(candidate => candidate.ParentInstanceId == instanceId)
            .OrderBy(candidate => candidate.Tokens.Count == 0
                ? DateTime.MaxValue
                : candidate.Tokens.Min(token => token.StartTime))
            .ThenBy(candidate => candidate.InstanceId);

        return await children.ToCalledInstanceDtosAsync(storage.DefinitionStorage, parent);
    }

    public async Task<bool> CanInspectAsync(Guid instanceId)
    {
        var (_, canInspect) = await GetPermissionsAsync();
        return canInspect && await FindAsync(instanceId) is not null;
    }

    private async Task<ProcessInstanceInfo?> FindAsync(Guid instanceId)
    {
        try
        {
            return await storage.InstanceStorage.GetProcessInstance(instanceId);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    // Erst objektberechtigte Instanzen projizieren. Outcome erfordert keine Diagnoseberechtigung
    // und gibt keine Variablen/Tokens weiter; die bestehende DTO-Privacy bleibt unverändert.
    private async Task<List<ProcessInstanceInfoDto>> ProjectAsync(IEnumerable<ProcessInstanceInfo> instances, bool canInspect)
    {
        var visible = instances.ToArray();
        var dtos = await visible.ToDtosAsync(storage.DefinitionStorage, canInspect);
        var projectedOutcomes = await outcomes.ProjectBatchAsync(visible);
        for (var index = 0; index < visible.Length; index++)
            dtos[index].Outcome = projectedOutcomes[index];
        return dtos;
    }

    private async Task<IReadOnlyDictionary<Guid, UserTaskWorkState>> LoadWorkStates(
        IEnumerable<UserTaskSubscription> tasks)
    {
        try { return await storage.UserTaskLifecycleStorage.GetMany(tasks.Select(task => task.Id)); }
        catch (NotSupportedException) { return new Dictionary<Guid, UserTaskWorkState>(); }
    }
}
