using Npgsql;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Nicht-transaktionale Sicht auf die PostgreSQL-Ablage fuer Lesepfade der Controller:
/// jede Operation nutzt eine Verbindung aus dem Pool.
/// </summary>
public sealed class PostgreSqlStorage : IStorageSystem, IDisposable
{
    private readonly PostgreSqlSession _session;

    public PostgreSqlStorage(NpgsqlDataSource dataSource, string schema)
    {
        _session = new PostgreSqlSession(dataSource, schema, transactional: false);
        DefinitionStorage = new PostgreSqlDefinitionStorage(_session);
        SubscriptionStorage = new PostgreSqlSubscriptionStorage(_session, DefinitionStorage);
        InstanceStorage = new PostgreSqlInstanceStorage(_session);
        FormStorage = new PostgreSqlFormStorage(_session);
    }

    public IDefinitionStorage DefinitionStorage { get; }
    public IMessageSubscriptionStorage SubscriptionStorage { get; }
    public IInstanceStorage InstanceStorage { get; }
    public IFormStorage FormStorage { get; }

    public void Dispose() => _session.Dispose();
}

/// <summary>
/// Transaktionale Sicht: alle Aenderungen laufen in einer Datenbanktransaktion und werden
/// erst mit <see cref="CommitChanges"/> sichtbar. Entsorgen ohne Commit rollt zurueck.
/// </summary>
public sealed class PostgreSqlTransactionalStorage : ITransactionalStorage
{
    private readonly PostgreSqlSession _session;

    public PostgreSqlTransactionalStorage(NpgsqlDataSource dataSource, string schema)
    {
        _session = new PostgreSqlSession(dataSource, schema, transactional: true);
        DefinitionStorage = new PostgreSqlDefinitionStorage(_session);
        SubscriptionStorage = new PostgreSqlSubscriptionStorage(_session, DefinitionStorage);
        InstanceStorage = new PostgreSqlInstanceStorage(_session);
        FormStorage = new PostgreSqlFormStorage(_session);
    }

    public IDefinitionStorage DefinitionStorage { get; }
    public IMessageSubscriptionStorage SubscriptionStorage { get; }
    public IInstanceStorage InstanceStorage { get; }
    public IFormStorage FormStorage { get; }

    public void CommitChanges() => _session.Commit();

    public void RollbackTransaction() => _session.Rollback();

    public void Dispose() => _session.Dispose();
}

public sealed class PostgreSqlTransactionalStorageProvider(NpgsqlDataSource dataSource, string schema) : ITransactionalStorageProvider
{
    public ITransactionalStorage GetTransactionalStorage() => new PostgreSqlTransactionalStorage(dataSource, schema);
}
