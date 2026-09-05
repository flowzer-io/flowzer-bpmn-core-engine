namespace WebApiEngine.Middleware;

/// <summary>
/// CORS-Richtlinie der API (Abschnitt <c>Cors</c>). Konfigurierte Origins werden exakt zugelassen.
/// Ohne Konfiguration gilt im Development-Modus weiterhin "jede Origin" (Blazor-Dev-Server,
/// Playwright); in allen anderen Umgebungen werden keine Cross-Origin-Zugriffe erlaubt. Hinter dem
/// Runtime-Gateway laufen API und Frontend unter derselben Origin und brauchen kein CORS.
/// </summary>
public static class FlowzerCorsExtensions
{
    public const string PolicyName = "FlowzerCors";
    public const string SectionName = "Cors";

    public static IServiceCollection AddFlowzerCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var allowedOrigins = configuration
            .GetSection($"{SectionName}:AllowedOrigins")
            .Get<string[]>()?
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.TrimEnd('/'))
            .ToArray() ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
                }
                else if (environment.IsDevelopment())
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                }
                // sonst: keine Origin erlaubt, die Middleware setzt keine CORS-Header
            });
        });

        return services;
    }

    public static IApplicationBuilder UseFlowzerCors(this IApplicationBuilder app)
    {
        return app.UseCors(PolicyName);
    }
}
