using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Zwei PostgreSQL-Adapter koennen dieselbe erwartete Revision nicht beide
    // schreiben; Namenseindeutigkeit und CAS werden in der Datenbank atomar erzwungen.
    [Test]
    public async Task AiConnectionStorage_ShouldEnforceRevisionAndUniqueNameAcrossProcesses()
    {
        var first = new PostgreSqlStorage(_dataSource!, Schema);
        var second = new PostgreSqlStorage(_dataSource!, Schema);
        var id = Guid.NewGuid();
        var initial = CreateAiConnection(id, "Produktion", 1, "env:FLOWZER_AI_FIRST");
        (await first.AiConnectionStorage.TryCreate(initial)).Status.Should().Be(AiConnectionWriteStatus.Written);

        var writes = await Task.WhenAll(
            first.AiConnectionStorage.TryUpdate(initial with { Name = "Produktion A", Revision = 2 }, 1),
            second.AiConnectionStorage.TryUpdate(initial with { Name = "Produktion B", Revision = 2 }, 1));

        writes.Should().ContainSingle(result => result.Status == AiConnectionWriteStatus.Written);
        writes.Should().ContainSingle(result => result.Status == AiConnectionWriteStatus.Conflict);
        var current = (await first.AiConnectionStorage.Get(id))!;
        current.Revision.Should().Be(2);
        writes.Single(result => result.Status == AiConnectionWriteStatus.Conflict).CurrentRevision.Should().Be(2);

        var duplicate = await second.AiConnectionStorage.TryCreate(
            CreateAiConnection(Guid.NewGuid(), current.Name.ToUpperInvariant(), 1, "env:FLOWZER_AI_SECOND"));
        duplicate.Status.Should().Be(AiConnectionWriteStatus.Conflict);
        (await second.AiConnectionStorage.List()).Should().ContainSingle();
    }

    private static AiConnection CreateAiConnection(Guid id, string name, long revision, string secretReference) => new(
        id,
        name,
        AiProviderKind.OpenAi,
        AiProcessingLocation.Cloud,
        null,
        "gpt-example",
        secretReference,
        true,
        revision,
        new DateTimeOffset(2026, 9, 9, 16, 0, 0, TimeSpan.Zero),
        Guid.Parse("C3333333-3333-4333-8333-333333333333"));
}
