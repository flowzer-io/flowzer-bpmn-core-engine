namespace Model;


public class FormMetadata
{
    public Guid FormId { get; set; }
    public required string Name { get; set; }
    /// <summary>Reine Katalogorganisation; die Laufzeit bindet weiterhin nur FormId und Version.</summary>
    public Guid? FolderId { get; set; }
}
