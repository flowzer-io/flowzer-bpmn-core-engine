using System.Diagnostics;
using WebApiEngine.Diagnostics;

namespace WebApiEngine.Middleware;

/// <summary>
/// Ergänzt kleine, fokussierte Diagnoseinformationen für zentrale API-Pfade,
/// ohne eine komplette Observability-Plattform vorauszusetzen.
/// </summary>
public sealed class FlowzerRequestDiagnosticsMiddleware(
    RequestDelegate next,
    ILogger<FlowzerRequestDiagnosticsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
        var method = context.Request.Method;
        var routeName = context.GetEndpoint()?.DisplayName ?? path;

        using var activity = FlowzerDiagnostics.ActivitySource.StartActivity("flowzer.request", ActivityKind.Server);
        activity?.SetTag("http.method", method);
        activity?.SetTag("url.path", path);
        activity?.SetTag("flowzer.route_name", routeName);

        var stopwatch = Stopwatch.StartNew();

        await next(context);

        stopwatch.Stop();

        var statusCode = context.Response.StatusCode;
        FlowzerDiagnostics.RecordHttpRequest(method, routeName, statusCode, stopwatch.Elapsed);

        activity?.SetTag("http.status_code", statusCode);
        activity?.SetTag("flowzer.duration_ms", stopwatch.Elapsed.TotalMilliseconds);

        if (!ShouldLog(path, method, statusCode, stopwatch.Elapsed))
        {
            return;
        }

        var logLevel = statusCode >= StatusCodes.Status500InternalServerError
            ? LogLevel.Error
            : statusCode >= StatusCodes.Status400BadRequest || stopwatch.Elapsed >= TimeSpan.FromSeconds(1)
                ? LogLevel.Warning
                : LogLevel.Information;

        var activityTraceId = activity?.TraceId.ToString();
        var activitySpanId = activity?.SpanId.ToString();

        logger.Log(
            logLevel,
            "Handled {Method} {Path} with status {StatusCode} in {DurationMs:0.0} ms (RequestId: {RequestId}, ActivityTraceId: {ActivityTraceId}, ActivitySpanId: {ActivitySpanId}).",
            method,
            path,
            statusCode,
            stopwatch.Elapsed.TotalMilliseconds,
            context.TraceIdentifier,
            activityTraceId,
            activitySpanId);
    }

    private static bool ShouldLog(string path, string method, int statusCode, TimeSpan duration)
    {
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (statusCode >= StatusCodes.Status400BadRequest)
        {
            return true;
        }

        if (!HttpMethods.IsGet(method))
        {
            return true;
        }

        return duration >= TimeSpan.FromSeconds(1);
    }
}

public static class FlowzerRequestDiagnosticsExtensions
{
    public static IApplicationBuilder UseFlowzerRequestDiagnostics(this IApplicationBuilder app)
    {
        return app.UseMiddleware<FlowzerRequestDiagnosticsMiddleware>();
    }
}
