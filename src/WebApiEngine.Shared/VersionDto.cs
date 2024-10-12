namespace WebApiEngine.Shared;

public class VersionDto
{
    public int Major { get; set; }
    public int Minor { get; set; }

    public VersionDto()
    {
    }
    
    // vergleichsoperator == und !=
    public static bool operator ==(VersionDto? left, VersionDto? right)
    {
        if (left is null && right is null)
        {
            return true;
        }
        if(left is null || right is null)
        {
            return false;
        }
        return left.Major == right.Major && left.Minor == right.Minor;
    }
    
    public static bool operator !=(VersionDto? left, VersionDto? right)
    {
        return !(left == right);
    }
    
    

    public override string ToString()
    {
        return $"{Major}.{Minor}";
    }
}