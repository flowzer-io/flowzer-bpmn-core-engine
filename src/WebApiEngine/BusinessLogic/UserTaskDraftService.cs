using System.Dynamic;
using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;
using WebApiEngine.Forms;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Autorisierter Anwendungsfall fuer private Aufgabenentwuerfe. Aufgabe, Instanztoken,
/// Formularbindung, Eigentuemer und Revision werden innerhalb derselben Storage-Sicht geprueft.
/// </summary>
public sealed class UserTaskDraftService(
    ITransactionalStorageProvider storageProvider,
    ICurrentUserContextAccessor currentUserAccessor,
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider)
{
    public async Task<UserTaskDraftDto?> GetAsync(Guid userTaskId)
    {
        var request = await GetRequestContext();
        using var storage = storageProvider.GetTransactionalStorage();
        var task = await FindAuthorizedTask(storage, userTaskId, request);
        if (task is null) return null;

        var ownerKey = UserTaskDraftOwnerKey.Create(request.User);
        var draft = await storage.UserTaskDraftStorage.Get(userTaskId, ownerKey);
        if (draft is not null) EnsureBinding(draft, task.Value);
        return ToDto(userTaskId, draft);
    }

    public async Task<UserTaskDraftDto?> SaveAsync(
        Guid userTaskId,
        SaveUserTaskDraftRequestDto requestDto)
    {
        ArgumentNullException.ThrowIfNull(requestDto);
        if (requestDto.ExpectedRevision < 0)
            throw new ArgumentException("ExpectedRevision must not be negative.", nameof(requestDto));

        var request = await GetRequestContext();
        using var storage = storageProvider.GetTransactionalStorage();
        var task = await FindAuthorizedTask(storage, userTaskId, request);
        if (task is null) return null;

        var formKey = (task.Value.Token.CurrentFlowNode as BPMN.HumanInteraction.UserTask)?.Implementation;
        var resolved = await new FormKeyResolver(storage).ResolveAsync(formKey, task.Value.Subscription.DefinitionId);
        if (resolved.Form?.FormData is not { } schema)
            throw new InvalidOperationException(resolved.ErrorMessage ?? "The user task form is unavailable.");
        var contract = FormContractCompiler.Compile(schema);
        var context = TaskFormContext.Read(task.Value.Instance.Tokens, task.Value.Token);
        var dataJson = FormDraftProjector.Project(contract, requestDto.Data, context);
        var ownerKey = UserTaskDraftOwnerKey.Create(request.User);
        var draft = new UserTaskDraft
        {
            UserTaskId = userTaskId,
            OwnerKey = ownerKey,
            OwnerUserId = request.User.UserId,
            TokenId = task.Value.Token.Id,
            ProcessInstanceId = task.Value.Instance.InstanceId,
            DefinitionId = task.Value.Subscription.DefinitionId,
            Revision = checked(requestDto.ExpectedRevision + 1),
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            DataJson = dataJson
        };
        var written = await storage.UserTaskDraftStorage.TrySave(draft, requestDto.ExpectedRevision);
        if (written.Status == UserTaskDraftWriteStatus.TaskNotFound) return null;
        if (written.Status == UserTaskDraftWriteStatus.RevisionConflict)
            throw new UserTaskDraftConflictException(requestDto.ExpectedRevision, written.CurrentRevision);
        storage.CommitChanges();
        return ToDto(userTaskId, written.Draft!);
    }

    public async Task<bool> DeleteAsync(Guid userTaskId, long expectedRevision)
    {
        if (expectedRevision < 0)
            throw new ArgumentException("ExpectedRevision must not be negative.", nameof(expectedRevision));
        var request = await GetRequestContext();
        using var storage = storageProvider.GetTransactionalStorage();
        var task = await FindAuthorizedTask(storage, userTaskId, request);
        if (task is null) return false;

        var result = await storage.UserTaskDraftStorage.TryDelete(
            userTaskId, UserTaskDraftOwnerKey.Create(request.User), expectedRevision);
        if (result.Status == UserTaskDraftDeleteStatus.TaskNotFound) return false;
        if (result.Status == UserTaskDraftDeleteStatus.RevisionConflict)
            throw new UserTaskDraftConflictException(expectedRevision, result.CurrentRevision);
        storage.CommitChanges();
        return true;
    }

    private async Task<(CurrentUserContext User, bool CanOperate)> GetRequestContext()
    {
        var user = currentUserAccessor.GetCurrentUser();
        user.RequireResolvedUserId("accessing user-task drafts");
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new UnauthorizedAccessException("A request context is required for accessing user-task drafts.");
        var canOperate = (await authorizationService.AuthorizeAsync(principal, FlowzerPolicies.Operator)).Succeeded;
        return (user, canOperate);
    }

    private static async Task<AuthorizedTask?> FindAuthorizedTask(
        IStorageSystem storage,
        Guid userTaskId,
        (CurrentUserContext User, bool CanOperate) request)
    {
        var subscription = await storage.SubscriptionStorage.GetUserTaskExtended(userTaskId);
        if (subscription is null || subscription.ProcessInstanceId is not { } instanceId
            || subscription.Token is not { State: FlowNodeState.Active }
            || subscription.Token.CurrentFlowNode is not BPMN.HumanInteraction.UserTask)
            return null;

        UserTaskAssignment.EnsureAssignmentFromModel(subscription);
        var directorySnapshot = request.CanOperate
            ? null
            : await UserTaskAssignment.LoadDirectorySnapshotIfRequiredAsync(
                storage.IdentityDirectoryStorage, [subscription]);
        if (!UserTaskAssignment.IsVisibleTo(
                subscription, request.User, directorySnapshot, request.CanOperate)) return null;

        ProcessInstanceInfo instance;
        try { instance = await storage.InstanceStorage.GetProcessInstance(instanceId); }
        catch (FileNotFoundException) { return null; }
        var tokens = instance.Tokens.Where(candidate => candidate.Id == subscription.Token.Id).ToArray();
        if (tokens.Length != 1 || tokens[0] is not { State: FlowNodeState.Active } token
            || token.CurrentFlowNode is not BPMN.HumanInteraction.UserTask
            || instance.InstanceId != instanceId
            || instance.DefinitionId != subscription.DefinitionId
            || instance.metaDefinitionId != subscription.MetaDefinitionId
            || instance.ProcessId != subscription.ProcessId
            || token.CurrentFlowNode.Id != subscription.Token.CurrentFlowNode.Id
            || token.ProcessInstanceId != subscription.Token.ProcessInstanceId)
            return null;

        return new AuthorizedTask(subscription, instance, token);
    }

    private static void EnsureBinding(UserTaskDraft draft, AuthorizedTask task)
    {
        if (draft.UserTaskId != task.Subscription.Id
            || draft.TokenId != task.Token.Id
            || draft.ProcessInstanceId != task.Instance.InstanceId
            || draft.DefinitionId != task.Subscription.DefinitionId)
            throw new InvalidDataException("Stored user-task draft binding is inconsistent.");
    }

    private static UserTaskDraftDto ToDto(Guid userTaskId, UserTaskDraft? draft) => new()
    {
        UserTaskId = userTaskId,
        Revision = draft?.Revision ?? 0,
        UpdatedAtUtc = draft?.UpdatedAtUtc,
        Data = draft is null ? new ExpandoObject() : ParseData(draft.DataJson)
    };

    private static ExpandoObject ParseData(string json) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<ExpandoObject>(
            json,
            new Newtonsoft.Json.Converters.ExpandoObjectConverter())
        ?? throw new InvalidDataException("Stored user-task draft data is empty.");

    private readonly record struct AuthorizedTask(
        ExtendedUserTaskSubscription Subscription,
        ProcessInstanceInfo Instance,
        Token Token);
}

/// <summary>Enthaelt nur Revisionen, niemals den konkurrierenden Entwurfsinhalt.</summary>
public sealed class UserTaskDraftConflictException(long expectedRevision, long currentRevision)
    : Exception("The user-task draft has changed since it was loaded.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long CurrentRevision { get; } = currentRevision;
}
