using Model;

namespace WebApiEngine.Ai;

/// <summary>Waehlt genau den persistierten Provider und erlaubt keinerlei Fallback-Kette.</summary>
internal sealed class AiProviderRegistry
{
    private readonly IReadOnlyDictionary<AiProviderKind, IAiProviderAdapter> _adapters;

    public AiProviderRegistry(IEnumerable<IAiProviderAdapter> adapters)
    {
        var registered = new Dictionary<AiProviderKind, IAiProviderAdapter>();
        foreach (var adapter in adapters)
        {
            if (!registered.TryAdd(adapter.Provider, adapter))
                throw new InvalidOperationException($"AI provider '{adapter.Provider}' is registered more than once.");
        }
        _adapters = registered;
    }

    public IAiProviderAdapter Get(AiProviderKind provider, AiProviderCapability requiredCapabilities)
    {
        if (!_adapters.TryGetValue(provider, out var adapter))
            throw new AiProviderCallException(
                "ai.provider.unsupported",
                retryable: false,
                "The configured AI provider is not supported by this installation.");
        if ((adapter.Capabilities & requiredCapabilities) != requiredCapabilities)
            throw new AiProviderCallException(
                "ai.provider.capability_unsupported",
                retryable: false,
                "The configured AI provider does not support the required capability.");
        return adapter;
    }
}
