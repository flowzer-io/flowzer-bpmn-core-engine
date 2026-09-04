namespace FilesystemStorageSystem;

/// <summary>
/// Datei-Primitive fuer die JSON-Ablage. Schreibzugriffe laufen ueber eine temporaere Datei und
/// ein atomares Umbenennen, damit gleichzeitige Leser nie eine halb geschriebene Datei sehen.
/// Lesezugriffe auf enumerierte Dateien tolerieren, dass eine Datei zwischen Verzeichnislisting
/// und Oeffnen bereits von einem anderen Vorgang geloescht wurde.
/// </summary>
internal static class StorageFile
{
    private const string TemporarySuffix = ".tmp-";

    public static async Task WriteAllTextAtomicAsync(string path, string content)
    {
        var temporaryPath = $"{path}{TemporarySuffix}{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static void WriteAllTextAtomic(string path, string content)
    {
        var temporaryPath = $"{path}{TemporarySuffix}{Guid.NewGuid():N}";
        File.WriteAllText(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <summary>
    /// Liest den Inhalt einer Datei oder liefert <c>null</c>, wenn sie inzwischen nicht mehr existiert.
    /// </summary>
    public static string? ReadAllTextIfExists(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public static async Task<string?> ReadAllTextIfExistsAsync(string path)
    {
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Enumeriert die JSON-Dateien eines Verzeichnisses (Suchmuster ohne Temporaerdateien) und liefert
    /// ihren Inhalt. Zwischenzeitlich geloeschte Dateien werden uebersprungen.
    /// </summary>
    public static IEnumerable<(string Path, string Content)> ReadExistingFiles(string directory, string searchPattern)
    {
        foreach (var path in Directory.GetFiles(directory, searchPattern))
        {
            var content = ReadAllTextIfExists(path);
            if (content is not null)
            {
                yield return (path, content);
            }
        }
    }

    public static void DeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
