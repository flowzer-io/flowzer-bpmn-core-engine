using Model;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Löst den Form-Key eines User-Tasks auf ein konkretes Formular auf.
///
/// Der Form-Key stammt aus <c>zeebe:formDefinition/@formKey</c> und hat entweder die
/// Form <c>Formularname</c> (dann gilt die neueste Version) oder
/// <c>Formularname:1.0</c> (dann gilt genau diese Version).
///
/// Die Auflösung lag bisher im Blazor-Client und benötigte dort drei API-Aufrufe.
/// Als Teil der API gehört sie hierher: die Regel ist fachlich, nicht darstellend.
/// </summary>
public sealed class UserTaskFormResolver(IStorageSystem storageSystem)
{
    public sealed record Result(Form? Form, string? ErrorMessage)
    {
        public static Result Success(Form form) => new(form, null);
        public static Result Failure(string message) => new(null, message);
    }

    public async Task<Result> ResolveAsync(string? formKey)
    {
        if (string.IsNullOrWhiteSpace(formKey))
        {
            return Result.Failure(
                "The user task has no form key. Set 'formKey' in the BPMN task properties.");
        }

        var (formName, requestedVersion, versionError) = SplitFormKey(formKey);
        if (versionError is not null)
        {
            return Result.Failure(versionError);
        }

        var metadata = (await storageSystem.FormStorage.GetFormMetadatas())
            .Where(candidate => string.Equals(candidate.Name, formName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (metadata.Length == 0)
        {
            return Result.Failure($"No form named \"{formName}\" was found.");
        }

        if (metadata.Length > 1)
        {
            return Result.Failure(
                $"Multiple forms named \"{formName}\" were found. Form names must be unique.");
        }

        var versions = (await storageSystem.FormStorage.GetForms(metadata[0].FormId)).ToArray();
        if (versions.Length == 0)
        {
            return Result.Failure($"The form \"{formName}\" has no saved version yet.");
        }

        var match = requestedVersion is null
            ? versions.MaxBy(version => version.Version)
            : versions.SingleOrDefault(version => version.Version.Equals(requestedVersion));

        if (match is null)
        {
            return Result.Failure($"Version {requestedVersion} of form \"{formName}\" was not found.");
        }

        return Result.Success(match);
    }

    private static (string Name, Model.Version? Version, string? Error) SplitFormKey(string formKey)
    {
        var separatorIndex = formKey.IndexOf(':');
        if (separatorIndex < 0)
        {
            return (formKey.Trim(), null, null);
        }

        var name = formKey[..separatorIndex].Trim();
        var versionPart = formKey[(separatorIndex + 1)..].Trim();

        if (versionPart.Length == 0)
        {
            return (name, null, null);
        }

        try
        {
            return (name, Model.Version.FromString(versionPart), null);
        }
        catch (ArgumentException exception)
        {
            return (name, null, $"The form key \"{formKey}\" has an invalid version: {exception.Message}");
        }
    }
}
