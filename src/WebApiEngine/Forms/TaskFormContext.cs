using System.Dynamic;
using BPMN.Flowzer;

namespace WebApiEngine.Forms;

/// <summary>
/// Gemeinsame Quelle für Anzeige und Read-only-Prüfung: lokale Taskwerte überlagern
/// den nächsten Prozessscope. Explizite Input-Mappings öffnen keinen Parent-Fallback.
/// </summary>
public static class TaskFormContext
{
    public static ExpandoObject Read(IReadOnlyList<Token> tokens, Token task)
    {
        ExpandoObject result = new();
        var values = (IDictionary<string, object?>)result;
        var current = task;
        HashSet<Guid> seen = [];
        while (seen.Add(current.Id))
        {
            if (current.Variables is not null)
                foreach (var pair in current.Variables) values.TryAdd(pair.Key, pair.Value);
            if (current.CurrentBaseElement is BPMN.Process.Process or BPMN.Activities.SubProcess
                || current == task && task.CurrentFlowNode is IFlowzerInputMapping { InputMappings.Count: > 0 }) return result;
            if (current.ParentTokenId is null) return result;
            var parents = tokens.Where(token => token.Id == current.ParentTokenId).Take(2).ToArray();
            if (parents.Length != 1) return new ExpandoObject();
            current = parents[0];
        }
        return new ExpandoObject(); // Defekter/zyklischer Scope ist keine Datenfreigabe.
    }
}
