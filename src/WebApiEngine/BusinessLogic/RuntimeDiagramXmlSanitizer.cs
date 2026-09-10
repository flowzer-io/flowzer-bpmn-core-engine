using System.Xml.Linq;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Erzeugt aus der unveränderlich gebundenen BPMN-Version eine reine Diagrammansicht.
/// Ausführungs-, Ausdrucks-, Dokumentations- und Erweiterungsdaten dürfen nicht über die
/// Betriebsansicht in den Browser gelangen.
/// </summary>
internal static class RuntimeDiagramXmlSanitizer
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly HashSet<string> SensitiveElements = new(StringComparer.Ordinal)
    {
        "extensionElements",
        "documentation",
        "conditionExpression",
        "formalExpression",
        "script",
        "text",
        "dataObject",
        "dataObjectReference",
        "dataStoreReference",
        "property",
        "dataInputAssociation",
        "dataOutputAssociation",
        "ioSpecification",
        "inputSet",
        "outputSet",
        "dataInput",
        "dataOutput",
        "resourceRole",
        "resourceAssignmentExpression"
    };
    private static readonly HashSet<string> SensitiveAttributes = new(StringComparer.Ordinal)
    {
        "implementation",
        "operationRef",
        "calledElement",
        "messageRef",
        "signalRef",
        "errorRef",
        "escalationRef",
        "resourceRef",
        "itemSubjectRef",
        "scriptFormat"
    };

    internal static string? Sanitize(string xml, string processId)
    {
        if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(processId)) return null;

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            return null;
        }

        var process = document.Descendants(Bpmn + "process")
            .SingleOrDefault(element => string.Equals((string?)element.Attribute("id"), processId, StringComparison.Ordinal));
        if (process is null) return null;

        foreach (var otherProcess in document.Descendants(Bpmn + "process").Where(element => element != process).ToArray())
            otherProcess.Remove();

        foreach (var participant in document.Descendants(Bpmn + "participant").ToArray())
        {
            var processRef = (string?)participant.Attribute("processRef");
            if (!string.IsNullOrWhiteSpace(processRef)
                && !string.Equals(processRef, processId, StringComparison.Ordinal))
                participant.Remove();
        }

        foreach (var element in document.Descendants().Where(IsSensitive).ToArray())
            element.Remove();

        // Camunda-/Zeebe-/herstellerspezifische Attribute können Ausdrücke, Zieladressen
        // oder Bearbeiter enthalten. Standard-BPMN-Attribute sind unqualifiziert; XML-
        // Namespace-Deklarationen bleiben für ein gültiges Dokument erhalten.
        foreach (var attribute in document.Root!.DescendantsAndSelf()
                     .Attributes()
                     .Where(attribute => !attribute.IsNamespaceDeclaration
                         && (!string.IsNullOrEmpty(attribute.Name.NamespaceName)
                             || SensitiveAttributes.Contains(attribute.Name.LocalName)))
                     .ToArray())
            attribute.Remove();

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static bool IsSensitive(XElement element) =>
        element.Name.Namespace == Bpmn && SensitiveElements.Contains(element.Name.LocalName);
}
