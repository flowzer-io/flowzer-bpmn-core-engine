using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebApiEngine.Limits;

namespace WebApiEngine.Tests;

public class FlowzerForwardedHeadersExtensionsTest
{
    // Testzweck: Mehrstufige, explizit vertraute Proxy-Ketten muessen vollstaendig
    // ausgewertet werden koennen, damit externes HTTPS und die echte Clientadresse
    // nicht bereits am internen Gateway verloren gehen.
    [Test]
    public void AddFlowzerForwardedHeaders_ShouldApplyConfiguredForwardLimit()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/12",
                ["ForwardedHeaders:ForwardLimit"] = "3"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddFlowzerForwardedHeaders(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        options.ForwardLimit.Should().Be(3);
    }
}
