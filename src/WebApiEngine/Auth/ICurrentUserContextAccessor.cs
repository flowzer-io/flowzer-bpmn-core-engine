namespace WebApiEngine.Auth;

public interface ICurrentUserContextAccessor
{
    CurrentUserContext GetCurrentUser();
}
