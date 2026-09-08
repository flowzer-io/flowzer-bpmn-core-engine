using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Auth;

/// <summary>
/// Verbindet die unveränderliche Modellzuweisung mit dem tatsächlichen Laufzeitzustand.
/// Nach einer Übernahme gilt ausschließlich der tatsächliche Bearbeiter; eine Kandidatenrolle
/// ist dann kein paralleles Arbeitsrecht mehr.
/// </summary>
public static class UserTaskWorkAuthorization
{
    public static async Task<UserTaskAccess> EvaluateAsync(
        IStorageSystem storage,
        UserTaskSubscription task,
        CurrentUserContext currentUser,
        bool canOperate,
        DirectorySnapshot? snapshot = null)
    {
        UserTaskAssignment.EnsureAssignmentFromModel(task);
        var state = await GetState(storage, task.Id);
        if (state?.AssigneeOwnerKey is { } owner)
        {
            var isAssignee = string.Equals(owner, UserTaskDraftOwnerKey.Create(currentUser), StringComparison.Ordinal);
            return new UserTaskAccess(
                State: state,
                CanSee: canOperate || isAssignee,
                CanWork: canOperate || isAssignee,
                IsAssignedToCurrentUser: isAssignee,
                CanClaim: false,
                CanRelease: canOperate || isAssignee,
                CanAssign: canOperate,
                CanDelegate: canOperate || isAssignee);
        }

        snapshot ??= await UserTaskAssignment.LoadDirectorySnapshotIfRequiredAsync(
            storage.IdentityDirectoryStorage, [task]);
        var isCandidate = UserTaskAssignment.IsVisibleTo(task, currentUser, snapshot, seeAll: false);
        return new UserTaskAccess(
            State: state,
            CanSee: canOperate || isCandidate,
            CanWork: canOperate || isCandidate,
            IsAssignedToCurrentUser: false,
            CanClaim: canOperate || isCandidate,
            CanRelease: false,
            CanAssign: canOperate,
            CanDelegate: false);
    }

    public static UserTaskWorkStateDto ToDto(UserTaskAccess access) => new()
    {
        Revision = access.State?.Revision ?? 0,
        Claimed = access.State?.AssigneeOwnerKey is not null,
        IsAssignedToCurrentUser = access.IsAssignedToCurrentUser,
        ActualAssignee = access.State?.DirectoryAssigneeUserId is { } id
            ? new WebApiEngine.Shared.SubjectRefDto { Kind = "user", Id = id }
            : null,
        ActualAssigneeDisplayName = access.State?.AssigneeDisplayName,
        CanWork = access.CanWork,
        CanClaim = access.CanClaim,
        CanRelease = access.CanRelease,
        CanAssign = access.CanAssign,
        CanDelegate = access.CanDelegate
    };

    private static async Task<UserTaskWorkState?> GetState(IStorageSystem storage, Guid taskId)
    {
        try { return await storage.UserTaskLifecycleStorage.Get(taskId); }
        catch (NotSupportedException) { return null; }
    }
}

public sealed record UserTaskAccess(
    UserTaskWorkState? State,
    bool CanSee,
    bool CanWork,
    bool IsAssignedToCurrentUser,
    bool CanClaim,
    bool CanRelease,
    bool CanAssign,
    bool CanDelegate);
