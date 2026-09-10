using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>Vollständige Formularfixtures statt früherer nicht auflösbarer Platzhalter.</summary>
internal static class FormTestSeed
{
    internal static async Task StoreAsync(IStorageSystem storage, string name, string schema = "{\"components\":[]}")
    {
        if ((await storage.FormStorage.GetFormMetadatas()).Any(form => form.Name == name)) return;
        var formId = Guid.NewGuid();
        await storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = name });
        await storage.FormStorage.SaveForm(new Form
        {
            Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 0), FormData = schema
        });
    }

    /// <summary>
    /// API-Vertragsfixtures besitzen kein echtes Deployment. Sie erhalten ihren Snapshot
    /// ausdrücklich beim Aufbau, nicht dynamisch im Fake-Lesepfad. Fehler-/Legacy-Fixtures
    /// bleiben ohne Bindung; echte Deployment-Szenarien deckt FormDeploymentBindingTest ab.
    /// </summary>
    internal static async Task BindFixtureAsync(IStorageSystem storage, BpmnDefinition definition, string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith("camunda-forms:bpmn:", StringComparison.Ordinal)) return;
        var form = (await new FormKeyResolver(storage).ResolveForDeploymentAsync(key, definition.Id)).Form;
        if (form?.FormData is null) return;
        definition.FormBindings ??= [];
        definition.FormBindings[key] = new BoundForm(form.Id, form.FormId, form.Version?.ToString(), form.FormData);
    }
}
