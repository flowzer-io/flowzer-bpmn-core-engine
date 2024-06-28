using System.Diagnostics.CodeAnalysis;

namespace Model;


public class BpmnVersion: IComparable<BpmnVersion>
{
    public BpmnVersion()
    {
    }
    
    [SetsRequiredMembers]
    public BpmnVersion(int major, int minor)
    {
        Major = major;
        Minor = minor;
    }

    public required int Major { get; set; }
    public required int Minor { get; set; }
    
    //+ operator which increases the minor version
    public static BpmnVersion operator +(BpmnVersion version, int increase)
    {
        version.Minor += increase;
        return version;
    }

    public int CompareTo(BpmnVersion? other)
    {
        if (other == null)
            return 1;
        if (Major == other.Major)
            return Minor.CompareTo(other.Minor);
        return Major.CompareTo(other.Major);
    }
}