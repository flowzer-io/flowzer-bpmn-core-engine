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
        if (state?.AssigneeOwnerKey is not null)
        {
            snapshot ??= await UserTaskAssignment.LoadDirectorySnapshotIfRequiredAsync(
                storage.IdentityDirectoryStorage, [task], state.DirectoryAssigneeUserId.HasValue);
            var isAssignee = IsActualAssignee(state, task, currentUser, snapshot);
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

    /// <summary>
    /// Ein Claim ersetzt die Kandidatenprüfung, aber nicht den aktiven Identitätsnachweis.
    /// Directory-Zuweisungen bleiben auch bei einem Textmodell an die aktuelle stabile ID
    /// gebunden. Reine Text-Claims benötigen weiterhin kein synchronisiertes Verzeichnis.
    /// </summary>
    public static bool IsActualAssignee(
        UserTaskWorkState state,
        UserTaskSubscription task,
        CurrentUserContext currentUser,
        DirectorySnapshot? snapshot)
    {
        if (!string.Equals(state.AssigneeOwnerKey, UserTaskDraftOwnerKey.Create(currentUser), StringComparison.Ordinal))
            return false;
        UserTaskAssignment.EnsureAssignmentFromModel(task);
        if (state.DirectoryAssigneeUserId is { } id)
            return DirectoryIdentityAccess.Resolve(currentUser, snapshot)?.Matches(
                new SubjectRef(DirectorySubjectKind.User, id)) == true;
        return task.AssignmentMode != BPMN.HumanInteraction.UserTaskAssignmentMode.Directory
               || DirectoryIdentityAccess.Resolve(currentUser, snapshot) is not null;
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
