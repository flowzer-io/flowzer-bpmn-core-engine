using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using WebApiEngine.Middleware;

namespace WebApiEngine.Auth;

/// <summary>
/// Verdrahtet die optionale JWT-Bearer- beziehungsweise BFF-Authentifizierung. Bei aktivem Schema gilt eine
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

        // Der Controller bleibt in allen Betriebsarten registriert und liefert ohne BFF 404.
        // Seine Abhaengigkeit muss deshalb auch im reinen Bearer-/None-Modus aufloesbar sein;
        // Cookies werden dadurch nicht automatisch erzeugt und die Middleware bleibt optional.
        services.AddAntiforgery(antiforgery =>
        {
            antiforgery.HeaderName = "X-Flowzer-CSRF";
            antiforgery.Cookie.Name = "__Host-Flowzer-Csrf";
            antiforgery.Cookie.HttpOnly = true;
            antiforgery.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            antiforgery.Cookie.SameSite = SameSiteMode.Strict;
            antiforgery.Cookie.Path = "/";
        });

        if (!options.IsAuthenticationEnabled)
        {
            return services.AddFlowzerOpenApplicationRolePolicies();
        }

        if (options.IsBffEnabled)
        {
            services.AddDataProtection()
                .SetApplicationName("Flowzer.WebApi")
                .PersistKeysToFileSystem(new DirectoryInfo(options.Bff.DataProtectionKeysPath));
            services.AddScoped<BffAccessTokenClaimsValidator>();

            services.AddAuthentication(authentication =>
                {
                    authentication.DefaultScheme = FlowzerAuthenticationSchemes.Application;
                    authentication.DefaultAuthenticateScheme = FlowzerAuthenticationSchemes.Application;
                    authentication.DefaultChallengeScheme = FlowzerAuthenticationSchemes.Application;
                    authentication.DefaultSignInScheme = FlowzerAuthenticationSchemes.Cookie;
                })
                .AddPolicyScheme(FlowzerAuthenticationSchemes.Application, null, policy =>
                {
                    policy.ForwardDefaultSelector = context =>
                        context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                            ? JwtBearerDefaults.AuthenticationScheme
                            : FlowzerAuthenticationSchemes.Cookie;
                })
                .AddCookie(FlowzerAuthenticationSchemes.Cookie, cookie =>
                {
                    cookie.Cookie.Name = "__Host-Flowzer-Session";
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    cookie.Cookie.SameSite = SameSiteMode.Lax;
                    cookie.Cookie.Path = "/";
                    // Rechte werden nur beim OIDC-Login neu geprueft; deshalb keine gleitende
                    // Cookie-Laufzeit. Der Validator begrenzt sie weiter auf das Access-Token-Ende.
                    cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
                    cookie.SlidingExpiration = false;
                    cookie.Events = new CookieAuthenticationEvents
                    {
                        OnRedirectToLogin = context =>
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        },
                        OnRedirectToAccessDenied = context =>
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return Task.CompletedTask;
                        }
                    };
                })
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwt => ConfigureJwt(jwt, options))
                .AddOpenIdConnect(FlowzerAuthenticationSchemes.OpenIdConnect, oidc =>
                {
                    oidc.Authority = options.JwtBearer.Authority;
                    oidc.ClientId = options.Bff.ClientId;
                    oidc.ClientSecret = options.Bff.ClientSecret;
                    oidc.RequireHttpsMetadata = options.JwtBearer.RequireHttpsMetadata;
                    oidc.ResponseType = "code";
                    oidc.UsePkce = true;
                    oidc.SaveTokens = false;
                    oidc.MapInboundClaims = false;
                    oidc.CallbackPath = "/bff/signin-oidc";
                    oidc.Scope.Clear();
                    oidc.Scope.Add("openid");
                    oidc.Scope.Add("profile");
                    oidc.Scope.Add("email");
                    foreach (var scope in options.Bff.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)))
                    {
                        oidc.Scope.Add(scope);
                    }

                    oidc.Events = new OpenIdConnectEvents
                    {
                        OnTokenValidated = async context =>
                        {
                            var validator = context.HttpContext.RequestServices.GetRequiredService<BffAccessTokenClaimsValidator>();
                            context.Principal = await validator.CreateCookiePrincipalAsync(context);
                        }
                    };
                });
        }
        else
        {
            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(jwt => ConfigureJwt(jwt, options));
        }

        var authorization = services.AddAuthorizationBuilder()
            .SetFallbackPolicy(BuildBasePolicy(options).Build())
            .AddPolicy(FlowzerPolicies.Session, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(FlowzerPolicies.Access, policy => policy.Combine(BuildBasePolicy(options).Build()));
        AddApplicationRolePolicies(authorization, options);
        AddStrictIdentityDirectoryPolicy(authorization, options);

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, FlowzerAuthorizationResultHandler>();

        return services;
    }

    private static void ConfigureJwt(JwtBearerOptions jwt, FlowzerAuthenticationOptions options)
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
                     (FlowzerPolicies.Operator, options.JwtBearer.Roles.Operator),
                     (FlowzerPolicies.Worker, options.JwtBearer.Roles.Worker)
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

    private static void AddStrictIdentityDirectoryPolicy(
        AuthorizationBuilder authorization,
        FlowzerAuthenticationOptions options)
    {
        var audience = options.JwtBearer.Audience;
        var operatorRole = options.JwtBearer.Roles.Operator;
        authorization.AddPolicy(FlowzerPolicies.IdentityDirectoryOperator, policy =>
        {
            policy.Combine(BuildBasePolicy(options).Build());
            // Anders als historische Endpunkte gibt es fuer den neuen administrativen Vertrag
            // keine rollenlose Kompatibilitaetsfreigabe. Fehlende Konfiguration verweigert alles.
            policy.RequireAssertion(context =>
                !string.IsNullOrWhiteSpace(operatorRole)
                && TokenRoles.HasRole(context.User, audience, operatorRole));
        });
    }

    /// <summary>
    /// Ohne aktives JWT-Schema gibt es keine Rollen; die Policies muessen trotzdem existieren,
    /// weil die Controller sie benennen.
    /// </summary>
    public static IServiceCollection AddFlowzerOpenApplicationRolePolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            // Ohne Auth-Schema erreicht der deaktivierte BFF-Controller seine 404-Pruefung.
            .AddPolicy(FlowzerPolicies.Session, policy => policy.RequireAssertion(_ => true))
            .AddPolicy(FlowzerPolicies.Access, policy => policy.RequireAssertion(_ => true))
            .AddPolicy(FlowzerPolicies.Modeler, policy => policy.RequireAssertion(_ => true))
            .AddPolicy(FlowzerPolicies.Operator, policy => policy.RequireAssertion(_ => true))
            .AddPolicy(FlowzerPolicies.IdentityDirectoryOperator, policy => policy.RequireAssertion(_ => true))
            .AddPolicy(FlowzerPolicies.Worker, policy => policy.RequireAssertion(_ => true));

        return services;
    }

    public static IApplicationBuilder UseFlowzerAuthentication(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<FlowzerAuthenticationOptions>();
        if (!options.IsAuthenticationEnabled)
        {
            return app;
        }

        app.UseAuthentication();
        if (options.IsBffEnabled)
        {
            app.UseMiddleware<FlowzerBffCsrfMiddleware>();
        }

        app.UseAuthorization();
        return app;
    }
}
