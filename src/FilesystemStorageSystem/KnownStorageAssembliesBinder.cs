using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace FilesystemStorageSystem;

/// <summary>
/// Begrenzt polymorphe Bestandsdokumente auf die Assemblies, die Flowzer selbst schreibt.
/// Ohne Binder koennte ein manipulierter <c>$type</c>-Wert beliebige Fremdtypen erzeugen.
/// </summary>
internal sealed class KnownStorageAssembliesBinder : DefaultSerializationBinder
{
    private static readonly HashSet<string> AllowedAssemblies = new(StringComparer.Ordinal)
    {
        "FlowzerBPMN", "Model", "StorageSystemShared", "core-engine", "Flowzer.Shared",
        "System.Private.CoreLib", "System.Collections", "System.Linq", "System.Runtime", "mscorlib", "netstandard"
    };

    public override Type BindToType(string? assemblyName, string typeName)
    {
        var simpleAssemblyName = assemblyName?.Split(',')[0].Trim();
        if (simpleAssemblyName is null || !AllowedAssemblies.Contains(simpleAssemblyName))
        {
            throw new JsonSerializationException(
                $"Type '{typeName}' from assembly '{assemblyName}' is not allowed in stored documents.");
        }

        var type = base.BindToType(assemblyName, typeName);
        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
        {
            var argumentAssembly = argument.Assembly.GetName().Name;
            if (argumentAssembly is null || !AllowedAssemblies.Contains(argumentAssembly))
            {
                throw new JsonSerializationException(
                    $"Generic argument '{argument.FullName}' is not allowed in stored documents.");
            }
        }

        return type;
    }
}
