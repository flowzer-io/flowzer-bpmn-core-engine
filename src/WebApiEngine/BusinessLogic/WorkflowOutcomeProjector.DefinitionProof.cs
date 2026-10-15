using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using BPMN.Events;
using BPMN.Process;

namespace WebApiEngine.BusinessLogic;

public sealed partial class WorkflowOutcomeProjector
{
    // Keine Instanzdaten/Entscheidung im Proof. Das Objekt wird nur in der lokalen
    // Dictionary eines einzigen Batch-/Single-Aufrufs gehalten, nicht im Scoped-Service.
    private sealed record ReviewedDefinitionProof(string DefinitionsId, XElement ProcessXml);

    private async Task<ReviewedDefinitionProof?> LoadDefinitionProofAsync(Guid definitionId, WorkflowOutcomeDefinitionOptions mapping)
    {
        try
        {
            var definition = await storage.DefinitionStorage.GetDefinitionById(definitionId);
            if (definition.Id != definitionId || !Same(definition.DefinitionId, mapping.CatalogId)) return null;
            var xml = await storage.DefinitionStorage.GetBinary(definitionId);
            // GUID-Inhalte können beide Ablagen überschreiben: erst der explizite Inhaltshash
            // bindet diesen Read-Proof an die tatsächlich reviewte Originaldatei.
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
            if (!string.Equals(hash, mapping.BpmnSha256, StringComparison.OrdinalIgnoreCase)) return null;
            // Einzige Parse-Stelle: nur beim ersten Proof-Laden für diese GUID im Batch.
            // Pro-Instanz-Prüfungen lesen dieselbe unveränderte XML-Struktur, parsen nicht neu.
            return ParseReviewedDefinition(xml, mapping);
        }
        catch (FileNotFoundException) { return null; }
        catch (XmlException) { return null; }
    }

    private static ReviewedDefinitionProof? ParseReviewedDefinition(string xml, WorkflowOutcomeDefinitionOptions mapping)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024
        });
        var document = XDocument.Load(reader);
        var root = document.Root;
        if (root?.Name != Bpmn + "definitions" || string.IsNullOrWhiteSpace((string?)root.Attribute("id"))) return null;
        var ids = root.DescendantsAndSelf().Where(element => element.Name.Namespace == Bpmn)
            .Select(element => (string?)element.Attribute("id")).Where(id => id is not null).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) return null;
        var processes = root.Elements(Bpmn + "process").ToArray();
        if (processes.Length != 1 || !Same((string?)processes[0].Attribute("id"), mapping.ProcessId)) return null;
        var process = processes[0];
        var ends = process.Elements(Bpmn + "endEvent").ToArray();
        if (ends.Length != 2 || !ends.Select(end => (string?)end.Attribute("id")).ToHashSet(StringComparer.Ordinal)
            .SetEquals([mapping.ApprovedRootEndId, mapping.RejectedRootEndId])) return null;
        if (ends.Any(end => end.Descendants().Any(element => element.Name.Namespace == Bpmn
                    && element.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal))
                || end.Elements(Bpmn + "outgoing").Any()
                || process.Elements(Bpmn + "sequenceFlow").Any(flow => Same((string?)flow.Attribute("sourceRef"), (string?)end.Attribute("id")))))
            return null;
        return new ReviewedDefinitionProof((string)root.Attribute("id")!, process);
    }

    private static bool MatchesMasterModel(ReviewedDefinitionProof proof, Process masterProcess, WorkflowOutcomeDefinitionOptions mapping)
    {
        if (!Same(proof.DefinitionsId, masterProcess.DefinitionsId)) return false;
        // Geladene Originaldatei und gespeichertes Rootmodell sind je Instanz zu prüfen:
        // ein gültiger Proof der ersten Instanz legitimiert keinen anderen Master.
        var modelElements = masterProcess.FlowElements;
        var xmlRootIds = proof.ProcessXml.Elements().Where(element => element.Name.Namespace == Bpmn)
            .Select(element => (string?)element.Attribute("id")).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        if (modelElements is null || modelElements.Any(element => element is null)
            || modelElements.Select(element => element.Id).Distinct(StringComparer.Ordinal).Count() != modelElements.Count
            || !xmlRootIds.SetEquals(modelElements.Select(element => element.Id))) return false;
        var modelEnds = modelElements.OfType<EndEvent>().ToArray();
        return modelEnds.Length == 2 && modelEnds.All(end => end.GetType() == typeof(EndEvent) && end.EventDefinitions is not { Count: > 0 })
            && modelEnds.Select(end => end.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals([mapping.ApprovedRootEndId, mapping.RejectedRootEndId]);
    }
}
