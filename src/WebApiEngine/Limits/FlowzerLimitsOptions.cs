namespace WebApiEngine.Limits;

/// <summary>
/// Grenzen, die einzelne Aufrufer davon abhalten, den Dienst fuer alle unbrauchbar zu machen:
/// ein Kontingent an Anfragen je Zeitfenster und eine Obergrenze fuer Uploads.
/// </summary>
public sealed class FlowzerRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Standardmaessig aktiv. Wer bewusst ohne Kontingent fahren will, schaltet es ab.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Anfragen je Fenster und Aufrufer. Grosszuegig genug fuer normale Bedienung.</summary>
    public int PermitLimit { get; set; } = 300;

    public int WindowSeconds { get; set; } = 60;

    /// <summary>Wartende Anfragen; 0 lehnt sofort ab, statt Verzoegerung aufzubauen.</summary>
    public int QueueLimit { get; set; }

    public void Validate()
    {
        if (PermitLimit <= 0)
        {
            throw new InvalidOperationException($"{SectionName}:PermitLimit must be greater than zero.");
        }

        if (WindowSeconds <= 0)
        {
            throw new InvalidOperationException($"{SectionName}:WindowSeconds must be greater than zero.");
        }

        if (QueueLimit < 0)
        {
            throw new InvalidOperationException($"{SectionName}:QueueLimit must not be negative.");
        }
    }
}

/// <summary>Obergrenze fuer Anfragekoerper, vor allem Definitions- und Formular-Uploads.</summary>
public sealed class FlowzerUploadLimitOptions
{
    public const string SectionName = "Limits";

    /// <summary>
    /// 8 MiB, abgestimmt auf <c>client_max_body_size</c> des mitgelieferten Gateways. Ein BPMN-Modell
    /// liegt um Groessenordnungen darunter; alles darueber ist ein Fehler oder ein Angriff.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 8L * 1024 * 1024;

    public void Validate()
    {
        if (MaxUploadBytes <= 0)
        {
            throw new InvalidOperationException($"{SectionName}:MaxUploadBytes must be greater than zero.");
        }
    }
}
