using System.Text;
using System.Text.Json;
using Npgsql;

namespace Flowzer.Smoke;

/// <summary>Reine Fakes: keine Verbindung, Prozesse, HTTP-Clients, realen Uhren oder Provider.</summary>
internal static class ContractTests
{
    private static readonly string[] PublicSentinels = Enumerable.Range(0, 5).Select(i => new string((char)('A' + i), 32)).ToArray();
    private static string Document() => JsonSerializer.Serialize(new {
        version = 1, role = "fixture", nonce = new string('a', 32), expectedMigration = 21,
        values = new Dictionary<string, string> {
            ["adminPassword"] = PublicSentinels[0], ["migrationPassword"] = PublicSentinels[1],
            ["runtimePassword"] = PublicSentinels[2], ["bffClientSecret"] = PublicSentinels[3],
            ["directoryClientSecret"] = PublicSentinels[4] }
    });
    private sealed class Clock : IClock { public long Milliseconds { get; set; } }
    private sealed class Input(params InputPart[] parts) : IInput
    {
        private int index;
        public Task<InputPart> ReadAsync(int maximumBytes, int remainingMs) => Task.FromResult(parts[index++]);
    }
    private static Task<FixturePacket> Packet(string? text = null) => FixtureInput.ReadAsync(
        new Input(new InputPart(Encoding.UTF8.GetBytes(text ?? Document()), true)), new Clock());
    private static void Check(bool valid) { if (!valid) throw new InvalidOperationException("CONTRACT_ASSERTION"); }
    private static async Task Reject(Failure kind, Func<Task> action)
    {
        try { await action(); } catch (SmokeFailure error) { Check(error.Kind == kind); return; }
        throw new InvalidOperationException("EXPECTED_REJECTION");
    }
    private sealed class Ports : IFixturePorts
    {
        internal List<string> Calls { get; } = [];
        internal List<byte[]> Inputs { get; } = [];
        internal RoleProof Roles { get; set; } = new("fixture_owner", "fz_migrator", "fz_runtime",
            Rights.Connect | Rights.Read | Rights.Write, Rights.Connect | Rights.Read | Rights.Write | Rights.Ddl);
        internal ChildResult Migration { get; set; } = new(0, [], []);
        internal ReadinessProof Ready { get; set; } = new("PostgreSQL (schema flowzer)", "UpToDate", 0, 21, "Feel", 401);
        internal CleanupProof Cleanup { get; set; } = new(true, 2, new(0, [], []));
        internal bool ThrowOnStart { get; set; }
        public Task<RoleProof> ProvisionAsync(FixturePacket packet) { Calls.Add("roles"); return Task.FromResult(Roles); }
        public Task<ChildResult> MigrateAsync(byte[] input, int remainingMs) { Calls.Add("migrate"); Inputs.Add(input); return Task.FromResult(Migration); }
        public Task StartApiAsync(byte[] input, int remainingMs)
        {
            Calls.Add("api"); Inputs.Add(input);
            if (ThrowOnStart) throw new InvalidOperationException(PublicSentinels[0]);
            return Task.CompletedTask;
        }
        public Task<ReadinessProof> ObserveAsync(int remainingMs) { Calls.Add("observe"); return Task.FromResult(Ready); }
        public Task<CleanupProof> FinishAsync(int remainingMs) { Calls.Add("finish"); return Task.FromResult(Cleanup); }
    }
    internal static async Task<int> RunAsync()
    {
        var failed = 0; var count = 0;
        async Task Case(string id, Func<Task> test)
        {
            count++;
            try { await test(); Console.WriteLine("PASS " + id); }
            catch { failed++; Console.WriteLine("FAIL " + id); } // Keine Exception-/Sentinelwerte ausgeben.
        }
        // Testzweck: Erfolg nur nach vollständigem EOF, auch bei mehreren kurzen Reads.
        await Case("packet-short-reads-eof", async () => {
            var data = Encoding.UTF8.GetBytes(Document());
            var p = await FixtureInput.ReadAsync(new Input(new(data[..5]), new(data[5..], true)), new Clock());
            Check(p.Secrets.Count == 5 && !p.ToString().Contains(PublicSentinels[0]));
        });
        // Testzweck: Fehlendes EOF/Timeout/Übergröße leitet niemals zur Provisionierung über.
        await Case("packet-eof-size-timeout", async () => {
            foreach (var part in new[] { new InputPart([], false), new InputPart([], false, true), new InputPart(new byte[16385], true) })
                await Reject(Failure.Input, () => FixtureInput.ReadAsync(new Input(part), new Clock()));
        });
        // Testzweck: Version, Rolle, Duplikate, Fremdschlüssel und Secrettrennung sind feste Verträge.
        await Case("packet-schema", async () => {
            foreach (var text in new[] { Document().Replace("\"version\":1", "\"version\":2"),
                Document().Replace("fixture", "api"), Document().Replace("\"version\":1", "\"version\":1,\"version\":1"),
                Document().Replace("\"version\":1", "\"version\":1,\"extra\":0"),
                Document().Replace(PublicSentinels[1], PublicSentinels[0]), "{}", "null", "{" })
                await Reject(Failure.Input, () => Packet(text));
        });
        // Testzweck: Verstrichene Gesamtuhr auch nach einem scheinbar erfolgreichen Read durchsetzen.
        await Case("packet-clock", async () => {
            var clock = new Clock(); var input = new AdvancingInput(clock);
            await Reject(Failure.Input, () => FixtureInput.ReadAsync(input, clock));
        });
        // Testzweck: Reihenfolge und genau ein Start pro Runner; kein automatischer Wiederanlauf.
        await Case("sequence-once", async () => {
            var ports = new Ports(); var diagnostic = new FixtureDiagnostics();
            var runner = new FixtureRunner(ports, new Clock(), diagnostic);
            await runner.RunAsync(await Packet());
            Check(ports.Calls.SequenceEqual(new[] { "roles", "migrate", "api", "observe", "finish" }));
            await Reject(Failure.Sequence, () => runner.RunAsync(awaitPacket()));
            FixturePacket awaitPacket() => Packet().GetAwaiter().GetResult();
            Check(ports.Calls.Count == 5);
            // Testzweck: Sichere Diagnose enthält nur feste Phasen/Enums, nie fremde Exception-/Sentineltexte.
            var failed = new FixtureDiagnostics(); failed.Enter(FixturePhase.Migration);
            failed.Record(new InvalidOperationException(PublicSentinels[0]));
            var output = Program.Rejection(failed);
            Check(!output.Contains(PublicSentinels[0]));
            using var report = JsonDocument.Parse(output);
            Check(report.RootElement.GetProperty("diagnostic").GetProperty("failurePhase").GetString() == "Migration");
            Check(report.RootElement.GetProperty("diagnostic").GetProperty("failureCode").GetString() == "Sequence");
        });
        // Testzweck: Gruppenrollen dürfen nicht zusammenfallen oder Runtime-/Migratorrechte ausweiten.
        await Case("roles-least-privilege", async () => {
            foreach (var roles in new[] { new Ports().Roles with { Runtime = "fixture_owner" },
                new Ports().Roles with { RuntimeRights = Rights.Superuser },
                new Ports().Roles with { MigratorRights = Rights.CreateDb } }) {
                var ports = new Ports { Roles = roles, Cleanup = new(true, 0, new(0, [], [])) };
                await Reject(Failure.Role, async () => await new FixtureRunner(ports, new Clock()).RunAsync(await Packet()));
                Check(!ports.Calls.Contains("migrate") && ports.Calls.Last() == "finish");
            }
        });
        // Testzweck: Migrationfehler/Timeout stoppt vor API, ohne Administratorfallback.
        await Case("migration-failure", async () => {
            foreach (var result in new[] { new ChildResult(1, [], []), new ChildResult(0, [], [], TimedOut: true) }) {
                var ports = new Ports { Migration = result, Cleanup = new(true, 1, new(0, [], [])) };
                var diagnostic = new FixtureDiagnostics();
                await Reject(Failure.Output, async () => await new FixtureRunner(ports, new Clock(), diagnostic).RunAsync(await Packet()));
                using var proof = JsonDocument.Parse(Program.Rejection(diagnostic));
                Check(proof.RootElement.GetProperty("diagnostic").GetProperty("failurePhase").GetString() == "Migration");
                Check(proof.RootElement.GetProperty("diagnostic").GetProperty("cleanup").GetString() == "Complete");
                Check(!ports.Calls.Contains("api") && ports.Calls.Last() == "finish");
            }
        });
        // Testzweck: Nur rollenpassende Werte pro Kind; kein Fixtureadmin in Kindpaketen.
        await Case("child-projections", async () => {
            var ports = new Ports(); await new FixtureRunner(ports, new Clock()).RunAsync(await Packet());
            Check(ports.Inputs.Count == 2);
            foreach (var data in ports.Inputs) Check(!Encoding.UTF8.GetString(data).Contains(PublicSentinels[0]));
            using var migration = JsonDocument.Parse(ports.Inputs[0]); using var api = JsonDocument.Parse(ports.Inputs[1]);
            Check(migration.RootElement.GetProperty("role").GetString() == "migrate");
            Check(api.RootElement.GetProperty("values").EnumerateObject().Count() == 3);
        });
        // Testzweck: Npgsql-Builder nur als reine Datenoperation, keinerlei Verbindungsinitialisierung.
        await Case("connection-builder", () => {
            var builder = new NpgsqlConnectionStringBuilder(FixtureRunner.Connection("fz_runtime", PublicSentinels[2]));
            Check(builder.Host == "/flowzer-socket" && builder.Username == "fz_runtime" && !builder.Pooling);
            Check(builder.Password == PublicSentinels[2] && !builder.IncludeErrorDetail); return Task.CompletedTask;
        });
        // Testzweck: Healthy allein reicht nicht; jede sichere Readiness-/Auth-/Artefaktkomponente zählt.
        await Case("readiness-components", async () => {
            var original = new Ports().Ready;
            foreach (var proof in new[] { original with { Provider = "Filesystem" }, original with { State = "Pending" },
                original with { Pending = 1 }, original with { Migration = 20 }, original with { ExpressionEngine = "Simple" },
                original with { AnonymousStatus = 200 }, original with { FrameworkBound = false }, original with { NativeAssetsBound = false } })
                await Reject(Failure.Readiness, async () => await new FixtureRunner(new Ports { Ready = proof }, new Clock()).RunAsync(await Packet()));
        });
        // Testzweck: Beide Kanäle, einschließlich Base64 und doppeltem Base64, niemals ungeprüft weiterreichen.
        await Case("output-sentinel-encodings", async () => {
            var packet = await Packet(); var raw = PublicSentinels[2];
            foreach (var value in new[] { raw, Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)))) })
                foreach (var stderr in new[] { false, true })
                    await Reject(Failure.Output, () => { OutputGuard.Verify(new(0, stderr ? [] : Encoding.UTF8.GetBytes(value), stderr ? Encoding.UTF8.GetBytes(value) : []), packet); return Task.CompletedTask; });
        });
        // Testzweck: Überlauf, unvollständige Kanäle und nichtterminaler Kindzustand sind kein Erfolg.
        await Case("output-bounds-and-terminal", async () => {
            foreach (var result in new[] { new ChildResult(0, new byte[65537], []), new ChildResult(0, [], [], Eof: false),
                new ChildResult(0, [], [], Terminal: false), new ChildResult(0, [], [], TimedOut: true) })
                await Reject(Failure.Output, async () => OutputGuard.Verify(result, await Packet()));
        });
        // Testzweck: Unklarer Cleanup oder falsche Kinderzahl überstimmt einen sonst grünen Ablauf.
        await Case("cleanup-proof", async () => {
            foreach (var proof in new[] { new CleanupProof(false, 2, new(0, [], [])), new CleanupProof(true, 3, new(0, [], [])),
                new CleanupProof(true, 2, new(0, [], [], Eof: false)) })
                await Reject(Failure.Cleanup, async () => await new FixtureRunner(new Ports { Cleanup = proof }, new Clock()).RunAsync(await Packet()));
        });
        // Testzweck: Auch ein unbekannter partieller APIstart ruft genau den eigenen Finishrand auf.
        await Case("partial-start-fails-safe", async () => {
            var ports = new Ports { ThrowOnStart = true };
            await Reject(Failure.Sequence, async () => await new FixtureRunner(ports, new Clock()).RunAsync(await Packet()));
            Check(ports.Calls.Last() == "finish");
            // Testzweck: Primärer Fehler bleibt sichtbar, auch wenn der unabhängige Cleanup ebenfalls scheitert.
            var diagnostic = new FixtureDiagnostics();
            var twoFaults = new Ports { ThrowOnStart = true, Cleanup = new(false, 2, new(0, [], [])) };
            await Reject(Failure.Cleanup, async () => await new FixtureRunner(twoFaults, new Clock(), diagnostic).RunAsync(await Packet()));
            using var proof = JsonDocument.Parse(Program.Rejection(diagnostic));
            var report = proof.RootElement.GetProperty("diagnostic");
            Check(report.GetProperty("failurePhase").GetString() == "ApiStart"
                && report.GetProperty("failureCode").GetString() == "Sequence"
                && report.GetProperty("cleanup").GetString() == "Unknown");
            Check(!Program.Rejection(diagnostic).Contains(PublicSentinels[0]));
        });
        // Testzweck: Der normale Einstieg ist bewusst noch geschlossen; keine Fake-Runtimeabnahme.
        await Case("entrypoint-closed", () => {
            Check(Program.IsContractMode(["--contract-test"]) && !Program.IsContractMode([])
                && !Program.IsContractMode(["--contract-test", "extra"])); return Task.CompletedTask;
        });
        Console.WriteLine($"CONTRACTS total={count} passed={count - failed} failed={failed} skipped=0");
        return failed == 0 ? 0 : 1;
    }
    private sealed class AdvancingInput(Clock clock) : IInput
    {
        public Task<InputPart> ReadAsync(int maximumBytes, int remainingMs)
        { clock.Milliseconds += 5001; return Task.FromResult(new InputPart(Encoding.UTF8.GetBytes(Document()), true)); }
    }
}
