using System.Xml.Linq;
using core_engine;
using core_engine.Exceptions;

namespace WebApiEngine.ProcessPackages;

/// <summary>Ein Form-Key so, wie er im Modell steht, samt der Stelle, an der er steht.</summary>
public sealed record ProcessPackageFormKey(string Key, string ElementId);

/// <summary>
/// Die wenigen BPMN-Griffe, die das Paket braucht: die Prozesse nennen, die Form-Keys finden,
/// die Katalogkennung setzen und einen Form-Key auf den importierten Stand umschreiben.
///
/// Bewusst auf XML-Ebene und nicht ueber das Domaenenmodell: Ein Paket kann ein Modell
/// enthalten, das diese Installation nicht ausfuehren kann. Es soll trotzdem lesbar bleiben und
/// als Entwurf ankommen, statt am Parser zu scheitern.
/// </summary>
public static class ProcessPackageBpmn
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    public static XDocument Parse(string xml)
    {
        try
        {
            return XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            throw new ProcessPackageException("package.workflow.invalid",
                "Das BPMN-Dokument im Paket ist nicht lesbar.", unprocessable: true);
        }
    }

    /// <summary>Die Kennungen der Prozesse; der ausfuehrbare Hauptprozess steht vorn.</summary>
    public static string[] ProcessIds(string xml)
    {
        var processes = Parse(xml).Root?.Elements(Bpmn + "process").ToArray() ?? [];
        return processes
            .OrderByDescending(process =>
                string.Equals(process.Attribute("isExecutable")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            .Select(process => process.Attribute("id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
    }

    /// <summary>
    /// Alle Form-Keys des Modells — die der Benutzeraufgaben ebenso wie den des Startformulars.
    /// Ein Schluessel, der an mehreren Stellen steht, kommt nur einmal vor.
    /// </summary>
    public static IReadOnlyList<ProcessPackageFormKey> FormKeys(string xml)
    {
        Dictionary<string, ProcessPackageFormKey> found = new(StringComparer.Ordinal);

        foreach (var definition in Parse(xml).Descendants()
                     .Where(element => element.Name.LocalName == "formDefinition"))
        {
            var key = definition.Attribute("formKey")?.Value ?? definition.Attribute("formId")?.Value;
            if (string.IsNullOrWhiteSpace(key)) continue;

            var owner = definition.Ancestors()
                .FirstOrDefault(ancestor => ancestor.Name.LocalName is not "extensionElements"
                    && ancestor.Attribute("id") is not null);
            found.TryAdd(key.Trim(), new ProcessPackageFormKey(key.Trim(), owner?.Attribute("id")?.Value ?? ""));
        }

        return found.Values.ToArray();
    }

    /// <summary>Setzt die Katalogkennung; sie steht im BPMN und entscheidet, wo der Import landet.</summary>
    public static string WithDefinitionId(string xml, string definitionId)
    {
        var document = Parse(xml);
        if (document.Root is null)
            throw new ProcessPackageException("package.workflow.invalid",
                "Das BPMN-Dokument im Paket hat keine Wurzel.", unprocessable: true);

        document.Root.SetAttributeValue("id", definitionId);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Schreibt Form-Keys auf die Staende um, die der Import tatsaechlich angelegt hat.
    /// Ohne diesen Schritt zeigte ein importierter Workflow auf einen Formularnamen, den es hier
    /// so nicht gibt, oder — schlimmer — auf einen gleichnamigen fremden Stand.
    /// </summary>
    public static string WithFormKeys(string xml, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0) return xml;

        var document = Parse(xml);
        foreach (var definition in document.Descendants()
                     .Where(element => element.Name.LocalName == "formDefinition"))
        {
            var attribute = definition.Attribute("formKey") ?? definition.Attribute("formId");
            if (attribute is null || !replacements.TryGetValue(attribute.Value.Trim(), out var replacement)) continue;

            // Der neue Schluessel gehoert immer nach `formKey`; `formId` ist die aeltere
            // Schreibweise und bliebe sonst als zweite, widersprechende Angabe stehen.
            definition.SetAttributeValue("formId", null);
            definition.SetAttributeValue("formKey", replacement);
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Prueft das Modell gegen den ausfuehrbaren Teil des Faehigkeitsvertrags und liefert die
    /// Befunde, statt zu werfen. Die Vorschau soll sie alle zeigen koennen.
    /// </summary>
    public static IReadOnlyList<BpmnCapabilityIssue> DeploymentIssues(string xml)
    {
        try
        {
            BpmnCapabilityMatrix.ValidateForDeployment(xml);
            return [];
        }
        catch (BpmnCapabilityValidationException failure)
        {
            return failure.Issues.Count > 0
                ? failure.Issues
                : [new BpmnCapabilityIssue(failure.Code, failure.ElementId, failure.PropertyPath, failure.Message)];
        }
    }
}
