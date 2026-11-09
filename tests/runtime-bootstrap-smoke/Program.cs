namespace Flowzer.Smoke;

/// <summary>Source-Prototyp: Contracttests vor jedem Transport; Liveadapter sind noch gesperrt.</summary>
internal static class Program
{
    internal static bool IsContractMode(string[] args) => args.SequenceEqual(new[] { "--contract-test" });

    public static async Task<int> Main(string[] args)
    {
        if (IsContractMode(args)) return await ContractTests.RunAsync();
        // Nur der später separat freigegebene Parent setzt diese nichtgeheimen Bindungen.
        if (args.Length != 4 || args[0] != "--bound-fixture"
            || Environment.GetEnvironmentVariable("FLOWZER_SMOKE_BOUND_NONCE") != args[1]
            || !System.Text.RegularExpressions.Regex.IsMatch(Environment.GetEnvironmentVariable("FLOWZER_SMOKE_BINDING_SHA") ?? "", "^[a-f0-9]{64}$")
            || !long.TryParse(Environment.GetEnvironmentVariable("FLOWZER_SMOKE_BOUND_UNTIL"), out var until)
            || until <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() || until > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300)
        {
            Console.Error.WriteLine("FLOWZER_SMOKE_SOURCE_ONLY");
            return 78;
        }
        var diagnostic = new FixtureDiagnostics();
        try
        {
            var clock = new LiveClock();
            var packet = await FixtureInput.ReadAsync(new StdinInput(), clock);
            if (packet.Nonce != args[1]) throw new SmokeFailure(Failure.Input);
            diagnostic.Enter(FixturePhase.Binding);
            await new FixtureRunner(new LiveFixturePorts(packet, args[2], args[3]), clock, diagnostic).RunAsync(packet);
            diagnostic.Enter(FixturePhase.Complete);
            Console.WriteLine("FLOWZER_FIXTURE_OK");
            return 0;
        }
        catch (Exception error)
        {
            diagnostic.Record(error); Console.Error.WriteLine(Rejection(diagnostic)); return 2;
        }
    }
    /// <summary>Wertfreie endliche Fehlerprojektion statt Exception-/Paket-/Prozessausgaben.</summary>
    internal static string Rejection(FixtureDiagnostics diagnostic) => System.Text.Json.JsonSerializer.Serialize(new {
        result = "FLOWZER_SMOKE_REJECTED", diagnostic = diagnostic.Snapshot()
    });
}

/// <summary>Begrenzter Paketkern; der Leserrand erhält eine explizite verbleibende Frist.</summary>
internal static class FixtureInput
{
    internal static async Task<FixturePacket> ReadAsync(IInput input, IClock clock)
    {
        var buffer = new byte[16385]; var length = 0; var start = clock.Milliseconds;
        try
        {
            while (true)
            {
                var elapsed = clock.Milliseconds - start;
                if (elapsed < 0 || elapsed >= 5000) throw new SmokeFailure(Failure.Input);
                var part = await input.ReadAsync(buffer.Length - length, (int)(5000 - elapsed));
                if (part.TimedOut || part.Data.Length > buffer.Length - length
                    || clock.Milliseconds - start >= 5000 || clock.Milliseconds < start)
                    throw new SmokeFailure(Failure.Input);
                part.Data.CopyTo(buffer, length); length += part.Data.Length;
                if (length > 16384) throw new SmokeFailure(Failure.Input);
                if (part.Eof) break;
                if (part.Data.Length == 0) throw new SmokeFailure(Failure.Input);
            }
            using var doc = System.Text.Json.JsonDocument.Parse(buffer.AsMemory(0, length),
                new System.Text.Json.JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            Exact(root, ["version", "role", "nonce", "expectedMigration", "values"]);
            if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("role").GetString() != "fixture")
                throw new SmokeFailure(Failure.Input);
            var nonce = root.GetProperty("nonce").GetString() ?? "";
            var migration = root.GetProperty("expectedMigration").GetInt32();
            if (!System.Text.RegularExpressions.Regex.IsMatch(nonce, "^[a-f0-9]{32}$") || migration < 1 || migration > 4096)
                throw new SmokeFailure(Failure.Input);
            string[] names = ["adminPassword", "migrationPassword", "runtimePassword", "bffClientSecret", "directoryClientSecret"];
            var values = root.GetProperty("values"); Exact(values, names);
            var secrets = names.Select(name => values.GetProperty(name).GetString() ?? "").ToArray();
            if (secrets.Distinct(StringComparer.Ordinal).Count() != 5
                || secrets.Any(value => !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9_-]{32,128}$")))
                throw new SmokeFailure(Failure.Input);
            return new FixturePacket(secrets, nonce, migration);
        }
        catch { throw new SmokeFailure(Failure.Input); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer); }
    }
    private static void Exact(System.Text.Json.JsonElement value, string[] keys)
    {
        if (value.ValueKind != System.Text.Json.JsonValueKind.Object) throw new SmokeFailure(Failure.Input);
        var actual = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (actual.Length != keys.Length || actual.Distinct(StringComparer.Ordinal).Count() != keys.Length
            || keys.Except(actual, StringComparer.Ordinal).Any()) throw new SmokeFailure(Failure.Input);
    }
}

/// <summary>Reale Ränder werden ausschließlich hinter dem frischen Parentvertrag konstruiert.</summary>
internal sealed class LiveClock : IClock
{
    private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
    public long Milliseconds => watch.ElapsedMilliseconds;
}
internal sealed class StdinInput : IInput
{
    private readonly Stream input = Console.OpenStandardInput();
    public async Task<InputPart> ReadAsync(int maximumBytes, int remainingMs)
    {
        var data = new byte[maximumBytes];
        var read = Task.Run(async () => await input.ReadAsync(data));
        _ = read.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        var count = await read.WaitAsync(TimeSpan.FromMilliseconds(remainingMs));
        return new(data[..count], count == 0);
    }
}
