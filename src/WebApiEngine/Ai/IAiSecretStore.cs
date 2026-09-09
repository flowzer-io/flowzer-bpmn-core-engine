using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace WebApiEngine.Ai;

/// <summary>
/// Austauschbarer serverseitiger Secret-Store. Der aktuelle Anwendungsfall fragt nur den
/// Verfuegbarkeitsstatus ab; spaetere Provideradapter erhalten geheime Werte ueber eine
/// getrennte, bewusst kleine Erweiterung dieser Abstraktion.
/// </summary>
public interface IAiSecretStore
{
    ValueTask<bool> ExistsAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loest das Secret erst unmittelbar vor einem Provideraufruf auf. Aufrufer muessen den
    /// Rueckgabewert entsorgen und duerfen ihn weder speichern noch protokollieren.
    /// </summary>
    ValueTask<AiSecretValue?> ResolveAsync(string reference, CancellationToken cancellationToken = default);
}

/// <summary>Moeglichst kurzlebiger, nach Verwendung ueberschriebener Secret-Puffer.</summary>
public sealed class AiSecretValue : IDisposable
{
    private char[]? _buffer;

    internal AiSecretValue(string value) => _buffer = value.ToCharArray();

    public ReadOnlyMemory<char> Value => _buffer ?? throw new ObjectDisposedException(nameof(AiSecretValue));

    public void Dispose()
    {
        if (_buffer is null) return;
        Array.Clear(_buffer);
        _buffer = null;
    }

    public override string ToString() => "[REDACTED]";
}

/// <summary>
/// Prueft und liest ausschließlich freigegebene Umgebungsvariablen. Referenzen folgen
/// <c>env:NAME</c> und muessen im konfigurierten Flowzer-Namensraum liegen; der Wert
/// verlaesst diese serverseitige Abstraktion nur als kurzlebiger, entsorgbarer Puffer.
/// </summary>
public sealed partial class EnvironmentAiSecretStore(IOptions<FlowzerAiOptions> options) : IAiSecretStore
{
    public ValueTask<bool> ExistsAsync(string reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetVariableName(reference, options.Value, out var variableName))
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variableName)));
    }

    public ValueTask<AiSecretValue?> ResolveAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetVariableName(reference, options.Value, out var variableName))
            return ValueTask.FromResult<AiSecretValue?>(null);
        var value = Environment.GetEnvironmentVariable(variableName);
        return ValueTask.FromResult(string.IsNullOrEmpty(value) ? null : new AiSecretValue(value));
    }

    internal static bool IsValidReference(string reference, FlowzerAiOptions options) =>
        TryGetVariableName(reference, options, out _);

    private static bool TryGetVariableName(string reference, FlowzerAiOptions options, out string variableName)
    {
        variableName = string.Empty;
        if (string.IsNullOrWhiteSpace(reference)
            || !reference.StartsWith("env:", StringComparison.Ordinal)
            || !options.IsValid())
        {
            return false;
        }

        var candidate = reference[4..];
        if (!candidate.StartsWith(options.SecretEnvironmentVariablePrefix, StringComparison.Ordinal)
            || !EnvironmentVariableName().IsMatch(candidate))
        {
            return false;
        }

        variableName = candidate;
        return true;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex EnvironmentVariableName();
}
