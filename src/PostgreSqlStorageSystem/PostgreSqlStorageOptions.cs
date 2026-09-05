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

        if (string.IsNullOrWhiteSpace(Schema) || !Schema.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_'))
        {
            throw new InvalidOperationException("Storage:PostgreSql:Schema must consist of lowercase letters, digits or underscores.");
        }
    }
}
