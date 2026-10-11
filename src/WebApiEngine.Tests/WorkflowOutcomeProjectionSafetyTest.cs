using BPMN.Events;
using BPMN.Foundation;
using BPMN.Process;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Negativpfade auf frisch gespeicherten Originaldemo-Snapshots; keine Engine-/Auth-Lockerungen.</summary>
[NonParallelizable]
public sealed class WorkflowOutcomeProjectionSafetyTest
{
    // Testzweck: Technischer State allein, unvollständige Rootspuren und Betriebseingriffe erlauben keine fachliche Aussage.
    [TestCase("not-finished")]
    [TestCase("failed-instance")]
    [TestCase("terminated-instance")]
    [TestCase("empty-instance")]
    [TestCase("empty-master-scope")]
    [TestCase("foreign-token-scope")]
    [TestCase("wrong-definition")]
    [TestCase("wrong-catalog")]
    [TestCase("wrong-process")]
    [TestCase("master-active")]
    [TestCase("master-wrong-process")]
    [TestCase("master-wrong-definitions")]
    [TestCase("master-missing-root-end")]
    [TestCase("two-masters")]
    [TestCase("withdrawn-master")]
    [TestCase("migration")]
    [TestCase("modification")]
    [TestCase("two-root-ends")]
    [TestCase("same-root-end-twice")]
    [TestCase("duplicate-token-id")]
    [TestCase("root-end-active")]
    [TestCase("root-end-failed")]
    [TestCase("root-end-terminated")]
    [TestCase("root-end-withdrawn")]
    [TestCase("root-end-missing")]
    [TestCase("nested-end-only")]
    [TestCase("root-token-active")]
    [TestCase("nested-token-failed")]
    [TestCase("orphan-token")]
    [TestCase("cyclic-parent")]
    public async Task InconsistentSnapshot_ShouldRemainUnknown(string scenario)
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        var instance = await context.Demo.InstanceAsync();
        var master = instance.Tokens.Single(token => token.ParentTokenId is null);
        var end = instance.Tokens.Single(token => token.ParentTokenId == master.Id && token.CurrentBaseElement.Id == "End_Approved");
        var root = instance.Tokens.First(token => token.ParentTokenId == master.Id && token != end);
        switch (scenario)
        {
            case "not-finished": instance.IsFinished = false; break;
            case "failed-instance": instance.State = ProcessInstanceState.Failed; break;
            case "terminated-instance": instance.State = ProcessInstanceState.Terminated; break;
            case "empty-instance": instance.InstanceId = Guid.Empty; break;
            case "empty-master-scope":
                instance.Tokens.Remove(master);
                instance.Tokens.Add(Copy(master, id: master.Id, scope: Guid.Empty));
                break;
            case "foreign-token-scope":
                instance.Tokens.Remove(root);
                instance.Tokens.Add(Copy(root, id: root.Id, scope: Guid.NewGuid()));
                break;
            case "wrong-definition": instance.DefinitionId = Guid.NewGuid(); break;
            case "wrong-catalog": instance.metaDefinitionId += "X"; break;
            case "wrong-process": instance.ProcessId += "X"; break;
            case "master-active": master.State = FlowNodeState.Active; break;
            case "master-wrong-process": Replace(instance, master, ((Process)master.CurrentBaseElement) with { Id = "other" }); break;
            case "master-wrong-definitions": Replace(instance, master, ((Process)master.CurrentBaseElement) with { DefinitionsId = "other" }); break;
            case "master-missing-root-end": ((Process)master.CurrentBaseElement).FlowElements.RemoveAll(element => element.Id == "End_Approved"); break;
            case "two-masters": instance.Tokens.Add(Copy(master)); break;
            case "withdrawn-master": master.Withdrawal = new ProcessWithdrawal(master.Initiator!, AuthenticatedWorkflowTestContext.UserId, DateTimeOffset.UtcNow); break;
            case "migration": instance.Migrations.Add(new(Guid.NewGuid(), instance.DefinitionId, DateTimeOffset.UtcNow, AuthenticatedWorkflowTestContext.UserId)); break;
            case "modification": instance.Modifications.Add(new(DateTimeOffset.UtcNow, AuthenticatedWorkflowTestContext.UserId, [], [], [])); break;
            case "two-root-ends": instance.Tokens.Add(Copy(end, new EndEvent { Id = "End_Rejected", Name = "Synthetic" })); break;
            case "same-root-end-twice": instance.Tokens.Add(Copy(end)); break;
            case "duplicate-token-id": instance.Tokens.Add(end); break;
            case "root-end-active": end.State = FlowNodeState.Active; break;
            case "root-end-failed": end.State = FlowNodeState.Failed; break;
            case "root-end-terminated": end.State = FlowNodeState.Terminated; break;
            case "root-end-withdrawn": end.State = FlowNodeState.Withdrawn; break;
            case "root-end-missing": instance.Tokens.Remove(end); break;
            case "nested-end-only":
                instance.Tokens.Remove(end);
                instance.Tokens.Add(Copy(end, parent: root.Id));
                break;
            case "root-token-active": root.State = FlowNodeState.Active; break;
            case "nested-token-failed": instance.Tokens.First(token => token.ParentTokenId != master.Id && token.ParentTokenId is not null).State = FlowNodeState.Failed; break;
            case "orphan-token": instance.Tokens.Add(Copy(root, parent: Guid.NewGuid())); break;
            case "cyclic-parent":
                var cyclicId = Guid.NewGuid();
                instance.Tokens.Add(Copy(root, parent: cyclicId, id: cyclicId));
                break;
        }
        (await context.Projector.ProjectAsync(instance)).Should().Be(ProcessInstanceOutcomeDto.Unknown);
    }

    // Testzweck: Exakte, valide und eindeutige Serverbindungen sind zwingend; Anzeigenamen/Variablen oder Hash-Metadaten zählen nicht.
    [TestCase("empty")]
    [TestCase("duplicate")]
    [TestCase("wrong-guid")]
    [TestCase("invalid-guid")]
    [TestCase("wrong-catalog")]
    [TestCase("wrong-process")]
    [TestCase("wrong-end")]
    [TestCase("equal-ends")]
    [TestCase("missing-hash")]
    [TestCase("invalid-hash")]
    [TestCase("wrong-hash")]
    [TestCase("wildcard")]
    [TestCase("case-mismatch")]
    public async Task InvalidOrAmbiguousConfiguration_ShouldRemainUnknown(string scenario)
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        var mapping = context.Mapping;
        switch (scenario)
        {
            case "empty": context.Options.Definitions.Clear(); break;
            case "duplicate": context.Options.Definitions.Add(mapping); break;
            case "wrong-guid": mapping.DefinitionId = Guid.NewGuid().ToString("D"); break;
            case "invalid-guid": mapping.DefinitionId = "latest"; break;
            case "wrong-catalog": mapping.CatalogId += "X"; break;
            case "wrong-process": mapping.ProcessId += "X"; break;
            case "wrong-end": mapping.ApprovedRootEndId = "End_ReviewRound"; break;
            case "equal-ends": mapping.RejectedRootEndId = mapping.ApprovedRootEndId; break;
            case "missing-hash": mapping.BpmnSha256 = ""; break;
            case "invalid-hash": mapping.BpmnSha256 = new string('Z', 64); break;
            case "wrong-hash": mapping.BpmnSha256 = new string('0', 64); break;
            case "wildcard": mapping.ProcessId = "*"; break;
            case "case-mismatch": mapping.CatalogId = mapping.CatalogId.ToUpperInvariant(); break;
        }
        (await context.Projector.ProjectAsync(await context.Demo.InstanceAsync())).Should().Be(ProcessInstanceOutcomeDto.Unknown);
    }

    private static void Replace(ProcessInstanceInfo instance, Token old, IBaseElement element)
    {
        instance.Tokens.Remove(old);
        instance.Tokens.Add(Copy(old, element, id: old.Id));
    }

    internal static Token Copy(Token source, IBaseElement? element = null, Guid? parent = null, Guid? id = null, Guid? scope = null) => new()
    {
        Id = id ?? Guid.NewGuid(), ProcessInstanceId = scope ?? source.ProcessInstanceId,
        ParentTokenId = parent ?? source.ParentTokenId, CurrentBaseElement = element ?? source.CurrentBaseElement,
        ActiveBoundaryEvents = [], State = source.State, Initiator = source.Initiator, Withdrawal = source.Withdrawal,
        StartTime = source.StartTime, LastStateChangeTime = source.LastStateChangeTime
    };
}
