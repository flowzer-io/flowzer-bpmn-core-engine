using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FlowzerFrontend;
using FlowzerFrontend.Models;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var flowzerApiOptions =
    builder.Configuration.GetSection(FlowzerApiOptions.SectionName).Get<FlowzerApiOptions>() ??
    new FlowzerApiOptions();
var isDevelopmentLikeEnvironment =
    builder.HostEnvironment.IsDevelopment() ||
    string.Equals(builder.HostEnvironment.Environment, "Playwright", StringComparison.OrdinalIgnoreCase);

builder.Services.AddSingleton<ExampleRestRequestBuilder>();
builder.Services.AddSingleton(flowzerApiOptions);
builder.Services.AddScoped(_ =>
{
    var httpClient = new HttpClient
    {
        BaseAddress = flowzerApiOptions.ResolveBaseAddress(builder.HostEnvironment.BaseAddress)
    };

    flowzerApiOptions.ApplyDefaultHeaders(httpClient, isDevelopmentLikeEnvironment);
    return httpClient;
});
builder.Services.AddScoped<FlowzerApi>();
builder.Services.AddFluentUIComponents();

await builder.Build().RunAsync();


