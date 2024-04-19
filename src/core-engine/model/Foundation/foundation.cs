using System.ComponentModel.DataAnnotations;

namespace Foundation;
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

public interface IRootElement;
public abstract class RootElement : IRootElement;

public interface IBaseElement
{
    [Required] public string Id { get; set; }
    public List<Documentation> Documentations { get; set; }
    public List<ExtensionDefinition> ExtensionDefinitions { get; set; }
}

public abstract class BaseElement : IBaseElement
{
    [Required] public string Id { get; set; } = "";
    public List<Documentation> Documentations { get; set; } = [];
    public List<ExtensionDefinition> ExtensionDefinitions { get; set; } = [];
}

public class ExtensionDefinition
{
    [Required] public string Name { get; set; } = "";
    [Required] public List<ExtensionAttributeDefinition> ExtensionAttributeDefinitions { get; set; } = [];
}

public class ExtensionAttributeDefinition
{
    [Required] public string Name { get; set; } = "";
    [Required] public string Type { get; set; } = "";
    public bool IsReference { get; set; }
}

public class Extension
{
    public bool MustUnderstand { get; set; }
}

public class Documentation : BaseElement
{
    [Required] public string Text { get; set; } = "";
    public string? TextFormat { get; set; }
}

public class Relationship : BaseElement
{
    public string Type { get; set; } = "";
    public RelationshipDirection RelationshipDirection { get; set; }
}

public enum RelationshipDirection
{
    None, Forward, Backward, Both
}