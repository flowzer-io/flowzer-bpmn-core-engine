namespace core_engine;

public class InteractionResult
{
   
    /// <summary>
    /// Die BPMN-Aktivität an die Daten übergeben werden sollen (BaseElement.Id)
    /// </summary>
    public required string ActivityId { get; set; }
    public required Dictionary<string, object> ResultData { get; set; }
    
    
}