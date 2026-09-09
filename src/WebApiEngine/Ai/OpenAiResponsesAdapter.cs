using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Model;

namespace WebApiEngine.Ai;

/// <summary>OpenAI Responses-API mit festem Cloudziel und deaktivierter Antwortspeicherung.</summary>
internal sealed class OpenAiResponsesAdapter(HttpClient client) : AiHttpProviderAdapter(client)
{
    private static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");

    public override AiProviderKind Provider => AiProviderKind.OpenAi;
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
            ["instructions"] = GuardedInstruction(request),
            ["input"] = request.Inputs.GetRawText(),
            ["max_output_tokens"] = request.MaxOutputTokens,
            ["store"] = false,
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = $"flowzer_result_v{request.InstructionVersion}",
                    ["schema"] = schema,
                    ["strict"] = false
                }
            }
        };
        var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new string(secret.Span));
        return message;
    }

    protected override AiProviderResult ParseResponse(JsonElement response)
    {
        var output = response.GetProperty("output")
            .EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "message")
            .SelectMany(item => item.GetProperty("content").EnumerateArray())
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "output_text")
            .Select(item => RequiredString(item, "text"))
            .FirstOrDefault();
        if (output is null) throw InvalidResponse();
        return new AiProviderResult(
            output,
            RequiredString(response, "model"),
            ReadUsage(response, "input_tokens", "output_tokens", "total_tokens"));
    }
}
