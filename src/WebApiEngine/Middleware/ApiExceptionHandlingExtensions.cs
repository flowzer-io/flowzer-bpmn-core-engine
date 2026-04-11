using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using WebApiEngine.Shared;

namespace WebApiEngine.Middleware;

public static class ApiExceptionHandlingExtensions
{
    /// <summary>
    /// Liefert auch für unbehandelte Fehler konsistente JSON-Fehlerverträge,
    /// damit Frontend, Tests und Monitoring keine HTML-Fehlerseiten oder
    /// framework-spezifische Antworten interpretieren müssen.
    /// </summary>
    public static IApplicationBuilder UseFlowzerApiExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseExceptionHandler(exceptionHandlerApp =>
        {
            exceptionHandlerApp.Run(async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerPathFeature>()?.Error;
                if (exception is null)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new ApiStatusResult("An unexpected server error occurred."));
                    return;
                }

                context.Response.StatusCode = MapStatusCode(exception);
                context.Response.ContentType = "application/json";

                var errorMessage = context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? "An unexpected server error occurred."
                    : exception.Message;

                var payload = new ApiStatusResult
                {
                    Successful = false,
                    ErrorMessage = errorMessage
                };

                await context.Response.WriteAsJsonAsync(payload);
            });
        });
    }

    private static int MapStatusCode(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException or KeyNotFoundException => StatusCodes.Status404NotFound,
            ArgumentException or FormatException or JsonException => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            NotImplementedException => StatusCodes.Status501NotImplemented,
            _ => StatusCodes.Status500InternalServerError
        };
    }
}
