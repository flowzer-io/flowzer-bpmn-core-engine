using Microsoft.AspNetCore.Mvc;

namespace WebApiEngine.Middleware;

/// <summary>Problem Details mit den kompatiblen Flowzer-Fehlerfeldern.</summary>
public sealed class ApiProblemDetails : ProblemDetails
{
    public bool Successful => false;
    public string ErrorMessage => Detail ?? Title ?? "The request failed.";
}
