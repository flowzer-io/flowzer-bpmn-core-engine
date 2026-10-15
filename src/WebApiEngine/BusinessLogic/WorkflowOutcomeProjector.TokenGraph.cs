using BPMN.Common;
using BPMN.Process;
using System.Xml.Linq;

namespace WebApiEngine.BusinessLogic;

public sealed partial class WorkflowOutcomeProjector
{
    // Die Tokens sind ein persistierter Scopegraph, keine frei interpretierbare Node-ID-Liste.
    // Jeder Pfad muss im eindeutigen Master enden und an seinem direkten Modellcontainer hängen.
    // Unterstützungsgrenze bewusst konservativ: komplexe/abweichende Container bleiben Unknown.
    private static bool HasConsistentTokenGraph(ProcessInstanceInfo instance, Token master, XElement processXml)
    {
        var tokens = instance.Tokens.ToDictionary(token => token.Id);
        var xmlScopes = processXml.DescendantsAndSelf().Where(element => element.Name.Namespace == Bpmn && element.Attribute("id") is not null)
            .ToDictionary(element => (string)element.Attribute("id")!, StringComparer.Ordinal);
        var verified = new HashSet<Guid> { master.Id };
        foreach (var token in instance.Tokens)
        {
            var current = token;
            var path = new HashSet<Guid>();
            while (!verified.Contains(current.Id))
            {
                if (!path.Add(current.Id) || current.ParentTokenId is not { } parentId
                    || !tokens.TryGetValue(parentId, out var parent)
                    || current.CurrentBaseElement is not FlowNode
                    || parent.CurrentBaseElement is not IFlowElementContainer container
                    || !xmlScopes.TryGetValue(parent.CurrentBaseElement.Id, out var scopeXml)) return false;
                var modelNodes = container.FlowElements?.Where(node => node is not null && Same(node.Id, current.CurrentBaseElement.Id)).ToArray();
                var xmlNodes = scopeXml.Elements().Where(element => element.Name.Namespace == Bpmn
                    && Same((string?)element.Attribute("id"), current.CurrentBaseElement.Id)).ToArray();
                if (modelNodes is not { Length: 1 } || xmlNodes.Length != 1
                    || modelNodes[0].GetType() != current.CurrentBaseElement.GetType()) return false;
                current = parent;
            }
            // Bereits validierte Parentketten werden wiederverwendet; wiederholte Prüfrunden
            // müssen nicht je Token dieselbe lange Ahnenkette erneut ablaufen.
            verified.UnionWith(path);
        }
        return true;
    }
}
