namespace WebApiEngine.Auth;

/// <summary>
/// Konfiguration der API-Authentifizierung (Abschnitt <c>Authentication</c>).
/// <c>None</c> behaelt das bisherige Verhalten (kein Schutz, im Development-Modus technischer
/// Benutzerheader). <c>JwtBearer</c> verlangt fuer alle Fachendpunkte ein gueltiges OIDC-Token des
/// konfigurierten Identity Providers. <c>Bff</c> ergaenzt eine serverseitige Browser-Session, ohne
/// den Bearer-Vertrag fuer externe Clients abzuschalten. Health-Endpunkte bleiben anonym erreichbar.
/// </summary>
public sealed class FlowzerAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public const string SchemeNone = "None";
    public const string SchemeJwtBearer = "JwtBearer";
    public const string SchemeBff = "Bff";

    public string Scheme { get; set; } = SchemeNone;

    public JwtBearerSettings JwtBearer { get; set; } = new();

    public BffSettings Bff { get; set; } = new();

    public bool IsJwtBearerEnabled => string.Equals(Scheme, SchemeJwtBearer, StringComparison.OrdinalIgnoreCase);

    public bool IsBffEnabled => string.Equals(Scheme, SchemeBff, StringComparison.OrdinalIgnoreCase);

    public bool IsAuthenticationEnabled => IsJwtBearerEnabled || IsBffEnabled;

    public sealed class JwtBearerSettings
    {
        /// <summary>OIDC-Issuer, z. B. <c>https://login.microsoftonline.com/{tenant}/v2.0</c> oder ein Keycloak-Realm.</summary>
        public string Authority { get; set; } = string.Empty;

        /// <summary>Erwartete Audience (Client-/App-Id der API) im Token.</summary>
        public string Audience { get; set; } = string.Empty;

        /// <summary>Nur fuer lokale Identity Provider ohne TLS auf <c>false</c> setzen.</summary>
        public bool RequireHttpsMetadata { get; set; } = true;

        /// <summary>
        /// Pflichtrolle fuer jeden Fachendpunkt: Das Token muss die Rolle als Keycloak-Clientrolle
        /// unter <c>resource_access.&lt;Audience&gt;.roles</c> oder als App-Rolle im Claim
        /// <c>roles</c> (Entra ID) tragen; sonst antwortet die API 403. Leer ist nur mit
        /// <see cref="LegacyPermissiveRoles"/> zulaessig und heisst dann: jede authentifizierte Person.
        /// </summary>
        public string RequiredRole { get; set; } = string.Empty;

        /// <summary>
        /// Namen der Anwendungsrollen. Modeler, Operator und Worker muessen bei aktiver
        /// Authentifizierung gesetzt sein; leer sind sie nur mit <see cref="LegacyPermissiveRoles"/>
        /// zulaessig, dann ist die jeweilige Faehigkeit fuer alle zugelassenen Personen offen.
        /// </summary>
        public ApplicationRoles Roles { get; set; } = new();

        /// <summary>
        /// Nur fuer Bestandsinstallationen: erlaubt leere privilegierte Rollennamen
        /// (<see cref="RequiredRole"/>, Modeler, Operator, Worker); jede angemeldete Person darf
        /// dann, was der fehlende Name schuetzen wuerde. Ohne diesen Schalter startet die API
        /// nicht, solange einer dieser Namen fehlt. Die KI-Rollen sind davon unberuehrt und
        /// bleiben bei leerem Wert gesperrt.
        /// </summary>
        public bool LegacyPermissiveRoles { get; set; } = false;

        /// <summary>
        /// Liefert die Konfigurationsschluessel der privilegierten Rollennamen, die leer sind.
        /// Die KI-Rollen gehoeren nicht dazu: Sie sind bei leerem Wert bereits fail-closed.
        /// </summary>
        public IReadOnlyList<string> MissingPrivilegedRoleKeys()
        {
            var missing = new List<string>();
            AddIfMissing(missing, RequiredRole, "Authentication:JwtBearer:RequiredRole");
            AddIfMissing(missing, Roles.Modeler, "Authentication:JwtBearer:Roles:Modeler");
            AddIfMissing(missing, Roles.Operator, "Authentication:JwtBearer:Roles:Operator");
            AddIfMissing(missing, Roles.Worker, "Authentication:JwtBearer:Roles:Worker");
            return missing;
        }

        private static void AddIfMissing(List<string> missing, string? value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(key);
            }
        }
    }

    /// <summary>
    /// Zugang und Faehigkeit sind zwei verschiedene Fragen: <c>RequiredRole</c> entscheidet, wer
    /// Flowzer benutzen darf, diese Rollen entscheiden, wer veroeffentlichen und wer den Betrieb
    /// einsehen darf.
    /// </summary>
    public sealed class ApplicationRoles
    {
        /// <summary>
        /// Darf Definitionen und Formulare anlegen, aendern und veroeffentlichen. Leer nur mit
        /// <see cref="JwtBearerSettings.LegacyPermissiveRoles"/> zulaessig.
        /// </summary>
        public string Modeler { get; set; } = string.Empty;

        /// <summary>
        /// Darf Diagnose sehen, alle Aufgaben sehen und Instanzen abbrechen. Leer nur mit
        /// <see cref="JwtBearerSettings.LegacyPermissiveRoles"/> zulaessig.
        /// </summary>
        public string Operator { get; set; } = string.Empty;

        /// <summary>
        /// Darf Auftraege fuer Service-Tasks abholen und zurueckmelden. Leer nur mit
        /// <see cref="JwtBearerSettings.LegacyPermissiveRoles"/> zulaessig.
        /// </summary>
        public string Worker { get; set; } = string.Empty;

        /// <summary>Darf aktive KI-Verbindungen beim Modellieren verwenden.</summary>
        public string AiConnectionUser { get; set; } = string.Empty;

        /// <summary>Darf KI-Verbindungen und deren Secret-Referenzen verwalten.</summary>
        public string AiConnectionManager { get; set; } = string.Empty;
    }

    /// <summary>Vertraulicher OIDC-Client und persistenter Schluesselring fuer Browser-Sessions.</summary>
    public sealed class BffSettings
    {
        /// <summary>Client-ID des vertraulichen OIDC-Clients.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>Client-Secret; ausschliesslich serverseitig aus einem Secret Store laden.</summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>OIDC-Scopes zusaetzlich zu openid/profile/email.</summary>
        public string[] Scopes { get; set; } = [];

        /// <summary>Persistentes, nur fuer die API beschreibbares Verzeichnis der Data-Protection-Schluessel.</summary>
        public string DataProtectionKeysPath { get; set; } = string.Empty;
    }

    public void Validate()
    {
        if (!IsAuthenticationEnabled)
        {
            if (!string.Equals(Scheme, SchemeNone, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Authentication:Scheme must be '{SchemeNone}', '{SchemeJwtBearer}' or '{SchemeBff}', but was '{Scheme}'.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(JwtBearer.Authority))
        {
            throw new InvalidOperationException(
                $"Authentication:JwtBearer:Authority must be set to the OIDC issuer when Authentication:Scheme is '{Scheme}'.");
        }

        if (string.IsNullOrWhiteSpace(JwtBearer.Audience))
        {
            throw new InvalidOperationException(
                $"Authentication:JwtBearer:Audience must be set when Authentication:Scheme is '{Scheme}'.");
        }

        // Leere privilegierte Rollennamen oeffnen die Faehigkeit fuer jede angemeldete Person.
        // Das darf nicht still passieren, sondern nur als ausdrueckliche Wahl fuer den Bestand.
        var missingRoleKeys = JwtBearer.MissingPrivilegedRoleKeys();
        if (missingRoleKeys.Count > 0 && !JwtBearer.LegacyPermissiveRoles)
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", missingRoleKeys)} must be set when Authentication:Scheme is '{Scheme}'. "
                + "Empty role names grant the capability to every authenticated person; to keep that legacy behaviour, "
                + "set Authentication:JwtBearer:LegacyPermissiveRoles=true explicitly.");
        }

        if (!IsBffEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Bff.ClientId))
        {
            throw new InvalidOperationException(
                "Authentication:Bff:ClientId must be set when Authentication:Scheme is 'Bff'.");
        }

        if (string.IsNullOrWhiteSpace(Bff.ClientSecret))
        {
            throw new InvalidOperationException(
                "Authentication:Bff:ClientSecret must be set when Authentication:Scheme is 'Bff'.");
        }

        if (string.IsNullOrWhiteSpace(Bff.DataProtectionKeysPath))
        {
            throw new InvalidOperationException(
                "Authentication:Bff:DataProtectionKeysPath must be set when Authentication:Scheme is 'Bff'.");
        }
    }
}
