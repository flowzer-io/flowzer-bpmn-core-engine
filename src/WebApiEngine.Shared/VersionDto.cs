namespace WebApiEngine.Shared;

public sealed class VersionDto : IEquatable<VersionDto>
{
    public int Major { get; set; }
    public int Minor { get; set; }

    public VersionDto()
    {
    }

    public VersionDto(int major, int minor)
    {
        Major = major;
        Minor = minor;
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

    public bool Equals(VersionDto? other)
    {
        if (other is null)
        {
            return false;
        }

        return Major == other.Major && Minor == other.Minor;
    }

    public override bool Equals(object? obj)
    {
        return obj is VersionDto other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor);
    }

    public override string ToString()
    {
        return $"{Major}.{Minor}";
    }

    public static VersionDto FromString(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var parts = version.Split('.');
        if (parts.Length != 2)
        {
            throw new ArgumentException("Version string must have two parts separated by a dot.", nameof(version));
        }

        if (!int.TryParse(parts[0], out var major))
        {
            throw new ArgumentException("Major version must be an integer.", nameof(version));
        }

        if (!int.TryParse(parts[1], out var minor))
        {
            throw new ArgumentException("Minor version must be an integer.", nameof(version));
        }

        return new VersionDto(major, minor);
    }
}
