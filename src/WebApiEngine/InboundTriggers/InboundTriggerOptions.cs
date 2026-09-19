using System.Security.Cryptography;
using System.Text;

namespace WebApiEngine.InboundTriggers;

/// <summary>
/// Grenzen und Schlüsselmaterial der eingehenden Auslöser.
/// </summary>
public sealed class InboundTriggerOptions
{
    public const string SectionName = "InboundTriggers";

    /// <summary>Mindestlänge des installationsweiten Schlüssels, damit er nicht zu raten ist.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>
    /// Der installationsweite Schlüssel, unter dem die Geheimnisse der Auslöser in der Ablage
    /// versiegelt liegen. Absichtlich ohne Standardwert: Ein eingebauter Schlüssel stünde im
    /// Quelltext und wäre damit keiner. Ohne diesen Wert nimmt die Installation keine Auslöser
    /// an — wie bei der leeren Freigabeliste ausgehender Webhooks.
    ///
    /// Geht er verloren, lassen sich vorhandene Auslöser nicht mehr prüfen. Sie müssen dann ein
    /// neues Geheimnis bekommen; die Auslöser selbst und ihre Adressen bleiben bestehen.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>Anzahl Aufrufe je Auslöser und Fenster.</summary>
    public int PermitLimit { get; set; } = 60;

    /// <summary>Länge des Fensters in Sekunden.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Kein Wert heisst „nicht eingerichtet" und ist gueltig; die Installation nimmt dann keine
    /// Ausloeser an. Ein <em>zu kurzer</em> Wert ist dagegen ein Konfigurationsfehler und haelt
    /// den Start an, statt einen schwachen Schluessel still zu benutzen.
    /// </summary>
    public bool IsValid() =>
        (string.IsNullOrWhiteSpace(SecretKey) || SecretKey.Trim().Length >= MinimumKeyLength)
        && PermitLimit > 0
        && WindowSeconds is > 0 and <= 3600;

    /// <summary>
    /// Der abgeleitete 256-Bit-Schlüssel, oder eine leere Folge, wenn nichts konfiguriert ist.
    /// Die Ableitung erlaubt eine beliebig lange Passphrase in der Konfiguration, ohne dass der
    /// Betrieb Bytes abzählen muss.
    /// </summary>
    public byte[] ResolveKey()
    {
        var configured = SecretKey?.Trim();
        return string.IsNullOrEmpty(configured)
            ? []
            : SHA256.HashData(Encoding.UTF8.GetBytes($"flowzer:inbound-trigger:v1:{configured}"));
    }
}
