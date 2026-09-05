using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using WebApiEngine.Shared;

namespace WebApiEngine.Limits;

public static class FlowzerLimitsExtensions
{
    /// <summary>Pfade, die auch unter Last erreichbar bleiben muessen.</summary>
    private static readonly string[] ExemptPathPrefixes = ["/health"];

    public static IServiceCollection AddFlowzerLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var rateLimiting = configuration.GetSection(FlowzerRateLimitingOptions.SectionName).Get<FlowzerRateLimitingOptions>()
                           ?? new FlowzerRateLimitingOptions();
        rateLimiting.Validate();
        services.AddSingleton(rateLimiting);

        var uploadLimit = configuration.GetSection(FlowzerUploadLimitOptions.SectionName).Get<FlowzerUploadLimitOptions>()
                          ?? new FlowzerUploadLimitOptions();
        uploadLimit.Validate();
        services.AddSingleton(uploadLimit);

        if (!rateLimiting.Enabled)
        {
            return services;
        }

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (IsExempt(context))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                return RateLimitPartition.GetFixedWindowLimiter(ResolvePartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimiting.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimiting.WindowSeconds),
                    QueueLimit = rateLimiting.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
                    ? value
                    : TimeSpan.FromSeconds(rateLimiting.WindowSeconds);

                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                context.HttpContext.Response.ContentType = "application/json";

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiStatusResult
                    {
                        Successful = false,
                        ErrorMessage = "Too many requests. Please retry after the indicated number of seconds."
                    },
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Groessengrenze fuer Anfragekoerper. Gehoert vor die Authentifizierung: Ein zu grosser
    /// Koerper soll gar nicht erst gepuffert werden.
    /// </summary>
    public static IApplicationBuilder UseFlowzerUploadLimit(this IApplicationBuilder app)
    {
        var uploadLimit = app.ApplicationServices.GetRequiredService<FlowzerUploadLimitOptions>();

        app.Use(async (context, next) =>
        {
            if (!IsExempt(context))
            {
                // Die Kappung selbst macht der Server anhand dieses Features; der Content-Length-
                // Vergleich ist nur die schnellere Absage. Ohne Header (chunked, HTTP/2) greift
                // weiterhin das Feature beim Lesen des Koerpers.
                var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                {
                    sizeFeature.MaxRequestBodySize = uploadLimit.MaxUploadBytes;
                }

                if (context.Request.ContentLength is { } declaredLength && declaredLength > uploadLimit.MaxUploadBytes)
                {
                    throw new BadHttpRequestException(
                        $"Request body exceeds the configured limit of {uploadLimit.MaxUploadBytes} bytes.",
                        StatusCodes.Status413PayloadTooLarge);
                }
            }

            await next();
        });

        return app;
    }

    /// <summary>
    /// Kontingent je Aufrufer. Gehoert <b>nach</b> die Authentifizierung: Davor ist
    /// <c>HttpContext.User</c> leer, und jede Anfrage fiele in dieselbe Adress-Partition.
    /// </summary>
    public static IApplicationBuilder UseFlowzerRateLimiting(this IApplicationBuilder app)
    {
        var rateLimiting = app.ApplicationServices.GetRequiredService<FlowzerRateLimitingOptions>();
        if (rateLimiting.Enabled)
        {
            app.UseRateLimiter();
        }

        return app;
    }

    private static bool IsExempt(HttpContext context) =>
        ExemptPathPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Kontingent je angemeldeter Person, sonst je Adresse. Hinter einem Reverse Proxy liefert
    /// die Weiterleitungs-Middleware die echte Adresse; ohne sie teilen sich alle den Proxy-Eintrag.
    /// </summary>
    private static string ResolvePartitionKey(HttpContext context)
    {
        var subject = context.User.FindFirst("sub")?.Value
                      ?? context.User.FindFirst("oid")?.Value
                      ?? context.User.Identity?.Name;

        if (!string.IsNullOrWhiteSpace(subject))
        {
            return $"user:{subject}";
        }

        var address = context.Connection.RemoteIpAddress;
        return address is null ? "anonymous" : $"ip:{(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)}";
    }
}
