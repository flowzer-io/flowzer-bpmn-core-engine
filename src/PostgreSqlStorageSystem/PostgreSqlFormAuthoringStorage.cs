using Model;
using Npgsql;
using NpgsqlTypes;
using StorageSystem;
using StorageSystem.Exceptions;

namespace PostgreSqlStorageSystem;

/// <summary>
/// PostgreSQL-CAS fuer Formularautoren. Publish sperrt den Entwurf und den Metadatensatz,
/// bestimmt die Folgeversion und entfernt den Entwurf in einer Datenbanktransaktion.
/// </summary>
internal sealed class PostgreSqlFormAuthoringStorage(PostgreSqlSession session) : IFormAuthoringStorage
{
    public Task<FormAuthoringDraft?> Get(Guid formId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.form_authoring_drafts WHERE form_id = @formId");
        command.Parameters.AddWithValue("formId", formId);
        return await command.ExecuteScalarAsync() is string body
            ? StorageJson.Deserialize<FormAuthoringDraft>(body)
            : null;
    });

    public Task<FormAuthoringWriteResult> TrySave(FormAuthoringDraft draft, long expectedRevision) =>
        session.RunAsync(async (connection, transaction) =>
        {
            if (draft.FormId == Guid.Empty || expectedRevision < 0
                || draft.Revision != checked(expectedRevision + 1))
                throw new ArgumentOutOfRangeException(nameof(expectedRevision));

            var sql = expectedRevision == 0
                ? """
                  INSERT INTO {schema}.form_authoring_drafts (form_id, revision, updated_at, body)
                  SELECT @formId, @revision, @updatedAt, @body
                  WHERE EXISTS (SELECT 1 FROM {schema}.form_metadata WHERE form_id = @formId)
                  ON CONFLICT (form_id) DO NOTHING
                  RETURNING body
                  """
                : """
                  UPDATE {schema}.form_authoring_drafts SET
                      revision = @revision, updated_at = @updatedAt, body = @body
                  WHERE form_id = @formId AND revision = @expectedRevision
                  RETURNING body
                  """;
            await using var command = session.CreateCommand(connection, transaction, sql);
            AddParameters(command, draft, expectedRevision);
            if (await command.ExecuteScalarAsync() is string written)
            {
                var stored = StorageJson.Deserialize<FormAuthoringDraft>(written);
                return new FormAuthoringWriteResult(FormAuthoringWriteStatus.Written, stored, stored.Revision);
            }

            var state = await ReadState(connection, transaction, draft.FormId);
            return state.FormExists
                ? new FormAuthoringWriteResult(
                    FormAuthoringWriteStatus.RevisionConflict, state.Draft, state.Draft?.Revision ?? 0)
                : new FormAuthoringWriteResult(FormAuthoringWriteStatus.FormNotFound, null, 0);
        });

    public Task<FormAuthoringDeleteResult> TryDelete(Guid formId, long expectedRevision) =>
        session.RunAsync(async (connection, transaction) =>
        {
            if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            await using (var command = session.CreateCommand(connection, transaction, """
                DELETE FROM {schema}.form_authoring_drafts
                WHERE form_id = @formId AND revision = @expectedRevision
                """))
            {
                command.Parameters.AddWithValue("formId", formId);
                command.Parameters.AddWithValue("expectedRevision", expectedRevision);
                if (await command.ExecuteNonQueryAsync() == 1)
                    return new FormAuthoringDeleteResult(FormAuthoringDeleteStatus.Deleted, 0);
            }

            var state = await ReadState(connection, transaction, formId);
            if (!state.FormExists)
                return new FormAuthoringDeleteResult(FormAuthoringDeleteStatus.FormNotFound, 0);
            if (state.Draft is null && expectedRevision == 0)
                return new FormAuthoringDeleteResult(FormAuthoringDeleteStatus.Deleted, 0);
            return new FormAuthoringDeleteResult(
                FormAuthoringDeleteStatus.RevisionConflict, state.Draft?.Revision ?? 0);
        });

    public Task<FormAuthoringPublishResult> TryPublish(
        Guid formId,
        long expectedRevision,
        Guid publishedFormId) => session.RunAsync(async (connection, transaction) =>
    {
        if (expectedRevision <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (publishedFormId == Guid.Empty) throw new ArgumentException("Published form ID is required.", nameof(publishedFormId));

        // Der Metadaten-Lock serialisiert auch erste Veroeffentlichungen, bei denen noch keine
        // Zeile in `forms` existiert. Der Draft-Lock verhindert Aenderungen waehrend Publish.
        await using (var lockMetadata = session.CreateCommand(connection, transaction,
                         "SELECT 1 FROM {schema}.form_metadata WHERE form_id = @formId FOR UPDATE"))
        {
            lockMetadata.Parameters.AddWithValue("formId", formId);
            if (await lockMetadata.ExecuteScalarAsync() is null)
                return new FormAuthoringPublishResult(FormAuthoringPublishStatus.FormNotFound, null, 0);
        }

        FormAuthoringDraft? draft;
        await using (var readDraft = session.CreateCommand(connection, transaction,
                         "SELECT body FROM {schema}.form_authoring_drafts WHERE form_id = @formId FOR UPDATE"))
        {
            readDraft.Parameters.AddWithValue("formId", formId);
            draft = await readDraft.ExecuteScalarAsync() is string body
                ? StorageJson.Deserialize<FormAuthoringDraft>(body)
                : null;
        }
        if (draft?.Revision != expectedRevision)
            return new FormAuthoringPublishResult(
                FormAuthoringPublishStatus.RevisionConflict, null, draft?.Revision ?? 0);

        int major;
        int minor;
        await using (var versionCommand = session.CreateCommand(connection, transaction, """
                         SELECT version_major, version_minor FROM {schema}.forms
                         WHERE form_id = @formId
                         ORDER BY version_major DESC, version_minor DESC LIMIT 1
                         """))
        {
            versionCommand.Parameters.AddWithValue("formId", formId);
            await using var reader = await versionCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                major = reader.GetInt32(0);
                minor = checked(reader.GetInt32(1) + 1);
            }
            else { major = 0; minor = 1; }
        }

        var published = new Form
        {
            Id = publishedFormId,
            FormId = formId,
            Version = new Model.Version(major, minor),
            FormData = draft.FormData
        };
        await using (var insert = session.CreateCommand(connection, transaction, """
                         INSERT INTO {schema}.forms (id, form_id, version_major, version_minor, body)
                         VALUES (@id, @formId, @major, @minor, @body)
                         """))
        {
            insert.Parameters.AddWithValue("id", published.Id);
            insert.Parameters.AddWithValue("formId", formId);
            insert.Parameters.AddWithValue("major", major);
            insert.Parameters.AddWithValue("minor", minor);
            insert.Parameters.AddWithValue("body", StorageJson.Serialize(published));
            try { await insert.ExecuteNonQueryAsync(); }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new DefinitionStorageConflictException(
                    $"Published form '{formId}' already contains ID '{publishedFormId}' or version {major}.{minor}.");
            }
        }
        await using (var delete = session.CreateCommand(connection, transaction,
                         "DELETE FROM {schema}.form_authoring_drafts WHERE form_id = @formId AND revision = @revision"))
        {
            delete.Parameters.AddWithValue("formId", formId);
            delete.Parameters.AddWithValue("revision", expectedRevision);
            if (await delete.ExecuteNonQueryAsync() != 1)
                throw new InvalidDataException("The locked form-authoring draft changed unexpectedly.");
        }
        return new FormAuthoringPublishResult(FormAuthoringPublishStatus.Published, published, 0);
    });

    private async Task<(bool FormExists, FormAuthoringDraft? Draft)> ReadState(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid formId)
    {
        await using var command = session.CreateCommand(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM {schema}.form_metadata WHERE form_id = @formId),
                   (SELECT body FROM {schema}.form_authoring_drafts WHERE form_id = @formId)
            """);
        command.Parameters.AddWithValue("formId", formId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetBoolean(0), reader.IsDBNull(1)
            ? null
            : StorageJson.Deserialize<FormAuthoringDraft>(reader.GetString(1)));
    }

    private static void AddParameters(NpgsqlCommand command, FormAuthoringDraft draft, long expectedRevision)
    {
        command.Parameters.AddWithValue("formId", draft.FormId);
        command.Parameters.AddWithValue("revision", draft.Revision);
        command.Parameters.AddWithValue("expectedRevision", expectedRevision);
        var updatedAt = command.Parameters.AddWithValue("updatedAt", draft.UpdatedAtUtc);
        updatedAt.NpgsqlDbType = NpgsqlDbType.TimestampTz;
        command.Parameters.AddWithValue("body", StorageJson.Serialize(draft));
    }
}
