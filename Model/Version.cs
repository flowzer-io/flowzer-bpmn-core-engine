using System.Diagnostics.CodeAnalysis;

namespace Model;


public class Version: IComparable<Version>
{
    public Version()
    {
    }
    
    [SetsRequiredMembers]
    public Version(int major, int minor)
    {
        Major = major;
        Minor = minor;
    }

    public required int Major { get; set; }
    public required int Minor { get; set; }
    
    //+ operator which increases the minor version
    public static Version operator +(Version version, int increase)
    {
        version.Minor += increase;
        return version;
    }

    public override string ToString()
    {
        return $"{Major}.{Minor}";
    }

    public int CompareTo(Version? other)
    {
        if (other == null)
            return 1;
        if (Major == other.Major)
            return Minor.CompareTo(other.Minor);
        return Major.CompareTo(other.Major);
    }
}