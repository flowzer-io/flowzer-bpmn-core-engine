using System.Xml;
using System.Xml.Linq;
using Model;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Forms;

namespace WebApiEngine.Persistence;

/// <summary>
/// Technischer Update-Schritt, kein Benutzerworkflow. Ergänzt fehlende Formularsnapshots
/// nur bei eindeutigem historischen Stand. Bestehende Bindungen, Formulare, Instanzen und
/// BPMN-Dokumente werden niemals ersetzt. Der Aufrufer hält die Update-Transaktion.
/// </summary>
public static class LegacyFormBindingUpgrade
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Zeebe = "http://camunda.org/schema/zeebe/1.0";

    public static async Task<int> ApplyAsync(IStorageSystem storage)
    {
        var resolver = new FormKeyResolver(storage);
        List<BpmnDefinition> updates = [];
        foreach (var definition in (await storage.DefinitionStorage.GetAllDefinitions())
            .Where(item => item.FormBindings is null && (item.IsActive || item.DeployedOn.HasValue)))
        {
            var xml = await storage.DefinitionStorage.GetBinary(definition.Id);
            Dictionary<string, BoundForm> bindings = new(StringComparer.Ordinal);
            foreach (var key in ReadKeys(xml))
            {
                var resolved = await resolver.ResolveForUpgradeAsync(key, definition.Id);
                if (resolved.Form is not { FormData: not null } form)
                    throw new InvalidOperationException($"Update-Vorprüfung: Workflow {definition.Id}, Formular {key}: {resolved.ErrorMessage}");
                var contract = FormContractCompiler.Compile(form.FormData);
                bindings.Add(key, new BoundForm(form.Id, form.FormId, form.Version?.ToString(), form.FormData, contract.ValidationProfile));
            }
            definition.FormBindings = bindings;
            updates.Add(definition);
        }

        // Erst wenn alle Kandidaten geprüft sind schreiben. Auch die Entwicklungsablage
        // ohne echten Rollback darf bei einem fachlichen Fehler keinen Teilbestand ändern.
        foreach (var definition in updates) await storage.DefinitionStorage.StoreDefinition(definition);
        return updates.Count;
    }

    private static IEnumerable<string> ReadKeys(string xml)
    {
        // Kein vollständiger Engine-Parse: ein nicht beteiligter Legacy-Service-Task darf
        // die eindeutige Bindung eines wartenden Formulars nicht verhindern.
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 10_000_000
        });
        var document = XDocument.Load(reader);
        return document.Descendants()
            .Where(node => node.Name == Bpmn + "startEvent" || node.Name == Bpmn + "userTask")
            .Elements(Bpmn + "extensionElements").Elements(Zeebe + "formDefinition")
            .Select(node => (string?)node.Attribute("formKey") ?? (string?)node.Attribute("formId"))
            .Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key!.Trim())
            .Distinct(StringComparer.Ordinal).ToArray();
    }
}
