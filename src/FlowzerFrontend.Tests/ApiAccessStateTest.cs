using System.Net;
using FluentAssertions;
using FlowzerFrontend.Auth;

namespace FlowzerFrontend.Tests;

/// <summary>
/// Wer angemeldet ist, aber die Zugriffsrolle nicht hat, bekam bisher rohe API-Fehler zu sehen.
/// Der Zustand macht daraus eine Aussage, die die Oberflaeche anzeigen kann.
/// </summary>
public class ApiAccessStateTest
{
    // Testzweck: Eine 403-Antwort der API setzt den Zustand auf "kein Zugang" und meldet das.
    [Test]
    public async Task Handler_ShouldMarkAccessAsDenied_WhenTheApiAnswersForbidden()
    {
        var state = new ApiAccessState();
        var notified = 0;
        state.Changed += () => notified++;
        using var client = CreateClient(state, HttpStatusCode.Forbidden);

        await client.GetAsync("https://api.test/usertask");

        state.IsAccessDenied.Should().BeTrue();
        notified.Should().Be(1);
    }

    // Testzweck: Eine erfolgreiche Antwort hebt einen frueheren Verweigerungszustand wieder auf,
    // damit eine nachtraeglich vergebene Rolle ohne Neuladen wirkt.
    [Test]
    public async Task Handler_ShouldClearAccessDenied_WhenTheApiAnswersSuccessfully()
    {
        var state = new ApiAccessState();
        state.MarkAccessDenied();
        using var client = CreateClient(state, HttpStatusCode.OK);

        await client.GetAsync("https://api.test/usertask");

        state.IsAccessDenied.Should().BeFalse();
    }

    // Testzweck: 401 bleibt Sache der Anmeldung, nicht der Rollenanzeige; sonst wuerde die
    // Oberflaeche bei abgelaufener Sitzung faelschlich "kein Zugang" zeigen.
    [Test]
    public async Task Handler_ShouldIgnoreUnauthorized()
    {
        var state = new ApiAccessState();
        using var client = CreateClient(state, HttpStatusCode.Unauthorized);

        await client.GetAsync("https://api.test/usertask");

        state.IsAccessDenied.Should().BeFalse();
    }

    // Testzweck: Wiederholte 403-Antworten melden den Zustand nur einmal, damit die Oberflaeche
    // nicht bei jedem Hintergrundaufruf neu zeichnet.
    [Test]
    public async Task Handler_ShouldNotifyOnlyOnStateChange()
    {
        var state = new ApiAccessState();
        var notified = 0;
        state.Changed += () => notified++;
        using var client = CreateClient(state, HttpStatusCode.Forbidden);

        await client.GetAsync("https://api.test/usertask");
        await client.GetAsync("https://api.test/definition/meta");

        notified.Should().Be(1);
    }

    // Testzweck: Eine 403 wegen einer fehlenden Einzelberechtigung darf nicht als kompletter
    // Zugangsverlust erscheinen; wer sie bekommt, ist fuer Flowzer offensichtlich freigeschaltet.
    [Test]
    public async Task Handler_ShouldNotReportAccessDenied_WhenOnlyASingleCapabilityIsMissing()
    {
        var state = new ApiAccessState();
        state.MarkAccessDenied();
        using var client = CreateClient(state, HttpStatusCode.Forbidden, deniedHeader: "capability");

        await client.PostAsync("https://api.test/definition/deploy", new StringContent(""));

        state.IsAccessDenied.Should().BeFalse();
    }

    // Testzweck: Eine 403 ohne Einordnung wird weiterhin als fehlender Zugang gewertet, damit
    // eine aeltere API-Version die Anzeige nicht verschluckt.
    [Test]
    public async Task Handler_ShouldAssumeMissingAccess_WhenTheApiSendsNoClassification()
    {
        var state = new ApiAccessState();
        using var client = CreateClient(state, HttpStatusCode.Forbidden);

        await client.GetAsync("https://api.test/usertask");

        state.IsAccessDenied.Should().BeTrue();
    }

    private static HttpClient CreateClient(ApiAccessState state, HttpStatusCode statusCode, string? deniedHeader = null)
    {
        var handler = new ApiAccessStateHandler(state) { InnerHandler = new StubHandler(statusCode, deniedHeader) };
        return new HttpClient(handler);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string? deniedHeader = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (deniedHeader is not null)
            {
                response.Headers.Add(ApiAccessStateHandler.AccessDeniedHeader, deniedHeader);
            }

            return Task.FromResult(response);
        }
    }
}
