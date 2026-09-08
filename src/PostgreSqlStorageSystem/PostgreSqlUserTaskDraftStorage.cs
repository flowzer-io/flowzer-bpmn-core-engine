using Model;
using Npgsql;
using NpgsqlTypes;
using StorageSystem;

namespace PostgreSqlStorageSystem;

/// <summary>
/// PostgreSQL-CAS fuer private Aufgabenentwuerfe. Ein einzelnes INSERT/UPDATE-Statement
/// entscheidet die Revision; der FK auf die Subscription macht den Lebenszyklus atomar.
/// </summary>
internal sealed class PostgreSqlUserTaskDraftStorage(PostgreSqlSession session) : IUserTaskDraftStorage
{
    public Task<UserTaskDraft?> Get(Guid userTaskId, string ownerKey) =>
        session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.user_task_drafts
                WHERE user_task_id = @taskId AND owner_key = @ownerKey
                """);
            command.Parameters.AddWithValue("taskId", userTaskId);
            command.Parameters.AddWithValue("ownerKey", ownerKey);
            return await command.ExecuteScalarAsync() is string body
                ? StorageJson.Deserialize<UserTaskDraft>(body)
                : null;
        });

    public Task<UserTaskDraftWriteResult> TrySave(UserTaskDraft draft, long expectedRevision) =>
        session.RunAsync(async (connection, transaction) =>
        {
            if (expectedRevision < 0 || draft.Revision != checked(expectedRevision + 1))
                throw new ArgumentOutOfRangeException(nameof(expectedRevision));

            try
            {
                // Ein UPDATE darf einen zwischenzeitlich geloeschten Entwurf nicht als neue
                // Revision > 1 wieder anlegen. Zwei getrennte Statements halten diese
                // Create-versus-Replace-Invariante direkt in PostgreSQL fest.
                var sql = expectedRevision == 0
                    ? """
                      INSERT INTO {schema}.user_task_drafts
                          (user_task_id, owner_key, owner_user_id, token_id, process_instance_id,
                           definition_id, revision, updated_at, body)
                      VALUES (@taskId, @ownerKey, @ownerUserId, @tokenId, @instanceId,
                              @definitionId, @revision, @updatedAt, @body)
                      ON CONFLICT (user_task_id, owner_key) DO NOTHING
                      RETURNING body
                      """
                    : """
                      UPDATE {schema}.user_task_drafts SET
                          owner_user_id = @ownerUserId,
                          token_id = @tokenId,
                          process_instance_id = @instanceId,
                          definition_id = @definitionId,
                          revision = @revision,
                          updated_at = @updatedAt,
                          body = @body
                      WHERE user_task_id = @taskId
                        AND owner_key = @ownerKey
                        AND revision = @expectedRevision
                      RETURNING body
                      """;
                await using var command = session.CreateCommand(connection, transaction, sql);
                AddParameters(command, draft, expectedRevision);
                if (await command.ExecuteScalarAsync() is string written)
                {
                    var stored = StorageJson.Deserialize<UserTaskDraft>(written);
                    return new UserTaskDraftWriteResult(
                        UserTaskDraftWriteStatus.Written, stored, stored.Revision);
                }
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                return new UserTaskDraftWriteResult(UserTaskDraftWriteStatus.TaskNotFound, null, 0);
            }

            var state = await ReadState(connection, transaction, draft.UserTaskId, draft.OwnerKey);
            return state.TaskExists
                ? new UserTaskDraftWriteResult(
                    UserTaskDraftWriteStatus.RevisionConflict, state.Draft, state.Draft?.Revision ?? 0)
                : new UserTaskDraftWriteResult(UserTaskDraftWriteStatus.TaskNotFound, null, 0);
        });

    public Task<UserTaskDraftDeleteResult> TryDelete(
        Guid userTaskId,
        string ownerKey,
        long expectedRevision) => session.RunAsync(async (connection, transaction) =>
    {
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        await using (var command = session.CreateCommand(connection, transaction, """
                         DELETE FROM {schema}.user_task_drafts
                         WHERE user_task_id = @taskId AND owner_key = @ownerKey AND revision = @expectedRevision
                         """))
        {
            command.Parameters.AddWithValue("taskId", userTaskId);
            command.Parameters.AddWithValue("ownerKey", ownerKey);
            command.Parameters.AddWithValue("expectedRevision", expectedRevision);
            if (await command.ExecuteNonQueryAsync() == 1)
                return new UserTaskDraftDeleteResult(UserTaskDraftDeleteStatus.Deleted, 0);
        }

        var state = await ReadState(connection, transaction, userTaskId, ownerKey);
        if (!state.TaskExists)
            return new UserTaskDraftDeleteResult(UserTaskDraftDeleteStatus.TaskNotFound, 0);
        if (state.Draft is null && expectedRevision == 0)
            return new UserTaskDraftDeleteResult(UserTaskDraftDeleteStatus.Deleted, 0);
        return new UserTaskDraftDeleteResult(
            UserTaskDraftDeleteStatus.RevisionConflict, state.Draft?.Revision ?? 0);
    });

    private async Task<(bool TaskExists, UserTaskDraft? Draft)> ReadState(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userTaskId,
        string ownerKey)
    {
        await using var command = session.CreateCommand(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM {schema}.user_task_subscriptions WHERE id = @taskId),
                   (SELECT body FROM {schema}.user_task_drafts
                    WHERE user_task_id = @taskId AND owner_key = @ownerKey)
            """);
        command.Parameters.AddWithValue("taskId", userTaskId);
        command.Parameters.AddWithValue("ownerKey", ownerKey);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (
            reader.GetBoolean(0),
            reader.IsDBNull(1) ? null : StorageJson.Deserialize<UserTaskDraft>(reader.GetString(1)));
    }

    private static void AddParameters(NpgsqlCommand command, UserTaskDraft draft, long expectedRevision)
    {
        command.Parameters.AddWithValue("taskId", draft.UserTaskId);
        command.Parameters.AddWithValue("ownerKey", draft.OwnerKey);
        command.Parameters.AddWithValue("ownerUserId", draft.OwnerUserId);
        command.Parameters.AddWithValue("tokenId", draft.TokenId);
        command.Parameters.AddWithValue("instanceId", draft.ProcessInstanceId);
        command.Parameters.AddWithValue("definitionId", draft.DefinitionId);
        command.Parameters.AddWithValue("revision", draft.Revision);
        command.Parameters.AddWithValue("expectedRevision", expectedRevision);
        var updatedAt = command.Parameters.AddWithValue("updatedAt", draft.UpdatedAtUtc);
        updatedAt.NpgsqlDbType = NpgsqlDbType.TimestampTz;
        command.Parameters.AddWithValue("body", StorageJson.Serialize(draft));
    }
}
