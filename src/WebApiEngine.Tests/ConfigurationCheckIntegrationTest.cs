using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
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
        // Ohne die native V8-Bibliothek laeuft die Installation ohne FEEL weiter. Die Pruefung
        // muss das melden, sonst faellt es erst an falsch entschiedenen Prozessen auf.
        output.Should().Contain("Ausdruecke").And.Contain("libfeelin");
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

    // Testzweck: Ein leerer privilegierter Rollenname ohne ausdrueckliche Legacy-Wahl ist ein
    // Startfehler (Exit-Code 1); die Ausgabe nennt den fehlenden Schluessel und den Schalter.
    [Test]
    public async Task CheckConfig_ShouldFail_WhenAPrivilegedRoleIsEmptyWithoutLegacySwitch()
    {
        var settings = ValidConfiguration();
        settings["Authentication__JwtBearer__Roles__Modeler"] = "";

        var (exitCode, output) = await RunCheckAsync(settings);

        exitCode.Should().Be(1, "die Ausgabe war:\n{0}", output);
        output.Should().Contain("Authentication:JwtBearer:Roles:Modeler").And.Contain("LegacyPermissiveRoles");
        output.Should().NotContain("Unhandled exception");
    }

    // Testzweck: Mit ausdruecklich gewaehlter Legacy-Kompatibilitaet startet die Installation,
    // die Pruefung warnt aber in der Zeile Rollen und nennt den fehlenden Schluessel (Exit-Code 2).
    [Test]
    public async Task CheckConfig_ShouldWarn_WhenAPrivilegedRoleIsEmptyWithLegacySwitch()
    {
        var settings = ValidConfiguration();
        settings["Authentication__JwtBearer__Roles__Modeler"] = "";
        settings["Authentication__JwtBearer__LegacyPermissiveRoles"] = "true";

        var (exitCode, output) = await RunCheckAsync(settings);

        exitCode.Should().Be(2, "die Ausgabe war:\n{0}", output);
        output.Should().Contain("Rollen").And.Contain("Warnung");
        output.Should().Contain("Authentication:JwtBearer:Roles:Modeler");
    }

    // Testzweck: Im BFF-Modus muss der Schluesselring anlegbar und beschreibbar sein. Zeigt der
    // Pfad auf eine vorhandene Datei, meldet die Pruefung einen Fehler (Exit-Code 1), ohne das
    // Client-Secret auszugeben.
    [Test]
    public async Task CheckConfig_ShouldFail_WhenTheBffKeyringIsNotWritable()
    {
        var keyringFile = _storageRoot + "-keyring-file";
        await File.WriteAllTextAsync(keyringFile, "kein Verzeichnis");
        try
        {
            var settings = ValidConfiguration();
            settings["Authentication__Scheme"] = "Bff";
            settings["Authentication__Bff__ClientId"] = "flowzer-console";
            settings["Authentication__Bff__ClientSecret"] = "bff-streng-geheim";
            settings["Authentication__Bff__DataProtectionKeysPath"] = keyringFile;

            var (exitCode, output) = await RunCheckAsync(settings);

            exitCode.Should().Be(1, "die Ausgabe war:\n{0}", output);
            output.Should().Contain("Schluesselring").And.Contain("Fehler");
            output.Should().NotContain("bff-streng-geheim");
        }
        finally
        {
            File.Delete(keyringFile);
        }
    }

    // Testzweck: Meldet das Discovery-Dokument einen anderen Issuer als die konfigurierte
    // Authority, bleibt die Zeile OK (Exit-Code 0), nennt den Issuer aber als Hinweis: Bei Entra
    // common/organizations und hinter einem Proxy ist die Abweichung erwartet, zur Laufzeit gilt
    // der Issuer aus den Metadaten.
    [Test]
    public async Task CheckConfig_ShouldHint_WhenTheDiscoveryIssuerDiffersFromTheAuthority()
    {
        _authority!.Issuer = "https://anderer-issuer.example.invalid/realms/flowzer";

        var (exitCode, output) = await RunCheckAsync(ValidConfiguration());

        exitCode.Should().Be(0, "die Ausgabe war:\n{0}", output);
        output.Should().Contain("Authentifizierung").And.NotContain("Warnung");
        output.Should().Contain("https://anderer-issuer.example.invalid/realms/flowzer")
            .And.Contain("weicht von der konfigurierten Authority");
    }

    // Testzweck: Ein Discovery-Dokument ohne token_endpoint kann keinen Anmeldefluss tragen;
    // die Pruefung warnt (Exit-Code 2) und nennt das fehlende Feld.
    [Test]
    public async Task CheckConfig_ShouldWarn_WhenTheDiscoveryHasNoTokenEndpoint()
    {
        _authority!.OmitTokenEndpoint = true;

        var (exitCode, output) = await RunCheckAsync(ValidConfiguration());

        exitCode.Should().Be(2, "die Ausgabe war:\n{0}", output);
        output.Should().Contain("Authentifizierung").And.Contain("Warnung").And.Contain("token_endpoint");
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
        ["Authentication__JwtBearer__Roles__Modeler"] = "flowzer-modeler",
        ["Authentication__JwtBearer__Roles__Operator"] = "flowzer-operator",
        ["Authentication__JwtBearer__Roles__Worker"] = "flowzer-worker",
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
    /// Minimaler OIDC-Discovery-Endpunkt. Er liefert nur die beiden Felder, die die Pruefung
    /// liest: <c>issuer</c> (standardmaessig die eigene Adresse) und <c>token_endpoint</c>.
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

        /// <summary>Gemeldeter Issuer; ohne Angabe die eigene Adresse wie bei einem echten IdP.</summary>
        public string? Issuer { get; set; }

        /// <summary>Laesst token_endpoint weg, um ein unbrauchbares Discovery-Dokument nachzustellen.</summary>
        public bool OmitTokenEndpoint { get; set; }

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

                var fields = new Dictionary<string, string> { ["issuer"] = Issuer ?? Authority };
                if (!OmitTokenEndpoint)
                {
                    fields["token_endpoint"] = Authority + "/protocol/openid-connect/token";
                }
                var document = JsonSerializer.SerializeToUtf8Bytes(fields);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                context.Response.Close(document, willBlock: true);
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
