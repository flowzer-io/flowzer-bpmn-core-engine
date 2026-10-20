using Npgsql;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Konfiguration der PostgreSQL-Ablage (Abschnitt <c>Storage:PostgreSql</c>).
/// Laufzeit und Migration verwenden getrennte Identitaeten: Die Laufzeitrolle darf nur
/// lesen, einfuegen, aendern und loeschen; DDL laeuft ausschliesslich ueber die
/// Migrationsverbindung (Compose-Dienst <c>migrate</c> bzw. <c>--migrate</c>).
/// </summary>
public sealed class PostgreSqlStorageOptions
{
    public const string SectionName = "Storage:PostgreSql";
    public const string DefaultSchema = "flowzer";

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Verbindung mit DDL-Rechten fuer Migrationen; leer = wie <see cref="ConnectionString"/>.</summary>
    public string? MigrationConnectionString { get; set; }

    public string Schema { get; set; } = DefaultSchema;

    /// <summary>
    /// Nur fuer einfache Umgebungen: Migrationen beim Start der API ausfuehren. Fuer den
    /// Produktivbetrieb ist der getrennte Migrationsschritt vorgesehen.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }

    public string ResolveMigrationConnectionString() =>
        string.IsNullOrWhiteSpace(MigrationConnectionString) ? ConnectionString : MigrationConnectionString;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("Storage:PostgreSql:ConnectionString must be set when Storage:Provider is 'PostgreSql'.");
        }

        // #375: Der Npgsql-Parser kann unbekannte Schlüssel aus einer falsch gequoteten
        // Passwortzeichenfolge im Ausnahmetext wiederholen. Vor jedem API-/Migratorstart
        // prüfen und eine feste Meldung OHNE InnerException nach außen reichen.
        ValidateConnectionString(ConnectionString, "Storage:PostgreSql:ConnectionString");
        if (!string.IsNullOrWhiteSpace(MigrationConnectionString))
        {
            ValidateConnectionString(MigrationConnectionString, "Storage:PostgreSql:MigrationConnectionString");
        }

        if (string.IsNullOrWhiteSpace(Schema)
            || Schema.Length > 63
            || !(char.IsAsciiLetterLower(Schema[0]) || Schema[0] == '_')
            || !Schema.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_')
            || Schema.StartsWith("pg_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage:PostgreSql:Schema must start with a lowercase letter or underscore, consist of lowercase letters, digits or underscores, and must not start with 'pg_'.");
        }
    }
    private static void ValidateConnectionString(string value, string setting)
    {
        try
        {
            _ = new NpgsqlConnectionStringBuilder(value);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            // setting wird ausschließlich an den beiden festen Aufrufstellen bestimmt.
            throw new InvalidOperationException($"{setting} has invalid PostgreSQL connection string syntax.");
        }
    }

}
