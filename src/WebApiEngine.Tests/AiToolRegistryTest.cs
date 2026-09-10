using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Model;
using WebApiEngine.Ai;
using WebApiEngine.Controller;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Vertragstests fuer die serverseitig geschlossene KI-Werkzeugregistry.</summary>
public sealed class AiToolRegistryTest
{
    // Testzweck: Ein registriertes Werkzeug erhaelt einen deterministischen Vertragshash und
    // wird unabhaengig von der DI-Reihenfolge stabil nach ID und Version katalogisiert.
    [Test]
    public void Registry_ShouldExposeValidatedVersionedContractsInStableOrder()
    {
        var later = Tool("flowzer.zeta.read", 2);
        var first = Tool("flowzer.alpha.read", 1);

        var registry = new AiToolRegistry([later, first]);

        registry.List().Select(item => (item.Definition.Id, item.Definition.Version)).Should().Equal(
            ("flowzer.alpha.read", 1),
            ("flowzer.zeta.read", 2));
        registry.Require("flowzer.alpha.read", 1).ContractHash.Should().MatchRegex("^[A-F0-9]{64}$");
        new AiToolRegistry([first]).Require("flowzer.alpha.read", 1).ContractHash
            .Should().Be(registry.Require("flowzer.alpha.read", 1).ContractHash);
    }

    // Testzweck: Zwei Implementierungen derselben stabilen ID und Version duerfen den
    // effektiven Handler nicht von der DI-Reihenfolge abhaengig machen.
    [Test]
    public void Registry_ShouldRejectDuplicateIdentity()
    {
        var action = () => new AiToolRegistry([
            Tool("flowzer.directory.lookup", 1),
            Tool("flowzer.directory.lookup", 1)
        ]);

        action.Should().Throw<InvalidOperationException>().WithMessage("*duplicate*");
    }

    // Testzweck: Werkzeuge koennen keine unbeschraenkten oder unportablen JSON-Schemas in
    // Modellprompts einschleusen; beide Richtungen verwenden das begrenzte Flowzer-Profil.
    [Test]
    public void Registry_ShouldRejectUnsupportedSchema()
    {
        var invalid = new FakeTool(new AiToolDefinition(
            "flowzer.invalid.read",
            1,
            "Invalid",
            "Invalid schema",
            "{\"type\":\"object\",\"$ref\":\"https://example.invalid/schema\"}",
            ObjectSchema,
            AiToolSideEffect.ReadOnly,
            AllowsPreApproval: false));

        var action = () => new AiToolRegistry([invalid]);

        action.Should().Throw<ArgumentException>().WithMessage("*schema*");
    }

    // Testzweck: Die Katalog-API projiziert ausschliesslich den stabilen, nicht geheimen
    // Vertrag und gibt weder Implementierungstyp noch Laufzeitkonfiguration aus.
    [Test]
    public void Controller_ShouldReturnSafeCatalogProjection()
    {
        var registry = new AiToolRegistry([Tool("flowzer.directory.lookup", 1)]);

        var response = new AiToolController(registry).List();

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ApiStatusResult<AiToolDto[]>>().Subject;
        var item = payload.Result.Should().ContainSingle().Subject;
        item.Id.Should().Be("flowzer.directory.lookup");
        item.SideEffect.Should().Be(AiToolSideEffectDto.ReadOnly);
        item.ContractHash.Should().MatchRegex("^[A-F0-9]{64}$");
        typeof(AiToolDto).GetProperty("Implementation").Should().BeNull();
    }

    private static FakeTool Tool(string id, int version) => new(new AiToolDefinition(
        id,
        version,
        "Directory lookup",
        "Reads a bounded directory entry.",
        ObjectSchema,
        ObjectSchema,
        AiToolSideEffect.ReadOnly,
        AllowsPreApproval: false));

    private sealed class FakeTool(AiToolDefinition definition) : IAiTool
    {
        public AiToolDefinition Definition { get; } = definition;

        public ValueTask<AiToolExecutionResult> ExecuteAsync(
            AiToolExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AiToolExecutionResult(JsonDocument.Parse("{}").RootElement.Clone()));
    }

    private const string ObjectSchema = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";
}
