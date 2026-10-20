namespace Flowzer.Smoke;

internal enum Failure { Input, Role, Sequence, Output, Readiness, Cleanup }
internal sealed class SmokeFailure(Failure kind) : Exception("FLOWZER_SMOKE_" + kind)
{
    internal Failure Kind { get; } = kind;
}
internal enum FixturePhase { Input, Binding, Provision, Migration, ApiStart, Readiness, Cleanup, Complete }
internal enum FixtureCleanup { NotStarted, Complete, Unknown }
/// <summary>Nur feste Phasen/Enums beobachten; Fach-/Signal-/Timeoutregeln bleiben unverändert.</summary>
internal sealed class FixtureDiagnostics
{
    private FixturePhase phase = FixturePhase.Input;
    private FixturePhase? failurePhase;
    private Failure? failureCode;
    internal FixtureCleanup Cleanup { get; set; } = FixtureCleanup.NotStarted;
    internal void Enter(FixturePhase value) => phase = value;
    internal void Record(Exception error)
    {
        if (failurePhase is not null) return;
        failurePhase = phase;
        failureCode = error is SmokeFailure failure && Enum.IsDefined(failure.Kind) ? failure.Kind : Failure.Sequence;
    }
    internal object Snapshot() => new {
        phase = phase.ToString(), failurePhase = failurePhase?.ToString(),
        failureCode = failureCode?.ToString(), cleanup = Cleanup.ToString()
    };
}
internal interface IClock { long Milliseconds { get; } }
internal readonly record struct InputPart(byte[] Data, bool Eof = false, bool TimedOut = false);
/// <summary>Der spätere Adapter muss jede einzelne Leseoperation innerhalb remainingMs beenden.</summary>
internal interface IInput { Task<InputPart> ReadAsync(int maximumBytes, int remainingMs); }
internal sealed class FixturePacket(string[] secrets, string nonce, int expectedMigration)
{
    internal IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly((string[])secrets.Clone());
    internal string Nonce { get; } = nonce;
    internal int ExpectedMigration { get; } = expectedMigration;
    public override string ToString() => "FixturePacket(REDACTED)";
}
[Flags]
internal enum Rights { Connect = 1, Read = 2, Write = 4, Ddl = 8, Superuser = 16, CreateRole = 32, CreateDb = 64, Replication = 128, BypassRls = 256 }
internal sealed record RoleProof(string Admin, string Migrator, string Runtime, Rights RuntimeRights, Rights MigratorRights);
internal sealed record ChildResult(int Code, byte[] Stdout, byte[] Stderr, bool Eof = true, bool TimedOut = false, bool Terminal = true);
internal sealed record ReadinessProof(string Provider, string State, int Pending, int Migration,
    string ExpressionEngine, int AnonymousStatus, bool FrameworkBound = true, bool NativeAssetsBound = true);
internal sealed record CleanupProof(bool Empty, int Children, ChildResult ApiOutput);
/// <summary>Ausschließlich injizierte Grenzen; diese Sourcephase enthält keine Liveimplementierung.</summary>
internal interface IFixturePorts
{
    Task<RoleProof> ProvisionAsync(FixturePacket packet);
    Task<ChildResult> MigrateAsync(byte[] input, int remainingMs);
    Task StartApiAsync(byte[] input, int remainingMs);
    Task<ReadinessProof> ObserveAsync(int remainingMs);
    Task<CleanupProof> FinishAsync(int remainingMs);
}

/// <summary>Feste rollengetrennte Sequenz; jeder weitere Durchlauf benötigt einen neuen Runner.</summary>
internal sealed class FixtureRunner(IFixturePorts ports, IClock clock, FixtureDiagnostics? diagnostic = null)
{
    private bool used;
    private readonly FixtureDiagnostics diagnostic = diagnostic ?? new();
    internal async Task RunAsync(FixturePacket packet)
    {
        if (used) throw new SmokeFailure(Failure.Sequence);
        used = true;
        var start = clock.Milliseconds; var requested = 0;
        int Remaining()
        {
            var value = 120000 - (clock.Milliseconds - start);
            if (value <= 0 || value > 120000) throw new SmokeFailure(Failure.Sequence);
            return (int)value;
        }
        try
        {
            _ = Remaining();
            diagnostic.Enter(FixturePhase.Provision);
            var role = await ports.ProvisionAsync(packet);
            if (role.Admin != "fixture_owner" || role.Migrator != "fz_migrator" || role.Runtime != "fz_runtime"
                || role.RuntimeRights != (Rights.Connect | Rights.Read | Rights.Write)
                || role.MigratorRights != (Rights.Connect | Rights.Read | Rights.Write | Rights.Ddl))
                throw new SmokeFailure(Failure.Role);
            diagnostic.Enter(FixturePhase.Migration);
            requested = 1;
            OutputGuard.Verify(await ports.MigrateAsync(Input(packet, false), Remaining()), packet);
            diagnostic.Enter(FixturePhase.ApiStart);
            requested = 2; // Vor dem Aufruf: auch ein partiell gestartetes Kind gehört zum Cleanup.
            await ports.StartApiAsync(Input(packet, true), Remaining());
            diagnostic.Enter(FixturePhase.Readiness);
            var ready = await ports.ObserveAsync(Remaining());
            if (ready.Provider != "PostgreSQL (schema flowzer)" || ready.State != "UpToDate" || ready.Pending != 0
                || ready.Migration != packet.ExpectedMigration || ready.ExpressionEngine != "Feel"
                || ready.AnonymousStatus != 401 || !ready.FrameworkBound || !ready.NativeAssetsBound)
                throw new SmokeFailure(Failure.Readiness);
            _ = Remaining();
        }
        catch (SmokeFailure error) { diagnostic.Record(error); throw; }
        catch (Exception error) { diagnostic.Record(error); throw new SmokeFailure(Failure.Sequence); }
        finally
        {
            // Cleanup hat eine eigene feste Frist, auch wenn die Fachfrist bereits verstrichen ist.
            diagnostic.Enter(FixturePhase.Cleanup); diagnostic.Cleanup = FixtureCleanup.Unknown;
            try
            {
                var proof = await ports.FinishAsync(10000);
                if (!proof.Empty || proof.Children != requested) throw new SmokeFailure(Failure.Cleanup);
                OutputGuard.Verify(proof.ApiOutput, packet);
                diagnostic.Cleanup = FixtureCleanup.Complete;
            }
            catch (Exception error) { diagnostic.Record(error); throw new SmokeFailure(Failure.Cleanup); }
        }
    }
    internal static string Connection(string role, string secret) => new Npgsql.NpgsqlConnectionStringBuilder {
        Host = "/flowzer-socket", Database = "flowzer_smoke", Username = role, Password = secret,
        Pooling = false, Timeout = 5, CommandTimeout = 5, IncludeErrorDetail = false
    }.ConnectionString;

    private static byte[] Input(FixturePacket packet, bool api)
    {
        var values = api ? new Dictionary<string, string> {
            ["runtimeConnectionString"] = Connection("fz_runtime", packet.Secrets[2]),
            ["bffClientSecret"] = packet.Secrets[3], ["directoryClientSecret"] = packet.Secrets[4]
        } : new Dictionary<string, string> { ["migrationConnectionString"] = Connection("fz_migrator", packet.Secrets[1]) };
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { version = 1, role = api ? "api" : "migrate", values });
    }
}

/// <summary>Vollständige begrenzte Kanäle prüfen, bevor irgendeine Erfolgskategorie ausgegeben wird.</summary>
internal static class OutputGuard
{
    internal static void Verify(ChildResult result, FixturePacket packet)
    {
        if (result.Code != 0 || !result.Eof || result.TimedOut || !result.Terminal
            || (long)result.Stdout.Length + result.Stderr.Length > 65536) throw new SmokeFailure(Failure.Output);
        var combined = result.Stdout.Concat(result.Stderr).ToArray();
        foreach (var secret in packet.Secrets)
        {
            var value = System.Text.Encoding.UTF8.GetBytes(secret);
            for (var level = 0; level < 3; level++)
            {
                if (combined.AsSpan().IndexOf(value) >= 0) throw new SmokeFailure(Failure.Output);
                value = System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(value));
            }
        }
    }
}

/// <summary>Nur hinter der gebundenen Container-CLI; in allen jetzigen Tests durch IFixturePorts-Fakes ersetzt.</summary>
internal sealed class LiveFixturePorts : IFixturePorts
{
    private readonly FixturePacket packet;
    private readonly List<OwnedChild> children = [];
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(130));
    private readonly string runtime;
    private readonly long boundUntil;
    private OwnedChild? api;
    private bool provisioned;
    private sealed record OwnedChild(System.Diagnostics.Process Process, Task<byte[]> Out, Task<byte[]> Err);

    internal LiveFixturePorts(FixturePacket packet, string apiSha, string feederSha)
    {
        this.packet = packet;
        if (!long.TryParse(Environment.GetEnvironmentVariable("FLOWZER_SMOKE_BOUND_UNTIL"), out boundUntil))
            throw new SmokeFailure(Failure.Sequence);
        Fresh();
        runtime = Environment.GetEnvironmentVariable("FLOWZER_SMOKE_RUNTIME") ?? "";
        var native = Environment.GetEnvironmentVariable("FLOWZER_SMOKE_NATIVE_FILE") ?? "";
        var nativeSha = Environment.GetEnvironmentVariable("FLOWZER_SMOKE_NATIVE_SHA") ?? "";
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                != System.Runtime.InteropServices.Architecture.X64 || Environment.Version.ToString() != runtime)
            throw new SmokeFailure(Failure.Sequence);
        Pin("/app/WebApiEngine.dll", apiSha);
        Pin(typeof(Program).Assembly.Location, feederSha);
        if (!native.StartsWith("/app/", StringComparison.Ordinal) || !native.EndsWith(".so", StringComparison.Ordinal)
            || System.IO.Path.GetFullPath(native) != native) throw new SmokeFailure(Failure.Sequence);
        Pin(native, nativeSha);
        // SDK- und Laufzeitversion sind getrennt: tatsächliche Runtimeauflösung prüfen, nicht Patchgleichheit erfinden.
        if (new FileInfo("/app/WebApiEngine.runtimeconfig.json").Length > 16384) throw new SmokeFailure(Failure.Sequence);
        using var cfg = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes("/app/WebApiEngine.runtimeconfig.json"));
        var options = cfg.RootElement.GetProperty("runtimeOptions");
        var frameworks = options.GetProperty("frameworks").EnumerateArray().ToArray();
        if (frameworks.Length != 2 || !frameworks.Select(f => f.GetProperty("name").GetString()).ToHashSet()
                .SetEquals(["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"])) throw new SmokeFailure(Failure.Sequence);
        var current = Version.Parse(runtime);
        foreach (var framework in frameworks)
        {
            var declared = Version.Parse(framework.GetProperty("version").GetString() ?? "");
            var name = framework.GetProperty("name").GetString()!;
            var installed = Directory.GetDirectories("/usr/share/dotnet/shared/" + name).Select(System.IO.Path.GetFileName).ToArray();
            if (installed.Length != 1 || installed[0] != runtime || declared.Major != current.Major
                || declared.Minor != current.Minor || declared > current) throw new SmokeFailure(Failure.Sequence);
            if (options.TryGetProperty("rollForward", out var roll)
                && (roll.GetString() == "Disable" ? declared != current : roll.GetString() is not ("Minor" or "LatestPatch")))
                throw new SmokeFailure(Failure.Sequence);
        }
    }
    private void Fresh()
    {
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= boundUntil) throw new SmokeFailure(Failure.Sequence);
    }
    private static void Pin(string file, string digest)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(digest, "^[a-f0-9]{64}$")) throw new SmokeFailure(Failure.Sequence);
        var info = new FileInfo(file);
        if (!info.Exists || info.Length > 128 * 1024 * 1024 || info.LinkTarget is not null) throw new SmokeFailure(Failure.Sequence);
        using var input = File.OpenRead(file);
        if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).Equals(digest, StringComparison.OrdinalIgnoreCase))
            throw new SmokeFailure(Failure.Sequence);
    }
    private async Task<Npgsql.NpgsqlConnection> OpenAsync(string role, string secret, string database)
    {
        Fresh();
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(FixtureRunner.Connection(role, secret)) { Database = database };
        var connection = new Npgsql.NpgsqlConnection(builder.ConnectionString);
        try { await connection.OpenAsync(lifetime.Token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    private async Task Sql(Npgsql.NpgsqlConnection connection, string text)
    {
        Fresh();
        await using var command = new Npgsql.NpgsqlCommand(text, connection) { CommandTimeout = 5 };
        await command.ExecuteNonQueryAsync(lifetime.Token);
    }
    public async Task<RoleProof> ProvisionAsync(FixturePacket value)
    {
        Fresh();
        if (provisioned || value != packet) throw new SmokeFailure(Failure.Sequence);
        provisioned = true;
        Npgsql.NpgsqlConnection? admin = null;
        // Ausschließlich Readiness des eigenen frischen PG: niemals SQL-/Migrationsaktionen wiederholen.
        for (var i = 0; i < 50 && admin is null; i++)
        {
            try { admin = await OpenAsync("fixture_owner", packet.Secrets[0], "postgres"); }
            catch (Npgsql.NpgsqlException) { await Task.Delay(100, lifetime.Token); }
        }
        if (admin is null) throw new SmokeFailure(Failure.Role);
        await using (admin)
        {
            foreach (var (role, secret) in new[] { ("fz_migrator", packet.Secrets[1]), ("fz_runtime", packet.Secrets[2]) })
            {
                // PostgreSQL-Utility-DDL akzeptiert keine Bindparameter an PASSWORD. format(%L) quotiert serverseitig;
                // nur das Passwort ist ein Parameter, Bezeichner/SQL sind fest. Keine Exception-/SQLausgabe.
                await using var format = new Npgsql.NpgsqlCommand(
                    "SELECT format('CREATE ROLE " + role + " LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L', @p)", admin);
                format.Parameters.AddWithValue("p", secret);
                var ddl = (string?)await format.ExecuteScalarAsync(lifetime.Token) ?? throw new SmokeFailure(Failure.Role);
                await Sql(admin, ddl);
            }
            await Sql(admin, "CREATE DATABASE flowzer_smoke OWNER fz_migrator ENCODING 'UTF8'");
        }
        await using var database = await OpenAsync("fixture_owner", packet.Secrets[0], "flowzer_smoke");
        await Sql(database, "REVOKE ALL ON DATABASE flowzer_smoke FROM PUBLIC; GRANT CONNECT ON DATABASE flowzer_smoke TO fz_runtime; REVOKE ALL ON SCHEMA public FROM PUBLIC");
        await Sql(database, "CREATE SCHEMA flowzer AUTHORIZATION fz_migrator; CREATE TABLE flowzer.schema_migrations (version integer PRIMARY KEY, name text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now()); ALTER TABLE flowzer.schema_migrations OWNER TO fz_migrator");
        // Derselbe Fachvertrag wie deploy/postgresql/02: Historie bleibt readonly, auch nach Defaultprivilegien.
        await Sql(database, "GRANT USAGE ON SCHEMA flowzer TO fz_runtime; GRANT SELECT ON flowzer.schema_migrations TO fz_runtime; ALTER DEFAULT PRIVILEGES FOR ROLE fz_migrator IN SCHEMA flowzer GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO fz_runtime; ALTER DEFAULT PRIVILEGES FOR ROLE fz_migrator IN SCHEMA flowzer GRANT USAGE,SELECT ON SEQUENCES TO fz_runtime");
        await using var query = new Npgsql.NpgsqlCommand("SELECT rolname,rolsuper,rolcreatedb,rolcreaterole,rolreplication,rolbypassrls FROM pg_roles WHERE rolname IN ('fz_runtime','fz_migrator') ORDER BY rolname", database);
        await using var rows = await query.ExecuteReaderAsync(lifetime.Token);
        var rights = new Dictionary<string, Rights>();
        while (await rows.ReadAsync(lifetime.Token))
        {
            var flags = Rights.Connect | Rights.Read | Rights.Write;
            if (rows.GetString(0) == "fz_migrator") flags |= Rights.Ddl;
            foreach (var (column, flag) in new[] { (1, Rights.Superuser), (2, Rights.CreateDb), (3, Rights.CreateRole), (4, Rights.Replication), (5, Rights.BypassRls) })
                if (rows.GetBoolean(column)) flags |= flag;
            rights.Add(rows.GetString(0), flags);
        }
        if (rights.Count != 2) throw new SmokeFailure(Failure.Role);
        return new("fixture_owner", "fz_migrator", "fz_runtime", rights["fz_runtime"], rights["fz_migrator"]);
    }
    private async Task<OwnedChild> StartChild(byte[] input, int remainingMs, bool migration)
    {
        Fresh();
        var start = new System.Diagnostics.ProcessStartInfo("/usr/share/dotnet/dotnet") {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = "/app"
        };
        start.ArgumentList.Add("/app/WebApiEngine.dll"); start.ArgumentList.Add("--secrets-stdin");
        if (migration) start.ArgumentList.Add("--migrate");
        start.Environment.Clear();
        foreach (var pair in new Dictionary<string, string> {
            ["PATH"] = "/usr/bin:/bin", ["HOME"] = "/tmp/home", ["TMPDIR"] = "/tmp",
            ["ASPNETCORE_URLS"] = "http://127.0.0.1:5111", ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Storage__Provider"] = "PostgreSql", ["Storage__PostgreSql__Schema"] = "flowzer",
            ["Storage__PostgreSql__ApplyMigrationsOnStartup"] = "false",
            ["Authentication__Scheme"] = "JwtBearer", ["Authentication__JwtBearer__Authority"] = "https://flowzer-smoke.invalid/realms/synthetic",
            ["Authentication__JwtBearer__Audience"] = "flowzer-smoke-api", ["Authentication__JwtBearer__RequireHttpsMetadata"] = "true",
            ["Authentication__JwtBearer__RequiredRole"] = "access", ["Authentication__JwtBearer__LegacyPermissiveRoles"] = "false",
            ["Authentication__JwtBearer__Roles__Modeler"] = "modeler", ["Authentication__JwtBearer__Roles__Operator"] = "operator",
            ["Authentication__JwtBearer__Roles__Worker"] = "worker", ["Authentication__JwtBearer__Roles__AiConnectionUser"] = "ai-user",
            ["Authentication__JwtBearer__Roles__AiConnectionManager"] = "ai-manager", ["IdentityDirectory__Enabled"] = "false",
            ["TimerScheduler__Enabled"] = "false", ["UserTaskDeadlines__Enabled"] = "false", ["AiExecution__Enabled"] = "false",
            ["Ai__AllowCloudProviders"] = "false", ["Ai__AllowLocalEndpoints"] = "false", ["ServiceTaskWebhooks__Enabled"] = "false",
            ["Connectors__Http__Enabled"] = "false", ["Connectors__Email__Enabled"] = "false", ["Observability__Enabled"] = "false"
        }) start.Environment.Add(pair.Key, pair.Value);
        var process = System.Diagnostics.Process.Start(start) ?? throw new SmokeFailure(Failure.Sequence);
        var owned = new OwnedChild(process, ReadBounded(process.StandardOutput.BaseStream), ReadBounded(process.StandardError.BaseStream));
        children.Add(owned); // Vor Write/EOF vermerken: partieller Start muss ebenfalls abgeschlossen werden.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(Math.Min(remainingMs, 10000));
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(input, deadline.Token);
            await process.StandardInput.BaseStream.FlushAsync(deadline.Token);
        }
        finally { process.StandardInput.Close(); System.Security.Cryptography.CryptographicOperations.ZeroMemory(input); }
        return owned;
    }
    private async Task<byte[]> ReadBounded(Stream stream)
    {
        using var output = new MemoryStream(); var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, lifetime.Token);
            if (count == 0) return output.ToArray();
            if (output.Length + count > 65536) throw new SmokeFailure(Failure.Output);
            output.Write(buffer, 0, count);
        }
    }
    private async Task<ChildResult> Capture(OwnedChild child, int milliseconds)
    {
        await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(milliseconds));
        var channels = await Task.WhenAll(child.Out, child.Err).WaitAsync(TimeSpan.FromMilliseconds(milliseconds));
        return new(child.Process.ExitCode, channels[0], channels[1]);
    }
    public async Task<ChildResult> MigrateAsync(byte[] input, int remainingMs) => await Capture(await StartChild(input, remainingMs, true), remainingMs);
    public async Task StartApiAsync(byte[] input, int remainingMs) { api = await StartChild(input, remainingMs, false); }
    public async Task<ReadinessProof> ObserveAsync(int remainingMs)
    {
        if (api is null || api.Process.HasExited) throw new SmokeFailure(Failure.Readiness);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) {
            BaseAddress = new Uri("http://127.0.0.1:5111"), Timeout = TimeSpan.FromSeconds(2), MaxResponseContentBufferSize = 65536
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(Math.Min(remainingMs, 30000));
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                using var health = await http.GetAsync("/Health/ready", deadline.Token);
                if (health.StatusCode != System.Net.HttpStatusCode.OK) { await Task.Delay(100, deadline.Token); continue; }
                using var document = System.Text.Json.JsonDocument.Parse(await health.Content.ReadAsByteArrayAsync(deadline.Token));
                var result = document.RootElement.GetProperty("result"); var details = result.GetProperty("details");
                if (!document.RootElement.GetProperty("successful").GetBoolean() || result.GetProperty("status").GetString() != "Healthy"
                    || result.GetProperty("storage").GetString() != "Ready") throw new SmokeFailure(Failure.Readiness);
                using var anonymous = await http.GetAsync("/Definition", deadline.Token);
                await VerifyRuntimeAcl();
                return new(details.GetProperty("storageProvider").GetString() ?? "", details.GetProperty("migrationState").GetString() ?? "",
                    details.GetProperty("pendingMigrationCount").GetInt32(), details.GetProperty("expectedMigrationVersion").GetInt32(),
                    details.GetProperty("expressionEngine").GetString() ?? "", (int)anonymous.StatusCode,
                    Environment.Version.ToString() == runtime, true);
            }
            catch (HttpRequestException) { await Task.Delay(100, deadline.Token); }
        }
        throw new SmokeFailure(Failure.Readiness);
    }
    private async Task VerifyRuntimeAcl()
    {
        await using var connection = await OpenAsync("fz_runtime", packet.Secrets[2], "flowzer_smoke");
        const string sql = "SELECT current_user='fz_runtime' AND NOT has_schema_privilege('fz_runtime','flowzer','CREATE') AND has_table_privilege('fz_runtime','flowzer.schema_migrations','SELECT') AND NOT has_table_privilege('fz_runtime','flowzer.schema_migrations','INSERT') AND (SELECT max(version) FROM flowzer.schema_migrations)=@version";
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("version", packet.ExpectedMigration);
        if (!Equals(await command.ExecuteScalarAsync(lifetime.Token), true)) throw new SmokeFailure(Failure.Role);
    }
    public async Task<CleanupProof> FinishAsync(int remainingMs)
    {
        var empty = true; var start = System.Diagnostics.Stopwatch.StartNew(); ChildResult last = new(0, [], []);
        foreach (var child in children)
        {
            try
            {
                if (!child.Process.HasExited && Kill(child.Process.Id, 15) != 0) throw new SmokeFailure(Failure.Cleanup);
                var left = Math.Max(1, remainingMs - (int)start.ElapsedMilliseconds);
                var result = await Capture(child, left);
                OutputGuard.Verify(result, packet);
                if (child == api) last = result;
            }
            catch
            {
                empty = false;
                // Ausschließlich die eigenen Kinder im exklusiven Container; KILL ist kein positiver Abschluss.
                try { if (!child.Process.HasExited) child.Process.Kill(entireProcessTree: true); await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
                catch { }
            }
            finally { child.Process.Dispose(); }
        }
        lifetime.Cancel(); lifetime.Dispose();
        return new(empty, children.Count, last);
    }
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int process, int signal);
}
