using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using PostgreSqlStorageSystem;
using StorageSystem;
using Testcontainers.PostgreSql;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>
/// Zwei vollstaendig getrennte API-Hosts auf einer einzigen PostgreSQL-Datenbank.
///
/// Jeder Host besitzt seinen eigenen DI-Container und damit eigene prozesslokale Sperren
/// (<c>BpmnBusinessLogic._engineMutationLock</c>, <c>ServiceTaskJobService._assignmentLock</c>)
/// sowie eine eigene Verbindungsquelle. Was in diesem Aufbau haelt, haelt deshalb in der
/// Datenbank und nicht im Prozess — genau das ist der Nachweis fuer den Mehrprozessbetrieb.
///
/// Echte Betriebssystemprozesse waeren fuer diese Aussage nicht noetig: Der einzige Schutz,
/// den zwei Hosts im selben Testprozess noch teilen koennten, waeren statische Zustaende, und
/// die Engine benutzt dafuer ausschliesslich Instanzfelder der DI-Objekte.
/// </summary>
internal sealed class MultiProcessCluster : IAsyncDisposable
{
    internal const string Schema = "flowzer_multiprocess";

    private readonly PostgreSqlContainer _container;
    private readonly NpgsqlDataSource _dataSource;
    private readonly List<MultiProcessApiHost> _hosts = [];

    private MultiProcessCluster(PostgreSqlContainer container, string connectionString)
    {
        _container = container;
        ConnectionString = connectionString;
        _dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
    }

    internal string ConnectionString { get; }

    /// <summary>Der erste API-Host; entspricht einem Replikat hinter dem Lastverteiler.</summary>
    internal MultiProcessApiHost First { get; private set; } = null!;

    /// <summary>Der zweite API-Host auf derselben Datenbank und demselben Schema.</summary>
    internal MultiProcessApiHost Second { get; private set; } = null!;

    /// <summary>
    /// Startet Container, Migration und beide Hosts. Ohne erreichbaren Docker-Daemon wird
    /// <c>null</c> geliefert; der Aufrufer ueberspringt die Tests dann, statt rot zu werden.
    /// </summary>
    internal static async Task<MultiProcessCluster?> TryStartAsync()
    {
        PostgreSqlContainer container;
        try
        {
            container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await container.StartAsync();
        }
        catch (Exception)
        {
            return null;
        }

        // Das Schema wird genau einmal migriert. Genau so laeuft es im Betrieb: `--migrate`
        // ist ein eigener Schritt vor dem Start der Replikate, kein Teil des Hochlaufs.
        var connectionString = container.GetConnectionString();
        await PostgreSqlMigrator.ApplyAsync(connectionString, Schema);

        var cluster = new MultiProcessCluster(container, connectionString);
        cluster.First = cluster.CreateHost();
        cluster.Second = cluster.CreateHost();
        return cluster;
    }

    /// <summary>
    /// Ein weiterer Host auf derselben Datenbank. <paramref name="schedulers"/> schaltet die
    /// Hintergrunddienste ein; die Standardhosts lassen sie aus, damit die Runden der
    /// Konkurrenztests deterministisch bleiben und ihr Rennen sichtbar wird.
    /// </summary>
    internal MultiProcessApiHost CreateHost(bool schedulers = false, int pollIntervalSeconds = 1)
    {
        var host = new MultiProcessApiHost(ConnectionString, schedulers, pollIntervalSeconds);
        _hosts.Add(host);
        return host;
    }

    internal void Forget(MultiProcessApiHost host) => _hosts.Remove(host);

    /// <summary>Leert alle Laufzeittabellen zwischen zwei Tests.</summary>
    internal async Task ClearAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = string.Join(";", new[]
        {
            "definitions", "definition_binaries", "meta_definitions", "instances",
            "service_task_jobs", "service_task_webhooks",
            "message_subscriptions", "signal_subscriptions", "user_task_drafts",
            "user_task_notification_reads", "user_task_notifications", "user_task_deadlines",
            "user_task_work_states", "user_task_subscriptions", "user_task_assignment_events",
            "runtime_node_events", "ai_runs", "ai_connection_revisions", "ai_connections",
            "timer_subscriptions", "form_section_authoring_drafts", "form_section_versions",
            "form_section_metadata", "form_authoring_drafts", "forms", "form_metadata",
            "form_folders", "workflow_folders", "idempotency_records", "identity_directory_state"
        }.Select(table => $"DELETE FROM {Schema}.{table}"));
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Zaehlt Zeilen einer Laufzeittabelle direkt in der Datenbank.</summary>
    internal async Task<long> CountAsync(string table, string? where = null)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {Schema}.{table}"
                              + (where is null ? string.Empty : $" WHERE {where}");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }

        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>
/// Ein API-Host mit eigenem DI-Container, eigener Verbindungsquelle und signierten
/// synthetischen Identitaeten. Der Verbindungspool bleibt klein, weil mehrere Hosts
/// gleichzeitig gegen denselben Container laufen.
/// </summary>
internal sealed class MultiProcessApiHost : WebApplicationFactory<Program>
{
    private const string Issuer = "https://issuer.test/realms/flowzer";
    private const string Audience = "flowzer-api";

    // Ausschliesslich synthetischer Signaturschluessel fuer den lokalen Test-IdP.
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flowzer-test-only-signing-key-32-bytes-or-more"));

    private readonly string _connectionString;
    private readonly bool _schedulers;
    private readonly int _pollIntervalSeconds;

    internal MultiProcessApiHost(string connectionString, bool schedulers, int pollIntervalSeconds)
    {
        _connectionString = connectionString;
        _schedulers = schedulers;
        _pollIntervalSeconds = pollIntervalSeconds;
    }

    internal IStorageSystem Storage => Services.GetRequiredService<IStorageSystem>();

    internal BpmnBusinessLogic Engine => Services.GetRequiredService<BpmnBusinessLogic>();

    /// <summary>Derselbe Dienst, den der Hintergrunddienst dieses Hosts zyklisch aufruft.</summary>
    internal UserTaskDeadlineService Deadlines => Services.GetRequiredService<UserTaskDeadlineService>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Storage:Provider", "PostgreSql");
        // Ein kleiner Pool je Host: Mehrere Hosts teilen sich die Verbindungsgrenze des Containers.
        builder.UseSetting("Storage:PostgreSql:ConnectionString",
            _connectionString + ";Maximum Pool Size=8");
        builder.UseSetting("Storage:PostgreSql:Schema", MultiProcessCluster.Schema);
        builder.UseSetting("Storage:PostgreSql:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("TimerScheduler:Enabled", _schedulers ? "true" : "false");
        builder.UseSetting("TimerScheduler:PollIntervalSeconds", _pollIntervalSeconds.ToString());
        builder.UseSetting("UserTaskDeadlines:Enabled", _schedulers ? "true" : "false");
        builder.UseSetting("UserTaskDeadlines:PollIntervalSeconds", _pollIntervalSeconds.ToString());
        builder.UseSetting("ServiceTaskWebhooks:Enabled", "false");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting("Authentication:Scheme", "JwtBearer");
        builder.UseSetting("Authentication:JwtBearer:Authority", Issuer);
        builder.UseSetting("Authentication:JwtBearer:Audience", Audience);
        builder.UseSetting("Authentication:JwtBearer:RequiredRole", "access");
        builder.UseSetting("Authentication:JwtBearer:Roles:Operator", "operator");
        builder.UseSetting("Authentication:JwtBearer:Roles:Modeler", "modeler");
        builder.UseSetting("Authentication:JwtBearer:Roles:Worker", "worker");
        builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.ValidIssuers = [Issuer];
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                    new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { SigningKey } });
            }));
    }

    /// <summary>Ein Client mit allen Fachrollen; die Rechtefaelle pruefen eigene Testklassen.</summary>
    internal HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var jwt = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity([
                new Claim("sub", userId.ToString()),
                new Claim("preferred_username", "multi-process"),
                new Claim("roles", "access"),
                new Claim("roles", "operator"),
                new Claim("roles", "modeler"),
                new Claim("roles", "worker")
            ])
        });

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }
}
