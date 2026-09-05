using Npgsql;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Kapselt den Verbindungs- und Transaktionsumgang einer Ablage-Instanz.
/// Nicht-transaktional: je Operation eine Verbindung aus dem Pool.
/// Transaktional: eine Verbindung mit einer Transaktion, die erst mit <see cref="Commit"/>
/// wirksam wird; <see cref="Dispose"/> ohne Commit rollt zurueck.
/// </summary>
internal sealed class PostgreSqlSession(NpgsqlDataSource dataSource, string schema, bool transactional) : IDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private bool _completed;

    public string Schema { get; } = schema;

    /// <summary>Wie im Migrator: Bezeichner immer gequotet einsetzen, nie roh.</summary>
    private string QuotedSchema { get; } = "\"" + schema.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public async Task<T> RunAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction?, Task<T>> operation)
    {
        if (transactional)
        {
            var connection = await EnsureTransactionAsync();
            return await operation(connection, _transaction);
        }

        // Auch eine einzelne Operation kann mehrere Statements umfassen (z. B. Metadaten und
        // Versionen loeschen). Eine kurze Transaktion je Operation haelt sie atomar.
        await using var pooledConnection = await dataSource.OpenConnectionAsync();
        await using var shortTransaction = await pooledConnection.BeginTransactionAsync();
        var result = await operation(pooledConnection, shortTransaction);
        await shortTransaction.CommitAsync();
        return result;
    }

    public async Task RunAsync(Func<NpgsqlConnection, NpgsqlTransaction?, Task> operation)
    {
        await RunAsync(async (connection, transaction) =>
        {
            await operation(connection, transaction);
            return true;
        });
    }

    /// <summary>Fuer die wenigen synchronen Methoden des Ablagevertrags.</summary>
    public T Run<T>(Func<NpgsqlConnection, NpgsqlTransaction?, T> operation)
    {
        if (transactional)
        {
            var connection = EnsureTransaction();
            return operation(connection, _transaction);
        }

        using var pooledConnection = dataSource.OpenConnection();
        using var shortTransaction = pooledConnection.BeginTransaction();
        var result = operation(pooledConnection, shortTransaction);
        shortTransaction.Commit();
        return result;
    }

    public NpgsqlCommand CreateCommand(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql.Replace("{schema}", QuotedSchema, StringComparison.Ordinal);
        command.Transaction = transaction;
        return command;
    }

    public void Commit()
    {
        if (_transaction is null)
        {
            _completed = true;
            return;
        }

        _transaction.Commit();
        _completed = true;
    }

    public void Rollback()
    {
        if (_transaction is not null && !_completed)
        {
            _transaction.Rollback();
        }

        _completed = true;
    }

    public void Dispose()
    {
        if (_transaction is not null && !_completed)
        {
            try
            {
                _transaction.Rollback();
            }
            catch (Exception)
            {
                // Rollback beim Aufraeumen ist Best Effort; die Verbindung wird ohnehin geschlossen.
            }
        }

        _transaction?.Dispose();
        _connection?.Dispose();
        _transaction = null;
        _connection = null;
    }

    private async Task<NpgsqlConnection> EnsureTransactionAsync()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The transactional storage has already been committed or rolled back.");
        }

        if (_connection is null)
        {
            _connection = await dataSource.OpenConnectionAsync();
            _transaction = await _connection.BeginTransactionAsync();
        }

        return _connection;
    }

    private NpgsqlConnection EnsureTransaction()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The transactional storage has already been committed or rolled back.");
        }

        if (_connection is null)
        {
            _connection = dataSource.OpenConnection();
            _transaction = _connection.BeginTransaction();
        }

        return _connection;
    }
}
