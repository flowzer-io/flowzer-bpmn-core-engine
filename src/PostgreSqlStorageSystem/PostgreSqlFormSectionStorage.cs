using Model;
using Npgsql;
using NpgsqlTypes;
using StorageSystem;
using StorageSystem.Exceptions;

namespace PostgreSqlStorageSystem;

/// <summary>
/// PostgreSQL-Ablage der Formularabschnittsbibliothek. Der Metadatensatz dient beim Publish als
/// sperrbarer Anker, damit auch die erste Fassung pro Abschnitt exakt einmal eine Folgeversion
/// erhält. Das Löschen veröffentlichter Fassungen ist bewusst nicht Teil dieses Adapters.
/// </summary>
internal sealed class PostgreSqlFormSectionStorage(PostgreSqlSession session) : IFormSectionStorage
{
    public Task CreateMetadata(FormSectionMetadata metadata) => session.RunAsync(async (connection, transaction) =>
    {
        ValidateMetadata(metadata);
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.form_section_metadata (section_id, name, body)
            VALUES (@sectionId, @name, @body)
            """);
        command.Parameters.AddWithValue("sectionId", metadata.SectionId);
        command.Parameters.AddWithValue("name", metadata.Name);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(metadata));
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DefinitionStorageConflictException(
                $"Form section metadata '{metadata.SectionId}' already exists.");
        }
    });

    public Task<FormSectionMetadata> GetMetadata(Guid sectionId) => session.RunAsync(async (connection, transaction) =>
    {
        ValidateSectionId(sectionId);
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.form_section_metadata WHERE section_id = @sectionId");
        command.Parameters.AddWithValue("sectionId", sectionId);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null
            ? throw new FileNotFoundException($"Form section metadata not found with id: {sectionId}")
            : StorageJson.Deserialize<FormSectionMetadata>(body);
    });

    public Task<IReadOnlyList<FormSectionMetadata>> ListMetadata() =>
        session.RunAsync<IReadOnlyList<FormSectionMetadata>>(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction,
                "SELECT body FROM {schema}.form_section_metadata ORDER BY name, section_id");
            return (await ReadBodiesAsync<FormSectionMetadata>(command)).ToList();
        });

    public Task<FormSectionMetadata> RenameMetadata(Guid sectionId, string name) =>
        session.RunAsync(async (connection, transaction) =>
        {
            ValidateSectionId(sectionId);
            ValidateName(name);
            var renamed = new FormSectionMetadata(sectionId, name);
            await using var command = session.CreateCommand(connection, transaction, """
                UPDATE {schema}.form_section_metadata SET body = @body, name = @name
                WHERE section_id = @sectionId
                """);
            command.Parameters.AddWithValue("sectionId", sectionId);
            command.Parameters.AddWithValue("name", name);
            command.Parameters.AddWithValue("body", StorageJson.Serialize(renamed));
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new FileNotFoundException($"Form section metadata not found with id: {sectionId}");
            return renamed;
        });

    public Task<FormSectionVersion> GetVersion(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        ValidateSectionId(id);
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.form_section_versions WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null
            ? throw new FileNotFoundException($"Form section version not found with id: {id}")
            : StorageJson.Deserialize<FormSectionVersion>(body);
    });

    public Task<FormSectionVersion> GetVersion(Guid sectionId, Model.Version version) =>
        session.RunAsync(async (connection, transaction) =>
        {
            ValidateSectionId(sectionId);
            ArgumentNullException.ThrowIfNull(version);
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.form_section_versions
                WHERE section_id = @sectionId AND version_major = @major AND version_minor = @minor
                """);
            command.Parameters.AddWithValue("sectionId", sectionId);
            command.Parameters.AddWithValue("major", version.Major);
            command.Parameters.AddWithValue("minor", version.Minor);
            var body = await command.ExecuteScalarAsync() as string;
            return body is null
                ? throw new FileNotFoundException(
                    $"Form section version not found with section id: {sectionId} and version: {version}")
                : StorageJson.Deserialize<FormSectionVersion>(body);
        });

    public Task<IReadOnlyList<FormSectionVersion>> ListVersions(Guid sectionId) =>
        session.RunAsync<IReadOnlyList<FormSectionVersion>>(async (connection, transaction) =>
        {
            ValidateSectionId(sectionId);
            await using var command = session.CreateCommand(connection, transaction, """
                SELECT body FROM {schema}.form_section_versions
                WHERE section_id = @sectionId
                ORDER BY version_major, version_minor
                """);
            command.Parameters.AddWithValue("sectionId", sectionId);
            return (await ReadBodiesAsync<FormSectionVersion>(command)).ToList();
        });

    public Task<FormSectionAuthoringDraft?> GetDraft(Guid sectionId) => session.RunAsync(async (connection, transaction) =>
    {
        ValidateSectionId(sectionId);
        await using var command = session.CreateCommand(connection, transaction,
            "SELECT body FROM {schema}.form_section_authoring_drafts WHERE section_id = @sectionId");
        command.Parameters.AddWithValue("sectionId", sectionId);
        return await command.ExecuteScalarAsync() is string body
            ? StorageJson.Deserialize<FormSectionAuthoringDraft>(body)
            : null;
    });

    public Task<FormSectionAuthoringWriteResult> TrySave(
        FormSectionAuthoringDraft draft,
        long expectedRevision) => session.RunAsync(async (connection, transaction) =>
    {
        ValidateDraft(draft, expectedRevision);
        var sql = expectedRevision == 0
            ? """
              INSERT INTO {schema}.form_section_authoring_drafts (section_id, revision, updated_at, body)
              SELECT @sectionId, @revision, @updatedAt, @body
              WHERE EXISTS (SELECT 1 FROM {schema}.form_section_metadata WHERE section_id = @sectionId)
              ON CONFLICT (section_id) DO NOTHING
              RETURNING body
              """
            : """
              UPDATE {schema}.form_section_authoring_drafts SET
                  revision = @revision, updated_at = @updatedAt, body = @body
              WHERE section_id = @sectionId AND revision = @expectedRevision
              RETURNING body
              """;
        await using var command = session.CreateCommand(connection, transaction, sql);
        AddDraftParameters(command, draft, expectedRevision);
        if (await command.ExecuteScalarAsync() is string written)
        {
            var stored = StorageJson.Deserialize<FormSectionAuthoringDraft>(written);
            return new FormSectionAuthoringWriteResult(
                FormSectionAuthoringWriteStatus.Written, stored, stored.Revision);
        }

        var state = await ReadState(connection, transaction, draft.SectionId);
        return state.SectionExists
            ? new FormSectionAuthoringWriteResult(
                FormSectionAuthoringWriteStatus.RevisionConflict, state.Draft, state.Draft?.Revision ?? 0)
            : new FormSectionAuthoringWriteResult(FormSectionAuthoringWriteStatus.SectionNotFound, null, 0);
    });

    public Task<FormSectionAuthoringDeleteResult> TryDelete(Guid sectionId, long expectedRevision) =>
        session.RunAsync(async (connection, transaction) =>
        {
            ValidateSectionId(sectionId);
            if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            await using (var command = session.CreateCommand(connection, transaction, """
                DELETE FROM {schema}.form_section_authoring_drafts
                WHERE section_id = @sectionId AND revision = @expectedRevision
                """))
            {
                command.Parameters.AddWithValue("sectionId", sectionId);
                command.Parameters.AddWithValue("expectedRevision", expectedRevision);
                if (await command.ExecuteNonQueryAsync() == 1)
                    return new FormSectionAuthoringDeleteResult(FormSectionAuthoringDeleteStatus.Deleted, 0);
            }

            var state = await ReadState(connection, transaction, sectionId);
            if (!state.SectionExists)
                return new FormSectionAuthoringDeleteResult(FormSectionAuthoringDeleteStatus.SectionNotFound, 0);
            if (state.Draft is null && expectedRevision == 0)
                return new FormSectionAuthoringDeleteResult(FormSectionAuthoringDeleteStatus.Deleted, 0);
            return new FormSectionAuthoringDeleteResult(
                FormSectionAuthoringDeleteStatus.RevisionConflict, state.Draft?.Revision ?? 0);
        });

    public Task<FormSectionAuthoringPublishResult> TryPublish(
        Guid sectionId,
        long expectedRevision,
        Guid publishedSectionId) => session.RunAsync(async (connection, transaction) =>
    {
        ValidateSectionId(sectionId);
        if (expectedRevision <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (publishedSectionId == Guid.Empty)
            throw new ArgumentException("Published section ID is required.", nameof(publishedSectionId));

        // Dieser Lock serialisiert erste und spaetere Veröffentlichungen eines Abschnitts.
        await using (var lockMetadata = session.CreateCommand(connection, transaction,
                         "SELECT 1 FROM {schema}.form_section_metadata WHERE section_id = @sectionId FOR UPDATE"))
        {
            lockMetadata.Parameters.AddWithValue("sectionId", sectionId);
            if (await lockMetadata.ExecuteScalarAsync() is null)
                return new FormSectionAuthoringPublishResult(
                    FormSectionAuthoringPublishStatus.SectionNotFound, null, 0);
        }

        FormSectionAuthoringDraft? draft;
        await using (var readDraft = session.CreateCommand(connection, transaction,
                         "SELECT body FROM {schema}.form_section_authoring_drafts WHERE section_id = @sectionId FOR UPDATE"))
        {
            readDraft.Parameters.AddWithValue("sectionId", sectionId);
            draft = await readDraft.ExecuteScalarAsync() is string body
                ? StorageJson.Deserialize<FormSectionAuthoringDraft>(body)
                : null;
        }
        if (draft?.Revision != expectedRevision)
            return new FormSectionAuthoringPublishResult(
                FormSectionAuthoringPublishStatus.RevisionConflict, null, draft?.Revision ?? 0);

        int major;
        int minor;
        await using (var versionCommand = session.CreateCommand(connection, transaction, """
                         SELECT version_major, version_minor FROM {schema}.form_section_versions
                         WHERE section_id = @sectionId
                         ORDER BY version_major DESC, version_minor DESC LIMIT 1
                         """))
        {
            versionCommand.Parameters.AddWithValue("sectionId", sectionId);
            await using var reader = await versionCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                major = reader.GetInt32(0);
                minor = checked(reader.GetInt32(1) + 1);
            }
            else { major = 0; minor = 1; }
        }

        var published = new FormSectionVersion(
            publishedSectionId,
            sectionId,
            new Model.Version(major, minor),
            draft.SectionData);
        await using (var insert = session.CreateCommand(connection, transaction, """
                         INSERT INTO {schema}.form_section_versions
                             (id, section_id, version_major, version_minor, body)
                         VALUES (@id, @sectionId, @major, @minor, @body)
                         """))
        {
            insert.Parameters.AddWithValue("id", published.Id);
            insert.Parameters.AddWithValue("sectionId", published.SectionId);
            insert.Parameters.AddWithValue("major", major);
            insert.Parameters.AddWithValue("minor", minor);
            insert.Parameters.AddWithValue("body", StorageJson.Serialize(published));
            try
            {
                await insert.ExecuteNonQueryAsync();
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new DefinitionStorageConflictException(
                    $"Published form section '{sectionId}' already contains ID '{publishedSectionId}' or version {major}.{minor}.");
            }
        }
        await using (var delete = session.CreateCommand(connection, transaction, """
                         DELETE FROM {schema}.form_section_authoring_drafts
                         WHERE section_id = @sectionId AND revision = @revision
                         """))
        {
            delete.Parameters.AddWithValue("sectionId", sectionId);
            delete.Parameters.AddWithValue("revision", expectedRevision);
            if (await delete.ExecuteNonQueryAsync() != 1)
                throw new InvalidDataException("The locked form-section draft changed unexpectedly.");
        }
        return new FormSectionAuthoringPublishResult(
            FormSectionAuthoringPublishStatus.Published, published, 0);
    });

    private async Task<(bool SectionExists, FormSectionAuthoringDraft? Draft)> ReadState(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid sectionId)
    {
        await using var command = session.CreateCommand(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM {schema}.form_section_metadata WHERE section_id = @sectionId),
                   (SELECT body FROM {schema}.form_section_authoring_drafts WHERE section_id = @sectionId)
            """);
        command.Parameters.AddWithValue("sectionId", sectionId);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetBoolean(0), reader.IsDBNull(1)
            ? null
            : StorageJson.Deserialize<FormSectionAuthoringDraft>(reader.GetString(1)));
    }

    private static async Task<IReadOnlyList<T>> ReadBodiesAsync<T>(NpgsqlCommand command)
    {
        var result = new List<T>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(StorageJson.Deserialize<T>(reader.GetString(0)));
        return result;
    }

    private static void AddDraftParameters(
        NpgsqlCommand command,
        FormSectionAuthoringDraft draft,
        long expectedRevision)
    {
        command.Parameters.AddWithValue("sectionId", draft.SectionId);
        command.Parameters.AddWithValue("revision", draft.Revision);
        command.Parameters.AddWithValue("expectedRevision", expectedRevision);
        var updatedAt = command.Parameters.AddWithValue("updatedAt", draft.UpdatedAtUtc);
        updatedAt.NpgsqlDbType = NpgsqlDbType.TimestampTz;
        command.Parameters.AddWithValue("body", StorageJson.Serialize(draft));
    }

    private static void ValidateMetadata(FormSectionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateSectionId(metadata.SectionId);
        ValidateName(metadata.Name);
    }

    private static void ValidateDraft(FormSectionAuthoringDraft draft, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateSectionId(draft.SectionId);
        if (expectedRevision < 0 || draft.Revision != checked(expectedRevision + 1))
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (draft.UpdatedByUserId == Guid.Empty)
            throw new ArgumentException("Updating user ID is required.", nameof(draft));
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.SectionData);
    }

    private static void ValidateSectionId(Guid sectionId)
    {
        if (sectionId == Guid.Empty)
            throw new ArgumentException("Form section ID is required.", nameof(sectionId));
    }

    private static void ValidateName(string name) => ArgumentException.ThrowIfNullOrWhiteSpace(name);
}
