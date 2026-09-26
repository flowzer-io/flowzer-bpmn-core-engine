using Npgsql;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Eine eingebettete Schema-Migration ist gescheitert. Nennt Version und Namen der Migration,
/// an der der Lauf abbrach; die urspruengliche Ausnahme (meist <see cref="PostgresException"/>)
/// steht in <see cref="Exception.InnerException"/>. Erreicht die Ausnahme den Aufrufer, hat
/// <see cref="PostgreSqlMigrator.ApplyAsync"/> die Transaktion des Laufs bereits verworfen:
/// Keine Migration dieses Laufs ist angewendet, die Historie ist unveraendert.
/// </summary>
public sealed class SchemaMigrationFailedException : Exception
{
    public SchemaMigrationFailedException(int version, string name, Exception innerException)
        : base($"PostgreSQL migration {name} (version {version}) failed: {innerException.Message}", innerException)
    {
        Version = version;
        Name = name;
    }

    /// <summary>Versionsnummer der gescheiterten Migration, etwa 19.</summary>
    public int Version { get; }

    /// <summary>Name der gescheiterten Migration wie in der Historie, etwa <c>019_inbound_triggers</c>.</summary>
    public string Name { get; }

    /// <summary>SQLSTATE der Datenbank, falls die Ursache eine <see cref="PostgresException"/> ist.</summary>
    public string? SqlState => (InnerException as PostgresException)?.SqlState;
}
