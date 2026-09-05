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

    public static IApplicationBuilder UseFlowzerLimits(this IApplicationBuilder app)
    {
        var uploadLimit = app.ApplicationServices.GetRequiredService<FlowzerUploadLimitOptions>();

        // Vor der Authentifizierung: ein zu grosser Koerper soll gar nicht erst gepuffert werden.
        app.Use(async (context, next) =>
        {
            if (!IsExempt(context))
            {
                var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                {
                    sizeFeature.MaxRequestBodySize = uploadLimit.MaxUploadBytes;
                }

                if (context.Request.ContentLength > uploadLimit.MaxUploadBytes)
                {
                    // Der Content-Length-Header erlaubt die Absage, bevor ein einziges Byte gelesen wird.
                    throw new BadHttpRequestException(
                        $"Request body exceeds the configured limit of {uploadLimit.MaxUploadBytes} bytes.",
                        StatusCodes.Status413PayloadTooLarge);
                }
            }

            await next();
        });

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
