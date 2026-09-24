namespace WebApiEngine.Tests;

/// <summary>
/// Entscheidet, ob ein Test ohne seine Umgebung (Docker fuer PostgreSQL) uebersprungen
/// werden darf oder rot werden muss.
/// </summary>
/// <remarks>
/// Lokal ohne Docker sollen die PostgreSQL-Tests weiterhin als uebersprungen enden, damit
/// der restliche Lauf nutzbar bleibt. In der CI ist ein stilles Ueberspringen dagegen
/// gefaehrlich: Ein Runner ohne Docker liesse die gesamte Ablage- und Cluster-Pruefung
/// aus, und der Lauf bliebe trotzdem gruen. Mit
/// <c>FLOWZER_TESTS_REQUIRE_POSTGRESQL=1</c> wird das Fehlen der Umgebung deshalb zum
/// Testfehler mit Klartext. Zusaetzlich wertet <c>scripts/ci/check_skipped_tests.py</c>
/// in der CI jedes uebersprungene Ergebnis aus.
/// </remarks>
internal static class TestEnvironmentRequirements
{
    internal const string RequirePostgreSqlVariable = "FLOWZER_TESTS_REQUIRE_POSTGRESQL";

    /// <summary>Ob PostgreSQL-Tests verpflichtend sind: Variable auf <c>1</c> oder <c>true</c>.</summary>
    internal static bool PostgreSqlRequired =>
        IsRequired(Environment.GetEnvironmentVariable(RequirePostgreSqlVariable));

    /// <summary>Deutet den Wert der Umgebungsvariablen; nur <c>1</c> und <c>true</c> zaehlen.</summary>
    internal static bool IsRequired(string? value)
    {
        var trimmed = value?.Trim();
        return string.Equals(trimmed, "1", StringComparison.Ordinal)
            || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Klartext fuer das Protokoll: Was fehlt und, falls bekannt, warum.</summary>
    internal static string DescribeUnavailable(Exception? cause)
    {
        const string reason = "PostgreSQL-Container nicht verfuegbar (Docker fehlt?)";
        if (cause is null)
        {
            return reason;
        }

        var message = cause.Message.Trim().TrimEnd('.');
        return $"{reason}: {cause.GetType().Name}: {message}";
    }

    /// <summary>Hinweis, der im Fehlerfall an die Begruendung angehaengt wird.</summary>
    internal static string RequiredHint =>
        $"{RequirePostgreSqlVariable} ist gesetzt, deshalb darf dieser Test nicht uebersprungen "
        + "werden; Docker auf dem Runner pruefen.";

    /// <summary>
    /// Beendet den aktuellen Test bzw. die Fixture, weil kein PostgreSQL-Container gestartet
    /// werden konnte: als Fehler, wenn die Umgebung verpflichtend ist, sonst als uebersprungen.
    /// </summary>
    internal static void PostgreSqlUnavailable(Exception? cause)
    {
        var reason = DescribeUnavailable(cause);
        if (PostgreSqlRequired)
        {
            Assert.Fail($"{reason}. {RequiredHint}");
        }

        Assert.Ignore(reason);
    }
}
