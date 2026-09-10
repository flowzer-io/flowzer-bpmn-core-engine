using Microsoft.AspNetCore.Authorization;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.Forms;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Autorisierter Anwendungsfall für Übernahme, Freigabe, Zuweisung und Delegation. Akteur,
/// Kandidaten und Zielidentität stammen ausschließlich aus verifizierten Serverkontexten.
/// </summary>
public sealed class UserTaskLifecycleService(
    BpmnBusinessLogic businessLogic,
    ICurrentUserContextAccessor currentUserAccessor,
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorization,
    TimeProvider timeProvider)
{
    public Task<UserTaskWorkStateDto?> ClaimAsync(Guid taskId, long expectedRevision) =>
        MutateAsync(taskId, expectedRevision, "claim", reason: "Aufgabe übernommen", target: null);

    public Task<UserTaskWorkStateDto?> ReleaseAsync(Guid taskId, long expectedRevision, string reason) =>
        MutateAsync(taskId, expectedRevision, "release", reason, target: null);

    public Task<UserTaskWorkStateDto?> AssignAsync(
        Guid taskId, long expectedRevision, SubjectRefDto target, string reason) =>
        MutateAsync(taskId, expectedRevision, "assign", reason, target);

    public Task<UserTaskWorkStateDto?> DelegateAsync(
        Guid taskId, long expectedRevision, SubjectRefDto target, string reason) =>
        MutateAsync(taskId, expectedRevision, "delegate", reason, target);

    private async Task<UserTaskWorkStateDto?> MutateAsync(
        Guid taskId,
        long expectedRevision,
        string action,
        string? reason,
        SubjectRefDto? target)
    {
        ValidateExpectedRevision(expectedRevision);
        var currentUser = currentUserAccessor.GetCurrentUser();
        currentUser.RequireResolvedUserId("changing a user-task assignment");
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new UnauthorizedAccessException("A request context is required for changing user tasks.");
        var canOperate = (await authorization.AuthorizeAsync(principal, FlowzerPolicies.Operator)).Succeeded;
        var cancellationToken = httpContextAccessor.HttpContext?.RequestAborted ?? default;

        return await businessLogic.ExecuteUserTaskMutationAsync(async storage =>
        {
            if (!await storage.UserTaskLifecycleStorage.LockTask(taskId)) return null;
            var task = await storage.SubscriptionStorage.GetUserTaskExtended(taskId);
            if (task is null || task.Token is not { State: FlowNodeState.Active, CurrentFlowNode: BPMN.HumanInteraction.UserTask })
                return null;
            if (!await IsCurrentTask(storage, task)) return null;

            DirectorySnapshot? snapshot = null;
            try { snapshot = await storage.IdentityDirectoryStorage.GetActiveSnapshot(); }
            catch (NotSupportedException) { /* Textaufgaben bleiben ohne Directory nutzbar. */ }
            var access = await UserTaskWorkAuthorization.EvaluateAsync(storage, task, currentUser, canOperate, snapshot);
            // Ein entzogener tatsächlicher Besitzer erhält auch auf dem Claim-Nebenweg
            // keine Existenzbestätigung; andere konkurrierende Kandidaten behalten ihren 409.
            var isRevokedAssignee = access.State?.AssigneeOwnerKey is { } owner
                                   && string.Equals(owner, UserTaskDraftOwnerKey.Create(currentUser), StringComparison.Ordinal)
                                   && !access.IsAssignedToCurrentUser;
            if (action == "claim" && access.State?.AssigneeOwnerKey is not null
                && (canOperate || (!isRevokedAssignee
                    && UserTaskAssignment.IsVisibleTo(task, currentUser, snapshot, seeAll: false))))
            {
                // Ein konkurrierender Kandidat darf die Existenz weiterhin kennen, erhält aber
                // den revisionsgebundenen Konflikt statt eines irreführenden 404.
                throw new UserTaskLifecycleConflictException(expectedRevision, access.State.Revision);
            }
            if (!CanPerform(action, access, canOperate)) return null;

            var normalizedReason = action == "claim" ? "Aufgabe übernommen" : ValidateReason(reason);
            if (action is "assign" or "delegate") target = ValidateTarget(target!);

            var targetUser = target is not null
                ? ResolveTarget(snapshot, target)
                : action == "claim" ? ResolveCurrentUser(snapshot, currentUser) : null;
            if (action == "delegate" && !canOperate
                && !TargetIsCandidate(task, targetUser!, snapshot))
                throw Validation("assignee", "assignee.not_candidate");
            var outsideCandidatePool = action == "assign"
                                       && !TargetIsCandidate(task, targetUser!, snapshot);

            var now = timeProvider.GetUtcNow();
            var next = action == "release"
                ? new UserTaskWorkState
                {
                    UserTaskId = taskId,
                    Revision = checked(expectedRevision + 1),
                    UpdatedAtUtc = now
                }
                : CreateAssignedState(taskId, expectedRevision, now,
                    targetUser, target is null ? currentUser : null, outsideCandidatePool);
            var auditEvent = new UserTaskAssignmentEvent
            {
                Id = Guid.NewGuid(),
                UserTaskId = taskId,
                ProcessInstanceId = task.ProcessInstanceId,
                DefinitionId = task.DefinitionId,
                ProcessId = task.ProcessId,
                FlowNodeId = task.Token.CurrentFlowNode.Id,
                Revision = next.Revision,
                Action = action,
                ActorOwnerKey = UserTaskDraftOwnerKey.Create(currentUser),
                ActorUserId = currentUser.UserId,
                ActorDisplayName = DisplayName(currentUser),
                PreviousDirectoryAssigneeUserId = access.State?.DirectoryAssigneeUserId,
                NextDirectoryAssigneeUserId = next.DirectoryAssigneeUserId,
                PreviousAssigneeUserId = access.State?.AssigneeUserId,
                NextAssigneeUserId = next.AssigneeUserId,
                PreviousAssigneeDisplayName = access.State?.AssigneeDisplayName,
                NextAssigneeDisplayName = next.AssigneeDisplayName,
                Reason = normalizedReason,
                CorrelationId = httpContextAccessor.HttpContext?.TraceIdentifier
                                ?? System.Diagnostics.Activity.Current?.TraceId.ToString()
                                ?? Guid.NewGuid().ToString("N"),
                OccurredAtUtc = now
            };
            var result = await storage.UserTaskLifecycleStorage.TryWrite(next, expectedRevision, auditEvent);
            if (result.Status == UserTaskLifecycleWriteStatus.TaskNotFound) return null;
            if (result.Status == UserTaskLifecycleWriteStatus.RevisionConflict)
                throw new UserTaskLifecycleConflictException(expectedRevision, result.CurrentRevision);

            var updatedAccess = await UserTaskWorkAuthorization.EvaluateAsync(
                storage, task, currentUser, canOperate, snapshot);
            return UserTaskWorkAuthorization.ToDto(updatedAccess with { State = result.State });
        }, cancellationToken);
    }

    private static bool CanPerform(string action, UserTaskAccess access, bool canOperate) => action switch
    {
        "claim" => access.CanClaim,
        "release" => access.CanRelease,
        "assign" => canOperate && access.CanAssign,
        "delegate" => access.CanDelegate,
        _ => false
    };

    private static async Task<bool> IsCurrentTask(IStorageSystem storage, ExtendedUserTaskSubscription task)
    {
        if (task.ProcessInstanceId is not { } instanceId) return false;
        ProcessInstanceInfo instance;
        try { instance = await storage.InstanceStorage.GetProcessInstance(instanceId); }
        catch (FileNotFoundException) { return false; }
        var tokens = instance.Tokens.Where(token => token.Id == task.Token.Id).Take(2).ToArray();
        return tokens.Length == 1
               && tokens[0] is { State: FlowNodeState.Active, CurrentFlowNode: BPMN.HumanInteraction.UserTask activeTask }
               && activeTask.Id == task.Token.CurrentFlowNode?.Id
               && instance.DefinitionId == task.DefinitionId
               && instance.metaDefinitionId == task.MetaDefinitionId
               && instance.ProcessId == task.ProcessId;
    }

    private static UserTaskWorkState CreateAssignedState(
        Guid taskId,
        long expectedRevision,
        DateTimeOffset now,
        DirectoryUser? target,
        CurrentUserContext? actor,
        bool candidateOverride)
    {
        if (target is not null)
        {
            return new UserTaskWorkState
            {
                UserTaskId = taskId,
                Revision = checked(expectedRevision + 1),
                AssigneeOwnerKey = UserTaskDraftOwnerKey.Create(new AuthenticatedSubject(target.Issuer, target.Subject)),
                DirectoryAssigneeUserId = target.Id,
                AssigneeDisplayName = target.DisplayName,
                WasAssignedOutsideCandidatePool = candidateOverride,
                UpdatedAtUtc = now
            };
        }

        if (actor is null) throw new InvalidOperationException("An assignment requires an actor or directory target.");
        return new UserTaskWorkState
        {
            UserTaskId = taskId,
            Revision = checked(expectedRevision + 1),
            AssigneeOwnerKey = UserTaskDraftOwnerKey.Create(actor),
            AssigneeUserId = actor.UserId,
            AssigneeDisplayName = DisplayName(actor),
            WasAssignedOutsideCandidatePool = false,
            UpdatedAtUtc = now
        };
    }

    private static DirectoryUser ResolveTarget(DirectorySnapshot? snapshot, SubjectRefDto target)
    {
        if (snapshot is null) throw Validation("assignee", "directory.unavailable");
        var users = snapshot.Users.Where(user => user.Id == target.Id && user.IsActive).Take(2).ToArray();
        return users.Length == 1 ? users[0] : throw Validation("assignee", "assignee.invalid");
    }

    private static DirectoryUser? ResolveCurrentUser(
        DirectorySnapshot? snapshot,
        CurrentUserContext currentUser)
    {
        if (snapshot is null || currentUser.Identity is null) return null;
        var users = snapshot.Users.Where(user => user.IsActive
                                                 && user.Issuer == currentUser.Identity.Issuer
                                                 && user.Subject == currentUser.Identity.Subject)
            .Take(2).ToArray();
        return users.Length == 1 ? users[0] : null;
    }

    private static bool TargetIsCandidate(
        UserTaskSubscription task,
        DirectoryUser target,
        DirectorySnapshot? snapshot)
    {
        var synthetic = new CurrentUserContext(Guid.Empty, "directory", IsFallback: false)
        {
            Identity = new AuthenticatedSubject(target.Issuer, target.Subject)
        };
        return UserTaskAssignment.IsVisibleTo(task, synthetic, snapshot, seeAll: false);
    }

    private static SubjectRefDto ValidateTarget(SubjectRefDto target)
    {
        if (target is null || !string.Equals(target.Kind, "user", StringComparison.OrdinalIgnoreCase)
                           || target.Id == Guid.Empty)
            throw Validation("assignee", "assignee.user_required");
        return target;
    }

    private static string ValidateReason(string? reason)
    {
        var normalized = reason?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 500)
            throw Validation("reason", "reason.required_or_too_long");
        return normalized;
    }

    private static string? DisplayName(CurrentUserContext user) =>
        user.Names.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)
                                           && !Guid.TryParse(value, out _));

    private static FormSubmissionException Validation(string field, string code) =>
        new(new Dictionary<string, string[]> { [field] = [code] });

    private static void ValidateExpectedRevision(long expectedRevision)
    {
        try
        {
            // Der Standard-Guard verhindert einen usergesteuerten Bypass vor der
            // Autorisierung; die Umwandlung erhaelt den bisherigen 422-Vertrag.
            ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision, nameof(expectedRevision));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Validation("expectedRevision", "revision.invalid");
        }
    }
}

public sealed class UserTaskLifecycleConflictException(long expectedRevision, long currentRevision)
    : Exception("The user-task assignment has changed since it was loaded.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long CurrentRevision { get; } = currentRevision;
}
