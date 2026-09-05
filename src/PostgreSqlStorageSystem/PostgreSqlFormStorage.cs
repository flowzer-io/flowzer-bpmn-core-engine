using Model;
using StorageSystem;
using Version = Model.Version;

namespace PostgreSqlStorageSystem;

internal sealed class PostgreSqlFormStorage(PostgreSqlSession session) : IFormStorage
{
    public Task SaveFormMetaData(FormMetadata formMetadata) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction,
            "INSERT INTO {schema}.form_metadata (form_id, body) VALUES (@formId, @body) ON CONFLICT (form_id) DO UPDATE SET body = EXCLUDED.body");
        command.Parameters.AddWithValue("formId", formMetadata.FormId);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(formMetadata));
        await command.ExecuteNonQueryAsync();
    });

    public Task<FormMetadata> GetFormMetaData(Guid formId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT body FROM {schema}.form_metadata WHERE form_id = @formId");
        command.Parameters.AddWithValue("formId", formId);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null
            ? throw new FileNotFoundException($"Form metadata not found with id: {formId}")
            : StorageJson.Deserialize<FormMetadata>(body);
    });

    public Task<IEnumerable<FormMetadata>> GetFormMetadatas() => session.RunAsync<IEnumerable<FormMetadata>>(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT body FROM {schema}.form_metadata");
        return await PostgreSqlDefinitionStorage.ReadBodiesAsync<FormMetadata>(command);
    });

    public async Task UpdateFormMetaData(FormMetadata formMetaData)
    {
        var updated = await session.RunAsync(async (connection, transaction) =>
        {
            await using var command = session.CreateCommand(connection, transaction, "UPDATE {schema}.form_metadata SET body = @body WHERE form_id = @formId");
            command.Parameters.AddWithValue("formId", formMetaData.FormId);
            command.Parameters.AddWithValue("body", StorageJson.Serialize(formMetaData));
            return await command.ExecuteNonQueryAsync();
        });

        if (updated == 0)
        {
            throw new FileNotFoundException($"Form metadata not found with id: {formMetaData.FormId}");
        }
    }

    public Task DeleteFormMetaData(Guid formId) => session.RunAsync(async (connection, transaction) =>
    {
        await using var deleteMetadata = session.CreateCommand(connection, transaction, "DELETE FROM {schema}.form_metadata WHERE form_id = @formId");
        deleteMetadata.Parameters.AddWithValue("formId", formId);
        await deleteMetadata.ExecuteNonQueryAsync();

        // Zu einer Form gehoerende Versionen werden gemeinsam mit dem Metadatensatz entfernt.
        await using var deleteForms = session.CreateCommand(connection, transaction, "DELETE FROM {schema}.forms WHERE form_id = @formId");
        deleteForms.Parameters.AddWithValue("formId", formId);
        await deleteForms.ExecuteNonQueryAsync();
    });

    public Task SaveForm(Form form) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, """
            INSERT INTO {schema}.forms (id, form_id, version_major, version_minor, body)
            VALUES (@id, @formId, @major, @minor, @body)
            ON CONFLICT (id) DO UPDATE SET form_id = EXCLUDED.form_id, version_major = EXCLUDED.version_major, version_minor = EXCLUDED.version_minor, body = EXCLUDED.body
            """);
        command.Parameters.AddWithValue("id", form.Id);
        command.Parameters.AddWithValue("formId", form.FormId);
        command.Parameters.AddWithValue("major", form.Version.Major);
        command.Parameters.AddWithValue("minor", form.Version.Minor);
        command.Parameters.AddWithValue("body", StorageJson.Serialize(form));
        await command.ExecuteNonQueryAsync();
    });

    public Task<Form> GetForm(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT body FROM {schema}.forms WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        var body = await command.ExecuteScalarAsync() as string;
        return body is null
            ? throw new FileNotFoundException("Form not found with id: " + id)
            : StorageJson.Deserialize<Form>(body);
    });

    public Task<IEnumerable<Form>> GetForms(Guid formId) => session.RunAsync<IEnumerable<Form>>(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "SELECT body FROM {schema}.forms WHERE form_id = @formId ORDER BY version_major, version_minor");
        command.Parameters.AddWithValue("formId", formId);
        return await PostgreSqlDefinitionStorage.ReadBodiesAsync<Form>(command);
    });

    public Task DeleteForm(Guid id) => session.RunAsync(async (connection, transaction) =>
    {
        await using var command = session.CreateCommand(connection, transaction, "DELETE FROM {schema}.forms WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    });

    public async Task<Version> GetMaxVersion(Guid formId)
    {
        var forms = (await GetForms(formId)).ToList();
        return forms.Count == 0 ? new Version() : forms.Max(form => form.Version) ?? new Version();
    }
}
