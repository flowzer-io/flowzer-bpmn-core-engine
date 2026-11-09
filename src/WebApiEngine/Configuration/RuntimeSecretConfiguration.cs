using Microsoft.Extensions.Configuration;

namespace WebApiEngine.Configuration;

/// <summary>Schmaler Adapter für den gewählten stdin-Bootstrap, ohne Host-/Serviceaufbau.</summary>
internal static class RuntimeSecretConfiguration
{
    private const int MaximumChainDepth = 8;
    private const int MaximumProviderCount = 64;

    /// <summary>
    /// Prüft auch innere Providerketten, bevor der feste In-Memory-Provider hinzukommt.
    /// Unprüfbare Sections, zyklische oder übergroße Ketten werden nicht als leer behandelt.
    /// </summary>
    internal static bool TryApply(RuntimeSecretInput input, ConfigurationManager configuration) =>
        input.TryApply(key => HasExistingValue(configuration, key),
            values => configuration.AddInMemoryCollection(values));

    private static bool HasExistingValue(IConfigurationRoot root, string key)
    {
        var path = new HashSet<IConfigurationRoot>(ReferenceEqualityComparer.Instance);
        var providerCount = 0;
        return Inspect(root, key, path, 0, ref providerCount);
    }

    private static bool Inspect(IConfigurationRoot root, string key, HashSet<IConfigurationRoot> path,
        int depth, ref int providerCount)
    {
        if (depth > MaximumChainDepth || !path.Add(root)) return true;
        try
        {
            foreach (var provider in root.Providers)
            {
                if (++providerCount > MaximumProviderCount) return true;
                if (provider is ChainedConfigurationProvider chained)
                {
                    // TryGet allein sieht nur den effektiven inneren Wert und übersieht
                    // darunterliegende Fremdsecrets, die ein höherer Leerwert verdeckt.
                    if (chained.Configuration is not IConfigurationRoot inner
                        || Inspect(inner, key, path, depth + 1, ref providerCount)) return true;
                }
                if (provider.TryGet(key, out var value) && !string.IsNullOrEmpty(value)) return true;
            }
            return false;
        }
        finally
        {
            path.Remove(root);
        }
    }
}
