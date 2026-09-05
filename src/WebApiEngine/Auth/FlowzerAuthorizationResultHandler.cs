using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace WebApiEngine.Auth;

/// <summary>
/// Ergaenzt jede Ablehnung um die Angabe, woran sie lag: am fehlenden Zugang zur Anwendung
/// oder nur an der fehlenden Berechtigung fuer diese eine Handlung. Die Oberflaeche kann
/// dadurch zwischen "Sie sind fuer Flowzer nicht freigeschaltet" und "diese Aktion steht
/// Ihnen nicht offen" unterscheiden.
/// </summary>
public sealed class FlowzerAuthorizationResultHandler(IAuthorizationService authorizationService)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            var hasApplicationAccess = (await authorizationService.AuthorizeAsync(context.User, FlowzerPolicies.Access)).Succeeded;
            context.Response.Headers[FlowzerPolicies.AccessDeniedHeader] =
                hasApplicationAccess ? FlowzerPolicies.DeniedCapability : FlowzerPolicies.DeniedApplication;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
