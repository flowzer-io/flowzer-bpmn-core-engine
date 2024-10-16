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

    public int Major { get; set; }
    public int Minor { get; set; }
    
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

    public static Version FromString(string versionString)
    {
        var parts = versionString.Split('.');
        if (parts.Length != 2)
            throw new ArgumentException("Version string must have two parts separated by a dot.");
        if (!int.TryParse(parts[0], out var major))
            throw new ArgumentException("Major version must be an integer.");
        if (!int.TryParse(parts[1], out var minor))
            throw new ArgumentException("Minor version must be an integer.");
        return new Version(major, minor);
    }
}