using StorageSystem;

namespace WebApiEngine.Forms;

/// <summary>
/// Prueft die stabilen Filterreferenzen eines Formularvertrags beim Deployment. Laufende
/// Instanzen behalten danach ihren gebundenen Vertrag; neue Werte werden trotzdem gegen den
/// jeweils aktiven Snapshot geprueft.
/// </summary>
public static class FormDirectoryPolicyValidator
{
    public static void Validate(FormContract contract, DirectorySnapshot? snapshot)
    {
        var policies = contract.Fields
            .Where(field => field.SubjectSelection is not null)
            .Select(field => field.SubjectSelection!)
            .ToArray();
        if (policies.Length == 0) return;
        if (snapshot is null)
            throw new InvalidOperationException("Unsupported form contract: directory.snapshot_missing.");

        var activeUsers = snapshot.Users.Where(user => user.IsActive).Select(user => user.Id).ToHashSet();
        var activeGroups = snapshot.Groups.Where(group => group.IsActive).Select(group => group.Id).ToHashSet();
        foreach (var policy in policies)
        {
            if (policy.AllowedUserIds?.Any(id => !activeUsers.Contains(id)) == true)
                throw new InvalidOperationException("Unsupported form contract: directory.user_reference.");
            if (policy.UserMemberOfGroupIds?.Any(id => !activeGroups.Contains(id)) == true
                || policy.AllowedGroupIds?.Any(id => !activeGroups.Contains(id)) == true)
                throw new InvalidOperationException("Unsupported form contract: directory.group_reference.");
        }
    }
}
