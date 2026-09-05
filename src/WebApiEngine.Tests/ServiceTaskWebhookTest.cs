using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using StorageSystem;
using WebApiEngine.Jobs;

namespace WebApiEngine.Tests;

/// <summary>
/// Webhook-Anmeldung eines Workers. Eine Anmeldung ist eine Aufforderung an die Engine, eine
/// fremde Adresse aufzurufen; sie muss deshalb eng begrenzt sein.
/// </summary>
[NonParallelizable]
public class ServiceTaskWebhookTest
{
    // Testzweck: Ohne freigegebene Zieladressen wird gar nichts angemeldet. Ein offener Standard
    // waere eine Einladung, die Engine interne Adressen aufrufen zu lassen.
    [Test]
    public async Task Register_ShouldBeRefused_WhenNoTargetHostsAreConfigured()
    {
        var service = CreateService(allowedHosts: []);

        var result = await service.Register("zahlung", "https://worker.example/hook", null, null, Guid.NewGuid());

        result.Webhook.Should().BeNull();
        result.Error.Should().Contain("No webhook target hosts");
    }

    // Testzweck: Nur freigegebene Adressen werden angenommen, Platzhalter gelten je Domain.
    [TestCase("https://worker.maass.it/hook", true)]
    [TestCase("https://anderer.worker.maass.it/hook", true)]
    [TestCase("https://boeser.example/hook", false)]
    [TestCase("https://maass.it.boeser.example/hook", false)]
    public async Task Register_ShouldAcceptOnlyAllowedHosts(string url, bool expected)
    {
        var service = CreateService(allowedHosts: ["worker.maass.it", "*.worker.maass.it"]);

        var result = await service.Register("zahlung", url, null, null, Guid.NewGuid());

        (result.Error is null).Should().Be(expected);
    }

    // Testzweck: Ohne TLS geht die Benachrichtigung im Klartext; das ist nur mit ausdruecklicher
    // Freigabe erlaubt.
    [Test]
    public async Task Register_ShouldRefuseHttp_UnlessExplicitlyAllowed()
    {
        var refusing = CreateService(allowedHosts: ["worker.maass.it"]);
        var allowing = CreateService(allowedHosts: ["worker.maass.it"], allowHttp: true);

        var refused = await refusing.Register("zahlung", "http://worker.maass.it/hook", null, null, Guid.NewGuid());
        var allowed = await allowing.Register("zahlung", "http://worker.maass.it/hook", null, null, Guid.NewGuid());

        refused.Error.Should().Contain("https");
        allowed.Error.Should().BeNull();
    }

    // Testzweck: Dieselbe Adresse zweimal anzumelden ergibt eine Anmeldung, nicht zwei; sonst
    // bekaeme derselbe Worker jede Benachrichtigung doppelt.
    [Test]
    public async Task Register_ShouldNotCreateADuplicateForTheSameTypeAndUrl()
    {
        var service = CreateService(allowedHosts: ["worker.maass.it"]);
        var first = await service.Register("zahlung", "https://worker.maass.it/hook", null, null, Guid.NewGuid());

        var second = await service.Register("zahlung", "https://worker.maass.it/hook", "geheim", "zweite Anmeldung", Guid.NewGuid());

        second.Webhook!.Id.Should().Be(first.Webhook!.Id);
        (await service.GetAll()).Should().ContainSingle();
    }

    // Testzweck: Die Signatur ist reproduzierbar und haengt am Geheimnis; ein Worker kann sie
    // damit selbst nachrechnen.
    [Test]
    public void ComputeSignature_ShouldBeStableAndDependOnTheSecret()
    {
        var first = ServiceTaskWebhookNotifier.ComputeSignature("geheim", "{\"jobId\":1}");
        var again = ServiceTaskWebhookNotifier.ComputeSignature("geheim", "{\"jobId\":1}");
        var other = ServiceTaskWebhookNotifier.ComputeSignature("anders", "{\"jobId\":1}");

        first.Should().StartWith("sha256=");
        first.Should().Be(again);
        first.Should().NotBe(other);
    }

    private static ServiceTaskWebhookService CreateService(string[] allowedHosts, bool allowHttp = false)
    {
        var storage = new InMemoryServiceTaskStorage();
        return new ServiceTaskWebhookService(
            new WebhookStorageProvider(storage),
            new FlowzerWebhookOptions { AllowedHosts = allowedHosts, AllowHttp = allowHttp },
            new FakeTimeProvider(DateTimeOffset.UtcNow));
    }

    private sealed class WebhookStorageProvider(IServiceTaskStorage serviceTaskStorage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() => new Wrapper(serviceTaskStorage);

        private sealed class Wrapper(IServiceTaskStorage serviceTaskStorage) : ITransactionalStorage
        {
            public IDefinitionStorage DefinitionStorage => throw new NotSupportedException();
            public IMessageSubscriptionStorage SubscriptionStorage => throw new NotSupportedException();
            public IInstanceStorage InstanceStorage => throw new NotSupportedException();
            public IFormStorage FormStorage => throw new NotSupportedException();
            public IServiceTaskStorage ServiceTaskStorage { get; } = serviceTaskStorage;

            public void CommitChanges()
            {
            }

            public void RollbackTransaction()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
