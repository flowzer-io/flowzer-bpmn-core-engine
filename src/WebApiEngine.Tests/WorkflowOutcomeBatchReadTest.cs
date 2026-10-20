using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Reale I/O-Zähler am bestehenden HTTP-Listenvertrag; keine Produktions-Testinstrumentierung.</summary>
[NonParallelizable]
public sealed class WorkflowOutcomeBatchReadTest
{
    // Testzweck: Zwei eigene abgeschlossene Instanzen derselben Version laden Definition/XML nur einmal je Outcome-Batch;
    // der bestehende VersionDto-Mapper liest die Definition zusätzlich genau einmal und bleibt separat ausgewiesen.
    [Test]
    public async Task HttpList_ShouldReadSameDefinitionProofOnlyOnce()
    {
        var counts = new ReadCounts();
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true,
            configureServices: services => services.AddSingleton<IStorageSystem>(_ => CreateCountingStorage(new Storage(), counts)));
        await context.Demo.StartAsync();
        await context.ApproveAsync();
        counts.Clear();
        using var owner = context.Demo.Client();
        var result = await owner.GetFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto[]>>("/instance");
        result!.Result.Should().HaveCount(2).And.OnlyContain(instance => instance.Outcome == ProcessInstanceOutcomeDto.Approved);
        counts.DefinitionReads.GetValueOrDefault(context.Demo.DefinitionId).Should().Be(2,
            "ein Definitionread gehört zum bestehenden VersionDto-Batch, genau einer zur Outcome-Proof-Prüfung");
        counts.BinaryReads.GetValueOrDefault(context.Demo.DefinitionId).Should().Be(1);
    }

    // Testzweck: Ein gültiger Versionsproof ist keine geteilte Entscheidung. Zustand, Mastermodell, Rootende und Parentgraph
    // bleiben für jede Instanz geprüft, auch wenn die inkonsistente Instanz zuerst im Batch steht.
    [TestCase("state", false)]
    [TestCase("state", true)]
    [TestCase("master", false)]
    [TestCase("master", true)]
    [TestCase("root-ends", false)]
    [TestCase("root-ends", true)]
    [TestCase("parent", false)]
    [TestCase("parent", true)]
    public async Task SharedProof_ShouldKeepInstanceDecisionsSeparate(string scenario, bool invalidFirst)
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        var counts = new ReadCounts();
        var projector = CountingProjector(context, counts);
        var valid = await context.Demo.InstanceAsync();
        var invalid = await context.Demo.InstanceAsync();
        var master = invalid.Tokens.Single(token => token.ParentTokenId is null);
        switch (scenario)
        {
            case "state": invalid.IsFinished = false; break;
            case "master": ((BPMN.Process.Process)master.CurrentBaseElement).FlowElements.RemoveAll(element => element.Id == "End_Approved"); break;
            case "root-ends":
                var end = invalid.Tokens.Single(token => token.CurrentBaseElement.Id == "End_Approved");
                invalid.Tokens.Add(WorkflowOutcomeProjectionSafetyTest.Copy(end,
                    new BPMN.Events.EndEvent { Id = "End_Rejected", Name = "Synthetic" }));
                break;
            case "parent":
                var child = invalid.Tokens.First(token => token.ParentTokenId == master.Id);
                invalid.Tokens.Add(WorkflowOutcomeProjectionSafetyTest.Copy(child, parent: Guid.NewGuid()));
                break;
        }
        var results = await projector.ProjectBatchAsync(invalidFirst ? [invalid, valid] : [valid, invalid]);
        results.Should().Equal(invalidFirst
            ? [ProcessInstanceOutcomeDto.Unknown, ProcessInstanceOutcomeDto.Approved]
            : [ProcessInstanceOutcomeDto.Approved, ProcessInstanceOutcomeDto.Unknown]);
        counts.DefinitionReads[context.Demo.DefinitionId].Should().Be(1);
        counts.BinaryReads[context.Demo.DefinitionId].Should().Be(1);
    }

    // Testzweck: Verschiedene GUID-Versionen erhalten getrennte Proofs statt eines Katalog-/latest-Caches.
    [Test]
    public async Task DifferentDefinitionVersions_ShouldHaveSeparateReads()
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        var (first, second) = await SecondVersionAsync(context);
        var counts = new ReadCounts();
        var results = await CountingProjector(context, counts).ProjectBatchAsync([first, second, first, second]);
        results.Should().OnlyContain(result => result == ProcessInstanceOutcomeDto.Approved);
        counts.DefinitionReads.Should().HaveCount(2).And.OnlyContain(read => read.Value == 1);
        counts.BinaryReads.Should().HaveCount(2).And.OnlyContain(read => read.Value == 1);
    }

    // Testzweck: Dasselbe Projektorobjekt darf keinen Cache über einen Batch-/Single-Aufruf hinaus behalten;
    // Binaryoverwrite und Wiederherstellung unter derselben GUID werden jeweils frisch erkannt.
    [Test]
    public async Task NextBatchAndSingleCalls_ShouldSeeBinaryOverwrite()
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        var counts = new ReadCounts();
        var projector = CountingProjector(context, counts);
        var instance = await context.Demo.InstanceAsync();
        var xml = await context.Demo.Storage.DefinitionStorage.GetBinary(instance.DefinitionId);
        (await projector.ProjectBatchAsync([instance, instance])).Should().OnlyContain(result => result == ProcessInstanceOutcomeDto.Approved);
        counts.BinaryReads[instance.DefinitionId].Should().Be(1);
        await context.Demo.Storage.DefinitionStorage.StoreBinary(instance.DefinitionId, xml + "\n");
        counts.Clear();
        (await projector.ProjectBatchAsync([instance, instance])).Should().OnlyContain(result => result == ProcessInstanceOutcomeDto.Unknown);
        counts.BinaryReads[instance.DefinitionId].Should().Be(1);
        counts.Clear();
        (await projector.ProjectAsync(instance)).Should().Be(ProcessInstanceOutcomeDto.Unknown);
        counts.BinaryReads[instance.DefinitionId].Should().Be(1);
        await context.Demo.Storage.DefinitionStorage.StoreBinary(instance.DefinitionId, xml);
        counts.Clear();
        (await projector.ProjectAsync(instance)).Should().Be(ProcessInstanceOutcomeDto.Approved);
        counts.BinaryReads[instance.DefinitionId].Should().Be(1);
    }

    // Testzweck: Ein technischer Fehler beim zweiten Versionsproof bricht den Batch ab; die erste gültige Instanz
    // darf weder Teilfreigabe noch stilles Löschen der zweiten Instanz im Caller bewirken.
    [Test]
    public async Task TechnicalFailureInSecondVersion_ShouldAbortEntireBatch()
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        var (first, second) = await SecondVersionAsync(context);
        var counts = new ReadCounts { FailingBinaryId = second.DefinitionId };
        var projector = CountingProjector(context, counts);
        Func<Task> execute = async () => _ = await projector.ProjectBatchAsync([first, second]);
        await execute.Should().ThrowAsync<IOException>().WithMessage("synthetic binary failure");
        counts.BinaryReads[first.DefinitionId].Should().Be(1);
        counts.BinaryReads[second.DefinitionId].Should().Be(1);
    }

    // Testzweck: Der reale HTTP-Listenvertrag liefert bei technischem I/O-Fehler weiterhin sicheren API-Fehler,
    // keine fälschlich leere Liste, keine Genehmigung und keine rohe Storagefehlermeldung.
    [Test]
    public async Task HttpListTechnicalReadFailure_ShouldRemainAnApiError()
    {
        var counts = new ReadCounts();
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true,
            configureServices: services => services.AddSingleton<IStorageSystem>(_ => CreateCountingStorage(new Storage(), counts)));
        counts.FailingBinaryId = context.Demo.DefinitionId;
        using var owner = context.Demo.Client();
        using var response = await owner.GetAsync("/instance");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("synthetic binary failure").And.NotContain("Approved");
    }

    private static WorkflowOutcomeProjector CountingProjector(WorkflowOutcomeTestContext context, ReadCounts counts) =>
        new(CreateCountingStorage(context.Demo.Storage, counts), Options.Create(context.Options));

    private static async Task<(ProcessInstanceInfo First, ProcessInstanceInfo Second)> SecondVersionAsync(WorkflowOutcomeTestContext context)
    {
        var first = await context.Demo.InstanceAsync();
        var second = await context.Demo.InstanceAsync();
        var original = await context.Demo.Storage.DefinitionStorage.GetDefinitionById(first.DefinitionId);
        var secondId = Guid.NewGuid();
        await context.Demo.Storage.DefinitionStorage.StoreDefinition(new BpmnDefinition
        {
            Id = secondId, DefinitionId = original.DefinitionId, Hash = original.Hash,
            SavedByUser = original.SavedByUser, SavedOn = original.SavedOn, Version = new Model.Version(2, 0), IsActive = false
        });
        await context.Demo.Storage.DefinitionStorage.StoreBinary(secondId,
            await context.Demo.Storage.DefinitionStorage.GetBinary(first.DefinitionId));
        var mapping = context.Mapping;
        context.Options.Definitions.Add(new WorkflowOutcomeDefinitionOptions
        {
            DefinitionId = secondId.ToString("D"), CatalogId = mapping.CatalogId, ProcessId = mapping.ProcessId,
            ApprovedRootEndId = mapping.ApprovedRootEndId, RejectedRootEndId = mapping.RejectedRootEndId, BpmnSha256 = mapping.BpmnSha256
        });
        second.DefinitionId = secondId;
        return (first, second);
    }

    internal static IStorageSystem CreateCountingStorage(IStorageSystem target, ReadCounts counts)
    {
        var definitions = DispatchProxy.Create<IDefinitionStorage, DefinitionReadProxy>();
        var definitionProxy = (DefinitionReadProxy)definitions;
        definitionProxy.Target = target.DefinitionStorage;
        definitionProxy.Counts = counts;
        var storage = DispatchProxy.Create<IStorageSystem, StorageReadProxy>();
        var storageProxy = (StorageReadProxy)storage;
        storageProxy.Target = target;
        storageProxy.Definitions = definitions;
        return storage;
    }

    /// <summary>Ausschließlich testlokale Zähler der tatsächlich aufgerufenen Storage-Lesemethoden.</summary>
    internal sealed class ReadCounts
    {
        internal Dictionary<Guid, int> DefinitionReads { get; } = [];
        internal Dictionary<Guid, int> BinaryReads { get; } = [];
        internal Guid? FailingBinaryId { get; set; }
        internal void Clear() { DefinitionReads.Clear(); BinaryReads.Clear(); }
    }

    /// <summary>Durchsichtiger Testproxy; alle anderen Storageoperationen behalten ihren echten lokalen Pfad.</summary>
    public class DefinitionReadProxy : DispatchProxy
    {
        internal IDefinitionStorage Target { get; set; } = null!;
        internal ReadCounts Counts { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IDefinitionStorage.GetDefinitionById))
            {
                var id = (Guid)args![0]!;
                Counts.DefinitionReads[id] = Counts.DefinitionReads.GetValueOrDefault(id) + 1;
            }
            if (method.Name == nameof(IDefinitionStorage.GetBinary))
            {
                var id = (Guid)args![0]!;
                Counts.BinaryReads[id] = Counts.BinaryReads.GetValueOrDefault(id) + 1;
                if (Counts.FailingBinaryId == id) return Task.FromException<string>(new IOException("synthetic binary failure"));
            }
            return method.Invoke(Target, args);
        }
    }

    /// <summary>Nur die Definitionsleseseite wird beobachtet, weder Transaktionen noch Engine-/Aufgabenrechte ersetzt.</summary>
    public class StorageReadProxy : DispatchProxy
    {
        internal IStorageSystem Target { get; set; } = null!;
        internal IDefinitionStorage Definitions { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == "get_DefinitionStorage" ? Definitions : method.Invoke(Target, args);
    }
}
