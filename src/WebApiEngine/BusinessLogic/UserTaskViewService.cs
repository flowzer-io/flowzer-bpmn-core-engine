using WebApiEngine.Forms;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>Verhindert, dass autorisierte Aufgabenlisten ungefilterte Prozessdaten ausliefern.</summary>
public sealed class UserTaskViewService(FormKeyResolver forms)
{
    public async Task<ExtendedUserTaskSubscriptionDto> ProjectAsync(ExtendedUserTaskSubscription task, bool canInspect)
    {
        // Ohne Betriebsrecht nicht erst den Modellgraphen serialisieren und danach wegwerfen.
        var dto = task.ToDto(includeTokenContext: canInspect);
        if (canInspect) return dto;
        var form = await forms.ResolveAsync(dto.FormKey, task.DefinitionId);
        dto.Token.Variables = FormContextProjection.Project(form.Form?.FormData, task.Token.Variables);
        return dto;
    }
}
