using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Model;

namespace WebApiEngine.Ai;

/// <summary>Anthropic Messages-API mit festem Cloudziel und strukturiertem Ausgabeformat.</summary>
internal sealed class AnthropicMessagesAdapter(HttpClient client) : AiHttpProviderAdapter(client)
{
    private static readonly Uri Endpoint = new("https://api.anthropic.com/v1/messages");

    public override AiProviderKind Provider => AiProviderKind.Anthropic;
    public override AiProviderCapability Capabilities => AiProviderCapability.StructuredOutput;

    protected override HttpRequestMessage BuildRequest(
        AiProviderRequest request,
        ReadOnlyMemory<char> secret)
    {
        var schema = JsonNode.Parse(request.ResultSchema)
                     ?? throw InvalidResponse();
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["max_tokens"] = request.MaxOutputTokens,
            ["system"] = GuardedInstruction(request),
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = request.Inputs.GetRawText() }
            },
            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["schema"] = schema
                }
            }
        };
        var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.TryAddWithoutValidation("x-api-key", new string(secret.Span));
        message.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        return message;
    }

    protected override AiProviderResult ParseResponse(JsonElement response)
    {
        var output = response.GetProperty("content")
            .EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "text")
            .Select(item => RequiredString(item, "text"))
            .FirstOrDefault();
        if (output is null) throw InvalidResponse();
        return new AiProviderResult(
            output,
            RequiredString(response, "model"),
            ReadUsage(response, "input_tokens", "output_tokens"));
    }
}
