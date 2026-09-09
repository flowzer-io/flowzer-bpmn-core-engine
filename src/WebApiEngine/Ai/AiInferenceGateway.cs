using System.Text;
using System.Text.Json;
using core_engine;
using Microsoft.Extensions.Options;
using StorageSystem;

namespace WebApiEngine.Ai;

/// <summary>Providerneutraler, vollstaendig validierter Auftrag fuer einen einzelnen Modellaufruf.</summary>
internal sealed record AiInferenceCommand(
    Guid ConnectionId,
    string? Model,
    int InstructionVersion,
    string Instruction,
    JsonElement Inputs,
    string ResultSchema,
    int MaxInputTokens,
    int MaxOutputTokens,
    TimeSpan Timeout);

/// <summary>Geprueftes strukturiertes Ergebnis ohne rohe Providerantwort oder Header.</summary>
internal sealed record AiInferenceResult(
    JsonElement Output,
    string Model,
    AiTokenUsage Usage);

/// <summary>
/// Verbindet persistierte Metadaten, kurzlebige Secrets und exakt einen Provideradapter. Die
/// Klasse ist noch keine Laufzeit-Zustandsmaschine und fuehrt von sich aus keine Retries aus.
/// </summary>
internal sealed class AiInferenceGateway(
    IAiConnectionStorage connections,
    IAiSecretStore secrets,
    AiProviderRegistry providers,
    IOptions<FlowzerAiOptions> options)
{
    private const int MaximumInstructionLength = 20_000;
    private const int MaximumModelLength = 200;
    private const int MaximumInputBytes = 1024 * 1024;

    public async Task<AiInferenceResult> ExecuteAsync(
        AiInferenceCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await connections.Get(command.ConnectionId)
                         ?? throw Failure("ai.connection.not_found", "The AI connection was not found.");
        if (!connection.Enabled)
            throw Failure("ai.connection.disabled", "The AI connection is disabled.");

        try
        {
            var normalizedTarget = AiConnectionSecurityPolicy.ValidateAndNormalizeTarget(
                connection.Provider,
                connection.Location,
                connection.BaseAddress,
                options.Value);
            if (!string.Equals(normalizedTarget, connection.BaseAddress, StringComparison.Ordinal)
                || !EnvironmentAiSecretStore.IsValidReference(connection.SecretReference, options.Value))
                throw new ArgumentException("The persisted AI connection is not normalized.");
        }
        catch (ArgumentException exception)
        {
            throw new AiProviderCallException(
                "ai.connection.invalid",
                retryable: false,
                "The persisted AI connection is outside the installation policy.",
                exception);
        }

        var adapter = providers.Get(connection.Provider, AiProviderCapability.StructuredOutput);
        using var secret = await secrets.ResolveAsync(connection.SecretReference, cancellationToken);
        if (secret is null)
            throw Failure("ai.connection.secret_missing", "The AI connection secret is unavailable.");
        if (!IsSafeSecret(secret.Value.Span))
            throw Failure("ai.connection.secret_invalid", "The AI connection secret is invalid.");

        var request = new AiProviderRequest(
            connection,
            NormalizeModel(command.Model, connection.DefaultModel),
            command.InstructionVersion,
            command.Instruction.Trim(),
            command.Inputs.Clone(),
            command.ResultSchema,
            command.MaxInputTokens,
            command.MaxOutputTokens,
            command.Timeout);
        var providerResult = await adapter.ExecuteAsync(request, secret.Value, cancellationToken);
        EnsureUsageWithinBudget(providerResult.Usage, command);
        if (string.IsNullOrWhiteSpace(providerResult.Model)
            || providerResult.Model.Length > MaximumModelLength)
            throw Failure("ai.provider.invalid_response", "The AI provider returned an invalid response.");

        try
        {
            var output = AiResultSchemaProfile.ParseAndValidateResult(
                command.ResultSchema,
                providerResult.OutputJson);
            return new AiInferenceResult(output, providerResult.Model, providerResult.Usage);
        }
        catch (AiResultSchemaException exception)
        {
            throw new AiProviderCallException(
                exception.Code,
                retryable: false,
                "The AI provider result did not satisfy the configured result contract.",
                exception);
        }
    }

    private static void ValidateCommand(AiInferenceCommand command)
    {
        if (command.ConnectionId == Guid.Empty
            || command.InstructionVersion < 1
            || string.IsNullOrWhiteSpace(command.Instruction)
            || command.Instruction.Trim().Length > MaximumInstructionLength
            || command.Inputs.ValueKind != JsonValueKind.Object
            || Encoding.UTF8.GetByteCount(command.Inputs.GetRawText()) > MaximumInputBytes
            || command.MaxInputTokens is < 1 or > 128_000
            || command.MaxOutputTokens is < 1 or > 32_768
            || command.Timeout < TimeSpan.FromSeconds(1)
            || command.Timeout > TimeSpan.FromSeconds(300)
            || command.Model?.Trim().Length > MaximumModelLength)
            throw Failure("ai.request.invalid", "The AI inference request is invalid.");

        try
        {
            AiResultSchemaProfile.ValidateSchema(command.ResultSchema);
        }
        catch (AiResultSchemaException exception)
        {
            throw new AiProviderCallException(
                exception.Code,
                retryable: false,
                "The AI result schema is invalid.",
                exception);
        }
    }

    private static string NormalizeModel(string? overrideModel, string defaultModel)
    {
        var model = string.IsNullOrWhiteSpace(overrideModel) ? defaultModel.Trim() : overrideModel.Trim();
        if (model.Length is 0 or > MaximumModelLength)
            throw Failure("ai.request.invalid", "The AI inference request is invalid.");
        return model;
    }

    private static void EnsureUsageWithinBudget(AiTokenUsage usage, AiInferenceCommand command)
    {
        if (usage.InputTokens > command.MaxInputTokens)
            throw Failure("ai.provider.input_budget_exceeded", "The AI provider exceeded the input token budget.");
        if (usage.OutputTokens > command.MaxOutputTokens)
            throw Failure("ai.provider.output_budget_exceeded", "The AI provider exceeded the output token budget.");
    }

    private static bool IsSafeSecret(ReadOnlySpan<char> value)
    {
        if (value.Length is 0 or > 16_384) return false;
        foreach (var character in value)
        {
            if (char.IsControl(character)) return false;
        }
        return true;
    }

    private static AiProviderCallException Failure(string code, string message) =>
        new(code, retryable: false, message);
}
