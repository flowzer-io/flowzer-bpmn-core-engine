using BPMN.Infrastructure;

namespace core_engine;

public static class ModellCreator
{
   
    /// <summary>
    /// Parse a BPMN model from a stream
    /// </summary>
    /// <param name="stream">The stream to read the model from</param>
    /// <returns>The parsed BPMN model</returns>
    public static Task<Definitions> ParseModel(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var xml = await reader.ReadToEndAsync();
        return ParseModel(xml);
    }
    
    /// <summary>
    /// Parse a BPMN model from a string
    /// </summary>
    /// <param name="xml">The string to read the model from</param>
    /// <returns>The parsed BPMN model</returns>
    public static Task<Definitions> ParseModel(string xml)
    {
        throw new NotImplementedException();
    }
}