using BPMN_Model.Common;

namespace core_engine;

public static class ModelParser
{
   
    /// <summary>
    /// Parse a BPMN_as_Standard model from a stream
    /// </summary>
    /// <param name="stream">The stream to read the model from</param>
    /// <returns>The parsed BPMN_as_Standard model</returns>
    public static async Task<Model> ParseModel(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var xml = await reader.ReadToEndAsync();
        return await ParseModel(xml);
    }
    
    /// <summary>
    /// Parse a BPMN_as_Standard model from a string
    /// </summary>
    /// <param name="xml">The string to read the model from</param>
    /// <returns>The parsed BPMN_as_Standard model</returns>
    public static Task<Model> ParseModel(string xml)
    {
        throw new NotImplementedException();
    }
}