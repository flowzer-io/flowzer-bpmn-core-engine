using System.Xml.Linq;
using BPMN.Infrastructure;
using BPMN.Process;

namespace core_engine;

public static class ModelParser
{
   
    /// <summary>
    /// Parse a FlowzerBPMN model from a stream
    /// </summary>
    /// <param name="stream">The stream to read the model from</param>
    /// <returns>The parsed FlowzerBPMN model</returns>
    public static async Task<Definitions> ParseModel(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var xml = await reader.ReadToEndAsync();
        return ParseModel(xml);
    }
    
    /// <summary>
    /// Parse a FlowzerBPMN model from a string
    /// </summary>
    /// <param name="xml">The string to read the model from</param>
    /// <returns>The parsed FlowzerBPMN model</returns>
    public static Definitions ParseModel(string xml)
    {
        // Laden der XML-Datei
        var xDocument = XDocument.Parse(xml);
        var root = xDocument.Root!;
        var model = new Definitions
        {
            Id = root.Attribute("id")!.Value,
        };
        
        // Gib mir alle Nodes unter root, die vom Typ "bpmn:process" sind

        var xmlProcessNodes = root.Elements().Where(n =>
            n.Name.LocalName.Equals("process", StringComparison.InvariantCultureIgnoreCase) &&
            n.Attribute("isExecutable")?.Value.Equals("true", StringComparison.InvariantCultureIgnoreCase) == true);

        foreach (var xmlProcessNode in xmlProcessNodes)
        {
            var process = new Process
            {
                Name = xmlProcessNode.Attribute("name")!.Value,
                Id = xmlProcessNode.Attribute("id")!.Value,
                IsExecutable = true,
                // FlowzerProcessHash = xmlProcessNode.Value
            };
            model.RootElements.Add(process);
        }

        return model;
    }
}