using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WebApiEngine.Shared;

namespace WebApiEngine.ProcessPackages;

/// <summary>
/// Ein abgelehntes Paket. Der Code ist stabil und maschinenlesbar, die Meldung erklaert ihn
/// einer Bedienung. <see cref="Unprocessable"/> unterscheidet „das ist keine Paketdatei“ (400)
/// von „das ist ein Paket, aber dieses hier geht nicht“ (422).
/// </summary>
public sealed class ProcessPackageException(string code, string message, bool unprocessable = false)
    : Exception(message)
{
    public string Code { get; } = code;
    public bool Unprocessable { get; } = unprocessable;
}

/// <summary>Der gelesene Inhalt eines Pakets: Manifest, BPMN und die Formulardateien.</summary>
public sealed record ProcessPackageContent(
    ProcessPackageManifestDto Manifest,
    string WorkflowXml,
    IReadOnlyDictionary<string, string> Forms);

/// <summary>
/// Schreibt und liest die ZIP-Huelle eines Prozesspakets.
///
/// Beim Lesen gilt durchgehend Misstrauen: Ein hochgeladenes Archiv kommt von aussen. Geprueft
/// werden Eintragsnamen (keine Pfadanteile, keine absoluten Pfade — „Zip-Slip“), die Zahl der
/// Eintraege und vor allem die <em>entpackte</em> Groesse, die beim Lesen mitgezaehlt und nicht
/// aus dem Verzeichnis des Archivs geglaubt wird.
/// </summary>
public static class ProcessPackageArchive
{
    public static byte[] Write(
        ProcessPackageManifestDto manifest,
        string workflowXml,
        IReadOnlyDictionary<string, string> forms,
        string readme)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, ProcessPackageFormat.ManifestEntry,
                JsonSerializer.Serialize(manifest, ProcessPackageFormat.JsonOptions));
            WriteEntry(archive, ProcessPackageFormat.WorkflowEntry, workflowXml);
            foreach (var (path, content) in forms.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                WriteEntry(archive, path, content);
            WriteEntry(archive, ProcessPackageFormat.ReadmeEntry, readme);
        }

        return buffer.ToArray();
    }

    public static ProcessPackageContent Read(Stream stream)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new ProcessPackageException("package.archive.invalid",
                "Die Datei ist kein lesbares ZIP-Archiv.");
        }

        using (archive)
        {
            if (archive.Entries.Count > ProcessPackageFormat.MaxEntries)
                throw new ProcessPackageException("package.archive.too_many_entries",
                    $"Ein Prozesspaket hat höchstens {ProcessPackageFormat.MaxEntries} Einträge.");

            Dictionary<string, string> entries = new(StringComparer.Ordinal);
            long total = 0;

            foreach (var entry in archive.Entries)
            {
                // Verzeichniseintraege tragen keinen Inhalt; sie werden nicht angelegt und
                // koennen deshalb auch nicht aus dem Zielordner herausfuehren.
                if (entry.FullName.EndsWith('/')) continue;

                EnsureSafeEntryName(entry.FullName);
                entries[entry.FullName] = ReadEntry(entry, ref total);
            }

            if (!entries.TryGetValue(ProcessPackageFormat.ManifestEntry, out var manifestJson))
                throw new ProcessPackageException("package.manifest.missing",
                    $"Im Paket fehlt „{ProcessPackageFormat.ManifestEntry}“.");

            var manifest = ParseManifest(manifestJson);

            if (!entries.TryGetValue(ProcessPackageFormat.WorkflowEntry, out var workflowXml))
                throw new ProcessPackageException("package.workflow.missing",
                    $"Im Paket fehlt „{ProcessPackageFormat.WorkflowEntry}“.", unprocessable: true);

            var forms = entries
                .Where(entry => entry.Key.StartsWith(ProcessPackageFormat.FormEntryPrefix, StringComparison.Ordinal))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

            return new ProcessPackageContent(manifest, workflowXml, forms);
        }
    }

    /// <summary>Nur Format und Formatversion — mehr braucht es nicht, um „nein“ zu sagen.</summary>
    private sealed record FormatProbe(string? Format, int? FormatVersion);

    /// <summary>
    /// Liest das Manifest in zwei Schritten. Zuerst nur Format und Formatversion: Ein Paket aus
    /// einer kuenftigen Fassung soll eine klare Auskunft bekommen und nicht an einem Feld
    /// scheitern, das es damals noch gar nicht gab. Erst danach wird der volle Vertrag gelesen.
    /// </summary>
    private static ProcessPackageManifestDto ParseManifest(string json)
    {
        FormatProbe? probe;
        try
        {
            probe = JsonSerializer.Deserialize<FormatProbe>(json, ProcessPackageFormat.JsonOptions);
        }
        catch (JsonException)
        {
            throw new ProcessPackageException("package.manifest.invalid",
                "Das Manifest des Pakets ist kein lesbares JSON.");
        }

        if (probe is null)
            throw new ProcessPackageException("package.manifest.invalid", "Das Manifest des Pakets ist leer.");

        if (!string.Equals(probe.Format, ProcessPackageFormat.FormatName, StringComparison.Ordinal))
            throw new ProcessPackageException("package.format.unsupported",
                $"Das Paketformat „{probe.Format}“ wird nicht unterstützt; erwartet wird "
                + $"„{ProcessPackageFormat.FormatName}“.", unprocessable: true);

        if (probe.FormatVersion != ProcessPackageFormat.CurrentFormatVersion)
            throw new ProcessPackageException("package.format_version.unsupported",
                $"Die Formatversion {probe.FormatVersion?.ToString() ?? "(fehlt)"} wird nicht unterstützt; "
                + $"erwartet wird {ProcessPackageFormat.CurrentFormatVersion}.", unprocessable: true);

        try
        {
            return JsonSerializer.Deserialize<ProcessPackageManifestDto>(json, ProcessPackageFormat.JsonOptions)
                ?? throw new ProcessPackageException("package.manifest.incomplete",
                    "Das Manifest des Pakets ist leer.", unprocessable: true);
        }
        catch (JsonException)
        {
            throw new ProcessPackageException("package.manifest.incomplete",
                "Dem Manifest des Pakets fehlen Angaben, die das Format verlangt.", unprocessable: true);
        }
    }

    /// <summary>
    /// Ein Eintragsname darf nur ein Name sein. Alles, was einen Pfad aufmacht — ein
    /// Verzeichnistrenner nach oben, ein absoluter Pfad, ein Laufwerksbuchstabe, ein
    /// Steuerzeichen —, wird abgelehnt, bevor der Eintrag ueberhaupt gelesen wird.
    /// </summary>
    private static void EnsureSafeEntryName(string name)
    {
        var invalid = name.Length == 0
            || name.Length > 200
            || name.StartsWith('/')
            || name.StartsWith('\\')
            || name.Contains('\\', StringComparison.Ordinal)
            || name.Contains(':', StringComparison.Ordinal)
            || name.Split('/').Any(segment => segment is "" or "." or "..")
            || name.Any(char.IsControl);

        if (invalid)
            throw new ProcessPackageException("package.entry.unsafe_path",
                $"Der Eintrag „{Sanitize(name)}“ hat keinen zulässigen Namen.");
    }

    /// <summary>
    /// Liest einen Eintrag und zaehlt dabei mit. Die Groesse aus dem Archivverzeichnis wird
    /// bewusst nicht geglaubt: Sie ist Teil der hochgeladenen Datei und damit gestaltbar.
    /// </summary>
    private static string ReadEntry(ZipArchiveEntry entry, ref long total)
    {
        var remaining = ProcessPackageFormat.MaxUncompressedBytes - total;
        using var source = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;

        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > remaining)
                throw new ProcessPackageException("package.archive.too_large",
                    $"Das entpackte Paket überschreitet {ProcessPackageFormat.MaxUncompressedBytes / (1024 * 1024)} MiB.");

            buffer.Write(chunk, 0, read);
        }

        total += buffer.Length;
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Ein Name aus dem Upload gehoert nicht ungefiltert in eine Meldung.</summary>
    private static string Sanitize(string name) => new(name
        .Where(character => !char.IsControl(character))
        .Take(60)
        .ToArray());

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
