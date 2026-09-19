namespace FlowzerDmn.Model;

/// <summary>
/// Eine Liste zulaessiger Werte (<c>inputValues</c> beziehungsweise <c>outputValues</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Text"/> ist der unveraenderte FEEL-Unary-Test, so wie er in der Datei steht —
/// damit laesst sich ein Eingabewert in einem Schritt pruefen.
/// </para>
/// <para>
/// <see cref="Entries"/> ist derselbe Text, auf oberster Ebene an den Kommas zerlegt. Die
/// Reihenfolge ist fuer PRIORITY und OUTPUT ORDER massgeblich: Der erste Eintrag hat die
/// hoechste Prioritaet.
/// </para>
/// </remarks>
public sealed record DmnUnaryTests
{
    /// <summary>Der Unary-Test im Original.</summary>
    public required string Text { get; init; }

    /// <summary>Die einzelnen Eintraege in Dokumentreihenfolge.</summary>
    public required IReadOnlyList<string> Entries { get; init; }
}
