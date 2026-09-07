using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Einziger HTTP-Einstieg für menschliche Aufgabenabschlüsse. Beide historischen
/// Routen beziehen Akteur und Betriebsrecht hier aus dem Request, niemals aus dem Body.
/// Die aufgabenbezogene Prüfung erfolgt anschließend innerhalb der Engine-Transaktion.
/// </summary>
public sealed class UserTaskCompletionService(
    BpmnBusinessLogic businessLogic,
    ICurrentUserContextAccessor currentUserAccessor,
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService)
{
    /// <summary>Schließt eine Aufgabe im authentifizierten Request-Kontext ab.</summary>
    public async Task<UserTaskCompletionOutcome> CompleteAsync(UserTaskResult result)
    {
        var currentUser = currentUserAccessor.GetCurrentUser();
        currentUser.RequireResolvedUserId("completing user tasks");
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new UnauthorizedAccessException("A request context is required for completing user tasks.");
        var canOperate = (await authorizationService.AuthorizeAsync(httpContext.User, FlowzerPolicies.Operator)).Succeeded;

        return await businessLogic.CompleteUserTaskAsync(result, currentUser, canOperate, httpContext.RequestAborted);
    }
}

/// <summary>Erwartbare fachliche Ergebnisse; unbekannte und fremde Aufgaben bleiben ununterscheidbar.</summary>
public enum UserTaskCompletionOutcome
{
    Completed,
    NotFound
}
