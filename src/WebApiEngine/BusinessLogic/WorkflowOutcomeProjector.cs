using System.Xml.Linq;
using BPMN.Activities;
using BPMN.Events;
using BPMN.Process;
using Microsoft.Extensions.Options;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Liefert ausschließlich eine reviewte Minimalentscheidung aus dem gespeicherten Abschlussstand.
/// Das ist keine vollständige Ereignishistorie und keine Ableitung aus Formular-/Prozessvariablen.
/// Objektberechtigung muss der aufrufende Anwendungsfall bereits geprüft haben.
/// </summary>
public sealed partial class WorkflowOutcomeProjector(IStorageSystem storage, IOptions<WorkflowOutcomeOptions> options)
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    /// <summary>
    /// Fehlende/abweichende Version oder inkonsistenter Stand bleibt Unknown. Technische Storagefehler
    /// werden nicht in eine Entscheidung umgedeutet, sondern folgen dem bestehenden API-Fehlervertrag.
    /// </summary>
    public Task<ProcessInstanceOutcomeDto> ProjectAsync(ProcessInstanceInfo instance) =>
        ProjectOneAsync(instance, new Dictionary<Guid, ReviewedDefinitionProof?>());

    /// <summary>
    /// Projiziert ausschließlich bereits objektberechtigte Instanzen in derselben Reihenfolge.
    /// Definition/XML-Proof wird genau innerhalb dieses Aufrufs je GUID geteilt; Zustände,
    /// Mastermodell und Tokengraph bleiben instanzbezogen. Der nächste Batch liest erneut.
    /// Technische I/O-Fehler brechen den Batch bewusst nach dem bestehenden Fehlervertrag ab.
    /// </summary>
    public async Task<IReadOnlyList<ProcessInstanceOutcomeDto>> ProjectBatchAsync(IReadOnlyList<ProcessInstanceInfo> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var proofs = new Dictionary<Guid, ReviewedDefinitionProof?>();
        var results = new List<ProcessInstanceOutcomeDto>(instances.Count);
        foreach (var instance in instances) results.Add(await ProjectOneAsync(instance, proofs));
        return results;
    }

    private async Task<ProcessInstanceOutcomeDto> ProjectOneAsync(ProcessInstanceInfo instance,
        IDictionary<Guid, ReviewedDefinitionProof?> proofs)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var mapping = FindMapping(instance.DefinitionId);
        if (mapping is null || !HasConsistentCompletion(instance, mapping, out var master))
            return ProcessInstanceOutcomeDto.Unknown;
        if (!proofs.TryGetValue(instance.DefinitionId, out var proof))
        {
            // Nur Versions-/XML-Prüfung teilen, niemals einen fremden Master oder Outcome.
            proof = await LoadDefinitionProofAsync(instance.DefinitionId, mapping);
            proofs.Add(instance.DefinitionId, proof);
        }
        if (proof is null || !MatchesMasterModel(proof, (Process)master!.CurrentBaseElement, mapping)
            || !HasConsistentTokenGraph(instance, master, proof.ProcessXml)) return ProcessInstanceOutcomeDto.Unknown;
        return ProjectRootTrace(instance, master, proof.ProcessXml, mapping);
    }

    private WorkflowOutcomeDefinitionOptions? FindMapping(Guid definitionId)
    {
        var mappings = options.Value.Definitions;
        if (mappings is null || mappings.Any(item => item is null || !item.IsValid())
            || mappings.Select(item => Guid.Parse(item.DefinitionId)).Distinct().Count() != mappings.Count)
            return null;
        return mappings.SingleOrDefault(item => Guid.Parse(item.DefinitionId) == definitionId);
    }

    private static bool HasConsistentCompletion(ProcessInstanceInfo instance,
        WorkflowOutcomeDefinitionOptions mapping, out Token? master)
    {
        master = null;
        if (!instance.IsFinished || instance.State != ProcessInstanceState.Completed
            || instance.InstanceId == Guid.Empty || instance.DefinitionId == Guid.Empty
            || !Same(instance.metaDefinitionId, mapping.CatalogId) || !Same(instance.ProcessId, mapping.ProcessId)
            || instance.Migrations is null || instance.Migrations.Count != 0
            || instance.Modifications is null || instance.Modifications.Count != 0
            || instance.Tokens is null || instance.Tokens.Count == 0
            || instance.Tokens.Any(token => token is null || token.Id == Guid.Empty
                || token.Withdrawal is not null)
            || instance.Tokens.Select(token => token.Id).Distinct().Count() != instance.Tokens.Count)
            return false;
        var masters = instance.Tokens.Where(token => token.ParentTokenId is null).ToArray();
        if (masters.Length != 1 || masters[0].State != FlowNodeState.Completed
            || masters[0].CurrentBaseElement is not Process process || !Same(process.Id, mapping.ProcessId))
            return false;
        master = masters[0];
        // Der belegte Speichervertrag verwendet zwei verschiedene IDs: InstanceEngine
        // erzeugt die äußere (objektberechtigte) Instanz-ID, ProcessEngine/CoreEngine erzeugen
        // separat den internen Master.ProcessInstanceId-Scope. Keine Gleichheit zur äußeren
        // ID erfinden: alle Tokens müssen zum eindeutigen nichtleeren Master-Scope gehören.
        var tokenScope = master.ProcessInstanceId;
        if (tokenScope == Guid.Empty
            || instance.Tokens.Any(token => token.ProcessInstanceId != tokenScope)) return false;
        // Unterbrochene innere Prüfrunden behalten Withdrawn-Tokens. Failed/Terminated oder
        // noch lebende Tokens sind dagegen kein konsistenter fachlicher Rootabschluss.
        return instance.Tokens.All(token => token.State is FlowNodeState.Completed or FlowNodeState.Merged or FlowNodeState.Withdrawn);
    }

    private static ProcessInstanceOutcomeDto ProjectRootTrace(ProcessInstanceInfo instance, Token master,
        XElement processXml, WorkflowOutcomeDefinitionOptions mapping)
    {
        var roots = instance.Tokens.Where(token => token.ParentTokenId == master.Id).ToArray();
        var rootIds = processXml.Elements().Where(element => element.Name.Namespace == Bpmn
                && element.Name != Bpmn + "sequenceFlow")
            .Select(element => (string?)element.Attribute("id")).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        if (roots.Any(token => token.CurrentBaseElement is null || !rootIds.Contains(token.CurrentBaseElement.Id)
            // Nur ein unterbrochener Subprozess darf auf dem Rootpfad Withdrawn sein:
            // Rückgabe/Ablehnung canceln die innere Runde, nicht das fachliche Rootende.
            || (token.State == FlowNodeState.Withdrawn && token.CurrentBaseElement is not SubProcess)))
            return ProcessInstanceOutcomeDto.Unknown;
        var ends = roots.Where(token => token.CurrentBaseElement is EndEvent
            || Same(token.CurrentBaseElement.Id, mapping.ApprovedRootEndId)
            || Same(token.CurrentBaseElement.Id, mapping.RejectedRootEndId)).ToArray();
        if (ends.Length != 1 || ends[0].State != FlowNodeState.Completed
            || ends[0].CurrentBaseElement.GetType() != typeof(EndEvent)
            || ((EndEvent)ends[0].CurrentBaseElement).EventDefinitions is { Count: > 0 })
            return ProcessInstanceOutcomeDto.Unknown;
        return Same(ends[0].CurrentBaseElement.Id, mapping.ApprovedRootEndId)
            ? ProcessInstanceOutcomeDto.Approved
            : Same(ends[0].CurrentBaseElement.Id, mapping.RejectedRootEndId)
                ? ProcessInstanceOutcomeDto.Rejected : ProcessInstanceOutcomeDto.Unknown;
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.Ordinal);
}
