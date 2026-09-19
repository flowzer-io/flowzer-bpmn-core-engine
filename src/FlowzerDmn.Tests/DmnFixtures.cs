using System.Reflection;
using FlowzerDmn.Model;
using FlowzerDmn.Parsing;

namespace FlowzerDmn.Tests;

/// <summary>
/// Laedt die DMN-Beispieldateien, die als EmbeddedResource im Testprojekt liegen.
/// </summary>
/// <remarks>
/// Eingebettet statt kopiert, damit eine Datei nicht unbemerkt fehlt: Ein Tippfehler im
/// Namen faellt hier sofort auf, statt erst als leerer Stream.
/// </remarks>
internal static class DmnFixtures
{
    private static readonly Assembly Assembly = typeof(DmnFixtures).Assembly;

    /// <summary>Liefert den Inhalt einer Beispieldatei, zum Beispiel <c>dish.dmn</c>.</summary>
    internal static string Load(string fileName)
    {
        var resourceName = Assembly.GetManifestResourceNames()
                               .SingleOrDefault(name => name.EndsWith("." + fileName, StringComparison.Ordinal))
                           ?? throw new InvalidOperationException(
                               $"Die Beispieldatei '{fileName}' ist nicht eingebettet. Vorhanden: " +
                               string.Join(", ", Assembly.GetManifestResourceNames()));

        using var stream = Assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Liest eine Beispieldatei gleich in das Modell ein.</summary>
    internal static DmnDefinitions Parse(string fileName) => DmnModelParser.Parse(Load(fileName));
}
