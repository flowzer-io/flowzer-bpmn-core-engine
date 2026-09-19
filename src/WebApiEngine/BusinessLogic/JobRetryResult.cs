namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Ausgang einer Freigabe von Hand. Die Faelle sind getrennt, weil sie dem Betrieb
/// Verschiedenes sagen: „gibt es nicht mehr" heisst erledigt, „liegt nicht" heisst, dass
/// jemand anderes schneller war.
/// </summary>
public enum JobRetryResult
{
    Ok,

    /// <summary>Der Auftrag ist erledigt, abgeraeumt oder sein Token wartet nicht mehr.</summary>
    NotFound,

    /// <summary>Der Auftrag hat noch Versuche; er liegt also gar nicht.</summary>
    NotExhausted,

    /// <summary>Ein Worker haelt gerade eine gueltige Sperre darauf.</summary>
    StillLocked
}
