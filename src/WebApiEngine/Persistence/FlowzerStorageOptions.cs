using PostgreSqlStorageSystem;

namespace WebApiEngine.Persistence;

/// <summary>
/// Auswahl der Ablage (Abschnitt <c>Storage</c>): <c>Filesystem</c> (Default, JSON-Dateien
/// unter FLOWZER_STORAGE_ROOT) oder <c>PostgreSql</c> (Abschnitt <c>Storage:PostgreSql</c>).
/// </summary>
public sealed class FlowzerStorageOptions
{
    public const string SectionName = "Storage";
    public const string ProviderFilesystem = "Filesystem";
    public const string ProviderPostgreSql = "PostgreSql";

    public string Provider { get; set; } = ProviderFilesystem;

    public PostgreSqlStorageOptions PostgreSql { get; set; } = new();

    public bool IsPostgreSql => string.Equals(Provider, ProviderPostgreSql, StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (IsPostgreSql)
        {
            PostgreSql.Validate();
            return;
        }

        if (!string.Equals(Provider, ProviderFilesystem, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Storage:Provider must be '{ProviderFilesystem}' or '{ProviderPostgreSql}', but was '{Provider}'.");
        }
    }

    public string Describe() => IsPostgreSql
        ? $"PostgreSQL (schema {PostgreSql.Schema})"
        : "Filesystem";
}
