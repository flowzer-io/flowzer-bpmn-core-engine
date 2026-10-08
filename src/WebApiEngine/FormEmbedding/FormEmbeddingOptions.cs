namespace WebApiEngine.FormEmbedding;

/// <summary>Geschlossenes Installations-Opt-in für den strikt lesenden Einmaleinstieg.</summary>
public sealed class FormEmbeddingOptions
{
    public const string SectionName = "FormEmbedding";
    public bool Enabled { get; set; }
    public string PublicOrigin { get; set; } = "";
    public string[] AllowedHostOrigins { get; set; } = [];
    /// <summary>Origins sind exakte HTTPS-Origins, keine Pfade, Wildcards oder Credentials.</summary>
    public bool IsValid() => !Enabled || IsOrigin(PublicOrigin)
        && AllowedHostOrigins.Length > 0 && AllowedHostOrigins.All(IsOrigin);
    public bool Allows(string? origin) => Enabled && origin is not null
        && AllowedHostOrigins.Contains(origin, StringComparer.Ordinal);
    private static bool IsOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.GetLeftPart(UriPartial.Authority) == value
        && string.IsNullOrEmpty(uri.UserInfo);
}
