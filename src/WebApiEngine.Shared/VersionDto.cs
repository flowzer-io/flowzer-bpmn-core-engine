namespace WebApiEngine.Shared;

public class VersionDto
{
    public int Major { get; set; }
    public int Minor { get; set; }
    
    public override string ToString()
    {
        return $"{Major}.{Minor}";
    }
}