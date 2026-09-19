using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebApiEngine.ProcessPackages;

/// <summary>
/// Die festen Groessen des Paketformats <c>flowzer.process-package/1</c>.
///
/// Ein Paket ist eine ZIP-Datei mit einem Manifest, genau einem BPMN-Dokument und den
/// Formularstaenden, die dieses Dokument braucht. Alles, was nur in der Quellinstallation
/// eine Bedeutung hat — Personen, Gruppen, KI-Verbindungen — steht im BPMN als Platzhalter
/// und im Manifest mit seinem Anzeigenamen. Secrets, Instanzen, Aufgaben und Historie gehoeren
/// nie in ein Paket.
/// </summary>
public static class ProcessPackageFormat
{
    public const string FormatName = "flowzer.process-package/1";
    public const int CurrentFormatVersion = 1;

    public const string ManifestEntry = "package.json";
    public const string WorkflowEntry = "workflow.bpmn";
    public const string ReadmeEntry = "README.md";
    public const string FormEntryPrefix = "forms/";

    /// <summary>Der exportierte Stand ist die veroeffentlichte Fassung.</summary>
    public const string SourceDeployed = "deployed";

    /// <summary>Der exportierte Stand ist der gespeicherte Entwurf; es gibt keine Veroeffentlichung.</summary>
    public const string SourceDraft = "draft";

    public const string ModeNew = "new";
    public const string ModeNewVersionOf = "newVersionOf";

    /// <summary>
    /// Obergrenze fuer das entpackte Paket. Sie liegt unter der Uploadgrenze von 8 MiB, damit
    /// ein stark komprimiertes Paket („Zip-Bombe“) den Speicher nicht trotzdem fuellen kann.
    /// </summary>
    public const long MaxUncompressedBytes = 8L * 1024 * 1024;

    /// <summary>Obergrenze fuer die Zahl der Eintraege; ein Paket hat wenige Dateien.</summary>
    public const int MaxEntries = 256;

    /// <summary>
    /// Der Kopf jeder Platzhalterkennung. Eine solche Kennung ist eine gueltige GUID, damit das
    /// mitgelieferte <c>workflow.bpmn</c> ein lesbares BPMN-Dokument bleibt und sich gegen den
    /// Faehigkeitsvertrag pruefen laesst. Sie traegt keinerlei Information ueber die
    /// Quellinstallation: Der Zaehler beginnt in jedem Paket wieder bei eins.
    /// </summary>
    private const string PlaceholderPrefix = "f1002ef0-0000-4000-8000-";

    /// <summary>Die Platzhalterkennung des <paramref name="ordinal"/>-ten Bezugs eines Pakets.</summary>
    public static Guid Placeholder(int ordinal) =>
        Guid.Parse(PlaceholderPrefix + ordinal.ToString("D12", CultureInfo.InvariantCulture));

    /// <summary>Ob dieser Wert eine Platzhalterkennung dieses Formats ist.</summary>
    public static bool IsPlaceholder(string? value) =>
        value is not null
        && value.StartsWith(PlaceholderPrefix, StringComparison.OrdinalIgnoreCase)
        && Guid.TryParse(value, out _);

    /// <summary>
    /// Serialisierung des Manifests. Kleingeschriebene Namen und Einrueckung: Ein Paket wird
    /// auch von Menschen geoeffnet, und ein Manifest ohne Zeilenumbrueche ist nicht pruefbar.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Ein Dateiname fuer den Download. Er nennt Workflow und Fassung, enthaelt aber nur
    /// Zeichen, die in jedem Dateisystem und in jedem Header unverfaenglich sind.
    /// </summary>
    public static string FileName(string name, string version)
    {
        var stem = new string(name
            .Normalize(NormalizationForm.FormD)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or ' ')
            .ToArray())
            .Trim()
            .Replace(' ', '-');
        while (stem.Contains("--", StringComparison.Ordinal)) stem = stem.Replace("--", "-", StringComparison.Ordinal);
        if (stem.Length > 60) stem = stem[..60].TrimEnd('-');
        if (stem.Length == 0) stem = "workflow";
        return $"{stem}-v{version}.flowzer.zip";
    }
}
