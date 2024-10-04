namespace Model;

public class Form
{
    public required Guid FormId { get; set; }
    public required Version Version { get; set; } = new Version(0,1);
    public required string FormData { get; set; }
}