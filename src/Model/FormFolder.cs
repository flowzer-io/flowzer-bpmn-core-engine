namespace Model;

/// <summary>
/// Hierarchischer Ordner der Formularbibliothek. Ordner strukturieren nur den Katalog und
/// werden weder in veröffentlichte Formulare noch in Prozessinstanzen übernommen.
/// </summary>
public sealed class FormFolder
{
    public required Guid Id { get; init; }
    public Guid? ParentId { get; set; }
    public required string Name { get; set; }
}
