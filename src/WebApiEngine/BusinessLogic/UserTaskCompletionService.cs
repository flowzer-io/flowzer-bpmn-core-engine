using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;
using WebApiEngine.Idempotency;

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
        var target = $"{result.ProcessInstanceId:D}/{result.TokenId:D}";
        // Legacy-Hashes bleiben exakt erhalten. Neue Bedingungen gehören dagegen
        // gemeinsam zum unveränderlichen Request in einem festen Vertragsnamensraum.
        var hasBinding = result.ExpectedUserTaskId.HasValue || result.ExpectedDefinitionId.HasValue
            || result.RequireAssignedToCurrentUser;
        var idempotency = HttpIdempotency.Create(httpContext.Request, currentUser,
            "user-task-completion", target, result,
            hasBinding ? "bound-user-task-completion:v1" : null);

        return await businessLogic.CompleteUserTaskAsync(result, currentUser, canOperate,
            httpContext.RequestAborted, idempotency);
    }
}

/// <summary>Erwartbare fachliche Ergebnisse; unbekannte und fremde Aufgaben bleiben ununterscheidbar.</summary>
public enum UserTaskCompletionOutcome
{
    Completed,
    NotFound
}

/// <summary>Sicherer Konflikt eines alten Formulars nach Versionswechsel, ohne interne Kennungen.</summary>
public sealed class UserTaskBindingConflictException()
    : Exception("The displayed user-task definition has changed. Reopen the task before submitting.");
