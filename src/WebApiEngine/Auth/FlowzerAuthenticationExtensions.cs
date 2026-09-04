using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace WebApiEngine.Auth;

/// <summary>
/// Verdrahtet die optionale JWT-Bearer-Authentifizierung. Bei aktivem Schema gilt eine
/// Fallback-Policy "authentifizierter Benutzer" fuer alle Endpunkte; Ausnahmen wie die
/// Health-Endpunkte tragen ausdruecklich <see cref="AllowAnonymousAttribute"/>.
/// </summary>
public static class FlowzerAuthenticationExtensions
{
    public static IServiceCollection AddFlowzerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(FlowzerAuthenticationOptions.SectionName).Get<FlowzerAuthenticationOptions>()
                      ?? new FlowzerAuthenticationOptions();
        options.Validate();
        services.AddSingleton(options);

        if (!options.IsJwtBearerEnabled)
        {
            return services;
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.JwtBearer.Authority;
                jwt.Audience = options.JwtBearer.Audience;
                jwt.RequireHttpsMetadata = options.JwtBearer.RequireHttpsMetadata;
                jwt.TokenValidationParameters.ValidIssuer = options.JwtBearer.Authority;
                jwt.TokenValidationParameters.ValidAudience = options.JwtBearer.Audience;
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }

    public static IApplicationBuilder UseFlowzerAuthentication(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<FlowzerAuthenticationOptions>();
        if (!options.IsJwtBearerEnabled)
        {
            return app;
        }

        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
