using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

public sealed partial class FormKeyResolver
{
    /// <summary>Alle Referenzen zuerst erfolgreich binden, erst danach aktivieren.</summary>
    public async Task<Dictionary<string, BoundForm>> BindAsync(IEnumerable<string?> formKeys, Guid definitionId)
    {
        Dictionary<string, BoundForm> bindings = new(StringComparer.Ordinal);
        foreach (var key in formKeys.Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key!.Trim()).Distinct(StringComparer.Ordinal))
        {
            var resolved = await ResolveForDeploymentAsync(key, definitionId);
            if (resolved.Form is not { FormData: not null } form)
                throw new InvalidOperationException($"Cannot create form binding for \"{key}\": {resolved.ErrorMessage}");

            bindings.Add(key, new BoundForm(form.Id, form.FormId, form.Version?.ToString(), form.FormData));
        }
        return bindings;
    }

    private static Result FromBinding(BoundForm bound)
    {
        // Neue DTO-Instanz statt veränderbarer Referenzen auf persistierte Metadaten.
        Model.Version? version = null;
        if (bound.Version is not null)
        {
            try { version = Model.Version.FromString(bound.Version); }
            catch (ArgumentException) { return Result.Failure("The deployment form binding contains an invalid version."); }
        }
        return new Result(new FormDto
        {
            Id = bound.Id, FormId = bound.FormId, FormData = bound.FormData,
            Version = version is null ? null : new VersionDto { Major = version.Major, Minor = version.Minor }
        }, null);
    }
}
