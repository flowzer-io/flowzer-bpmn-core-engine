using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Model;

namespace WebApiEngine.Ai;

/// <summary>Enger OpenAI-kompatibler Chat-Completions-Vertrag ohne Provider-Fallback.</summary>
internal sealed class OpenAiCompatibleChatAdapter(HttpClient client) : AiHttpProviderAdapter(client)
{
    public override AiProviderKind Provider => AiProviderKind.OpenAiCompatible;
    public override AiProviderCapability Capabilities => AiProviderCapability.StructuredOutput;

    protected override HttpRequestMessage BuildRequest(
        AiProviderRequest request,
        ReadOnlyMemory<char> secret)
    {
        if (string.IsNullOrWhiteSpace(request.Connection.BaseAddress)) throw InvalidResponse();
        var endpoint = new Uri($"{request.Connection.BaseAddress.TrimEnd('/')}/chat/completions");
        var schema = JsonNode.Parse(request.ResultSchema)
                     ?? throw InvalidResponse();
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = GuardedInstruction(request) },
                new JsonObject { ["role"] = "user", ["content"] = request.Inputs.GetRawText() }
            },
            ["max_tokens"] = request.MaxOutputTokens,
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = $"flowzer_result_v{request.InstructionVersion}",
                    ["schema"] = schema,
                    ["strict"] = false
                }
            }
        };
        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new string(secret.Span));
        return message;
    }

    protected override AiProviderResult ParseResponse(JsonElement response)
    {
        var choice = response.GetProperty("choices").EnumerateArray().FirstOrDefault();
        if (choice.ValueKind != JsonValueKind.Object
            || !choice.TryGetProperty("message", out var message))
            throw InvalidResponse();
        return new AiProviderResult(
            RequiredString(message, "content"),
            RequiredString(response, "model"),
            ReadUsage(response, "prompt_tokens", "completion_tokens", "total_tokens"));
    }
}
