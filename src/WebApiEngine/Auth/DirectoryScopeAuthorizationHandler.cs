using Microsoft.AspNetCore.Authorization;
using StorageSystem;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Auth;

/// <summary>Zusätzlicher, nicht durch Rollen ersetzbarer Scope aller Anwendungs-Policies.</summary>
internal sealed record DirectoryScopeRequirement(string Issuer, string RootGroupId) : IAuthorizationRequirement;

/// <summary>Prüft verifizierte stabile Identität gegen den aktuellen lokalen Directory-Stand.</summary>
internal sealed class DirectoryScopeAuthorizationHandler(IIdentityDirectoryStorage storage)
    : AuthorizationHandler<DirectoryScopeRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, DirectoryScopeRequirement requirement)
    {
        var identities = context.User.Identities.Where(identity => identity.IsAuthenticated).Take(2).ToArray();
        if (identities.Length != 1) return;
        var issuers = identities[0].FindAll("iss").Take(2).ToArray();
        var subjects = identities[0].FindAll("sub").Take(2).ToArray();
        if (issuers.Length != 1 || subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0].Value)) return;
        try
        {
            var snapshot = await storage.GetActiveSnapshot();
            if (DirectoryGroupScope.AllowsIdentity(snapshot, requirement.Issuer, requirement.RootGroupId,
                    issuers[0].Value, subjects[0].Value)) context.Succeed(requirement);
        }
        catch (Exception)
        {
            // Kein Cache-/Token-Gruppen-Fallback und kein Exception-Logging mit
            // möglichen Infrastruktur-/Providergeheimnissen bei Speicherfehlern.
        }
    }
}
