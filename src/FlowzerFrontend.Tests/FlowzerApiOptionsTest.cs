using System.Net;
using System.Text;
using FluentAssertions;
using FlowzerFrontend;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class FlowzerApiOptionsTest
{
    // Testzweck: Deckt den Fall „Resolve Base Address Should Fallback To Host Base Address When Base URL Is Missing“ ab.
    [Test]
    public void ResolveBaseAddress_ShouldFallbackToHostBaseAddress_WhenBaseUrlIsMissing()
    {
        var options = new FlowzerApiOptions();

        var result = options.ResolveBaseAddress("http://localhost:5269");

        result.Should().Be(new Uri("http://localhost:5269/"));
    }

    // Testzweck: Deckt den Fall „Resolve Base Address Should Use Configured Absolute Base Address When Present“ ab.
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

    // Testzweck: Deckt den Fall „Resolve Base Address Should Combine Relative Base Address With Frontend Host“ ab.
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

    // Testzweck: Deckt den Fall „Resolve Development User ID Should Return Configured User When Frontend Runs In Development“ ab.
    [Test]
    public void ResolveDevelopmentUserId_ShouldReturnConfiguredUser_WhenFrontendRunsInDevelopment()
    {
        var developmentUserId = Guid.NewGuid();
        var options = new FlowzerApiOptions
        {
            DevelopmentUserId = developmentUserId.ToString()
        };

        var result = options.ResolveDevelopmentUserId(isDevelopment: true);

        result.Should().Be(developmentUserId);
    }

    // Testzweck: Deckt den Fall „Resolve Development User ID Should Return Null When Frontend Does Not Run In Development“ ab.
    [Test]
    public void ResolveDevelopmentUserId_ShouldReturnNull_WhenFrontendDoesNotRunInDevelopment()
    {
        var options = new FlowzerApiOptions
        {
            DevelopmentUserId = Guid.NewGuid().ToString()
        };

        var result = options.ResolveDevelopmentUserId(isDevelopment: false);

        result.Should().BeNull();
    }

    // Testzweck: Deckt den Fall „Apply Default Headers Should Add Technical User Header When Development User Is Configured“ ab.
    [Test]
    public void ApplyDefaultHeaders_ShouldAddTechnicalUserHeader_WhenDevelopmentUserIsConfigured()
    {
        var developmentUserId = Guid.NewGuid();
        var options = new FlowzerApiOptions
        {
            DevelopmentUserId = developmentUserId.ToString()
        };
        using var httpClient = new HttpClient();

        options.ApplyDefaultHeaders(httpClient, isDevelopment: true);

        httpClient.DefaultRequestHeaders.Contains(FlowzerApiOptions.DevelopmentUserIdHeaderName).Should().BeTrue();
        httpClient.DefaultRequestHeaders
            .GetValues(FlowzerApiOptions.DevelopmentUserIdHeaderName)
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .Be(developmentUserId.ToString());
    }

    // Testzweck: Deckt den Fall „Apply Default Headers Should Not Add Technical User Header Outside Development“ ab.
    [Test]
    public void ApplyDefaultHeaders_ShouldNotAddTechnicalUserHeader_OutsideDevelopment()
    {
        var options = new FlowzerApiOptions
        {
            DevelopmentUserId = Guid.NewGuid().ToString()
        };
        using var httpClient = new HttpClient();

        options.ApplyDefaultHeaders(httpClient, isDevelopment: false);

        httpClient.DefaultRequestHeaders.Contains(FlowzerApiOptions.DevelopmentUserIdHeaderName).Should().BeFalse();
    }

    // Testzweck: Deckt den Fall „Get JSON Request Should Use Configured HTTP Method And JSON Payload“ ab.
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

    // Testzweck: Deckt den Fall „Get Signal Subscriptions Should Use Signals Route“ ab.
    [Test]
    public async Task GetSignalSubscriptions_ShouldUseSignalsRoute()
    {
        var instanceId = Guid.NewGuid();
        var handler = new RecordingHttpMessageHandler(CreateApiStatusResultJson(new[]
        {
            new SignalSubscriptionDto
            {
                Signal = "InvoiceReceived",
                ProcessId = "Process_Invoice",
                RelatedDefinitionId = "invoice-process",
                DefinitionId = Guid.NewGuid(),
                ProcessInstanceId = instanceId
            }
        }));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };

        var api = new FlowzerApi(httpClient);

        var result = await api.GetSignalSubscriptions(instanceId);

        result.Should().HaveCount(1);
        handler.LastRequestUri.Should().Be(new Uri($"http://localhost:5182/instance/{instanceId}/subscription/signals"));
    }

    // Testzweck: Deckt den Fall „Get Service Subscriptions Should Use Services Route“ ab.
    [Test]
    public async Task GetServiceSubscriptions_ShouldUseServicesRoute()
    {
        var instanceId = Guid.NewGuid();
        var handler = new RecordingHttpMessageHandler(CreateApiStatusResultJson(new[]
        {
            new TokenDto
            {
                CurrentFlowNodeId = "Activity_ServiceTask",
                State = FlowNodeStateDto.Active
            }
        }));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };

        var api = new FlowzerApi(httpClient);

        var result = await api.GetServiceSubscriptions(instanceId);

        result.Should().HaveCount(1);
        handler.LastRequestUri.Should().Be(new Uri($"http://localhost:5182/instance/{instanceId}/subscription/services"));
    }

    // Testzweck: Deckt den Fall „Get Latest Form Should Use Latest Form Route“ ab.
    [Test]
    public async Task GetLatestForm_ShouldUseLatestFormRoute()
    {
        var formId = Guid.NewGuid();
        var handler = new RecordingHttpMessageHandler(CreateApiStatusResultJson(new FormDto
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = new VersionDto(1, 2),
            FormData = "{\"type\":\"form\"}"
        }));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };

        var api = new FlowzerApi(httpClient);

        var result = await api.GetLatestForm(formId);

        result.FormId.Should().Be(formId);
        handler.LastRequestUri.Should().Be(new Uri($"http://localhost:5182/form/{formId}/latest"));
    }

    // Testzweck: Deckt den Fall „Get Form Should Use Versioned Form Route“ ab.
    [Test]
    public async Task GetForm_ShouldUseVersionedFormRoute()
    {
        var formId = Guid.NewGuid();
        var version = new VersionDto(2, 1);
        var handler = new RecordingHttpMessageHandler(CreateApiStatusResultJson(new FormDto
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Version = version,
            FormData = "{\"type\":\"form\"}"
        }));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };

        var api = new FlowzerApi(httpClient);

        var result = await api.GetForm(formId, version);

        result.Version.Should().BeEquivalentTo(version);
        handler.LastRequestUri.Should().Be(new Uri($"http://localhost:5182/form/{formId}/{version}"));
    }

    // Testzweck: Deckt den Fall „Upload Definition Should Include Previous GUID Query When Provided“ ab.
    [Test]
    public async Task UploadDefinition_ShouldIncludePreviousGuidQuery_WhenProvided()
    {
        var previousGuid = Guid.NewGuid();
        var definitionDto = CreateDefinitionDto();
        var handler = new RecordingHttpMessageHandler(System.Text.Json.JsonSerializer.Serialize(definitionDto));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };

        var api = new FlowzerApi(httpClient);

        var result = await api.UploadDefinition("<xml />", previousGuid);

        result.Id.Should().Be(definitionDto.Id);
        handler.LastRequestUri.Should().Be(new Uri($"http://localhost:5182/definition?previousGuid={previousGuid}"));
    }

    // Testzweck: Deckt den Fall „Upload Definition Should Not Include Previous GUID Query When GUID Is Empty“ ab.
    [Test]
    public async Task UploadDefinition_ShouldNotIncludePreviousGuidQuery_WhenGuidIsEmpty()
    {
        var definitionDto = CreateDefinitionDto();
        var handler = new RecordingHttpMessageHandler(System.Text.Json.JsonSerializer.Serialize(definitionDto));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };

        var api = new FlowzerApi(httpClient);

        _ = await api.UploadDefinition("<xml />", Guid.Empty);

        handler.LastRequestUri.Should().Be(new Uri("http://localhost:5182/definition"));
    }

    // Testzweck: Deckt den Fall „Deploy Definition Should Include Previous GUID Query When Provided“ ab.
    [Test]
    public async Task DeployDefinition_ShouldIncludePreviousGuidQuery_WhenProvided()
    {
        var previousGuid = Guid.NewGuid();
        var definitionDto = CreateDefinitionDto();
        var handler = new RecordingHttpMessageHandler(CreateApiStatusResultJson(definitionDto));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };

        var api = new FlowzerApi(httpClient);

        var result = await api.DeployDefinition("<xml />", previousGuid);

        result.Id.Should().Be(definitionDto.Id);
        handler.LastRequestUri.Should().Be(new Uri($"http://localhost:5182/definition/deploy?previousGuid={previousGuid}"));
    }

    private static string CreateApiStatusResultJson<T>(T result)
    {
        return System.Text.Json.JsonSerializer.Serialize(new ApiStatusResult<T>(result));
    }

    private static BpmnDefinitionDto CreateDefinitionDto()
    {
        return new BpmnDefinitionDto
        {
            Id = Guid.NewGuid(),
            DefinitionId = "definition-demo",
            Hash = "hash-demo",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new VersionDto(2, 1)
        };
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;

        public RecordingHttpMessageHandler(string responseContent = "{}")
        {
            _responseContent = responseContent;
        }

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
                Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
            };
        }
    }
}
