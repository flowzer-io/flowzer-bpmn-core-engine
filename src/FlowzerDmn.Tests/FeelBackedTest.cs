using FlowzerDmn.Evaluation;
using FlowzerDmn.Tests.Feel;
using FluentAssertions;

namespace FlowzerDmn.Tests;

/// <summary>
/// Basis fuer alle Tests, die wirklich FEEL rechnen. Sie haengen am libfeelin-Adapter
/// und damit an ClearScript/V8.
/// </summary>
/// <remarks>
/// Das Testprojekt bringt die nativen V8-Bibliotheken fuer Linux, macOS und Windows mit,
/// damit diese Tests auch in der CI laufen. Faellt V8 trotzdem aus — exotische Plattform,
/// fehlende Systembibliothek —, wird die Klasse uebersprungen statt rot zu werden; die
/// Meldung nennt den Grund.
/// </remarks>
public abstract class FeelBackedTest
{
    private IFeelEngine _feelEngine = null!;

    /// <summary>Der Auswerter ueber der echten FEEL-Engine der Engine.</summary>
    protected DmnDecisionEvaluator Evaluator { get; private set; } = null!;

    /// <summary>Die FEEL-Engine selbst, fuer Tests, die den Adapter direkt pruefen.</summary>
    protected IFeelEngine FeelEngine => _feelEngine;

    [OneTimeSetUp]
    public void CreateEvaluator()
    {
        try
        {
            _feelEngine = new FeelinFeelEngine();
        }
        catch (Exception exception)
        {
            Assert.Ignore("Ohne native V8-Bibliothek nicht pruefbar: " + exception.Message);
            return;
        }

        Evaluator = new DmnDecisionEvaluator(_feelEngine);
    }

    /// <summary>Liest das Ergebnis als einzelnes Ausgabeobjekt.</summary>
    protected static IReadOnlyDictionary<string, object?> SingleOutput(DmnDecisionResult result) =>
        result.Value.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;

    /// <summary>Liest das Ergebnis als Liste von Ausgabeobjekten.</summary>
    protected static IReadOnlyList<IReadOnlyDictionary<string, object?>> OutputList(DmnDecisionResult result) =>
        result.Value.Should().BeAssignableTo<IReadOnlyList<IReadOnlyDictionary<string, object?>>>().Subject;

    /// <summary>Kurzschreibweise fuer die Variablen einer Auswertung.</summary>
    protected static Dictionary<string, object?> Variables(params (string Name, object? Value)[] values) =>
        values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
