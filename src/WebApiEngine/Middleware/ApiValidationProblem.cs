namespace WebApiEngine.Middleware;

/// <summary>
/// Problem Details für neue Feldfehler und bisherige fachliche 422-Fehler. Die beiden
/// Legacy-Eigenschaften halten bestehende ApiStatusResult-Clients lesefähig.
/// </summary>
public class ApiValidationProblem : HttpValidationProblemDetails
{
    public bool Successful => false;
    public string ErrorMessage => Detail ?? Title ?? "Validation failed.";

    public ApiValidationProblem() { }
    public ApiValidationProblem(IDictionary<string, string[]> errors) : base(errors) { }
}
