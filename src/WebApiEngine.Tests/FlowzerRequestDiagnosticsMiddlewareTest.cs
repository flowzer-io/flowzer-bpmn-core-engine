using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WebApiEngine.Middleware;

namespace WebApiEngine.Tests;

public sealed class FlowzerRequestDiagnosticsMiddlewareTest
{
    // Testzweck: Ein nicht gematchter, frei formulierbarer Pfad darf nicht in die Lognachricht
    // gelangen; der Request muss bei einem Fehlerstatus trotzdem diagnostisch geloggt werden.
    [Test]
    public async Task InvokeAsync_ShouldNotLogUntrustedPath_WhenRequestIsUnmatched()
    {
        var logger = new CapturingLogger<FlowzerRequestDiagnosticsMiddleware>();
        var context = new DefaultHttpContext();
        const string path = "/api/objects/UNTRUSTED_PATH_INPUT\r\nInjected-Log-Line";
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;

        var middleware = new FlowzerRequestDiagnosticsMiddleware(
            next: httpContext =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        var log = logger.Entries.Should().ContainSingle().Subject;
        log.Message.Should().Contain("Handled route unmatched");
        log.Message.Should().NotContain(path);
        log.Message.Should().NotContain("UNTRUSTED_PATH_INPUT");
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    // Testzweck: Serverseitige Endpoint-Metadaten bleiben als sichere Korrelation im Log,
    // während der frei formulierbare Request-Pfad weiterhin ausgeschlossen wird.
    [Test]
    public async Task InvokeAsync_ShouldLogEndpointDisplayName_WithoutLoggingRequestPath()
    {
        var logger = new CapturingLogger<FlowzerRequestDiagnosticsMiddleware>();
        var context = new DefaultHttpContext();
        const string path = "/api/objects/UNTRUSTED_PATH_INPUT";
        const string endpointName = "GET /api/objects/{id}";
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(),
            endpointName));

        var middleware = new FlowzerRequestDiagnosticsMiddleware(
            next: httpContext =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        var log = logger.Entries.Should().ContainSingle().Subject;
        log.Message.Should().Contain(endpointName);
        log.Message.Should().NotContain(path);
        log.Message.Should().NotContain("UNTRUSTED_PATH_INPUT");
    }
}
