using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Model;
using WebApiEngine.Ai;

namespace WebApiEngine.Tests;

/// <summary>Transport- und Sicherheitsvertrag der providerbezogenen KI-Adapter.</summary>
public sealed class AiProviderAdapterTest
{
    // Testzweck: Der OpenAI-Adapter verwendet ausschließlich die feste Responses-API,
    // deaktiviert Providerspeicherung und fordert eine strukturierte JSON-Antwort an.
    [Test]
    public async Task OpenAi_ShouldSendBoundedStructuredRequestAndNormalizeResponse()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {
              "model": "gpt-result-model",
              "output": [{"type":"message","content":[{"type":"output_text","text":"{\"category\":\"support\"}"}]}],
              "usage": {"input_tokens":12,"output_tokens":5,"total_tokens":17}
            }
            """));
        var adapter = new OpenAiResponsesAdapter(new HttpClient(handler));

        var result = await adapter.ExecuteAsync(Request(AiProviderKind.OpenAi), "top-secret".AsMemory(), default);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri.Should().Be(new Uri("https://api.openai.com/v1/responses"));
        handler.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "top-secret"));
        handler.Body.Should().NotContain("top-secret");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("store").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("model").GetString().Should().Be("task-model");
        body.RootElement.GetProperty("max_output_tokens").GetInt32().Should().Be(512);
        body.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString()
            .Should().Be("json_schema");
        result.OutputJson.Should().Be("{\"category\":\"support\"}");
        result.Model.Should().Be("gpt-result-model");
        result.Usage.Should().Be(new AiTokenUsage(12, 5, 17));
    }

    // Testzweck: Ein kompatibler Endpunkt verwendet ausschließlich die administrativ
    // gespeicherte API-Wurzel und fällt weder auf OpenAI noch auf einen anderen Host zurück.
    [Test]
    public async Task OpenAiCompatible_ShouldUseConfiguredChatCompletionsEndpoint()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {
              "model":"local-result-model",
              "choices":[{"message":{"role":"assistant","content":"{\"category\":\"sales\"}"}}],
              "usage":{"prompt_tokens":8,"completion_tokens":3,"total_tokens":11}
            }
            """));
        var adapter = new OpenAiCompatibleChatAdapter(new HttpClient(handler));

        var result = await adapter.ExecuteAsync(
            Request(AiProviderKind.OpenAiCompatible, "https://models.example.test/api/v1"),
            "compatible-secret".AsMemory(),
            default);

        handler.RequestUri.Should().Be(new Uri("https://models.example.test/api/v1/chat/completions"));
        handler.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "compatible-secret"));
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("response_format").GetProperty("type").GetString()
            .Should().Be("json_schema");
        result.OutputJson.Should().Be("{\"category\":\"sales\"}");
        result.Usage.Should().Be(new AiTokenUsage(8, 3, 11));
    }

    // Testzweck: Der Anthropic-Adapter setzt nur die dokumentierten Auth-/Versionsheader,
    // die feste Messages-API und den providerneutralen Ergebnisschema-Vertrag ein.
    [Test]
    public async Task Anthropic_ShouldSendStructuredMessageAndNormalizeResponse()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {
              "model":"claude-result-model",
              "content":[{"type":"text","text":"{\"category\":\"support\"}"}],
              "usage":{"input_tokens":10,"output_tokens":4}
            }
            """));
        var adapter = new AnthropicMessagesAdapter(new HttpClient(handler));

        var result = await adapter.ExecuteAsync(Request(AiProviderKind.Anthropic), "anthropic-secret".AsMemory(), default);

        handler.RequestUri.Should().Be(new Uri("https://api.anthropic.com/v1/messages"));
        handler.Authorization.Should().BeNull();
        handler.Headers["x-api-key"].Should().Equal("anthropic-secret");
        handler.Headers["anthropic-version"].Should().Equal("2023-06-01");
        handler.Body.Should().NotContain("anthropic-secret");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString()
            .Should().Be("json_schema");
        result.Model.Should().Be("claude-result-model");
        result.Usage.Should().Be(new AiTokenUsage(10, 4, 14));
    }

    // Testzweck: Providerfehler werden ohne Rohantwort oder Secret in stabile Klassen fuer
    // spaetere Retry-/Incident-Entscheidungen uebersetzt.
    [TestCase(HttpStatusCode.Unauthorized, "ai.provider.authentication", false)]
    [TestCase(HttpStatusCode.TooManyRequests, "ai.provider.rate_limited", true)]
    [TestCase(HttpStatusCode.BadRequest, "ai.provider.rejected", false)]
    [TestCase(HttpStatusCode.ServiceUnavailable, "ai.provider.unavailable", true)]
    public async Task OpenAi_ShouldClassifyProviderErrorWithoutLeakingBody(
        HttpStatusCode status,
        string expectedCode,
        bool retryable)
    {
        const string sensitiveBody = "provider-secret-diagnostic";
        var handler = new RecordingHandler(_ => JsonResponse($"{{\"error\":\"{sensitiveBody}\"}}", status));
        var adapter = new OpenAiResponsesAdapter(new HttpClient(handler));

        var action = () => adapter.ExecuteAsync(Request(AiProviderKind.OpenAi), "top-secret".AsMemory(), default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be(expectedCode);
        exception.Retryable.Should().Be(retryable);
        exception.Message.Should().NotContain(sensitiveBody).And.NotContain("top-secret");
    }

    // Testzweck: Eine formal erfolgreiche, aber unvollstaendige Providerantwort wird nicht
    // als leeres fachliches Ergebnis weitergereicht.
    [Test]
    public async Task OpenAi_ShouldRejectResponseWithoutOutputText()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{\"model\":\"gpt-result-model\",\"output\":[]}"));
        var adapter = new OpenAiResponsesAdapter(new HttpClient(handler));

        var action = () => adapter.ExecuteAsync(Request(AiProviderKind.OpenAi), "top-secret".AsMemory(), default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.provider.invalid_response");
        exception.Retryable.Should().BeFalse();
    }

    // Testzweck: Ohne tatsaechliche Tokenmessung kann Flowzer die gespeicherten Budgets nicht
    // beweisen; kompatible Ziele muessen deshalb mindestens Ein- und Ausgabetokens melden.
    [Test]
    public async Task OpenAiCompatible_ShouldRejectResponseWithoutUsage()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {"model":"local-result-model","choices":[{"message":{"content":"{}"}}]}
            """));
        var adapter = new OpenAiCompatibleChatAdapter(new HttpClient(handler));

        var action = () => adapter.ExecuteAsync(
            Request(AiProviderKind.OpenAiCompatible, "https://models.example.test/v1"),
            "secret".AsMemory(),
            default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.provider.invalid_response");
    }

    // Testzweck: Widerspruechliche Summenwerte gelten nicht als belastbare Kosten- oder
    // Budgetgrundlage und werden ebenso wie fehlende Tokenzaehler verworfen.
    [Test]
    public async Task OpenAiCompatible_ShouldRejectInconsistentUsageTotal()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {
              "model":"local-result-model",
              "choices":[{"message":{"content":"{}"}}],
              "usage":{"prompt_tokens":8,"completion_tokens":3,"total_tokens":99}
            }
            """));
        var adapter = new OpenAiCompatibleChatAdapter(new HttpClient(handler));

        var action = () => adapter.ExecuteAsync(
            Request(AiProviderKind.OpenAiCompatible, "https://models.example.test/v1"),
            "secret".AsMemory(),
            default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.provider.invalid_response");
    }

    // Testzweck: Der aufgabengebundene Timeout beendet einen haengenden Provideraufruf und
    // wird von einer expliziten Abbruchanforderung des aufrufenden Workers unterschieden.
    [Test]
    public async Task OpenAi_ShouldClassifyTaskTimeoutButPropagateCallerCancellation()
    {
        var adapter = new OpenAiResponsesAdapter(new HttpClient(new DelayedHandler()));
        var timedOut = () => adapter.ExecuteAsync(
            Request(AiProviderKind.OpenAi) with { Timeout = TimeSpan.FromMilliseconds(20) },
            "top-secret".AsMemory(),
            default);

        var timeout = (await timedOut.Should().ThrowAsync<AiProviderCallException>()).Which;
        timeout.Code.Should().Be("ai.provider.timeout");
        timeout.Retryable.Should().BeTrue();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => adapter.ExecuteAsync(
            Request(AiProviderKind.OpenAi),
            "top-secret".AsMemory(),
            cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
    }

    // Testzweck: Der aufgabengebundene Timeout umfasst auch die DNS-/Clientvorbereitung
    // eines benutzerdefinierten Ziels und nicht erst das spaetere Lesen der HTTP-Antwort.
    [Test]
    public async Task OpenAiCompatible_ShouldApplyTaskTimeoutToEndpointPreparation()
    {
        var adapter = new OpenAiCompatibleChatAdapter(new DelayedClientLeaseFactory());
        var action = async () => await adapter.ExecuteAsync(
                Request(AiProviderKind.OpenAiCompatible, "https://models.example.test/v1") with
                {
                    Timeout = TimeSpan.FromMilliseconds(20)
                },
                "secret".AsMemory(),
                default)
            .WaitAsync(TimeSpan.FromSeconds(2));

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.provider.timeout");
        exception.Retryable.Should().BeTrue();
    }

    // Testzweck: Auch eine erfolgreiche HTTP-Antwort darf das feste Envelope-Limit nicht
    // umgehen und wird ohne Uebernahme ihres Inhalts als ungueltig klassifiziert.
    [Test]
    public async Task OpenAi_ShouldRejectOversizedResponseEnvelope()
    {
        var oversized = new string('x', 2 * 1024 * 1024 + 1);
        var adapter = new OpenAiResponsesAdapter(new HttpClient(
            new RecordingHandler(_ => JsonResponse(oversized))));

        var action = () => adapter.ExecuteAsync(Request(AiProviderKind.OpenAi), "top-secret".AsMemory(), default);

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.provider.invalid_response");
        exception.Message.Should().NotContain("xxxxx");
    }

    private static AiProviderRequest Request(AiProviderKind provider, string? baseAddress = null) => new(
        new AiConnection(
            Guid.Parse("A1111111-1111-4111-8111-111111111111"),
            "Provider",
            provider,
            baseAddress is null ? AiProcessingLocation.Cloud : AiProcessingLocation.Local,
            baseAddress,
            "connection-model",
            "env:FLOWZER_AI_TEST",
            true,
            1,
            DateTimeOffset.Parse("2026-09-09T12:00:00Z"),
            Guid.Parse("A2222222-2222-4222-8222-222222222222")),
        "task-model",
        3,
        "Classify the workflow input.",
        Json("{\"request\":\"Please help\"}"),
        "{\"type\":\"object\",\"properties\":{\"category\":{\"type\":\"string\"}},\"required\":[\"category\"],\"additionalProperties\":false}",
        4_096,
        512,
        TimeSpan.FromSeconds(30));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static HttpResponseMessage JsonResponse(
        string body,
        HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public Dictionary<string, string[]> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            foreach (var header in request.Headers) Headers[header.Key] = header.Value.ToArray();
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private sealed class DelayedClientLeaseFactory : IAiHttpClientLeaseFactory
    {
        public async ValueTask<AiHttpClientLease> CreateAsync(
            AiConnection connection,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }
}
