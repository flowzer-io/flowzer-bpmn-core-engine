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
    IAuthorizationService authorization)
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
        if (canInspect) return await instances.ToDtosAsync(storage.DefinitionStorage, canInspect: true);

        // Einmal laden, nicht eine vollständige Aufgabenliste je Instanz. Die Zuordnung
        // prüft zusätzlich die tatsächlichen aktiven Tokens der geladenen Instanz.
        var tasks = (await storage.SubscriptionStorage.GetAllUserTasksExtended(user.UserId))
            .Where(task => task.ProcessInstanceId.HasValue)
            .ToLookup(task => task.ProcessInstanceId!.Value);
        var visible = instances.Where(instance =>
            InstanceAccessPolicy.CanReadOverview(instance, user, tasks[instance.InstanceId], canInspect: false));
        return await visible.ToDtosAsync(storage.DefinitionStorage, canInspect: false);
    }

    public async Task<ProcessInstanceInfoDto?> GetAsync(Guid instanceId)
    {
        var (user, canInspect) = await GetPermissionsAsync();
        var instance = await FindAsync(instanceId);
        if (instance is null) return null;
        var tasks = canInspect ? [] : await storage.SubscriptionStorage.GetAllUserTasks(instanceId);
        return InstanceAccessPolicy.CanReadOverview(instance, user, tasks, canInspect: canInspect)
            ? await instance.ToDtoAsync(storage.DefinitionStorage, canInspect: canInspect)
            : null;
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
}
