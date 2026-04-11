using System.Diagnostics.CodeAnalysis;

namespace Model;


public class Version: IComparable<Version>, IEquatable<Version>
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
        return new Version(version.Major, version.Minor + increase);
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

    public bool Equals(Version? other)
    {
        // Versionen sind fachlich Wertobjekte und müssen deshalb über ihre Bestandteile verglichen werden.
        if (other is null)
            return false;

        return Major == other.Major && Minor == other.Minor;
    }

    public override bool Equals(object? obj)
    {
        return obj is Version other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor);
    }

    public static bool operator ==(Version? left, Version? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(Version? left, Version? right)
    {
        return !Equals(left, right);
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
