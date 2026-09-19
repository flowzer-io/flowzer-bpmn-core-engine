using System.Text.RegularExpressions;

namespace WebApiEngine.Connectors;

/// <summary>
/// Loest <c>secret:NAME</c> aus einer freigegebenen Umgebungsvariablen auf. Das Muster
/// entspricht dem KI-Secret-Store: Im Modell, im Auftrag und in jeder Antwort steht nur der
/// Name, der Wert lebt ausschliesslich in der Prozessumgebung und wird erst unmittelbar vor
/// dem Aufruf gelesen.
/// </summary>
public sealed partial class ConnectorSecretResolver(FlowzerConnectorOptions options)
{
    public const string ReferencePrefix = "secret:";

    public static bool IsReference(string? value) =>
        value is not null && value.StartsWith(ReferencePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Liefert den Wert hinter der Referenz. <c>null</c> heisst "nicht aufloesbar"; der
    /// Aufrufer meldet das als unvollstaendige Anfrage, ohne den Namen zu bewerten.
    /// </summary>
    public string? Resolve(string reference)
    {
        if (!TryGetVariableName(reference, out var variableName))
        {
            return null;
        }

        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Liest ein in der Konfiguration benanntes Secret, etwa das SMTP-Passwort.</summary>
    public string? ResolveByName(string secretName) =>
        string.IsNullOrWhiteSpace(secretName) ? null : Resolve(ReferencePrefix + secretName);

    private bool TryGetVariableName(string reference, out string variableName)
    {
        variableName = string.Empty;
        if (string.IsNullOrWhiteSpace(reference) || !IsReference(reference))
        {
            return false;
        }

        var candidate = options.SecretEnvironmentVariablePrefix + reference[ReferencePrefix.Length..];
        if (!SecretNameCharacters().IsMatch(candidate))
        {
            return false;
        }

        variableName = candidate;
        return true;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,190}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SecretNameCharacters();
}
