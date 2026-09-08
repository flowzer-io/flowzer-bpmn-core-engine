using System.Security.Cryptography;
using System.Text;

namespace WebApiEngine.Auth;

/// <summary>
/// Bildet den privaten Entwurfseigentuemer aus dem verifizierten Request-Kontext. Der Hash
/// vermeidet Issuer/Subject in Dateinamen und bleibt dennoch ueber Neustarts stabil.
/// </summary>
public static class UserTaskDraftOwnerKey
{
    public static string Create(CurrentUserContext currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        currentUser.RequireResolvedUserId("accessing user-task drafts");
        var material = currentUser.Identity is { } identity
            ? $"oidc\0{identity.Issuer}\0{identity.Subject}"
            : $"local\0{currentUser.Source}\0{currentUser.UserId:D}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
