namespace WebApiEngine.Shared;

public class FormDto
{
    public required Guid FormId { get; set; }
    public VersionDto Version { get; set; } = new VersionDto();
    public string? FormData { get; set; }
}