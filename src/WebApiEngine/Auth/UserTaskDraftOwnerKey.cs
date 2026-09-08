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

    /// <summary>Erzeugt denselben stabilen Schlüssel für einen serverseitig aufgelösten Directory-Benutzer.</summary>
    public static string Create(Model.AuthenticatedSubject identity) => Hash($"oidc\0{identity.Issuer}\0{identity.Subject}");

    private static string Hash(string material) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
}
