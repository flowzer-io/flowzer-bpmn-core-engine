namespace WebApiEngine.Auth;

/// <summary>
/// Hilfslogik für API-Pfade, die nur mit einem aufgelösten Benutzerkontext
/// ausgeführt werden dürfen.
/// </summary>
public static class CurrentUserContextExtensions
{
    public static Guid RequireResolvedUserId(this CurrentUserContext currentUserContext, string operationDescription)
    {
        ArgumentNullException.ThrowIfNull(currentUserContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationDescription);

        if (!currentUserContext.IsFallback)
        {
            return currentUserContext.UserId;
        }

        throw new UnauthorizedAccessException(
            $"A resolved user context is required for {operationDescription}.");
    }
}
