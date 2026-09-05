using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FlowzerFrontend;
using FlowzerFrontend.Auth;
using FlowzerFrontend.Models;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var flowzerApiOptions =
    builder.Configuration.GetSection(FlowzerApiOptions.SectionName).Get<FlowzerApiOptions>() ??
    new FlowzerApiOptions();
var oidcOptions =
    builder.Configuration.GetSection(FlowzerOidcOptions.SectionName).Get<FlowzerOidcOptions>() ??
    new FlowzerOidcOptions();
oidcOptions.Validate();

var isDevelopmentLikeEnvironment =
    builder.HostEnvironment.IsDevelopment() ||
    string.Equals(builder.HostEnvironment.Environment, "Playwright", StringComparison.OrdinalIgnoreCase);

builder.Services.AddSingleton<ExampleRestRequestBuilder>();
builder.Services.AddSingleton(flowzerApiOptions);
builder.Services.AddSingleton(oidcOptions);

if (oidcOptions.IsEnabled)
{
    // Anmeldung beim Identity Provider (Authorization Code + PKCE). Das Access-Token geht als
    // Bearer ausschliesslich an die konfigurierte API-Basisadresse.
    builder.Services.AddOidcAuthentication(options =>
    {
        options.ProviderOptions.Authority = oidcOptions.Authority;
        options.ProviderOptions.ClientId = oidcOptions.ClientId;
        options.ProviderOptions.ResponseType = "code";
        // Die Standard-Scopes der Bibliothek (openid, profile) bleiben erhalten; konfigurierte
        // Scopes wie der API-Scope oder offline_access kommen dazu.
        foreach (var scope in oidcOptions.ResolveScopes())
        {
            if (!options.ProviderOptions.DefaultScopes.Contains(scope))
            {
                options.ProviderOptions.DefaultScopes.Add(scope);
            }
        }

        options.UserOptions.NameClaim = "name";
    });
    builder.Services.AddScoped<FlowzerApiAuthorizationMessageHandler>();
}
else
{
    // Ohne Identity Provider gilt die Oberflaeche als angemeldeter technischer Benutzer, damit
    // dieselben [Authorize]-Regeln greifen. Die API erkennt ihn nur im Development-Modus.
    builder.Services.AddAuthorizationCore();
    builder.Services.AddScoped<AuthenticationStateProvider, DevelopmentAuthenticationStateProvider>();
}

builder.Services.AddSingleton<ApiAccessState>();

builder.Services.AddScoped(serviceProvider =>
{
    HttpMessageHandler handler = new HttpClientHandler();
    if (oidcOptions.IsEnabled)
    {
        var authorizationHandler = serviceProvider.GetRequiredService<FlowzerApiAuthorizationMessageHandler>();
        authorizationHandler.InnerHandler = handler;
        handler = authorizationHandler;
    }

    // Aussen um die Kette: sieht jede Antwort und erkennt daran, ob dieses Konto
    // ueberhaupt fuer Flowzer freigeschaltet ist.
    handler = new ApiAccessStateHandler(serviceProvider.GetRequiredService<ApiAccessState>()) { InnerHandler = handler };

    var httpClient = new HttpClient(handler)
    {
        BaseAddress = flowzerApiOptions.ResolveBaseAddress(builder.HostEnvironment.BaseAddress)
    };

    if (!oidcOptions.IsEnabled)
    {
        flowzerApiOptions.ApplyDefaultHeaders(httpClient, isDevelopmentLikeEnvironment);
    }

    return httpClient;
});
builder.Services.AddScoped<FlowzerApi>();
builder.Services.AddFluentUIComponents();

await builder.Build().RunAsync();
