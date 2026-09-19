using Model;
using WebApiEngine.Auth;

namespace WebApiEngine.InboundTriggers;

/// <summary>
/// Unter welcher Kennung ein Auslöser handelt.
///
/// Ein Aufruf von außen hat keine anmeldete Person hinter sich, und der Start einer Instanz
/// verlangt trotzdem einen aufgelösten Kontext (<see cref="CurrentUserContextExtensions.RequireResolvedUserId"/>).
/// Deshalb bekommt jeder Auslöser eine eigene, feste technische Identität — wie ein eingebauter
/// Dienst. Sie ist kein Benutzerkonto: Instanzen, die so entstehen, haben keinen menschlichen
/// Initiator und sind über die Initiatorregel der <see cref="InstanceAccessPolicy"/> für
/// niemanden sichtbar. Wer sie sehen soll, braucht die Betriebsrolle oder eine Aufgabe darin.
///
/// Im Subject steht die Kennung des Auslösers, nicht sein Name. Namen ändern sich, und eine
/// Identität, die sich beim Umbenennen mit ändert, wäre weder als Initiator noch als
/// Idempotenzbereich stabil.
/// </summary>
public static class InboundTriggerIdentity
{
    /// <summary>Herkunft dieser technischen Identitäten; keine Adresse eines Identity Providers.</summary>
    public const string Issuer = "urn:flowzer:inbound-trigger";

    /// <summary>
    /// Feste technische Benutzerkennung aller Auslöser. Sie ist absichtlich eine andere als die
    /// Ersatzkennung fehlender Anmeldung: Ein Aufruf von außen soll in Spuren und Zählern nicht
    /// wie eine unauthentifizierte Anfrage der Oberfläche aussehen.
    /// </summary>
    public static readonly Guid UserId = Guid.Parse("8A9C7E14-3F5B-4C02-9A6D-1B47E5D0C3A8");

    /// <summary>Die Identität, die als Initiator an der Instanz hängen bleibt.</summary>
    public static AuthenticatedSubject Subject(InboundTrigger trigger) =>
        new(Issuer, trigger.Id.ToString());

    /// <summary>
    /// Der Akteur für Pfade, die einen aufgelösten Benutzerkontext verlangen — Instanzstart und
    /// Idempotenz. <c>IsFallback</c> ist <c>false</c>, weil dieser Kontext nicht das Ergebnis
    /// einer fehlenden Anmeldung ist, sondern eine bewusst vergebene Dienstkennung.
    /// </summary>
    public static CurrentUserContext Actor(InboundTrigger trigger) =>
        new(UserId, "inbound-trigger", IsFallback: false) { Identity = Subject(trigger) };
}
