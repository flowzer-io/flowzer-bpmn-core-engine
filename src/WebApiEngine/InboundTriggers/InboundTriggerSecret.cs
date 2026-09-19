using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WebApiEngine.InboundTriggers;

/// <summary>
/// Schlüssel, Geheimnis und Signatur eines Auslösers.
///
/// <para><b>Warum hier verschlüsselt und nicht gehasht wird.</b> Der Aufrufer weist sich mit
/// <c>HMAC-SHA256</c> über Zeitstempel und Körper aus, so wie Webhook-Anbieter es verlangen.
/// HMAC ist symmetrisch: Wer eine Signatur prüfen will, muss denselben Schlüssel besitzen, mit
/// dem sie gebildet wurde. Aus einer Einwegableitung — PBKDF2, Argon2, gepfefferte Hashes —
/// lässt sich keine Signatur nachrechnen. Ein Geheimnis nur als Hash zu speichern und trotzdem
/// HMAC zu prüfen, geht nicht; eines von beidem muss weichen.</para>
///
/// <para>Weichen darf nicht die Signatur: Ohne sie müsste das fremde System das Geheimnis selbst
/// als Kopfzeile mitschicken, und jede Zwischenstelle, jedes Zugriffsprotokoll und jeder
/// Fehlerbericht sähe es im Klartext. Also bleibt die Signatur, und das Geheimnis liegt
/// <em>versiegelt</em> in der Ablage: AES-256-GCM unter einem installationsweiten Schlüssel, der
/// nicht in der Datenbank steht. Wer nur die Datenbank liest — Sicherung, Dump, fremder
/// Lesezugriff —, bekommt die Geheimnisse damit nicht. Wer zusätzlich die Konfiguration der
/// Anwendung liest, bekommt sie; das ist die Grenze dieses Verfahrens und steht so in
/// <c>docs/INBOUND-TRIGGERS.md</c>.</para>
///
/// <para>Das Format trägt seine Fassung im Wert (<c>aesgcm-v1$…</c>), damit eine spätere
/// Installation vorhandene Einträge erkennt, statt sie als Hash zu missdeuten.</para>
/// </summary>
public static class InboundTriggerSecret
{
    public const string TimestampHeader = "X-Flowzer-Timestamp";
    public const string SignatureHeader = "X-Flowzer-Signature";

    /// <summary>Fenster um den Zeitstempel, in dem eine Signatur gilt — Schutz vor Wiedereinspielung.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private const string Scheme = "aesgcm-v1";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyBytes = 24;
    private const int SecretBytes = 32;

    /// <summary>
    /// Ein URL-sicherer Zufallsschlüssel von 32 Zeichen. Er ist kein Geheimnis, verrät aber auch
    /// nichts über den Workflow dahinter.
    /// </summary>
    public static string NewKey() => Base64Url(RandomNumberGenerator.GetBytes(KeyBytes));

    /// <summary>Das Geheimnis selbst: 256 Bit Zufall, URL-sicher als 43 Zeichen.</summary>
    public static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>
    /// Versiegelt das Geheimnis für die Ablage. Die Kennung des Auslösers geht als zusätzlich
    /// authentifizierte Angabe mit ein: Ein umkopierter Eintrag lässt sich dadurch nicht unter
    /// einer fremden Kennung wieder öffnen.
    /// </summary>
    public static string Protect(string secret, Guid triggerId, byte[] installationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentNullException.ThrowIfNull(installationKey);

        var plaintext = Encoding.UTF8.GetBytes(secret);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(installationKey, TagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(triggerId));
        CryptographicOperations.ZeroMemory(plaintext);

        return string.Join('$',
            Scheme,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(tag));
    }

    /// <summary>
    /// Öffnet ein versiegeltes Geheimnis. Liefert <c>false</c>, wenn Format, Kennung oder
    /// Schlüssel nicht passen — etwa nach einem Wechsel des installationsweiten Schlüssels.
    /// </summary>
    public static bool TryUnprotect(
        string stored,
        Guid triggerId,
        byte[] installationKey,
        out string secret)
    {
        secret = string.Empty;
        if (string.IsNullOrEmpty(stored) || installationKey.Length == 0) return false;

        var parts = stored.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Scheme, StringComparison.Ordinal)) return false;

        byte[] nonce, ciphertext, tag;
        try
        {
            nonce = Convert.FromBase64String(parts[1]);
            ciphertext = Convert.FromBase64String(parts[2]);
            tag = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (nonce.Length != NonceBytes || tag.Length != TagBytes) return false;

        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(installationKey, TagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(triggerId));
            secret = Encoding.UTF8.GetString(plaintext);
            return true;
        }
        catch (CryptographicException)
        {
            // Falscher Schlüssel oder veränderter Eintrag. Beides ist kein Nachweis.
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Die Signatur über <c>"{timestamp}.{rawBody}"</c>. Der Zeitstempel gehört mit hinein:
    /// Ohne ihn wäre ein einmal mitgelesener Aufruf beliebig oft wiederholbar.
    /// </summary>
    public static string ComputeSignature(string secret, long timestamp, string rawBody)
    {
        var payload = string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{rawBody}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Vergleicht die mitgeschickte Signatur mit der erwarteten, ohne über die Dauer des
    /// Vergleichs zu verraten, wie weit ein geratener Wert stimmt. Ein Vergleich, der beim
    /// ersten abweichenden Zeichen abbricht, ließe die Signatur Zeichen für Zeichen erraten.
    /// </summary>
    public static bool SignatureMatches(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
    }

    /// <summary>Bindet das Siegel an den Auslöser, zu dem es gehört.</summary>
    private static byte[] AssociatedData(Guid triggerId) =>
        Encoding.UTF8.GetBytes($"flowzer:inbound-trigger:{triggerId:N}");

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
