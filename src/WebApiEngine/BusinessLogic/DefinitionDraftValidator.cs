using System.Xml;
using System.Xml.Linq;
using core_engine;
using core_engine.Exceptions;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Ein Entwurf ist ein sicher ablegbares BPMN-Dokument, nicht notwendigerweise ein
/// ausführbarer Prozess. Keine Provider-, Formular- oder Engine-Ausführung beim Speichern.
/// </summary>
public static class DefinitionDraftValidator
{
    public static string ReadDefinitionId(string xml)
    {
        XDocument document;
        try
        {
            using var input = new StringReader(xml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 10_000_000
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new BpmnCapabilityValidationException("bpmn.xml.invalid", null, null,
                BpmnCapabilityMatrix.Contract.ContractVersion, "The BPMN XML document is invalid.");
        }
        if (document.Root?.Name != XName.Get("definitions", "http://www.omg.org/spec/BPMN/20100524/MODEL"))
            throw new BpmnCapabilityValidationException("bpmn.xml.definitions_required", null, null,
                BpmnCapabilityMatrix.Contract.ContractVersion, "The document must contain a BPMN definitions root.");
        var id = (string?)document.Root.Attribute("id");
        DefinitionIdRules.EnsureValid(id!, "definitions/@id");
        return id!;
    }
}
