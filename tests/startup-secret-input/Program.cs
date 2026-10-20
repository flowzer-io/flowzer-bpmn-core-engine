using Microsoft.Extensions.Configuration;
using System.Text.Json;
using WebApiEngine.Configuration;

// Testzweck: Ausschließlich öffentliche synthetische Marker prüfen; niemals Werte ausgeben.
var fixture = args[0];
var startupArgs = args.Skip(1).ToArray();
// Testzweck: Tatsächlich geladene Assemblies statt bloßer Versionsetiketten binden.
if (fixture == "assembly-bindings")
{
    var assemblies = new[] { typeof(object).Assembly, typeof(JsonDocument).Assembly,
        typeof(ConfigurationManager).Assembly, typeof(IConfigurationProvider).Assembly };
    Console.WriteLine(JsonSerializer.Serialize(assemblies.Distinct().Select(assembly => new
    {
        Name = assembly.GetName().Name, Path = assembly.Location,
        Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location)))
    })));
    return 0;
}
Stream input = fixture switch
{
    "read-throws" => new ThrowingStream(),
    "read-partial-disconnect" => new PartialDisconnectStream(),
    "read-ignores-cancellation" => new StalledStream(),
    "read-blocks-synchronously" => new BlockingStream(),
    _ => Console.OpenStandardInput()
};
var result = await RuntimeSecretInput.LoadAsync(startupArgs, fixture == "default-input" ? null : input);
var applied = false;
var keysCorrect = false;
var valuesCorrect = false;
var lookupCalls = 0;
var applyCalls = 0;
if (result.Accepted && result.Selected && fixture.StartsWith("provider-", StringComparison.Ordinal))
{
    // Testzweck: Ausschließlich eigene In-Memory-Provider; keine Default-/Datei-/ENV-Quellen.
    using var configuration = new ConfigurationManager();
    using var inner = new ConfigurationManager();
    using var nested = new ConfigurationManager();
    var extraRoots = new List<ConfigurationManager>();
    if (fixture is "provider-count-limit" or "provider-count-exceeded")
    {
        var count = fixture == "provider-count-limit" ? 64 : 65;
        while (((IConfigurationRoot)configuration).Providers.Count() < count)
            configuration.AddInMemoryCollection();
    }
    else if (fixture == "provider-cycle")
    {
        configuration.AddConfiguration(new CyclicRoot());
    }
    else if (fixture is "provider-depth-limit" or "provider-depth-exceeded")
    {
        var next = inner;
        for (var i = 0; i < (fixture == "provider-depth-limit" ? 7 : 10); i++)
        {
            var outer = new ConfigurationManager();
            outer.AddConfiguration(next);
            extraRoots.Add(outer);
            next = outer;
        }
        configuration.AddConfiguration(next);
    }
    else if (fixture.StartsWith("provider-chain", StringComparison.Ordinal))
    {
        inner.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [RuntimeSecretInput.BffKey] = fixture == "provider-chain-empty" ? "" : Markers.Value
        });
        if (fixture == "provider-chain-hidden")
            inner.AddInMemoryCollection(new Dictionary<string, string?> { [RuntimeSecretInput.BffKey] = "" });
        nested.AddConfiguration(inner);
        configuration.AddConfiguration(nested);
    }
    else if (fixture == "provider-section")
    {
        inner.AddInMemoryCollection(new Dictionary<string, string?> { ["scope:" + RuntimeSecretInput.BffKey] = Markers.Value });
        inner.AddInMemoryCollection(new Dictionary<string, string?> { ["scope:" + RuntimeSecretInput.BffKey] = "" });
        configuration.AddConfiguration(inner.GetSection("scope"));
    }
    else if (fixture != "provider-empty")
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [fixture == "provider-case" ? RuntimeSecretInput.BffKey.ToLowerInvariant() : RuntimeSecretInput.BffKey]
                = fixture == "provider-placeholder" ? "" : fixture == "provider-whitespace" ? " " : Markers.Value
        });
        if (fixture == "provider-hidden")
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [RuntimeSecretInput.BffKey] = "" });
    }
    applied = RuntimeSecretConfiguration.TryApply(result, configuration);
    keysCorrect = applied && configuration[RuntimeSecretInput.RuntimeKey] == Markers.Value;
    // Zyklische/absichtlich unprüfbare Quellen nach Ablehnung nicht erneut auflösen.
    valuesCorrect = applied && configuration[RuntimeSecretInput.BffKey] == Markers.Value;
    foreach (var owned in extraRoots) owned.Dispose();
}
else if (result.Accepted && result.Selected)
{
    applied = result.TryApply(
        key =>
        {
            lookupCalls++;
            if (fixture == "lookup-throws") throw new InvalidOperationException(Markers.Trap);
            return fixture == "collision" || (fixture == "migration-collision" && key == RuntimeSecretInput.MigrationKey);
        },
        values =>
        {
            applyCalls++;
            if (fixture == "apply-throws") throw new InvalidOperationException(Markers.Trap);
            var migrate = result.Arguments.Contains("--migrate", StringComparer.OrdinalIgnoreCase);
            var expected = migrate
                ? new[] { RuntimeSecretInput.RuntimeKey, RuntimeSecretInput.MigrationKey }
                : new[] { RuntimeSecretInput.RuntimeKey, RuntimeSecretInput.BffKey, RuntimeSecretInput.DirectoryKey };
            keysCorrect = values.Keys.Order().SequenceEqual(expected.Order());
            valuesCorrect = values.Values.All(value => value == Markers.Value);
        });
}
var repeatRejected = !result.TryApply(_ => throw new Exception(Markers.Trap), _ => throw new Exception(Markers.Trap));
Console.WriteLine(JsonSerializer.Serialize(new
{
    result.Selected, result.Accepted, Applied = applied, KeysCorrect = keysCorrect,
    ValuesCorrect = valuesCorrect, RepeatRejected = repeatRejected, LookupCalls = lookupCalls, ApplyCalls = applyCalls,
    SafeDescription = result.ToString() == "Runtime secret input (values hidden)",
    ArgumentsPreserved = result.Arguments.SequenceEqual(startupArgs.Where(a => a != "--secrets-stdin"))
}));
return result.Accepted && (!result.Selected || applied) ? 0 : 2;

internal static class Markers
{
    internal const string Value = "PUBLIC_SYNTHETIC_BOOTSTRAP_MARKER";
    internal static readonly string Trap = Value + "|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Value))
        + "|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Value))));
}

// Testzweck: Callback-/Stream-Ausnahmen dürfen keine Klartext-/Kodierungsmarker transportieren.
internal sealed class ThrowingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new IOException(Markers.Trap);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException(Markers.Trap);
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

// Testzweck: Auch eine Quelle, die Cancellation ignoriert, darf den Start nicht endlos blockieren.
internal sealed class StalledStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(new TaskCompletionSource<int>().Task);
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

// Testzweck: Auch vor Rückgabe des ValueTask synchron blockierende Quellen bleiben begrenzt.
internal sealed class BlockingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Thread.Sleep(TimeSpan.FromSeconds(30));
        throw new IOException(Markers.Trap);
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

// Testzweck: Abbruch nach bereits gelieferten Bytes ist kein gültiges EOF und kein Fallback.
internal sealed class PartialDisconnectStream : Stream
{
    private bool read;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (read) throw new IOException(Markers.Trap);
        read = true;
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"version\":1");
        bytes.CopyTo(buffer);
        return ValueTask.FromResult(bytes.Length);
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

// Testzweck: Zyklus vor jedem TryGet erkennen, ohne Stackoverflow/ungeprüftes GetSection.
internal sealed class CyclicRoot : IConfigurationRoot
{
    public CyclicRoot() => Providers = [new ChainedConfigurationProvider(new ChainedConfigurationSource { Configuration = this })];
    public IEnumerable<IConfigurationProvider> Providers { get; }
    public string? this[string key] { get => throw new InvalidOperationException(Markers.Trap); set => throw new NotSupportedException(); }
    public IEnumerable<IConfigurationSection> GetChildren() => throw new NotSupportedException();
    public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);
    public void Reload() => throw new NotSupportedException();
}
