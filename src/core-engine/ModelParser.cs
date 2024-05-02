using System.Runtime.CompilerServices;
using System.Xml.Linq;
using BPMN_Model.Common;
using BPMN_Model.Process;

namespace core_engine;

public static class ModelParser
{
   
    /// <summary>
    /// Parse a BPMN_as_Standard model from a stream
    /// </summary>
    /// <param name="stream">The stream to read the model from</param>
    /// <returns>The parsed BPMN_as_Standard model</returns>
    public static async Task<Definitions> ParseModel(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var xml = await reader.ReadToEndAsync();
        return ParseModel(xml);
    }
    
    /// <summary>
    /// Parse a BPMN_as_Standard model from a string
    /// </summary>
    /// <param name="xml">The string to read the model from</param>
    /// <returns>The parsed BPMN_as_Standard model</returns>
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

        var processNodes = root.Elements().Where(n =>
            n.Name.LocalName.Equals("process", StringComparison.InvariantCultureIgnoreCase) &&
            n.Attribute("isExecutable")?.Value.Equals("true", StringComparison.InvariantCultureIgnoreCase) == true);

        var newProcesses = processNodes.Select(e => new Process
        {
            Definitions = model,
            Id = e.Attribute("id")!.Value
        });
        model.Processes.AddRange(newProcesses);

        return model;
    }
}