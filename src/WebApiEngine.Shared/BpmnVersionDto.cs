namespace WebApiEngine.Shared;

public class BpmnVersionDto
{
    public int Major { get; set; }
    public int Minor { get; set; }
    
    public override string ToString()
    {
        return $"{Major}.{Minor}";
    }
}