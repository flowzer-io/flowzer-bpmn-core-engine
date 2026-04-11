using System.Net;
using System.Text;
using FluentAssertions;
using FlowzerFrontend;

namespace FlowzerFrontend.Tests;

public class FlowzerApiOptionsTest
{
    [Test]
    public void ResolveBaseAddress_ShouldFallbackToHostBaseAddress_WhenBaseUrlIsMissing()
    {
        var options = new FlowzerApiOptions();

        var result = options.ResolveBaseAddress("http://localhost:5269");

        result.Should().Be(new Uri("http://localhost:5269/"));
    }

    [Test]
    public void ResolveBaseAddress_ShouldUseConfiguredAbsoluteBaseAddress_WhenPresent()
    {
        var options = new FlowzerApiOptions
        {
            BaseUrl = "https://api.flowzer.local/v1"
        };

        var result = options.ResolveBaseAddress("http://localhost:5269/");

        result.Should().Be(new Uri("https://api.flowzer.local/v1/"));
    }

    [Test]
    public void ResolveBaseAddress_ShouldCombineRelativeBaseAddress_WithFrontendHost()
    {
        var options = new FlowzerApiOptions
        {
            BaseUrl = "/backend"
        };

        var result = options.ResolveBaseAddress("http://localhost:5269");

        result.Should().Be(new Uri("http://localhost:5269/backend/"));
    }

    [Test]
    public async Task GetJsonRequest_ShouldUseConfiguredHttpMethod_AndJsonPayload()
    {
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };

        var api = new FlowzerApi(httpClient);
        const string payload = "{\"name\":\"demo\"}";

        using var response = await api.GetJsonRequest("definition", HttpMethod.Put, payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.LastMethod.Should().Be(HttpMethod.Put);
        handler.LastRequestUri.Should().Be(new Uri("http://localhost:5182/definition"));
        handler.LastContentType.Should().Be("application/json");
        handler.LastContent.Should().Be(payload);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastContentType { get; private set; }
        public string? LastContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            LastContent = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}
