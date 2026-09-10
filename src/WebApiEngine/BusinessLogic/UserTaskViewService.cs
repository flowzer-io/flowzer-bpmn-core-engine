using WebApiEngine.Forms;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>Verhindert, dass autorisierte Aufgabenlisten ungefilterte Prozessdaten ausliefern.</summary>
public sealed class UserTaskViewService(FormKeyResolver forms, IStorageSystem storage)
{
    private readonly Dictionary<Guid, ProcessInstanceInfo?> _instances = [];
    public async Task<ExtendedUserTaskSubscriptionDto> ProjectAsync(ExtendedUserTaskSubscription task, bool canInspect)
    {
        // Ohne Betriebsrecht nicht erst den Modellgraphen serialisieren und danach wegwerfen.
        var dto = task.ToDto(includeTokenContext: canInspect);
        var form = await forms.ResolveAsync(dto.FormKey, task.DefinitionId);
        // Auch Operator-Formulare dürfen keine unsichtbaren Zusatzvariablen zurücksenden.
        // Vollständige Diagnose bleibt über die separat berechtigte Instanz-API erreichbar.
        if (form.Form is null)
        {
            dto.Token.Variables = new System.Dynamic.ExpandoObject();
            return dto;
        }
        var instance = await FindInstanceAsync(task.ProcessInstanceId);
        var token = instance?.Tokens.SingleOrDefault(candidate => candidate.Id == task.Token.Id);
        var valid = instance is not null && token is { State: FlowNodeState.Active }
            && instance.DefinitionId == task.DefinitionId && instance.ProcessId == task.ProcessId
            && instance.metaDefinitionId == task.MetaDefinitionId
            && token.CurrentFlowNode?.Id == task.Token.CurrentFlowNode?.Id;
        var context = valid ? TaskFormContext.Read(instance!.Tokens, token!) : null;
        dto.Token.Variables = FormContextProjection.Project(form.Form?.FormData, context);
        return dto;
    }

    private async Task<ProcessInstanceInfo?> FindInstanceAsync(Guid? id)
    {
        if (id is null) return null;
        if (_instances.TryGetValue(id.Value, out var found)) return found;
        try { found = await storage.InstanceStorage.GetProcessInstance(id.Value); }
        catch (FileNotFoundException) { found = null; }
        _instances[id.Value] = found;
        return found;
    }
}
