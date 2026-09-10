using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using WebApiEngine.Auth;

namespace WebApiEngine.Middleware;

/// <summary>
/// Erzwingt Origin- und Antiforgery-Nachweise fuer jeden schreibenden Cookie-Request. Bearer-
/// Clients sind nicht CSRF-anfaellig und behalten deshalb ihren bisherigen API-Vertrag.
/// </summary>
public sealed class FlowzerBffCsrfMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { HttpMethods.Get, HttpMethods.Head, HttpMethods.Options, HttpMethods.Trace };

    public async Task InvokeAsync(
        HttpContext context,
        FlowzerAuthenticationOptions options,
        IAntiforgery antiforgery)
    {
        if (!options.IsBffEnabled
            || SafeMethods.Contains(context.Request.Method)
            || context.Request.Path.Equals("/bff/signin-oidc", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            // Die PolicyScheme-Auswahl bleibt strikt: Auch bei ungueltigem Bearer darf ein
            // vorhandenes Cookie nicht als versteckter Fallback verwendet werden.
            await next(context);
            return;
        }

        var cookie = await context.AuthenticateAsync(FlowzerAuthenticationSchemes.Cookie);
        if (!cookie.Succeeded)
        {
            // OIDC-Protokoll-Callbacks und spaeter die normale Autorisierung entscheiden selbst.
            await next(context);
            return;
        }

        if (!HasSameOrigin(context.Request))
        {
            await WriteProblemAsync(context, "Invalid request origin.");
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            await WriteProblemAsync(context, "Invalid or missing CSRF token.");
            return;
        }

        await next(context);
    }

    private static bool HasSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin))
        {
            return false;
        }

        var expected = new UriBuilder(request.Scheme, request.Host.Host)
        {
            Port = request.Host.Port ?? -1
        }.Uri;

        return string.Equals(parsedOrigin.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(parsedOrigin.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
               && parsedOrigin.Port == expected.Port
               && parsedOrigin.AbsolutePath == "/"
               && string.IsNullOrEmpty(parsedOrigin.Query)
               && string.IsNullOrEmpty(parsedOrigin.Fragment);
    }

    private static async Task WriteProblemAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = MediaTypeNames.Application.ProblemJson;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "CSRF validation failed.",
            Detail = detail,
            Type = "https://flowzer.io/problems/csrf-validation"
        };
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, cancellationToken: context.RequestAborted);
    }
}
