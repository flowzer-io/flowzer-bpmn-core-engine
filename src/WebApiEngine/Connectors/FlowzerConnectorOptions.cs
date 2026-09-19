namespace WebApiEngine.Connectors;

/// <summary>
/// Grenzen der mitgelieferten Konnektoren. Beide sind opt-in: Ohne ausdrueckliche Freigabe
/// holt der eingebaute Worker-Host keinen einzigen Auftrag. Ein Konnektor laeuft im
/// API-Prozess, ruft aber dieselben Auftragsmechanismen auf wie ein externer Worker.
/// </summary>
public sealed class FlowzerConnectorOptions
{
    public const string SectionName = "Connectors";

    /// <summary>Abstand zwischen zwei Durchgaengen je Konnektor.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Wie viele Auftraege ein Konnektor gleichzeitig bearbeitet.</summary>
    public int MaxConcurrentJobs { get; set; } = 4;

    /// <summary>
    /// Dauer der Sperre je uebernommenem Auftrag. Der Host verlaengert sie waehrend eines
    /// langen Laufs auf der Haelfte der Zeit; ohne Verlaengerung bekaeme ein zweiter Bezieher
    /// denselben Auftrag, waehrend der erste noch arbeitet.
    /// </summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>
    /// Namensraum der Umgebungsvariablen, aus denen eine <c>secret:NAME</c>-Referenz aufgeloest
    /// wird. Das Muster entspricht dem KI-Secret-Praefix: Ein Modell nennt nur den Namen,
    /// nie den Wert.
    /// </summary>
    public string SecretEnvironmentVariablePrefix { get; set; } = "FLOWZER_CONNECTOR_SECRET_";

    public HttpConnectorOptions Http { get; set; } = new();

    public EmailConnectorOptions Email { get; set; } = new();

    public int ResolvedPollIntervalSeconds => Math.Clamp(PollIntervalSeconds, 1, 3600);

    public int ResolvedMaxConcurrentJobs => Math.Clamp(MaxConcurrentJobs, 1, 100);

    public int ResolvedLeaseSeconds => Math.Clamp(LeaseSeconds, 10, 3600);

    /// <summary>
    /// Ein aktivierter E-Mail-Konnektor ohne Absender oder SMTP-Server kann nichts tun, wuerde
    /// aber Auftraege uebernehmen und wieder liegen lassen. Die Installation soll das beim
    /// Start bemerken, nicht erst am ersten Vorgang.
    /// </summary>
    public bool IsValid() =>
        SecretEnvironmentVariablePrefix is { Length: >= 3 and <= 64 }
        && SecretEnvironmentVariablePrefix[0] is >= 'A' and <= 'Z'
        && SecretEnvironmentVariablePrefix.All(character =>
            character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
        && (!Email.Enabled
            || (!string.IsNullOrWhiteSpace(Email.From) && !string.IsNullOrWhiteSpace(Email.Smtp.Host)));
}

/// <summary>Grenzen des HTTP-Konnektors (<c>flowzer:http</c>).</summary>
public sealed class HttpConnectorOptions
{
    public const string JobType = "flowzer:http";

    public bool Enabled { get; set; }

    /// <summary>
    /// Erlaubte Zieladressen, optional mit fuehrendem <c>*.</c> fuer eine Domain. Absichtlich
    /// leer als Standard: Ein Modell darf die Engine nicht zu einer beliebigen Adresse
    /// schicken, nur weil der Konnektor eingeschaltet ist.
    /// </summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>Nur fuer Ziele ohne TLS im selben Netz; standardmaessig aus.</summary>
    public bool AllowHttp { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Obergrenze fuer <c>timeoutSeconds</c> aus dem Auftrag.</summary>
    public int MaxTimeoutSeconds { get; set; } = 120;

    /// <summary>Default 1 MiB. Laengere Antworten werden gekuerzt.</summary>
    public int MaxResponseBytes { get; set; } = 1048576;

    public int RetryBackoffSeconds { get; set; } = 30;

    /// <summary>
    /// Leere Eintraege fallen heraus. Eine Installationsvorlage setzt den ersten Platz oft auf
    /// einen leeren Wert; ohne diese Bereinigung waere die Liste "nicht leer" und die Meldung
    /// spraeche von einem nicht freigegebenen Host statt von fehlender Freigabe.
    /// </summary>
    public string[] ResolvedAllowedHosts =>
        AllowedHosts.Where(host => !string.IsNullOrWhiteSpace(host)).Select(host => host.Trim()).ToArray();

    public int ResolvedTimeoutSeconds => Math.Clamp(TimeoutSeconds, 1, ResolvedMaxTimeoutSeconds);

    public int ResolvedMaxTimeoutSeconds => Math.Clamp(MaxTimeoutSeconds, 1, 600);

    public int ResolvedMaxResponseBytes => Math.Clamp(MaxResponseBytes, 1024, 33554432);

    public TimeSpan ResolvedRetryBackoff => TimeSpan.FromSeconds(Math.Clamp(RetryBackoffSeconds, 0, 3600));
}

/// <summary>Grenzen des E-Mail-Konnektors (<c>flowzer:email</c>).</summary>
public sealed class EmailConnectorOptions
{
    public const string JobType = "flowzer:email";

    public bool Enabled { get; set; }

    /// <summary>Absender aller Nachrichten; ein Modell waehlt ihn nicht.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Erlaubte Empfaengerdomaenen, optional mit fuehrendem <c>*.</c>. **Leer heisst: kein
    /// Versand.** Ein Testsystem mit echten Vorgangsdaten soll nicht versehentlich nach aussen
    /// mailen, nur weil die Freigabeliste vergessen wurde.
    /// </summary>
    public string[] AllowedRecipientDomains { get; set; } = [];

    public int RetryBackoffSeconds { get; set; } = 30;

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Leere Eintraege fallen heraus; siehe <see cref="HttpConnectorOptions"/>.</summary>
    public string[] ResolvedAllowedRecipientDomains =>
        AllowedRecipientDomains
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim())
            .ToArray();

    public TimeSpan ResolvedRetryBackoff => TimeSpan.FromSeconds(Math.Clamp(RetryBackoffSeconds, 0, 3600));
}

/// <summary>Zugang zum SMTP-Server. Das Passwort steht nur als Secret-Name hier.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Name hinter dem Secret-Praefix, also <c>FLOWZER_CONNECTOR_SECRET_&lt;Name&gt;</c>.
    /// Der Wert selbst steht nie in der Konfiguration.
    /// </summary>
    public string PasswordSecretName { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    public int ResolvedTimeoutSeconds => Math.Clamp(TimeoutSeconds, 1, 300);
}
