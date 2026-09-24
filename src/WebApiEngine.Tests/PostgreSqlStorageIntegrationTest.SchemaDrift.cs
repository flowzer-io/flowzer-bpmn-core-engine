using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PostgreSqlStorageSystem;
using WebApiEngine.Persistence;

namespace WebApiEngine.Tests;

/// <summary>
/// Schemadrift nach einem Update: Ein schrittweise aktualisiertes Schema darf sich strukturell
/// nicht von einem frisch angelegten unterscheiden. Sonst liefe eine Bestandsinstallation auf
/// anderen Tabellen, Indizes oder Constraints als jede Neuinstallation und jeder Test.
/// </summary>
public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Ein Schema, das von der aeltesten Baseline (bis 012) ueber den `--migrate`-Weg
    // auf den aktuellen Stand gehoben wurde, hat dieselben Tabellen, Spalten, Indizes,
    // Constraints, Routinen und Trigger wie ein frisch mit allen Migrationen angelegtes Schema,
    // und beide Historien fuehren dieselben Versionen. Abweichungen werden einzeln benannt.
    [Test]
    public async Task SchemaDrift_UpgradedSchemaShouldMatchFreshlyMigratedSchema()
    {
        var upgradedSchema = $"flowzer_drift_upgraded_{Guid.NewGuid():N}";
        var freshSchema = $"flowzer_drift_fresh_{Guid.NewGuid():N}";

        try
        {
            await ApplyMigrationsBelow(upgradedSchema, OldestUpgradeBaselineExclusiveMaxVersion);
            (await FlowzerStorageExtensions.RunMigrationsAsync(MigrationConfiguration(upgradedSchema), NullLogger.Instance))
                .Should().Be(0);
            (await FlowzerStorageExtensions.RunMigrationsAsync(MigrationConfiguration(freshSchema), NullLogger.Instance))
                .Should().Be(0);

            var upgraded = await DescribeSchemaAsync(upgradedSchema);
            var fresh = await DescribeSchemaAsync(freshSchema);

            // Gegenprobe, dass der Katalog ueberhaupt gelesen wurde: Ein leerer Vergleich
            // waere immer gruen.
            fresh.Columns.Should().Contain("definitions.body | text | not null | ");
            fresh.Indexes.Should().NotBeEmpty();
            fresh.Constraints.Should().NotBeEmpty();

            using (new AssertionScope())
            {
                ShouldMatch("Relationen", upgraded.Relations, fresh.Relations);
                ShouldMatch("Spalten", upgraded.Columns, fresh.Columns);
                ShouldMatch("Indizes", upgraded.Indexes, fresh.Indexes);
                ShouldMatch("Constraints", upgraded.Constraints, fresh.Constraints);
                ShouldMatch("Routinen", upgraded.Routines, fresh.Routines);
                ShouldMatch("Trigger", upgraded.Triggers, fresh.Triggers);
                ShouldMatch("Historieneintraege (schema_migrations)", upgraded.History, fresh.History);
            }

            // Und zwar jede eingebettete Migration genau einmal, unter ihrem Dateinamen.
            upgraded.History.Should().Equal(
                EmbeddedMigrations().Select(migration => $"{migration.Version:D3} | {migration.Name}"));
        }
        finally
        {
            await DropSchemaAsync(upgradedSchema);
            await DropSchemaAsync(freshSchema);
        }
    }

    private static void ShouldMatch(string kind, IReadOnlyList<string> upgraded, IReadOnlyList<string> fresh)
    {
        upgraded.Except(fresh, StringComparer.Ordinal).Should()
            .BeEmpty("das aktualisierte Schema keine zusaetzlichen {0} haben darf", kind);
        fresh.Except(upgraded, StringComparer.Ordinal).Should()
            .BeEmpty("dem aktualisierten Schema keine {0} des frischen fehlen duerfen", kind);
        // Die Mengenvergleiche uebersehen doppelte Zeilen; die Anzahl faengt sie ab, ohne die
        // Meldung mit beiden vollstaendigen Listen zu ueberfrachten.
        upgraded.Count.Should().Be(fresh.Count, "{0} in beiden Schemata gleich oft vorkommen muessen", kind);
    }

    /// <summary>
    /// Liest die Struktur eines Schemas aus pg_catalog als sortierte Textzeilen. Der Schemaname
    /// wird durch <c>&lt;schema&gt;</c> ersetzt, damit zwei Schemata direkt vergleichbar sind.
    /// </summary>
    private async Task<SchemaDescription> DescribeSchemaAsync(string schema)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();

        async Task<IReadOnlyList<string>> ReadAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("schema", schema);
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                var values = Enumerable.Range(0, reader.FieldCount)
                    .Select(index => reader.IsDBNull(index) ? string.Empty : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture)!);
                rows.Add(string.Join(" | ", values)
                    .Replace($"{Quote(schema)}.", "<schema>.", StringComparison.Ordinal)
                    .Replace($"{schema}.", "<schema>.", StringComparison.Ordinal));
            }

            rows.Sort(StringComparer.Ordinal);
            return rows;
        }

        return new SchemaDescription(
            Relations: await ReadAsync("""
                SELECT c.relkind::text, c.relname
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema
                """),
            Columns: await ReadAsync("""
                SELECT c.relname || '.' || a.attname,
                       format_type(a.atttypid, a.atttypmod),
                       CASE WHEN a.attnotnull THEN 'not null' ELSE 'null' END,
                       pg_get_expr(d.adbin, d.adrelid)
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
                WHERE n.nspname = @schema AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
                  AND a.attnum > 0 AND NOT a.attisdropped
                """),
            Indexes: await ReadAsync("""
                SELECT tablename, indexname, indexdef FROM pg_indexes WHERE schemaname = @schema
                """),
            Constraints: await ReadAsync("""
                SELECT coalesce(c.relname, ''), con.conname, con.contype::text, pg_get_constraintdef(con.oid)
                FROM pg_constraint con
                JOIN pg_namespace n ON n.oid = con.connamespace
                LEFT JOIN pg_class c ON c.oid = con.conrelid
                WHERE n.nspname = @schema
                """),
            Routines: await ReadAsync("""
                SELECT p.proname, pg_get_function_identity_arguments(p.oid),
                       md5(replace(pg_get_functiondef(p.oid), @schema, '<schema>'))
                FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = @schema AND p.prokind IN ('f', 'p')
                """),
            Triggers: await ReadAsync("""
                SELECT c.relname, t.tgname, pg_get_triggerdef(t.oid)
                FROM pg_trigger t
                JOIN pg_class c ON c.oid = t.tgrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema AND NOT t.tgisinternal
                """),
            // Dreistellig, damit die Textsortierung der Versionsreihenfolge entspricht.
            History: await ReadAsync($"""
                SELECT to_char(version, 'FM000'), name FROM {Quote(schema)}.schema_migrations
                """));
    }

    private sealed record SchemaDescription(
        IReadOnlyList<string> Relations,
        IReadOnlyList<string> Columns,
        IReadOnlyList<string> Indexes,
        IReadOnlyList<string> Constraints,
        IReadOnlyList<string> Routines,
        IReadOnlyList<string> Triggers,
        IReadOnlyList<string> History);
}
