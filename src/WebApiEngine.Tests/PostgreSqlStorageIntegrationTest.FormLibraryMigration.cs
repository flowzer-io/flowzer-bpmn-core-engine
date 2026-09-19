using FluentAssertions;
using Model;
using Npgsql;
using PostgreSqlStorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Das Upgrade von Schema 15 auf 16 uebernimmt Abschnittsmetadaten,
    // konkrete Versionen und den Entwurf IDs- und inhaltserhaltend, ohne einen bereits
    // veroeffentlichten Formularsnapshot zu veraendern.
    [Test]
    public async Task FormLibraryMigration_ShouldPreserveLegacySectionAndExistingFormSnapshot()
    {
        var migrationSchema = $"flowzer_form_library_{Guid.NewGuid():N}";
        var seed = new FormLibraryMigrationSeed
        {
            SectionId = Guid.NewGuid(),
            SectionVersionId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            SectionSchema = """
                {"display":"form","components":[{"type":"textfield","key":"street","label":"Straße"}]}
                """,
            ExistingFormId = Guid.NewGuid(),
            ExistingVersionId = Guid.NewGuid(),
            ExistingSnapshot = """{"components":[{"type":"textfield","key":"unchanged"}]}"""
        };

        try
        {
            await ApplyMigrationsBeforeFormLibrary(migrationSchema);
            await SeedLegacySectionAndExistingForm(migrationSchema, seed);

            // 16 laeuft, und nichts darunter laeuft erneut. Bewusst keine Gleichheit auf {16}:
            // Jede spaetere additive Migration stuende sonst hier und haette mit dem Zweck
            // dieses Tests — dem Upgrade von 15 auf 16 — nichts zu tun.
            (await PostgreSqlMigrator.ApplyAsync(_connectionString, migrationSchema))
                .Should().Contain(16).And.OnlyContain(version => version >= 16);

            using var storage = new PostgreSqlStorage(_dataSource!, migrationSchema);
            (await storage.FormStorage.GetFormMetaData(seed.SectionId)).Name.Should().Be("Adresse");
            var migratedVersion = (await storage.FormStorage.GetForms(seed.SectionId)).Should().ContainSingle().Which;
            migratedVersion.Id.Should().Be(seed.SectionVersionId);
            migratedVersion.Version.Should().Be(new Model.Version(1, 3));
            migratedVersion.FormData.Should().Be(seed.SectionSchema);
            var migratedDraft = await storage.FormAuthoringStorage.Get(seed.SectionId);
            migratedDraft.Should().NotBeNull();
            migratedDraft!.Revision.Should().Be(4);
            migratedDraft.UpdatedByUserId.Should().Be(seed.ActorId);
            migratedDraft.FormData.Should().Be(seed.SectionSchema);
            (await storage.FormStorage.GetForm(seed.ExistingVersionId)).FormData.Should().Be(seed.ExistingSnapshot);
        }
        finally
        {
            await using var connection = await _dataSource!.OpenConnectionAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {Quote(migrationSchema)} CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedLegacySectionAndExistingForm(
        string schema,
        FormLibraryMigrationSeed seed)
    {
        var sectionMetadata = new FormSectionMetadata(seed.SectionId, "Adresse");
        var sectionVersion = new FormSectionVersion(
            seed.SectionVersionId,
            seed.SectionId,
            new Model.Version(1, 3),
            seed.SectionSchema);
        var sectionDraft = new FormSectionAuthoringDraft(
            seed.SectionId,
            4,
            seed.ActorId,
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"),
            seed.SectionVersionId,
            new Model.Version(1, 3),
            seed.SectionSchema);
        var existingMetadata = new FormMetadata { FormId = seed.ExistingFormId, Name = "Unveraendert" };
        var existingForm = new Form
        {
            Id = seed.ExistingVersionId,
            FormId = seed.ExistingFormId,
            Version = new Model.Version(2, 1),
            FormData = seed.ExistingSnapshot
        };

        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"""
            INSERT INTO {Quote(schema)}.form_section_metadata (section_id, name, body)
            VALUES (@sectionId, @sectionName, @sectionMetadata);
            INSERT INTO {Quote(schema)}.form_section_versions
                (id, section_id, version_major, version_minor, body)
            VALUES (@sectionVersionId, @sectionId, 1, 3, @sectionVersion);
            INSERT INTO {Quote(schema)}.form_section_authoring_drafts
                (section_id, revision, updated_at, body)
            VALUES (@sectionId, 4, @updatedAt, @sectionDraft);
            INSERT INTO {Quote(schema)}.form_metadata (form_id, body)
            VALUES (@existingFormId, @existingMetadata);
            INSERT INTO {Quote(schema)}.forms
                (id, form_id, version_major, version_minor, body)
            VALUES (@existingVersionId, @existingFormId, 2, 1, @existingForm);
            """, connection);
        command.Parameters.AddWithValue("sectionId", seed.SectionId);
        command.Parameters.AddWithValue("sectionName", sectionMetadata.Name);
        command.Parameters.AddWithValue("sectionMetadata", StorageJson.Serialize(sectionMetadata));
        command.Parameters.AddWithValue("sectionVersionId", seed.SectionVersionId);
        command.Parameters.AddWithValue("sectionVersion", StorageJson.Serialize(sectionVersion));
        command.Parameters.AddWithValue("updatedAt", sectionDraft.UpdatedAtUtc);
        command.Parameters.AddWithValue("sectionDraft", StorageJson.Serialize(sectionDraft));
        command.Parameters.AddWithValue("existingFormId", seed.ExistingFormId);
        command.Parameters.AddWithValue("existingMetadata", StorageJson.Serialize(existingMetadata));
        command.Parameters.AddWithValue("existingVersionId", seed.ExistingVersionId);
        command.Parameters.AddWithValue("existingForm", StorageJson.Serialize(existingForm));
        await command.ExecuteNonQueryAsync();
    }

    private async Task ApplyMigrationsBeforeFormLibrary(string schema)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var setup = new NpgsqlCommand($"""
            CREATE SCHEMA {Quote(schema)};
            CREATE TABLE {Quote(schema)}.schema_migrations
                (version integer PRIMARY KEY, name text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now());
            """, connection, transaction))
        {
            await setup.ExecuteNonQueryAsync();
        }

        foreach (var (version, name, sql) in EmbeddedMigrations().Where(item => item.Version < 16))
        {
            await using var migration = new NpgsqlCommand(
                sql.Replace("{schema}", Quote(schema), StringComparison.Ordinal),
                connection,
                transaction);
            await migration.ExecuteNonQueryAsync();
            await using var history = new NpgsqlCommand($"""
                INSERT INTO {Quote(schema)}.schema_migrations (version, name)
                VALUES (@version, @name)
                """, connection, transaction);
            history.Parameters.AddWithValue("version", version);
            history.Parameters.AddWithValue("name", name);
            await history.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static IReadOnlyList<(int Version, string Name, string Sql)> EmbeddedMigrations()
    {
        var assembly = typeof(PostgreSqlMigrator).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(resourceName =>
            {
                var name = resourceName.Split('.')[^2];
                var separator = name.IndexOf('_');
                var version = int.Parse(name[..separator], System.Globalization.CultureInfo.InvariantCulture);
                using var stream = assembly.GetManifestResourceStream(resourceName)!;
                using var reader = new StreamReader(stream);
                return (Version: version, Name: name, Sql: reader.ReadToEnd());
            })
            .OrderBy(item => item.Version)
            .ToArray();
    }

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed record FormLibraryMigrationSeed
    {
        public required Guid SectionId { get; init; }
        public required Guid SectionVersionId { get; init; }
        public required Guid ActorId { get; init; }
        public required string SectionSchema { get; init; }
        public required Guid ExistingFormId { get; init; }
        public required Guid ExistingVersionId { get; init; }
        public required string ExistingSnapshot { get; init; }
    }
}
