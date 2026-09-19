using System.Diagnostics;
using System.Net;
using System.Text;
using FluentAssertions;

namespace WebApiEngine.Tests;

/// <summary>
/// Prueft das Startargument <c>--check-config</c> so, wie der Betrieb es benutzt: als eigener
/// Prozess ueber den <c>Program</c>-Einstieg. Damit sind Argumentauswertung, Hostbau,
/// Options-Validierung, die Tabelle und der Exit-Code gemeinsam belegt.
/// </summary>
[NonParallelizable]
public class ConfigurationCheckIntegrationTest
{
    private DiscoveryEndpointStub? _authority;
    private string? _storageRoot;

    [SetUp]
    public void StartAuthorityStub()
    {
        _authority = DiscoveryEndpointStub.Start();
        _storageRoot = Path.Combine(Path.GetTempPath(), "flowzer-check-config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);
    }

    [TearDown]
    public void StopAuthorityStub()
    {
        _authority?.Dispose();
        _authority = null;
        if (_storageRoot is not null && Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    // Testzweck: Eine vollstaendige, gueltige Konfiguration wird als Tabelle ausgegeben und
    // endet mit Exit-Code 0.
    [Test]
    public async Task CheckConfig_ShouldReportTableAndExitZero_ForValidConfiguration()
    {
        var (exitCode, output) = await RunCheckAsync(ValidConfiguration());

        output.Should().Contain("Bereich").And.Contain("Zustand").And.Contain("Hinweis");
        output.Should().Contain("Ablage").And.Contain("Authentifizierung").And.Contain("Webhook-Ziele");
        output.Should().Contain("keine Beanstandungen");
        exitCode.Should().Be(0, "die Ausgabe war:\n{0}", output);
    }

    // Testzweck: Eine unerreichbare Verbindungszeichenfolge meldet den Bereich Ablage als Fehler
    // und endet mit Exit-Code 1 - ohne die Verbindungszeichenfolge oder ein Passwort auszugeben.
    [Test]
    public async Task CheckConfig_ShouldNameStorageAndExitOne_ForUnreachableDatabase()
    {
        var settings = ValidConfiguration();
        settings["Storage__Provider"] = "PostgreSql";
        // Syntaktisch gueltig, aber niemand lauscht dort: genau der Betriebsfall
        // "falsche Adresse / Datenbank nicht erreichbar".
        settings["Storage__PostgreSql__ConnectionString"] =
            "Host=127.0.0.1;Port=1;Database=flowzer;Username=flowzer;Password=streng-geheim";
        settings["Storage__PostgreSql__Schema"] = "flowzer";

        var (exitCode, output) = await RunCheckAsync(settings);

        exitCode.Should().Be(1, "die Ausgabe war:\n{0}", output);
        output.Should().Contain("Ablage").And.Contain("Fehler");
        output.Should().NotContain("streng-geheim");
        output.Should().NotContain("Password=");
    }

    // Testzweck: Aktivierte Webhooks ohne freigegebenes Ziel sind kein Startfehler, aber eine
    // benannte Warnung mit Exit-Code 2.
    [Test]
    public async Task CheckConfig_ShouldWarnAndExitTwo_WhenWebhooksHaveNoAllowList()
    {
        var settings = ValidConfiguration();
        settings.Remove("ServiceTaskWebhooks__AllowedHosts__0");

        var (exitCode, output) = await RunCheckAsync(settings);

        exitCode.Should().Be(2, "die Ausgabe war:\n{0}", output);
        output.Should().Contain("Webhook-Ziele").And.Contain("Warnung");
        output.Should().Contain("ohne freigegebenes Ziel");
    }

    // Testzweck: Ein Abschnitt, der schon beim Registrieren der Dienste scheitert, erscheint als
    // benannte Fehlerzeile mit Exit-Code 1 - nicht als roher Abbruch des Prozesses.
    [Test]
    public async Task CheckConfig_ShouldNameTheSection_WhenAnOptionBreaksHostRegistration()
    {
        var settings = ValidConfiguration();
        settings["RateLimiting__PermitLimit"] = "0";

        var (exitCode, output) = await RunCheckAsync(settings);

        exitCode.Should().Be(1, "die Ausgabe war:\n{0}", output);
        output.Should().Contain("Grenzen").And.Contain("RateLimiting:PermitLimit");
        output.Should().NotContain("Unhandled exception");
    }

    private Dictionary<string, string> ValidConfiguration() => new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
        ["FLOWZER_STORAGE_ROOT"] = _storageRoot!,
        ["Storage__Provider"] = "Filesystem",
        ["Authentication__Scheme"] = "JwtBearer",
        ["Authentication__JwtBearer__Authority"] = _authority!.Authority,
        ["Authentication__JwtBearer__Audience"] = "flowzer-api",
        ["Authentication__JwtBearer__RequireHttpsMetadata"] = "false",
        ["Authentication__JwtBearer__RequiredRole"] = "flowzer-access",
        ["ServiceTaskWebhooks__Enabled"] = "true",
        ["ServiceTaskWebhooks__AllowedHosts__0"] = "worker.example.invalid"
    };

    private static async Task<(int ExitCode, string Output)> RunCheckAsync(IDictionary<string, string> settings)
    {
        var assembly = ResolveWebApiEngineAssembly();
        var startInfo = new ProcessStartInfo(ResolveDotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(assembly)!
        };
        startInfo.ArgumentList.Add(assembly);
        startInfo.ArgumentList.Add("--check-config");
        foreach (var (key, value) in settings)
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Der Pruefprozess liess sich nicht starten.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);

        var combined = new StringBuilder()
            .Append(await standardOutput)
            .Append(await standardError)
            .ToString();
        return (process.ExitCode, combined);
    }

    /// <summary>
    /// Die Web-API ist Projektreferenz dieses Testprojekts und wird deshalb in derselben
    /// Konfiguration gebaut. Fehlt das Ergebnis trotzdem, wird uebersprungen statt rot gemeldet.
    /// </summary>
    private static string ResolveWebApiEngineAssembly()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "core-engine.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            Assert.Ignore("Wurzel des Repositories nicht gefunden; --check-config kann nicht als Prozess geprueft werden.");
        }

        // .../src/WebApiEngine.Tests/bin/<Konfiguration>/<Zielframework>/
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var targetFramework = baseDirectory.Name;
        var configuration = baseDirectory.Parent!.Name;
        var assembly = Path.Combine(current!.FullName, "src", "WebApiEngine", "bin", configuration, targetFramework, "WebApiEngine.dll");
        if (!File.Exists(assembly))
        {
            Assert.Ignore($"{assembly} fehlt; --check-config kann nicht als Prozess geprueft werden.");
        }

        return assembly;
    }

    private static string ResolveDotnetHost()
    {
        var host = Environment.ProcessPath;
        if (host is not null && Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return host;
        }

        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "dotnet";
    }

    /// <summary>
    /// Minimaler OIDC-Discovery-Endpunkt. Die Pruefung fragt ihn nur mit HEAD ab; mehr braucht
    /// sie nicht, um "Authority antwortet" von "Authority nicht erreichbar" zu unterscheiden.
    /// </summary>
    private sealed class DiscoveryEndpointStub : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stopping = new();

        private DiscoveryEndpointStub(HttpListener listener, string authority)
        {
            _listener = listener;
            Authority = authority;
            _ = Task.Run(AcceptAsync);
        }

        public string Authority { get; }

        public static DiscoveryEndpointStub Start()
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var port = Random.Shared.Next(23000, 42000);
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    listener.Start();
                    return new DiscoveryEndpointStub(listener, $"http://127.0.0.1:{port}");
                }
                catch (HttpListenerException)
                {
                    listener.Close();
                }
            }

            Assert.Ignore("Kein freier lokaler Port fuer den Discovery-Platzhalter gefunden.");
            throw new InvalidOperationException("unerreichbar");
        }

        private async Task AcceptAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Close();
            _stopping.Dispose();
        }
    }
}
