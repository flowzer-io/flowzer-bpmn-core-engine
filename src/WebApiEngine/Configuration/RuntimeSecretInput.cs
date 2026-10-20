using System.Security.Cryptography;
using System.Text.Json;

namespace WebApiEngine.Configuration;

/// <summary>
/// Expliziter, begrenzter Einmal-Bootstrap aus stdin. Keine Secretdateien, ENV- oder
/// Argument-Fallbacks; die Rollen-Allowlist ist absichtlich nicht konfigurierbar.
/// </summary>
internal sealed class RuntimeSecretInput
{
    internal const string RuntimeKey = "Storage:PostgreSql:ConnectionString";
    internal const string MigrationKey = "Storage:PostgreSql:MigrationConnectionString";
    internal const string BffKey = "Authentication:Bff:ClientSecret";
    internal const string DirectoryKey = "IdentityDirectory:ClientSecret";
    internal const string RejectionMessage = "FLOWZER_SECRET_INPUT_REJECTED";
    private const string Switch = "--secrets-stdin";
    private const int MaximumBytes = 16 * 1024;
    private const int MaximumValueCharacters = 4096;
    private static readonly string[] ProtectedKeys = [RuntimeKey, MigrationKey, BffKey, DirectoryKey];
    private readonly IReadOnlyDictionary<string, string?> values;
    private int applicationAttempted;

    private RuntimeSecretInput(bool selected, bool accepted, string[] arguments,
        IReadOnlyDictionary<string, string?>? values = null)
    {
        Selected = selected;
        Accepted = accepted;
        Arguments = arguments;
        this.values = values ?? new Dictionary<string, string?>();
    }

    internal bool Selected { get; }
    internal bool Accepted { get; }
    internal string[] Arguments { get; }

    /// <summary>
    /// Liest nur bei exaktem Opt-in, maximal 16 KiB bis EOF und höchstens fünf Sekunden.
    /// Auch nicht kooperatives ReadAsync blockiert den Aufrufer nicht über diese Frist.
    /// Bei Ablehnung muss der Prozess enden, nicht mit einem Hoststart fortfahren.
    /// </summary>
    internal static async Task<RuntimeSecretInput> LoadAsync(string[] arguments, Stream? input = null)
    {
        var selected = arguments.Any(argument => argument.StartsWith(Switch, StringComparison.OrdinalIgnoreCase));
        if (!selected)
        {
            return new RuntimeSecretInput(false, true, (string[])arguments.Clone());
        }

        var remaining = arguments.Where(argument => argument != Switch).ToArray();
        var migration = remaining.Length == 1 && string.Equals(remaining[0], "--migrate", StringComparison.OrdinalIgnoreCase);
        if (arguments.Count(argument => argument == Switch) != 1 || (remaining.Length != 0 && !migration))
        {
            return Rejected();
        }

        // Ein zusätzliches Byte erkennt Übergröße, ohne unbeschränkt weiterzulesen.
        var buffer = new byte[MaximumBytes + 1];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            input ??= Console.OpenStandardInput();
            var length = 0;
            var token = timeout.Token;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var destination = buffer.AsMemory(length);
                // Auch ReadAsync selbst kann vor der Task-Rückgabe synchron blockieren.
                // Nur ein eigener Hintergrundworker liest; bei Timeout muss der Prozess enden.
                var read = Task.Run(async () => await input.ReadAsync(destination, token).ConfigureAwait(false), token);
                // Manche Streams ignorieren Cancellation. WaitAsync begrenzt auch diesen Fall.
                // Die terminale Ablehnung startet niemals API/Migrationen; ein möglicher späterer
                // Streamfehler wird nur beobachtet, niemals mit ToString/Message ausgegeben.
                _ = read.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                var count = await read.WaitAsync(timeout.Token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (count == 0) break;
                length += count;
                if (length > MaximumBytes) return Rejected();
            }

            using var document = JsonDocument.Parse(buffer.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 4 });
            var parsed = Parse(document.RootElement, migration);
            return parsed is null ? Rejected() : new RuntimeSecretInput(true, true, remaining, parsed);
        }
        catch (Exception)
        {
            // Parser-, I/O- und Timeout-Ausnahmen können Nutzlasten enthalten: nie weiterreichen.
            return Rejected();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    /// <summary>
    /// Prüft ALLE geschützten Schlüssel vor dem einmaligen Apply. Der Hostadapter muss dabei
    /// jeden einzelnen Konfigurationsprovider betrachten, nicht nur dessen effektiven Wert.
    /// Selbst rollenfremde oder überdeckte Fremdwerte sind verboten. Leere Vorlagen sind erlaubt.
    /// </summary>
    internal bool TryApply(Func<string, bool> hasExistingValue,
        Action<IReadOnlyDictionary<string, string?>> apply)
    {
        if (!Selected || !Accepted || Interlocked.Exchange(ref applicationAttempted, 1) != 0) return false;
        try
        {
            if (ProtectedKeys.Any(hasExistingValue)) return false;
            apply(values);
            return true;
        }
        catch (Exception)
        {
            // Auch Fehler des Konfigurationsadapters dürfen keine Secrets transportieren.
            return false;
        }
    }

    /// <summary>Diagnostik liefert niemals Eingabe-, Secret- oder Exceptioninhalte.</summary>
    public override string ToString() => "Runtime secret input (values hidden)";

    private static RuntimeSecretInput Rejected() => new(true, false, []);

    private static Dictionary<string, string?>? Parse(JsonElement root, bool migration)
    {
        if (!HasExactKeys(root, ["version", "role", "values"])) return null;
        var version = root.GetProperty("version");
        var role = root.GetProperty("role");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1
            || role.ValueKind != JsonValueKind.String || role.GetString() != (migration ? "migrate" : "api")) return null;

        var fields = root.GetProperty("values");
        string[] names = migration ? ["migrationConnectionString"]
            : ["runtimeConnectionString", "bffClientSecret", "directoryClientSecret"];
        if (!HasExactKeys(fields, names)) return null;
        var parsed = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var field = fields.GetProperty(name);
            if (field.ValueKind != JsonValueKind.String) return null;
            var value = field.GetString();
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumValueCharacters || value.Any(char.IsControl)) return null;
            switch (name)
            {
                case "migrationConnectionString":
                    parsed.Add(RuntimeKey, value);
                    parsed.Add(MigrationKey, value);
                    break;
                case "runtimeConnectionString": parsed.Add(RuntimeKey, value); break;
                case "bffClientSecret": parsed.Add(BffKey, value); break;
                case "directoryClientSecret": parsed.Add(DirectoryKey, value); break;
            }
        }
        return parsed;
    }

    private static bool HasExactKeys(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) return false;
        }
        return seen.Count == expected.Length;
    }
}
