namespace WebApiEngine.Auth;

/// <summary>
/// Konfiguration der API-Authentifizierung (Abschnitt <c>Authentication</c>).
/// <c>None</c> behaelt das bisherige Verhalten (kein Schutz, im Development-Modus technischer
/// Benutzerheader). <c>JwtBearer</c> verlangt fuer alle Fachendpunkte ein gueltiges OIDC-Token des
/// konfigurierten Identity Providers; Health-Endpunkte bleiben anonym erreichbar.
/// </summary>
public sealed class FlowzerAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public const string SchemeNone = "None";
    public const string SchemeJwtBearer = "JwtBearer";

    public string Scheme { get; set; } = SchemeNone;

    public JwtBearerSettings JwtBearer { get; set; } = new();

    public bool IsJwtBearerEnabled => string.Equals(Scheme, SchemeJwtBearer, StringComparison.OrdinalIgnoreCase);

    public sealed class JwtBearerSettings
    {
        /// <summary>OIDC-Issuer, z. B. <c>https://login.microsoftonline.com/{tenant}/v2.0</c> oder ein Keycloak-Realm.</summary>
        public string Authority { get; set; } = string.Empty;

        /// <summary>Erwartete Audience (Client-/App-Id der API) im Token.</summary>
        public string Audience { get; set; } = string.Empty;

        /// <summary>Nur fuer lokale Identity Provider ohne TLS auf <c>false</c> setzen.</summary>
        public bool RequireHttpsMetadata { get; set; } = true;

        /// <summary>
        /// Optionale Pflichtrolle. Leer = jede authentifizierte Person. Gesetzt = das Token muss die
        /// Rolle als Keycloak-Clientrolle unter <c>resource_access.&lt;Audience&gt;.roles</c> oder als
        /// App-Rolle im Claim <c>roles</c> (Entra ID) tragen; sonst antwortet die API 403.
        /// </summary>
        public string RequiredRole { get; set; } = string.Empty;

        /// <summary>
        /// Namen der Anwendungsrollen. Leer gelassen ist die jeweilige Faehigkeit fuer alle
        /// zugelassenen Personen offen; so aendert das Update fuer bestehende Installationen nichts.
        /// </summary>
        public ApplicationRoles Roles { get; set; } = new();
    }

    /// <summary>
    /// Zugang und Faehigkeit sind zwei verschiedene Fragen: <c>RequiredRole</c> entscheidet, wer
    /// Flowzer benutzen darf, diese Rollen entscheiden, wer veroeffentlichen und wer den Betrieb
    /// einsehen darf.
    /// </summary>
    public sealed class ApplicationRoles
    {
        /// <summary>Darf Definitionen und Formulare anlegen, aendern und veroeffentlichen.</summary>
        public string Modeler { get; set; } = string.Empty;

        /// <summary>Darf Diagnose sehen, alle Aufgaben sehen und Instanzen abbrechen.</summary>
        public string Operator { get; set; } = string.Empty;
    }

    public void Validate()
    {
        if (!IsJwtBearerEnabled)
        {
            if (!string.Equals(Scheme, SchemeNone, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Authentication:Scheme must be '{SchemeNone}' or '{SchemeJwtBearer}', but was '{Scheme}'.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(JwtBearer.Authority))
        {
            throw new InvalidOperationException(
                "Authentication:JwtBearer:Authority must be set to the OIDC issuer when Authentication:Scheme is 'JwtBearer'.");
        }

        if (string.IsNullOrWhiteSpace(JwtBearer.Audience))
        {
            throw new InvalidOperationException(
                "Authentication:JwtBearer:Audience must be set when Authentication:Scheme is 'JwtBearer'.");
        }
    }
}
