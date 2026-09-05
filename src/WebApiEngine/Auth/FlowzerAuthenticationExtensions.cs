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

                // Der gueltige Issuer kommt aus den OIDC-Metadaten der Authority. Ein fest auf die
                // Authority gesetzter ValidIssuer wuerde Tokens ablehnen, deren `iss` davon
                // abweicht (Entra-v1-Tokens, abschliessender Schraegstrich bei Keycloak).
                //
                // Claims bleiben unter ihren Originalnamen (`sub`, `oid`), damit der
                // Benutzerkontext sie so liest, wie es in OPERATIONS.md dokumentiert ist.
                jwt.MapInboundClaims = false;
            });

        var fallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
        if (!string.IsNullOrWhiteSpace(options.JwtBearer.RequiredRole))
        {
            // Ein Realm mit Selbstregistrierung stellt jedem ein gueltiges Token aus. Erst die
            // Pflichtrolle macht daraus einen Zugang zur Anwendung.
            var audience = options.JwtBearer.Audience;
            var requiredRole = options.JwtBearer.RequiredRole;
            fallbackPolicy.RequireAssertion(context => TokenRoles.HasRole(context.User, audience, requiredRole));
        }

        services.AddAuthorizationBuilder().SetFallbackPolicy(fallbackPolicy.Build());

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
