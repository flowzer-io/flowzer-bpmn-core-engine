using System.Text.Json;
using Model;

namespace WebApiEngine.Ai;

/// <summary>Vollstaendig gebundener, aber noch nicht persistierter Provideraufruf.</summary>
internal sealed record AiProviderRequest(
    AiConnection Connection,
    string Model,
    int InstructionVersion,
    string Instruction,
    JsonElement Inputs,
    string ResultSchema,
    int MaxInputTokens,
    int MaxOutputTokens,
    TimeSpan Timeout);

/// <summary>Providerneutral normalisierte, noch serverseitig zu pruefende Antwort.</summary>
internal sealed record AiProviderResult(
    string OutputJson,
    string Model,
    AiTokenUsage Usage);

internal sealed record AiTokenUsage(int InputTokens, int OutputTokens, int TotalTokens);

[Flags]
internal enum AiProviderCapability
{
    None = 0,
    StructuredOutput = 1
}

/// <summary>Port fuer genau eine Providerfamilie; Auswahl und Secret-Aufloesung liegen davor.</summary>
internal interface IAiProviderAdapter
{
    AiProviderKind Provider { get; }
    AiProviderCapability Capabilities { get; }

    Task<AiProviderResult> ExecuteAsync(
        AiProviderRequest request,
        ReadOnlyMemory<char> secret,
        CancellationToken cancellationToken);
}

/// <summary>
/// Stabil klassifizierter technischer Providerausgang. Die Meldung ist absichtlich generisch
/// und darf weder Provider-Body noch Prompt, Eingaben oder Secret enthalten.
/// </summary>
internal sealed class AiProviderCallException(
    string code,
    bool retryable,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}
