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
            return services.AddFlowzerOpenApplicationRolePolicies();
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

        var authorization = services.AddAuthorizationBuilder()
            .SetFallbackPolicy(BuildBasePolicy(options).Build());
        AddApplicationRolePolicies(authorization, options);

        return services;
    }

    /// <summary>
    /// Die Grundanforderung an jede Anfrage: angemeldet sein und, falls konfiguriert, die
    /// Zugangsrolle tragen. Ein Realm mit Selbstregistrierung stellt jedem ein gueltiges Token
    /// aus; erst die Pflichtrolle macht daraus einen Zugang zur Anwendung.
    /// </summary>
    private static AuthorizationPolicyBuilder BuildBasePolicy(FlowzerAuthenticationOptions options)
    {
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();

        if (!string.IsNullOrWhiteSpace(options.JwtBearer.RequiredRole))
        {
            var audience = options.JwtBearer.Audience;
            var requiredRole = options.JwtBearer.RequiredRole;
            policy.RequireAssertion(context => TokenRoles.HasRole(context.User, audience, requiredRole));
        }

        return policy;
    }

    /// <summary>
    /// Legt fuer jede Anwendungsrolle eine Policy an. Wichtig: Eine ausdrueckliche Policy am
    /// Endpunkt ersetzt die Fallback-Policy vollstaendig. Jede dieser Policies muss deshalb die
    /// Grundanforderung erneut enthalten, sonst waere ein Endpunkt mit Rollenpflicht
    /// ausgerechnet ohne Anmeldung und ohne Zugangsrolle erreichbar.
    ///
    /// Ist kein Rollenname konfiguriert, bleibt es bei der Grundanforderung: Ohne Rollenpflege
    /// soll sich gegenueber der bisherigen Installation nichts aendern.
    /// </summary>
    private static void AddApplicationRolePolicies(AuthorizationBuilder authorization, FlowzerAuthenticationOptions options)
    {
        var audience = options.JwtBearer.Audience;

        foreach (var (policyName, roleName) in new[]
                 {
                     (FlowzerPolicies.Modeler, options.JwtBearer.Roles.Modeler),
                     (FlowzerPolicies.Operator, options.JwtBearer.Roles.Operator)
                 })
        {
            var capabilityRole = roleName;
            authorization.AddPolicy(policyName, policy =>
            {
                var basePolicy = BuildBasePolicy(options).Build();
                policy.Combine(basePolicy);

                if (!string.IsNullOrWhiteSpace(capabilityRole))
                {
                    policy.RequireAssertion(context => TokenRoles.HasRole(context.User, audience, capabilityRole));
                }
            });
        }
    }

    /// <summary>
    /// Ohne aktives JWT-Schema gibt es keine Rollen; die Policies muessen trotzdem existieren,
    /// weil die Controller sie benennen.
    /// </summary>
    public static IServiceCollection AddFlowzerOpenApplicationRolePolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(FlowzerPolicies.Modeler, policy => policy.RequireAssertion(_ => true))
            .AddPolicy(FlowzerPolicies.Operator, policy => policy.RequireAssertion(_ => true));

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
