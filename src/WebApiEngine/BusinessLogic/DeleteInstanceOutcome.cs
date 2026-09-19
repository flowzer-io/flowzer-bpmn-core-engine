namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Ausgang eines Loeschversuchs an einer Instanz. Getrennt von einer Exception, weil alle drei
/// Faelle erwartbar sind und am API-Rand je einen eigenen Statuscode bekommen.
/// </summary>
public enum DeleteInstanceOutcome
{
    /// <summary>Die Instanz und alles, was an ihr hing, ist entfernt — 204.</summary>
    Deleted,

    /// <summary>Es gibt keine Instanz mit dieser Kennung — 404.</summary>
    NotFound,

    /// <summary>Die Instanz laeuft noch; zuerst abbrechen — 409.</summary>
    StillRunning
}
