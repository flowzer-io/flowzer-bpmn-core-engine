namespace FlowzerDmn.Evaluation;

/// <summary>
/// Die Verbindung zu FEEL. Die DMN-Bibliothek rechnet selbst nichts aus, sondern fragt
/// ueber diese Schnittstelle.
/// </summary>
/// <remarks>
/// Damit bleibt die Bibliothek frei von ClearScript und V8: Die Engine bringt ihren
/// FEEL-Handler mit, ein Trockenlauf oder ein Test darf einen anderen einsetzen.
/// </remarks>
public interface IFeelEngine
{
    /// <summary>
    /// Wertet einen FEEL-Ausdruck aus.
    /// </summary>
    /// <param name="expression">Der Ausdruck ohne fuehrendes Gleichheitszeichen.</param>
    /// <param name="context">Die sichtbaren Variablen.</param>
    /// <returns>Der Wert des Ausdrucks; <c>null</c>, wenn er keinen liefert.</returns>
    object? Evaluate(string expression, IReadOnlyDictionary<string, object?> context);

    /// <summary>
    /// Prueft einen FEEL-Unary-Test gegen einen Eingabewert, also etwa <c>&gt; 10</c>
    /// oder <c>"Winter","Fall"</c>.
    /// </summary>
    /// <param name="test">Der Unary-Test ohne fuehrendes Gleichheitszeichen.</param>
    /// <param name="inputValue">Der zu pruefende Eingabewert.</param>
    /// <param name="context">Die sichtbaren Variablen.</param>
    /// <returns><c>true</c>, wenn der Test zutrifft.</returns>
    bool UnaryTest(string test, object? inputValue, IReadOnlyDictionary<string, object?> context);
}
