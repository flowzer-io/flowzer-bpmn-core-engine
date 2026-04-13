using System.Net;
using System.Text;
using FluentAssertions;
using FlowzerFrontend.Exceptions;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class ApiExceptionContractTest
{
    // Testzweck: Prüft, dass eine fehlende JSON-Antwort beim Laden einer Definition als ApiContractException endet.
    [Test]
    public async Task GetDefinition_ShouldThrowApiContractException_WhenResponseBodyIsNull()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK))
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };
        var api = new FlowzerApi(httpClient);

        var action = async () => await api.GetDefinition(Guid.NewGuid());

        await action.Should()
            .ThrowAsync<ApiContractException>()
            .WithMessage("No definition found for definitionId *");
    }

    // Testzweck: Prüft, dass nicht erfolgreiche HTTP-Statuscodes beim Speichern eines Formulars als HttpRequestException propagiert werden.
    [Test]
    public async Task SaveForm_ShouldThrowHttpRequestException_WhenStatusCodeIsNotSuccessful()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "{}"))
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };
        var api = new FlowzerApi(httpClient);

        var action = async () => await api.SaveForm(new FormDto
        {
            Id = Guid.NewGuid(),
            FormId = Guid.NewGuid(),
            Version = new VersionDto(1, 0),
            FormData = "{\"type\":\"form\"}"
        });

        var exceptionAssertion = await action.Should().ThrowAsync<HttpRequestException>();
        exceptionAssertion.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string? responseContent = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent ?? "null", Encoding.UTF8, "application/json")
            });
        }
    }
}
